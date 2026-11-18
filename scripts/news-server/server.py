#!/usr/bin/env python3
"""BRS announcements: Python standard library only, behind a trusted reverse proxy."""
import argparse
import getpass
import hashlib
import hmac
import ipaddress
import json
import logging
import os
from pathlib import Path
import re
import secrets
import sqlite3
import threading
import time
from contextlib import contextmanager
from datetime import datetime, timezone
from http.cookies import SimpleCookie
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit

LOG = logging.getLogger("brs-news")
ITERATIONS = 600000
SESSION_TTL = 12 * 60 * 60
MAX_BODY = 256 * 1024
COOKIE_NAME = "brs_news_session"
STATIC_ROOT = Path(__file__).resolve().parent / "static"
ASSETS = {"app.css": "text/css; charset=utf-8", "public.js": "text/javascript; charset=utf-8",
          "admin.js": "text/javascript; charset=utf-8"}


def utc_now():
    return datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")


def password_hash(password, iterations=ITERATIONS):
    salt = secrets.token_hex(16)
    derived = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), bytes.fromhex(salt), iterations)
    return "pbkdf2_sha256${}${}${}".format(iterations, salt, derived.hex())


def valid_hash(encoded):
    try:
        algorithm, rounds, salt, digest = encoded.split("$")
        return (algorithm == "pbkdf2_sha256" and 100000 <= int(rounds) <= 2000000
                and len(bytes.fromhex(salt)) == 16 and len(bytes.fromhex(digest)) == 32)
    except (ValueError, TypeError, AttributeError):
        return False


def verify_password(password, encoded):
    if not valid_hash(encoded) or not isinstance(password, str) or len(password) > 1024:
        return False
    _, rounds, salt, expected = encoded.split("$")
    try:
        raw_password = password.encode("utf-8")
    except UnicodeError:
        return False
    actual = hashlib.pbkdf2_hmac("sha256", raw_password, bytes.fromhex(salt), int(rounds)).hex()
    return hmac.compare_digest(actual, expected)


class APIError(Exception):
    def __init__(self, status, message):
        self.status, self.message = status, message
        super().__init__(message)


