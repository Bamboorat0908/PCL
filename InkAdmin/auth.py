"""Verify the configured master key and manage the private dashboard's sessions."""
import hashlib
import hmac
import os
import re
import secrets
import threading
import time
from http.cookies import CookieError, SimpleCookie
from pathlib import Path

COOKIE = "brcl_ink_session"
COOKIE_PATH = "/BRCL/InkAdmin"
LIFETIME = 8 * 3600
VERIFIER = re.compile(r"pbkdf2_sha256\$600000\$([a-f0-9]{32})\$([a-f0-9]{64})")
TOKEN = re.compile(r"[a-f0-9]{64}")
VERIFIERS = threading.BoundedSemaphore(2)
SCHEMA = """
CREATE TABLE IF NOT EXISTS admin_sessions(
    digest TEXT PRIMARY KEY, expires INTEGER NOT NULL, key_tag TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS admin_expiry ON admin_sessions(expires);
"""


class LoginBusy(Exception):
    pass


def verifier():
    path = os.environ.get("INK_LOGIN_HASH_FILE", "/etc/brcl-inkadmin/login-key.hash")
    stored = Path(path).read_text(encoding="ascii").strip()
    match = VERIFIER.fullmatch(stored)
    if not match:
        raise RuntimeError("invalid server login configuration")
    return match


def authenticate(key):
    if not isinstance(key, str) or not 0 < len(key) <= 256:
        return None
    if not VERIFIERS.acquire(blocking=False):
        raise LoginBusy()
    try:
        spec = verifier()
        derived = hashlib.pbkdf2_hmac("sha256", key.encode("utf-8"), bytes.fromhex(spec[1]), 600000)
        if hmac.compare_digest(derived, bytes.fromhex(spec[2])):
            return hashlib.sha256(spec[0].encode("ascii")).hexdigest()
    finally:
        VERIFIERS.release()
    return None


def digest_cookie(header):
    jar = SimpleCookie()
    try:
        jar.load(header or "")
    except CookieError:
        return None
    item = jar.get(COOKIE)
    if item is None or not TOKEN.fullmatch(item.value):
        return None
    return hashlib.sha256(item.value.encode("ascii")).hexdigest()


def claim(db, digest):
    result = db.execute("SELECT key_tag FROM admin_sessions WHERE digest=? AND expires>?",
                        (digest, int(time.time()))).fetchone()
    if result:
        current = hashlib.sha256(verifier()[0].encode("ascii")).hexdigest()
        if hmac.compare_digest(result[0], current):
            return "maintainer"
    return None


def revoke(db, digest):
    db.execute("DELETE FROM admin_sessions WHERE digest=?", (digest,))


def issue(db, previous, tag):
    token = secrets.token_hex(32)
    digest = hashlib.sha256(token.encode("ascii")).hexdigest()
    now = int(time.time())
    db.execute("DELETE FROM admin_sessions WHERE expires<=? OR key_tag<>?", (now, tag))
    revoke(db, previous)
    db.execute("INSERT INTO admin_sessions VALUES (?,?,?)", (digest, now + LIFETIME, tag))
    return session_cookie(token, LIFETIME)


def session_cookie(token, age):
    return f"{COOKIE}={token}; Path={COOKIE_PATH}; Max-Age={age}; HttpOnly; Secure; SameSite=Strict"
