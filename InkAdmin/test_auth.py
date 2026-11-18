import hashlib
import os
import secrets
import sqlite3
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import auth


class LoginTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(":memory:")
        self.addCleanup(self.db.close)
        self.db.executescript(auth.SCHEMA)
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.file = Path(directory.name) / "key.hash"
        config = patch.dict(os.environ, {"INK_LOGIN_HASH_FILE": str(self.file)})
        config.start()
        self.addCleanup(config.stop)
        self.key = secrets.token_urlsafe(18)
        self.configure(self.key)

    def configure(self, key):
        salt = os.urandom(16)
        derived = hashlib.pbkdf2_hmac("sha256", key.encode(), salt, 600000)
        self.file.write_text("pbkdf2_sha256$600000$" + salt.hex() + "$" + derived.hex())

    def test_logout_and_login_rotation(self):
        self.assertIsNone(auth.authenticate("incorrect"))
        tag = auth.authenticate(self.key)
        first = auth.issue(self.db, None, tag)
        self.assertIn("HttpOnly; Secure; SameSite=Strict", first)
        self.assertIn("Path=/BRCL/InkAdmin", first)
        digest = auth.digest_cookie(first.split(";")[0])
        self.assertEqual("maintainer", auth.claim(self.db, digest))
        second = auth.issue(self.db, digest, tag)
        self.assertNotEqual(first, second)
        self.assertIsNone(auth.claim(self.db, digest))
        digest = auth.digest_cookie(second.split(";")[0])
        auth.revoke(self.db, digest)
        self.assertIsNone(auth.claim(self.db, digest))

    def test_expiry_and_key_rotation(self):
        tag = auth.authenticate(self.key)
        with patch("auth.time.time", return_value=100000):
            cookie = auth.issue(self.db, None, tag)
        digest = auth.digest_cookie(cookie.split(";")[0])
        with patch("auth.time.time", return_value=100000 + auth.LIFETIME):
            self.assertIsNone(auth.claim(self.db, digest))
        with patch("auth.time.time", return_value=100001):
            self.assertEqual("maintainer", auth.claim(self.db, digest))
            self.configure(secrets.token_urlsafe(18))
            self.assertIsNone(auth.claim(self.db, digest))

    def test_invalid_input_and_verifier(self):
        for key in ("", None, [], "A"*257):
            self.assertIsNone(auth.authenticate(key))
        for header in ("", "unused=1", auth.COOKIE+"=short"):
            self.assertIsNone(auth.digest_cookie(header))
        self.file.write_text("broken")
        with self.assertRaises(RuntimeError):
            auth.authenticate(self.key)


if __name__ == "__main__":
    unittest.main()