class Store:
    def __init__(self, path):
        self.path = str(path)
        Path(path).parent.mkdir(parents=True, exist_ok=True)
        with self.connect() as db:
            db.executescript("""
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS news (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    title TEXT NOT NULL,
                    category TEXT NOT NULL CHECK(category IN ('announcement','update')),
                    summary TEXT NOT NULL,
                    content TEXT NOT NULL,
                    status TEXT NOT NULL CHECK(status IN ('draft','published')),
                    pinned INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    published_at TEXT
                );
                CREATE INDEX IF NOT EXISTS news_published ON news(status,pinned DESC,published_at DESC,id DESC);
                CREATE TABLE IF NOT EXISTS sessions (
                    token_hash TEXT PRIMARY KEY,
                    csrf_token TEXT NOT NULL,
                    expires_at INTEGER NOT NULL
                );
            """)
        try:
            os.chmod(self.path, 0o600)
        except OSError:
            pass

    @contextmanager
    def connect(self):
        db = sqlite3.connect(self.path, timeout=15)
        db.row_factory = sqlite3.Row
        try:
            with db:
                yield db
        finally:
            db.close()

    @staticmethod
    def item(row, admin=False):
        result = {key: row[key] for key in ("id", "title", "category", "summary", "content", "published_at", "updated_at")}
        result["pinned"] = bool(row["pinned"])
        if admin:
            result.update(status=row["status"], created_at=row["created_at"])
        return result

    def listing(self, category=None, limit=100, offset=0, admin=False):
        clauses, parameters = ([] if admin else ["status = 'published'"]), []
        if category:
            clauses.append("category = ?")
            parameters.append(category)
        where = (" WHERE " + " AND ".join(clauses)) if clauses else ""
        with self.connect() as db:
            total = db.execute("SELECT COUNT(*) FROM news" + where, parameters).fetchone()[0]
            rows = db.execute("SELECT * FROM news" + where + " ORDER BY pinned DESC, COALESCE(published_at, updated_at) DESC, id DESC LIMIT ? OFFSET ?", parameters + [limit, offset]).fetchall()
        more = offset + len(rows) < total
        return {"items": [self.item(row, admin) for row in rows], "total": total, "limit": limit,
                "offset": offset, "has_more": more, "next_offset": offset + len(rows) if more else None}

    def get(self, news_id, admin=False):
        with self.connect() as db:
            row = db.execute("SELECT * FROM news WHERE id = ?" + ("" if admin else " AND status = 'published'"), (news_id,)).fetchone()
        if row is None:
            raise APIError(404, "这篇新闻不存在或尚未发布")
        return self.item(row, admin)

    def save(self, values, news_id=None):
        stamp = utc_now()
        with self.connect() as db:
            if news_id is None:
                published = stamp if values["status"] == "published" else None
                cursor = db.execute("INSERT INTO news(title,category,summary,content,status,pinned,created_at,updated_at,published_at) VALUES(?,?,?,?,?,?,?,?,?)", (values["title"], values["category"], values["summary"], values["content"], values["status"], int(values["pinned"]), stamp, stamp, published))
                news_id = cursor.lastrowid
            else:
                previous = db.execute("SELECT published_at FROM news WHERE id = ?", (news_id,)).fetchone()
                if previous is None:
                    raise APIError(404, "这篇新闻不存在")
                published = previous["published_at"] or (stamp if values["status"] == "published" else None)
                db.execute("UPDATE news SET title=?,category=?,summary=?,content=?,status=?,pinned=?,updated_at=?,published_at=? WHERE id=?", (values["title"], values["category"], values["summary"], values["content"], values["status"], int(values["pinned"]), stamp, published, news_id))
        return self.get(news_id, admin=True)

    def delete(self, news_id):
        with self.connect() as db:
            if not db.execute("DELETE FROM news WHERE id = ?", (news_id,)).rowcount:
                raise APIError(404, "这篇新闻不存在")

    def create_session(self):
        token, csrf = secrets.token_urlsafe(32), secrets.token_urlsafe(32)
        with self.connect() as db:
            db.execute("DELETE FROM sessions WHERE expires_at <= ?", (int(time.time()),))
            db.execute("INSERT INTO sessions VALUES(?,?,?)", (hashlib.sha256(token.encode()).hexdigest(), csrf, int(time.time()) + SESSION_TTL))
        return token, csrf

    def get_session(self, token):
        if not token or len(token) > 128:
            return None
        with self.connect() as db:
            row = db.execute("SELECT csrf_token FROM sessions WHERE token_hash = ? AND expires_at > ?", (hashlib.sha256(token.encode()).hexdigest(), int(time.time()))).fetchone()
        return row["csrf_token"] if row else None

    def delete_session(self, token):
        with self.connect() as db:
            db.execute("DELETE FROM sessions WHERE token_hash = ?", (hashlib.sha256(token.encode()).hexdigest(),))

    def revoke_sessions(self):
        with self.connect() as db:
            db.execute("DELETE FROM sessions")


def validate_news(data):
    if not isinstance(data, dict):
        raise APIError(400, "提交内容必须是 JSON 对象")
    result = {}
    for key, label, maximum, required in (("title", "标题", 160, True), ("summary", "摘要", 300, False), ("content", "正文", 50000, True)):
        value = data.get(key, "")
        if not isinstance(value, str):
            raise APIError(400, label + "格式不正确")
        value = value.strip()
        try:
            value.encode("utf-8")
        except UnicodeError:
            raise APIError(400, label + "包含无效字符")
        if required and not value:
            raise APIError(400, "请填写" + label)
        if len(value) > maximum or "\x00" in value:
            raise APIError(400, label + "过长或包含无效字符")
        result[key] = value
    if not result["summary"]:
        result["summary"] = " ".join(result["content"].split())[:160]
    if data.get("category") not in ("announcement", "update"):
        raise APIError(400, "请选择服务器公告或服务器动态")
    if data.get("status") not in ("draft", "published"):
        raise APIError(400, "发布状态不正确")
    if not isinstance(data.get("pinned", False), bool):
        raise APIError(400, "置顶状态不正确")
    result.update(category=data["category"], status=data["status"], pinned=data.get("pinned", False))
    return result


class LoginLimiter:
    def __init__(self):
        self.attempts = {}
        self.lock = threading.Lock()

    def check(self, key):
        now = time.monotonic()
        with self.lock:
            recent = [stamp for stamp in self.attempts.get(key, []) if now - stamp < 900]
            self.attempts[key] = recent
            if len(recent) >= 5:
                raise APIError(429, "登录尝试过多，请 15 分钟后重试")
            recent.append(now)
            if len(self.attempts) > 4096:
                self.attempts = {ip: stamps for ip, stamps in self.attempts.items() if stamps and now - stamps[-1] < 900}

    def reset(self, key):
        with self.lock:
            self.attempts.pop(key, None)


