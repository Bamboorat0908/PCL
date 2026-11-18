"""Run: python3 -m unittest -v test_server.py (isolated temporary database)."""
import http.client
import json
from pathlib import Path
import tempfile
import threading
import time
import unittest

import server


class NewsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.initial_hash = server.password_hash("test-admin-password", iterations=100000)

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.store = server.Store(Path(self.directory.name) / "news.sqlite3")
        self.web = server.NewsServer(("127.0.0.1", 0), self.store, "admin", self.initial_hash)
        self.thread = threading.Thread(target=self.web.serve_forever, daemon=True)
        self.thread.start()
        self.cookie = ""
        self.csrf = ""

    def tearDown(self):
        self.web.shutdown()
        self.web.server_close()
        self.thread.join(timeout=3)
        self.directory.cleanup()

    def request(self, method, path, data=None, authenticated=True, headers=None):
        prepared = {"Content-Type": "application/json"}
        if authenticated:
            prepared.update({"Cookie": self.cookie, "X-CSRF-Token": self.csrf})
        prepared.update(headers or {})
        connection = http.client.HTTPConnection("127.0.0.1", self.web.server_port, timeout=5)
        body = json.dumps(data, ensure_ascii=False).encode("utf-8") if data is not None else None
        connection.request(method, "/BRS/news" + path, body=body, headers=prepared)
        response = connection.getresponse()
        raw = response.read()
        result = json.loads(raw) if raw and response.getheader("Content-Type", "").startswith("application/json") else raw
        status, response_headers = response.status, dict(response.getheaders())
        connection.close()
        return status, result, response_headers

    def login(self, password="test-admin-password"):
        status, result, headers = self.request("POST", "/api/admin/login", {"username": "admin", "password": password}, authenticated=False)
        if status == 200:
            self.cookie = headers["Set-Cookie"].split(";", 1)[0]
            self.csrf = result["csrf_token"]
        return status, result, headers

    @staticmethod
    def article(**overrides):
        values = dict(title="服务器公告", summary="欢迎查看公告", content="第一段\n\n第二段", category="announcement", status="draft", pinned=False)
        values.update(overrides)
        return values

    def test_authentication_cookie_csrf_origin_and_logout(self):
        self.assertEqual(401, self.request("GET", "/api/admin/session")[0])
        self.assertEqual(401, self.request("POST", "/api/admin/news", self.article())[0])
        status, _, headers = self.login()
        self.assertEqual(200, status)
        for flag in ("HttpOnly", "SameSite=Strict", "Secure", "Path=/BRS/news"):
            self.assertIn(flag, headers["Set-Cookie"])
        self.assertIn("frame-ancestors 'none'", headers["Content-Security-Policy"])
        self.assertEqual(200, self.request("GET", "/api/admin/session")[0])
        self.assertEqual(403, self.request("POST", "/api/admin/news", self.article(), headers={"X-CSRF-Token": "wrong"})[0])
        self.assertEqual(403, self.request("POST", "/api/admin/news", self.article(), headers={"X-CSRF-Token": "\xff"})[0])
        self.assertEqual(403, self.request("POST", "/api/admin/news", self.article(), headers={"Origin": "https://attacker.invalid"})[0])
        self.assertEqual(415, self.request("POST", "/api/admin/news", self.article(), headers={"Content-Type": "text/plain"})[0])
        self.assertEqual(200, self.request("POST", "/api/admin/logout", {})[0])
        self.assertEqual(401, self.request("GET", "/api/admin/session")[0])

    def test_draft_publish_update_unpublish_delete_privacy(self):
        self.login()
        status, created, _ = self.request("POST", "/api/admin/news", self.article())
        self.assertEqual(201, status)
        news_id = created["item"]["id"]
        self.assertIsNone(created["item"]["published_at"])
        self.assertEqual([], self.request("GET", "/api/news")[1]["items"])
        self.assertEqual(404, self.request("GET", "/api/news/" + str(news_id))[0])
        self.assertEqual(1, len(self.request("GET", "/api/admin/news")[1]["items"]))
        status, published, _ = self.request("PUT", "/api/admin/news/" + str(news_id), self.article(status="published", pinned=True))
        self.assertEqual(200, status)
        self.assertTrue(published["item"]["published_at"].endswith("Z"))
        public = self.request("GET", "/api/news")[1]["items"][0]
        self.assertNotIn("status", public)
        self.assertNotIn("created_at", public)
        self.assertEqual("第一段\n\n第二段", public["content"])
        self.request("PUT", "/api/admin/news/" + str(news_id), self.article(status="draft"))
        self.assertEqual(404, self.request("GET", "/api/news/" + str(news_id))[0])
        self.assertEqual(200, self.request("DELETE", "/api/admin/news/" + str(news_id), {})[0])
        self.assertEqual(404, self.request("GET", "/api/admin/news/" + str(news_id))[0])

    def test_pagination_category_pinning_and_validation(self):
        for number in range(7):
            self.store.save(server.validate_news(self.article(title=str(number), category="announcement" if number % 2 else "update", status="published", pinned=number == 1)))
        self.store.save(server.validate_news(self.article(title="private")))
        status, page, _ = self.request("GET", "/api/news?limit=2&offset=0")
        self.assertEqual(200, status)
        self.assertEqual(7, page["total"])
        self.assertEqual("1", page["items"][0]["title"])
        self.assertTrue(page["has_more"])
        self.assertEqual(2, page["next_offset"])
        category = self.request("GET", "/api/news?category=announcement&limit=2&offset=2")[1]
        self.assertEqual(3, category["total"])
        self.assertFalse(category["has_more"])
        self.assertIsNone(category["next_offset"])
        for query in ("limit=0", "limit=-1", "limit=101", "limit=1&limit=2", "offset=1000001", "offset=NaN", "category=invalid", "category=%27%20OR%201=1--"):
            self.assertEqual(400, self.request("GET", "/api/news?" + query)[0], query)
        empty = self.request("GET", "/api/news?offset=1000")[1]
        self.assertEqual([], empty["items"])
        self.assertFalse(empty["has_more"])

    def test_plain_text_xss_sql_literals_and_rejected_input(self):
        self.login()
        content = "<script>alert('xss')</script>\n'; DROP TABLE news; --"
        status, result, _ = self.request("POST", "/api/admin/news", self.article(content=content, summary="", status="published"))
        self.assertEqual(201, status)
        news_id = result["item"]["id"]
        self.assertEqual(content, self.request("GET", "/api/news/" + str(news_id))[1]["item"]["content"])
        self.assertEqual(1, self.request("GET", "/api/news")[1]["total"])
        for overrides in (dict(title=""), dict(title="x" * 161), dict(summary="x" * 301), dict(content="x" * 50001), dict(category="other"), dict(status="hidden"), dict(pinned=1), dict(content=None)):
            self.assertEqual(400, self.request("POST", "/api/admin/news", self.article(**overrides))[0], str(overrides)[:80])
        self.assertEqual(404, self.request("GET", "/static/../server.py")[0])
        self.assertEqual(404, self.request("GET", "/api/news/1%20OR%201=1")[0])

    def test_rate_limit_wrong_logins_and_session_expiry(self):
        for _ in range(5):
            self.assertEqual(401, self.login("wrong")[0])
        status, _, headers = self.login("wrong")
        self.assertEqual(429, status)
        self.assertEqual("900", headers["Retry-After"])
        self.web.login_limiter.reset("127.0.0.1")
        self.assertEqual(200, self.login()[0])
        with self.store.connect() as db:
            db.execute("UPDATE sessions SET expires_at = ?", (int(time.time()) - 1,))
        self.assertEqual(401, self.request("GET", "/api/admin/session")[0])

    def test_password_change_revokes_and_survives_restart(self):
        self.login()
        self.assertEqual(400, self.request("POST", "/api/admin/password", dict(current_password="incorrect", new_password="replacement-password"))[0])
        self.assertEqual(200, self.request("GET", "/api/admin/session")[0])
        self.assertEqual(400, self.request("POST", "/api/admin/password", dict(current_password="test-admin-password", new_password="short"))[0])
        self.assertEqual(200, self.request("POST", "/api/admin/password", dict(current_password="test-admin-password", new_password="新密码replacement2026"))[0])
        self.assertEqual(401, self.request("GET", "/api/admin/session")[0])
        self.assertEqual(401, self.login()[0])
        self.assertEqual(200, self.login("新密码replacement2026")[0])
        other = server.NewsServer(("127.0.0.1", 0), self.store, "admin", self.initial_hash)
        try:
            self.assertTrue(server.verify_password("新密码replacement2026", other.admin_hash))
            self.assertFalse(server.verify_password("test-admin-password", other.admin_hash))
        finally:
            other.server_close()

    def test_static_and_head(self):
        for path in ("/", "/admin", "/static/app.css", "/static/public.js", "/static/admin.js"):
            self.assertEqual(200, self.request("GET", path)[0], path)
        status, body, headers = self.request("HEAD", "/")
        self.assertEqual(200, status)
        self.assertEqual(b"", body)
        self.assertGreater(int(headers["Content-Length"]), 0)


if __name__ == "__main__":
    unittest.main()
