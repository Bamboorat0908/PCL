"""Local protocol/security checks; never contacts Minecraft or the production economy."""
import copy
import http.client
import json
import tempfile
import threading
import time
import unittest
from pathlib import Path
from bank_service import BankApp, BankError, BankServer

PLAYER = "11111111-1111-1111-1111-111111111111"
OTHER = "33333333-3333-3333-3333-333333333333"
ACCOUNT = "22222222-2222-2222-2222-222222222222"


class LocalApp(BankApp):
    def __init__(self, database):
        super().__init__("http://127.0.0.1:8765/api/v1", "server-only-test-credential", database)
        self.granted = True
        self.balance = "9007199254740993"
        self.calls = []

    def verify(self, token):
        if token != "valid-minecraft-profile-token":
            raise BankError(401, "unauthorized")
        return {"uuid": PLAYER, "name": "BankProtocolFixture"}

    def trm(self, route):
        self.calls.append(route)
        account = {"id": ACCOUNT, "type": "team", "nativeType": "BLAZE_BANKER", "label": "协议验证账户", "balance": self.balance, "visibleBalance": "1073741823", "reserveBalance": str(int(self.balance) - 1073741823), "grants": [{"principalId": PLAYER if self.granted else OTHER, "role": "trusted"}]}
        response = {"currency": "sdmcoin", "unit": "copper", "capturedAt": int(time.time() * 1000)}
        if route.startswith("/accounts?"):
            response.update(accounts=[account] if self.granted else [], total=int(self.granted))
        elif route == "/accounts/" + ACCOUNT:
            response["account"] = account
        else:
            raise AssertionError("Unexpected or write-capable upstream route: " + route)
        return response


class BankTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.app = LocalApp(Path(self.temp.name) / "bank.sqlite3")
        self.server = BankServer(("127.0.0.1", 0), self.app)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.session = self.app.login("valid-minecraft-profile-token", "test")["token"]

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()
        self.temp.cleanup()

    def request(self, path, method="GET", token=None, body=None, extra=None):
        connection = http.client.HTTPConnection("127.0.0.1", self.server.server_port, timeout=5)
        headers = {"Authorization": "Bearer " + (token or self.session)}
        if body is not None:
            headers["Content-Type"] = "application/json"
        if extra:
            headers.update(extra)
        connection.request(method, "/bank/v1" + path, body=body, headers=headers)
        response = connection.getresponse()
        result = response.status, json.loads(response.read())
        self.assertEqual(response.getheader("Cache-Control"), "no-store")
        connection.close()
        return result

    def test_profile_identity_and_scope(self):
        status, session = self.request("/session", "POST", "valid-minecraft-profile-token", "{}")
        self.assertEqual(status, 200)
        self.assertEqual(session["player"]["uuid"], PLAYER)
        self.assertNotIn(self.app.admin_token, json.dumps(session))
        self.assertEqual(self.request("/session", "POST", "valid-minecraft-profile-token", json.dumps({"uuid": OTHER}))[0], 400)
        self.assertEqual(self.request("/session", "POST", "forged-client-uuid-token", "{}")[0], 401)
        status, data = self.request("/accounts")
        self.assertEqual(status, 200)
        self.assertEqual(data["accounts"][0]["balance"], "9007199254740993")
        self.assertIn("authorizedPlayer=" + PLAYER, self.app.calls[-1])
        self.assertEqual(self.request("/accounts", token="invalid-session-token")[0], 401)

    def test_revocation_blocks_existing_session_and_history(self):
        self.assertEqual(self.request("/accounts")[0], 200)
        self.assertEqual(self.request("/accounts/" + ACCOUNT + "/history?days=7")[0], 200)
        self.app.granted = False
        self.assertEqual(self.request("/accounts")[1]["accounts"], [])
        self.assertEqual(self.request("/accounts/" + ACCOUNT + "/history?days=7")[0], 404)
        self.assertEqual(self.request("/accounts/" + OTHER + "/history?days=7")[0], 503)

    def test_history_is_timestamped_sampling_not_a_ledger(self):
        player = {"uuid": PLAYER}
        data = self.app.accounts(player)
        first = data["accounts"][0]
        now = int(time.time() * 1000)
        old = copy.deepcopy(first)
        old["balance"] = "4000"
        self.app.record(old, now - 86400000)
        self.app.record(first, now - 86400000 + 1)
        status, history = self.request("/accounts/" + ACCOUNT + "/history?days=7")
        self.assertEqual(status, 200)
        self.assertEqual(history["source"], "observed_snapshots")
        times = [row["capturedAt"] for row in history["points"]]
        self.assertEqual(times, sorted(set(times)))
        self.assertEqual(len(times), 2)
        self.assertNotIn("income", history)
        self.assertEqual(history["points"][0]["balance"], self.app.balance)

    def test_route_and_request_boundaries(self):
        for method in ("POST", "PUT", "PATCH", "DELETE"):
            self.assertEqual(self.request("/accounts/" + ACCOUNT + "/adjustments", method, body='{"amount":"100"}')[0], 405)
        for query in ("days=0", "days=7&days=30", "days=%37", "days=7&uuid=" + OTHER):
            self.assertEqual(self.request("/accounts/" + ACCOUNT + "/history?" + query)[0], 400)
        self.assertEqual(self.request("/accounts?authorizedPlayer=" + OTHER)[0], 404)
        self.assertEqual(self.request("/accounts", body="{}")[0], 400)
        self.assertEqual(self.request("/session", "POST", "valid-minecraft-profile-token", "x" * 1025)[0], 413)
        self.assertTrue(all("adjustments" not in call for call in self.app.calls))

    def test_account_invariants_and_large_integer(self):
        raw = self.app.accounts({"uuid": PLAYER})["accounts"][0]
        for key, value in (("balance", 100), ("balance", "01"), ("balance", "1e8"), ("reserveBalance", "-1"), ("type", "personal")):
            changed = copy.deepcopy(raw)
            changed[key] = value
            with self.assertRaises(BankError):
                self.app.normalize(changed, {"uuid": PLAYER})
        with self.assertRaises(BankError):
            self.app.normalize(raw, {"uuid": OTHER})

    def test_expired_sessions_and_server_only_credentials(self):
        import hashlib
        key = hashlib.sha256(self.session.encode()).digest()
        self.app.sessions[key] = ({"uuid": PLAYER}, time.time() - 1)
        self.assertEqual(self.request("/accounts")[0], 401)
        self.assertEqual(self.request("/accounts", token=self.app.admin_token)[0], 401)


if __name__ == "__main__":
    unittest.main(verbosity=2)