class NewsServer(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = True

    def __init__(self, address, store, admin_user, admin_hash, base_path="/BRS/news", public_origin="https://zhushu.homes", secure_cookie=True, trust_proxy=True):
        self.password_file = Path(store.path).parent / "admin-password.hash"
        if self.password_file.exists():
            admin_hash = self.password_file.read_text(encoding="utf-8").strip()
        if not valid_hash(admin_hash):
            raise ValueError("BRS_NEWS_PASSWORD_HASH is missing or invalid; use --hash-password")
        if not re.fullmatch(r"/[A-Za-z0-9_/-]+", base_path) or base_path.endswith("/") or "//" in base_path:
            raise ValueError("Invalid base path")
        self.store, self.admin_user, self.admin_hash = store, admin_user, admin_hash
        self.base_path, self.public_origin = base_path, public_origin.rstrip("/")
        self.secure_cookie, self.trust_proxy = secure_cookie, trust_proxy
        self.login_limiter = LoginLimiter()
        self.auth_lock = threading.Lock()
        super().__init__(address, Handler)

    def change_password(self, current, replacement):
        if not isinstance(replacement, str) or not 12 <= len(replacement) <= 1024:
            raise APIError(400, "新密码长度须为 12 至 1024 位")
        try:
            replacement.encode("utf-8")
        except UnicodeError:
            raise APIError(400, "新密码包含无效字符")
        with self.auth_lock:
            if not verify_password(current, self.admin_hash):
                raise APIError(400, "当前密码不正确")
            if hmac.compare_digest(current.encode("utf-8"), replacement.encode("utf-8")):
                raise APIError(400, "新密码不能与当前密码相同")
            encoded = password_hash(replacement)
            temporary = self.password_file.with_name(".admin-password-" + secrets.token_hex(8))
            descriptor = os.open(str(temporary), os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            try:
                with os.fdopen(descriptor, "w", encoding="utf-8") as output:
                    output.write(encoded + "\n")
                    output.flush()
                    os.fsync(output.fileno())
                os.replace(str(temporary), str(self.password_file))
            finally:
                if temporary.exists():
                    temporary.unlink()
            self.admin_hash = encoded
            self.store.revoke_sessions()


class Handler(BaseHTTPRequestHandler):
    server_version = "BRSNews"
    sys_version = ""
    protocol_version = "HTTP/1.1"

    def setup(self):
        super().setup()
        self.connection.settimeout(20)

    def log_message(self, message, *args):
        # Request bodies, credentials and session tokens never enter logs.
        LOG.info("%s %s", self.client_address[0], (message % args).replace("\n", " ").replace("\r", " "))

    def send_payload(self, status, body, content_type="application/json; charset=utf-8", extra_headers=None, head=False):
        if not isinstance(body, bytes):
            body = json.dumps(body, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("X-Frame-Options", "DENY")
        self.send_header("Referrer-Policy", "same-origin")
        self.send_header("Content-Security-Policy", "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'")
        self.send_header("Permissions-Policy", "camera=(), microphone=(), geolocation=()")
        for key, value in (extra_headers or {}).items():
            self.send_header(key, value)
        self.end_headers()
        if not head:
            self.wfile.write(body)

    def send_json_error(self, status, message):
        self.send_payload(status, {"error": message}, extra_headers={"Retry-After": "900"} if status == 429 else None, head=self.command == "HEAD")

    def route(self):
        parsed = urlsplit(self.path)
        base = self.server.base_path
        if parsed.path == base:
            return "/", parse_qs(parsed.query, keep_blank_values=True)
        if not parsed.path.startswith(base + "/"):
            raise APIError(404, "页面不存在")
        return parsed.path[len(base):], parse_qs(parsed.query, keep_blank_values=True)

    def cookie_token(self):
        try:
            cookie = SimpleCookie()
            cookie.load(self.headers.get("Cookie", ""))
            return cookie[COOKIE_NAME].value if COOKIE_NAME in cookie else ""
        except Exception:
            return ""

    def session(self, csrf=False):
        token = self.cookie_token()
        expected = self.server.store.get_session(token)
        if expected is None:
            raise APIError(401, "请先登录管理员账号")
        if csrf and not hmac.compare_digest(self.headers.get("X-CSRF-Token", "").encode("utf-8"), expected.encode("ascii")):
            raise APIError(403, "安全校验失败，请刷新后重试")
        return token, expected

    def read_json(self):
        if self.headers.get("Content-Type", "").split(";", 1)[0].strip().lower() != "application/json":
            raise APIError(415, "请使用 JSON 格式提交")
        if self.headers.get("Transfer-Encoding"):
            self.close_connection = True
            raise APIError(400, "不支持该请求编码")
        length = self.headers.get("Content-Length", "")
        if not re.fullmatch(r"[0-9]{1,8}", length):
            self.close_connection = True
            raise APIError(400, "请求长度不正确")
        if int(length) > MAX_BODY:
            self.close_connection = True
            raise APIError(413, "提交内容过大")
        try:
            data = json.loads(self.rfile.read(int(length)).decode("utf-8"))
        except (ValueError, UnicodeError):
            raise APIError(400, "JSON 内容不正确")
        if not isinstance(data, dict):
            raise APIError(400, "提交内容必须是 JSON 对象")
        return data

    def check_origin(self):
        origin = self.headers.get("Origin")
        if origin and origin.rstrip("/") != self.server.public_origin:
            raise APIError(403, "不允许来自其他网站的操作")
        if self.headers.get("Sec-Fetch-Site") == "cross-site":
            raise APIError(403, "不允许来自其他网站的操作")

    def client_ip(self):
        address = self.client_address[0]
        if self.server.trust_proxy and ipaddress.ip_address(address).is_loopback:
            proposed = self.headers.get("X-Real-IP", "")
            try:
                address = str(ipaddress.ip_address(proposed))
            except ValueError:
                pass
        return address

    def session_cookie(self, token, clear=False):
        return "{}={}; Path={}; Max-Age={}; HttpOnly; SameSite=Strict{}".format(COOKIE_NAME, token, self.server.base_path, 0 if clear else SESSION_TTL, "; Secure" if self.server.secure_cookie else "")

    @staticmethod
    def pagination(query):
        def number(key, default, minimum, maximum):
            value = query.get(key, [str(default)])
            if len(value) != 1 or not re.fullmatch(r"[0-9]{1,7}", value[0]):
                raise APIError(400, "分页参数不正确")
            result = int(value[0])
            if not minimum <= result <= maximum:
                raise APIError(400, "分页参数超出范围")
            return result
        category = query.get("category", [""])
        if len(category) != 1 or category[0] not in ("", "announcement", "update"):
            raise APIError(400, "新闻分类不正确")
        return category[0] or None, number("limit", 100, 1, 100), number("offset", 0, 0, 1000000)

    def do_GET(self):
        self.handle_request()

    def do_HEAD(self):
        self.handle_request(head=True)

    def do_POST(self):
        self.handle_request()

    def do_PUT(self):
        self.handle_request()

    def do_DELETE(self):
        self.handle_request()

    def do_OPTIONS(self):
        self.send_json_error(405, "不支持此请求方法")

    def handle_request(self, head=False):
        try:
            self.dispatch(head)
        except APIError as error:
            if self.command not in ("GET", "HEAD"):
                self.close_connection = True
            self.send_json_error(error.status, error.message)
        except (BrokenPipeError, ConnectionResetError, TimeoutError):
            self.close_connection = True
        except Exception:
            LOG.exception("Request failed")
            self.close_connection = True
            self.send_json_error(500, "服务暂时不可用，请稍后重试")

    def dispatch(self, head):
        path, query = self.route()
        method = "GET" if self.command == "HEAD" else self.command
        if method == "GET":
            if path in ("/", "/admin", "/admin/"):
                filename = "admin.html" if path.startswith("/admin") else "index.html"
                self.send_payload(200, (STATIC_ROOT / filename).read_bytes(), "text/html; charset=utf-8", head=head)
                return
            if path.startswith("/static/") and path[8:] in ASSETS:
                asset = path[8:]
                self.send_payload(200, (STATIC_ROOT / asset).read_bytes(), ASSETS[asset], head=head)
                return
            if path == "/api/news":
                category, limit, offset = self.pagination(query)
                self.send_payload(200, self.server.store.listing(category, limit, offset), head=head)
                return
            found = re.fullmatch(r"/api/news/([1-9][0-9]{0,17})", path)
            if found:
                self.send_payload(200, {"item": self.server.store.get(int(found[1]))}, head=head)
                return
            if path == "/api/admin/session":
                _, csrf = self.session()
                self.send_payload(200, {"authenticated": True, "username": self.server.admin_user, "csrf_token": csrf}, head=head)
                return
            if path == "/api/admin/news":
                self.session()
                category, limit, offset = self.pagination(query)
                self.send_payload(200, self.server.store.listing(category, limit, offset, admin=True), head=head)
                return
            found = re.fullmatch(r"/api/admin/news/([1-9][0-9]{0,17})", path)
            if found:
                self.session()
                self.send_payload(200, {"item": self.server.store.get(int(found[1]), admin=True)}, head=head)
                return
            raise APIError(404, "页面不存在")

        self.check_origin()
        if path == "/api/admin/login" and method == "POST":
            data = self.read_json()
            address = self.client_ip()
            self.server.login_limiter.check(address)
            username, password = data.get("username", ""), data.get("password", "")
            # Always calculate the password derivation even for an incorrect username.
            with self.server.auth_lock:
                password_ok = verify_password(password, self.server.admin_hash)
                try:
                    username_ok = isinstance(username, str) and hmac.compare_digest(username.encode("utf-8"), self.server.admin_user.encode("utf-8"))
                except UnicodeError:
                    username_ok = False
                if not username_ok or not password_ok:
                    raise APIError(401, "账号或密码不正确")
                self.server.login_limiter.reset(address)
                old_token = self.cookie_token()
                if old_token:
                    self.server.store.delete_session(old_token)
                token, csrf = self.server.store.create_session()
            self.send_payload(200, {"authenticated": True, "username": self.server.admin_user, "csrf_token": csrf}, extra_headers={"Set-Cookie": self.session_cookie(token)})
            return
        if path == "/api/admin/logout" and method == "POST":
            token, _ = self.session(csrf=True)
            self.read_json()
            self.server.store.delete_session(token)
            self.send_payload(200, {"ok": True}, extra_headers={"Set-Cookie": self.session_cookie("", clear=True)})
            return
        if path == "/api/admin/password" and method == "POST":
            self.session(csrf=True)
            data = self.read_json()
            address = self.client_ip()
            self.server.login_limiter.check(address)
            self.server.change_password(data.get("current_password", ""), data.get("new_password", ""))
            self.server.login_limiter.reset(address)
            self.send_payload(200, {"ok": True}, extra_headers={"Set-Cookie": self.session_cookie("", clear=True)})
            return
        if path == "/api/admin/news" and method == "POST":
            self.session(csrf=True)
            self.send_payload(201, {"item": self.server.store.save(validate_news(self.read_json()))})
            return
        found = re.fullmatch(r"/api/admin/news/([1-9][0-9]{0,17})", path)
        if found and method in ("PUT", "DELETE"):
            self.session(csrf=True)
            if method == "PUT":
                self.send_payload(200, {"item": self.server.store.save(validate_news(self.read_json()), int(found[1]))})
            else:
                self.read_json()
                self.server.store.delete(int(found[1]))
                self.send_payload(200, {"ok": True})
            return
        raise APIError(404, "页面不存在")


def main():
    parser = argparse.ArgumentParser(description="BRS 新闻与公告服务")
    parser.add_argument("--hash-password", action="store_true", help="交互输入密码并输出 PBKDF2 哈希")
    args = parser.parse_args()
    if args.hash_password:
        first = getpass.getpass("管理员密码（至少 12 位）: ")
        second = getpass.getpass("再次输入密码: ")
        if first != second or len(first) < 12:
            parser.error("两次密码必须相同且至少 12 位")
        print(password_hash(first))
        return
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    store = Store(os.environ.get("BRS_NEWS_DB", "/var/lib/brs-news/news.sqlite3"))
    server = NewsServer((os.environ.get("BRS_NEWS_HOST", "127.0.0.1"), int(os.environ.get("BRS_NEWS_PORT", "18764"))), store,
                        os.environ.get("BRS_NEWS_ADMIN_USER", "admin"), os.environ.get("BRS_NEWS_PASSWORD_HASH", ""),
                        base_path=os.environ.get("BRS_NEWS_BASE_PATH", "/BRS/news"),
                        public_origin=os.environ.get("BRS_NEWS_PUBLIC_ORIGIN", "https://zhushu.homes"),
                        secure_cookie=os.environ.get("BRS_NEWS_SECURE_COOKIE", "1") == "1",
                        trust_proxy=os.environ.get("BRS_NEWS_TRUST_PROXY", "1") == "1")
    LOG.info("News service listening on %s:%s%s", *server.server_address, server.base_path)
    try:
        server.serve_forever(poll_interval=0.5)
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
