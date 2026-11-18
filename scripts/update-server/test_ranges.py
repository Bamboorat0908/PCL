"""Local release HTTP regression checks; no private key or production service."""
import http.client
import importlib.util
from pathlib import Path
import tempfile
import threading
import types
import sys
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location("brcl_update_server", Path(__file__).with_name("server.py"))
server = importlib.util.module_from_spec(spec)
# Crash intake has separate credentials; these checks exercise public downloads.
with patch.dict(sys.modules, reports=types.SimpleNamespace(handle_upload=lambda handler: handler.send_error(404))):
    spec.loader.exec_module(server)


class QuietHandler(server.Handler):
    def log_message(self, *_):
        pass


class RangeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.directory = tempfile.TemporaryDirectory()
        server.ROOT = Path(cls.directory.name)
        (server.ROOT / "releases").mkdir()
        cls.content = bytes(range(256)) * 4
        (server.ROOT / "releases" / "BRCL-2026.11.13.14.zip").write_bytes(cls.content)
        (server.ROOT / "latest.json").write_bytes(b'{"version":"test"}')
        (server.ROOT / "modpacks").mkdir()
        for name in ("TRM-1.10-high.zip", "TRM-1.11-high.zip", "TRM-1.11-low.zip", "TRM-1.11-other.zip"):
            (server.ROOT / "modpacks" / name).write_bytes(cls.content)
        cls.web = server.DownloadServer(("127.0.0.1", 0), QuietHandler)
        cls.thread = threading.Thread(target=cls.web.serve_forever, daemon=True)
        cls.thread.start()

    @classmethod
    def tearDownClass(cls):
        cls.web.shutdown()
        cls.web.server_close()
        cls.thread.join(timeout=3)
        cls.directory.cleanup()

    def request(self, header=None, path="/releases/BRCL-2026.11.13.14.zip", method="GET"):
        connection = http.client.HTTPConnection("127.0.0.1", self.web.server_port, timeout=3)
        try:
            connection.request(method, path, headers={"Range": header} if header else {})
            response = connection.getresponse()
            return response.status, dict(response.getheaders()), response.read()
        finally:
            connection.close()

    def test_pack_presets_and_ranges(self):
        for name in ("TRM-1.10-high.zip", "TRM-1.11-high.zip", "TRM-1.11-low.zip"):
            with self.subTest(name=name):
                path = "/modpacks/" + name
                self.assertEqual(200, self.request(path=path)[0])
                status, headers, body = self.request("bytes=10-19", path)
                self.assertEqual(206, status)
                self.assertEqual(self.content[10:20], body)
        self.assertEqual(404, self.request(path="/modpacks/TRM-1.11-other.zip")[0])

    def test_full_and_partial_downloads(self):
        self.assertEqual((200, self.content), (lambda result: (result[0], result[2]))(self.request()))
        for header, start, end in (("bytes=0-0", 0, 0), ("bytes=100-199", 100, 199),
                                   ("bytes=1000-", 1000, 1023), ("bytes=-32", 992, 1023),
                                   ("bytes=1000-9999", 1000, 1023)):
            with self.subTest(header=header):
                status, headers, body = self.request(header)
                self.assertEqual(206, status)
                self.assertEqual(f"bytes {start}-{end}/1024", headers["Content-Range"])
                self.assertEqual(self.content[start:end + 1], body)

    def test_unsatisfiable_or_malformed_ranges(self):
        for header in ("bytes=1024-", "bytes=10-2", "bytes=-0", "bytes=-", "bytes=0-1,3-4", "other=0-1"):
            with self.subTest(header=header):
                status, headers, body = self.request(header)
                self.assertEqual(416, status)
                self.assertEqual("bytes */1024", headers["Content-Range"])
                self.assertEqual(b"", body)

    def test_enormous_integers_and_leading_zeros(self):
        huge = "9" * 5000
        for header in ("bytes=-" + huge, "bytes=0-" + huge, "bytes=" + "0" * 5000 + "0-1023"):
            with self.subTest(header=header[:30]):
                status, headers, body = self.request(header)
                self.assertEqual(206, status)
                self.assertEqual("1024", headers["Content-Length"])
                self.assertEqual(self.content, body)
        self.assertEqual(416, self.request("bytes=" + huge + "-")[0])
        self.assertEqual(200, self.request(path="/latest.json")[0])

    def test_routes_head_and_cache(self):
        for path in ("/", "/../private.pem", "/reports", "/releases/not-a-release.zip"):
            self.assertEqual(404, self.request(path=path)[0])
        status, headers, body = self.request("bytes=0-15", method="HEAD")
        self.assertEqual(206, status)
        self.assertEqual("16", headers["Content-Length"])
        self.assertEqual(b"", body)
        self.assertEqual("no-store", self.request(path="/latest.json")[1]["Cache-Control"])


if __name__ == "__main__":
    unittest.main()
