"""Read-only, player-scoped bridge for TRM 0.3.1. Python 3.10+, standard library only."""
from __future__ import annotations

import hashlib
import ipaddress
import json
import os
import re
import secrets
import sqlite3
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from contextlib import contextmanager
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

PREFIX = "/bank/v1"
MONEY = re.compile(r"(?:0|[1-9][0-9]{0,199})\Z")
PROFILE = "https://api.minecraftservices.com/minecraft/profile"


class BankError(Exception):
    def __init__(self, status, code):
        self.status, self.code = status, code


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def identifier(value):
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-fA-F]{32}|[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}", value):
        raise BankError(502, "invalid_upstream_data")
    return str(uuid.UUID(value))


def amount(value):
    if not isinstance(value, str) or not MONEY.fullmatch(value):
        raise BankError(502, "invalid_upstream_data")
    return int(value)


def timestamp(value):
    if type(value) is not int or not 0 <= value <= 253402214400000:
        raise BankError(502, "invalid_upstream_data")
    return value


class BankApp:
    def __init__(self, upstream, admin_token, database):
        target = urllib.parse.urlsplit(upstream)
        if target.scheme not in ("http", "https") or target.username or target.password or target.query or target.fragment:
            raise ValueError("TRM_API_URL must be an HTTP(S) base URL")
        if target.scheme == "http" and target.hostname not in ("127.0.0.1", "localhost", "::1"):
            raise ValueError("Unencrypted TRM traffic is only allowed over loopback")
        if not admin_token or "\r" in admin_token or "\n" in admin_token:
            raise ValueError("TRM_API_TOKEN is required")
        self.upstream, self.admin_token = upstream.rstrip("/"), admin_token
        self.opener = urllib.request.build_opener(NoRedirect())
        self.sessions, self.login_limits = {}, {}
        self.lock, self.upstream_lock, self.db_lock = threading.Lock(), threading.Lock(), threading.Lock()
        self.next_request = 0.0
        self.database = str(database)
        Path(database).parent.mkdir(parents=True, exist_ok=True)
        with self.connect() as db:
            db.execute("CREATE TABLE IF NOT EXISTS samples(account TEXT NOT NULL, bucket INTEGER NOT NULL, captured_at INTEGER NOT NULL, balance TEXT NOT NULL, PRIMARY KEY(account,bucket))")

    @contextmanager
    def connect(self):
        database = sqlite3.connect(self.database, timeout=5)
        try:
            with database:
                yield database
        finally:
            database.close()

    def fetch(self, url, bearer, identity=False):
        request = urllib.request.Request(url, headers={"Authorization": "Bearer " + bearer, "Accept": "application/json"}, method="GET")
        try:
            with self.opener.open(request, timeout=8) as response:
                data = response.read(2097153)
                if len(data) > 2097152:
                    raise BankError(502, "invalid_upstream_data")
                return json.loads(data)
        except urllib.error.HTTPError as error:
            if identity and error.code in (400, 401, 403, 404):
                raise BankError(401, "unauthorized") from None
            if error.code == 404:
                raise BankError(404, "account_unavailable") from None
            if error.code == 429:
                raise BankError(429, "rate_limited") from None
            raise BankError(503, "service_unavailable") from None
        except (OSError, ValueError):
            raise BankError(503, "service_unavailable") from None

    def trm(self, route):
        # One shared paced queue stays below TRM's global 20 requests/second limit.
        with self.upstream_lock:
            delay = self.next_request - time.monotonic()
            if delay > 0:
                time.sleep(delay)
            self.next_request = time.monotonic() + 0.1
            data = self.fetch(self.upstream + route, self.admin_token)
        if not isinstance(data, dict) or data.get("currency") != "sdmcoin" or data.get("unit") != "copper":
            raise BankError(502, "invalid_upstream_data")
        timestamp(data.get("capturedAt"))
        return data

    def verify(self, minecraft_token):
        # UUID is obtained from the authenticated official profile response, never from the caller.
        profile = self.fetch(PROFILE, minecraft_token, identity=True)
        if not isinstance(profile, dict) or not isinstance(profile.get("name"), str):
            raise BankError(401, "unauthorized")
        return {"uuid": identifier(profile.get("id")), "name": profile["name"][:64]}

    def login(self, token, remote):
        now = time.time()
        with self.lock:
            self.login_limits = {key: value for key, value in self.login_limits.items() if value[0] > now - 60}
            start, count = self.login_limits.get(remote, (now, 0))
            if count >= 20 or len(self.login_limits) >= 4096:
                raise BankError(429, "rate_limited")
            self.login_limits[remote] = (start, count + 1)
        player = self.verify(token)
        credential = secrets.token_urlsafe(32)
        with self.lock:
            self.sessions = {key: value for key, value in self.sessions.items() if value[1] > now}
            if len(self.sessions) >= 4096:
                raise BankError(503, "service_unavailable")
            self.sessions[hashlib.sha256(credential.encode()).digest()] = (player, now + 1800)
        return {"token": credential, "expiresAt": int((now + 1800) * 1000), "player": player}

    def player(self, credential):
        with self.lock:
            session = self.sessions.get(hashlib.sha256(credential.encode()).digest())
        if session is None or session[1] <= time.time():
            raise BankError(401, "unauthorized")
        return session[0]

    def normalize(self, raw, player):
        if not isinstance(raw, dict) or raw.get("type") != "team" or raw.get("nativeType") != "BLAZE_BANKER":
            raise BankError(502, "invalid_upstream_data")
        grants = raw.get("grants")
        if not isinstance(grants, list):
            raise BankError(502, "invalid_upstream_data")
        principals = []
        for grant in grants:
            if not isinstance(grant, dict) or grant.get("role") != "trusted":
                raise BankError(502, "invalid_upstream_data")
            principals.append(identifier(grant.get("principalId")))
        if player["uuid"] not in principals:
            raise BankError(404, "account_unavailable")
        balance, visible, reserve = (amount(raw.get(key)) for key in ("balance", "visibleBalance", "reserveBalance"))
        if balance != visible + reserve or not isinstance(raw.get("label", ""), str):
            raise BankError(502, "invalid_upstream_data")
        return {"id": identifier(raw.get("id")), "type": "team", "nativeType": "BLAZE_BANKER", "label": raw.get("label", "")[:200], "balance": str(balance), "visibleBalance": str(visible), "reserveBalance": str(reserve), "grants": [{"principalId": principal, "role": "trusted"} for principal in sorted(set(principals))]}

    def record(self, account, captured):
        with self.db_lock, self.connect() as db:
            db.execute("INSERT INTO samples VALUES(?,?,?,?) ON CONFLICT(account,bucket) DO UPDATE SET captured_at=excluded.captured_at,balance=excluded.balance WHERE excluded.captured_at>=samples.captured_at", (account["id"], captured // 60000, captured, account["balance"]))
            db.execute("DELETE FROM samples WHERE captured_at < ?", (int((time.time() - 90 * 86400) * 1000),))

    def accounts(self, player):
        result, offset, captured = {}, 0, None
        while True:
            data = self.trm("/accounts?type=team&authorizedPlayer=" + player["uuid"] + "&limit=100&offset=" + str(offset))
            rows, total = data.get("accounts"), data.get("total")
            if not isinstance(rows, list) or type(total) is not int or not 0 <= total <= 1000000:
                raise BankError(502, "invalid_upstream_data")
            captured = data["capturedAt"] if captured is None else min(captured, data["capturedAt"])
            for raw in rows:
                account = self.normalize(raw, player)
                result[account["id"]] = account
                self.record(account, data["capturedAt"])
            offset += len(rows)
            if offset >= total:
                break
            if not rows:
                raise BankError(502, "invalid_upstream_data")
        return {"currency": "sdmcoin", "unit": "copper", "capturedAt": captured, "accounts": sorted(result.values(), key=lambda account: account["id"])}

    def history(self, player, account_id, days):
        # Re-check current direct grants on every history request, including empty history.
        data = self.trm("/accounts/" + account_id)
        account = self.normalize(data.get("account"), player)
        if account["id"] != account_id:
            raise BankError(502, "invalid_upstream_data")
        self.record(account, data["capturedAt"])
        cutoff = int((time.time() - days * 86400) * 1000)
        with self.db_lock, self.connect() as db:
            rows = db.execute("SELECT captured_at,balance FROM samples WHERE account=? AND captured_at>=? ORDER BY captured_at", (account_id, cutoff)).fetchall()
        if len(rows) > 480:
            # Keep the first and most recent sample; do not represent these as transactions.
            stride = (len(rows) + 478) // 479
            rows = rows[::stride] + ([rows[-1]] if rows[-1] != rows[::stride][-1] else [])
        return {"accountId": account_id, "currency": "sdmcoin", "unit": "copper", "source": "observed_snapshots", "points": [{"capturedAt": captured, "balance": balance} for captured, balance in rows]}


class BankServer(ThreadingHTTPServer):
    daemon_threads = True
    request_queue_size = 32

    def __init__(self, address, app):
        self.app, self.capacity = app, threading.BoundedSemaphore(32)
        super().__init__(address, BankHandler)

    def process_request(self, request, client_address):
        if not self.capacity.acquire(blocking=False):
            request.close()
            return
        try:
            super().process_request(request, client_address)
        except Exception:
            self.capacity.release()
            raise

    def process_request_thread(self, request, client_address):
        try:
            super().process_request_thread(request, client_address)
        finally:
            self.capacity.release()


class BankHandler(BaseHTTPRequestHandler):
    server_version = "BRSBank/1"

    def setup(self):
        super().setup()
        self.connection.settimeout(15)

    def log_message(self, *args):
        pass  # Authentication headers and identities must not appear in access/error logs.

    def send_json(self, status, body):
        encoded = json.dumps(body, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(encoded)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        if status == 429:
            self.send_header("Retry-After", "1")
        self.end_headers()
        self.wfile.write(encoded)

    def bearer(self):
        headers = self.headers.get_all("Authorization", [])
        if len(headers) != 1 or not re.fullmatch(r"Bearer [A-Za-z0-9._~-]{16,8192}", headers[0]):
            raise BankError(401, "unauthorized")
        return headers[0][7:]

    def dispatch(self):
        try:
            route = urllib.parse.urlsplit(self.path)
            if len(self.path) > 2048 or "%" in self.path or route.fragment:
                raise BankError(400, "invalid_request")
            bearer = self.bearer()
            if self.command == "POST" and route.path == PREFIX + "/session" and not route.query:
                lengths = self.headers.get_all("Content-Length", [])
                if self.headers.get("Transfer-Encoding") or len(lengths) != 1 or not re.fullmatch(r"[0-9]{1,4}", lengths[0]):
                    raise BankError(400, "invalid_request")
                size = int(lengths[0])
                if size > 1024:
                    raise BankError(413, "request_too_large")
                if self.headers.get_content_type() != "application/json" or json.loads(self.rfile.read(size)) != {}:
                    raise BankError(400, "invalid_request")
                remote = self.client_address[0]
                if os.environ.get("BANK_TRUST_PROXY") == "1" and remote in ("127.0.0.1", "::1"):
                    remote = str(ipaddress.ip_address(self.headers.get("X-Real-IP", remote)))
                self.send_json(200, self.server.app.login(bearer, remote))
                return
            if self.command != "GET":
                raise BankError(405, "method_not_allowed")
            if self.headers.get("Transfer-Encoding") or self.headers.get("Content-Length", "0") != "0":
                raise BankError(400, "invalid_request")
            player = self.server.app.player(bearer)
            if route.path == PREFIX + "/accounts" and not route.query:
                result = self.server.app.accounts(player)
            else:
                match = re.fullmatch(re.escape(PREFIX) + r"/accounts/([0-9a-f-]{36})/history", route.path)
                if not match:
                    raise BankError(404, "not_found")
                params = urllib.parse.parse_qs(route.query, keep_blank_values=True)
                if route.query not in ("days=7", "days=30", "days=90") or params.get("days") not in (["7"], ["30"], ["90"]):
                    raise BankError(400, "invalid_request")
                result = self.server.app.history(player, identifier(match[1]), int(params["days"][0]))
            self.send_json(200, result)
        except BankError as error:
            self.send_json(error.status, {"error": error.code})
        except (ValueError, TypeError, KeyError):
            self.send_json(400, {"error": "invalid_request"})
        except Exception:
            self.send_json(503, {"error": "service_unavailable"})

    do_GET = do_POST = do_PUT = do_PATCH = do_DELETE = dispatch


def load_admin_token():
    token = os.environ.get("TRM_API_TOKEN", "")
    source = os.environ.get("TRM_API_TOKEN_FILE")
    if not token and source:
        for line in Path(source).read_text(encoding="utf-8-sig").splitlines():
            match = re.fullmatch(r"\s*token\s*[:=]\s*([^\s\\]+)\s*", line)
            if match:
                token = match[1]
                break
    return token


def main():
    app = BankApp(os.environ.get("TRM_API_URL", "http://127.0.0.1:8765/api/v1"), load_admin_token(), os.environ.get("BANK_DATABASE", str(Path(__file__).parent / "data" / "bank.sqlite3")))
    server = BankServer(("127.0.0.1", int(os.environ.get("BANK_PORT", "8786"))), app)
    print("BRS Bank listening on loopback port", server.server_port, flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
