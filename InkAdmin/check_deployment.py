"""Verify deployed InkAdmin authentication. Optionally also replay a synthetic client fixture on the host."""
import argparse
import base64
import copy
import getpass
import json
import secrets
import sqlite3
import sys
from contextlib import closing
from html.parser import HTMLParser
from http.cookiejar import CookieJar
from pathlib import Path
from urllib import error, request
from urllib.parse import urlencode

ORIGIN = "https://zhushu.homes"
ROOT = ORIGIN + "/BRCL/InkAdmin"


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


class FormInspector(HTMLParser):
    def __init__(self):
        super().__init__()
        self.has_password = False

    def handle_starttag(self, tag, attrs):
        if tag == "input" and dict(attrs).get("type", "").lower() == "password":
            self.has_password = True


def login_page(page):
    form = FormInspector()
    form.feed(page.decode("utf-8"))
    return form.has_password


class Browser:
    def __init__(self):
        self.cookies = CookieJar()
        self.opener = request.build_opener(request.HTTPCookieProcessor(self.cookies))

    def query(self, path, expected, body=None):
        headers = {"User-Agent": "BRCL-Acceptance/2"}
        data = None
        if body is not None:
            headers.update({"Content-Type": "application/json", "Origin": ORIGIN})
            data = json.dumps(body).encode("utf-8")
        req = request.Request(ROOT + path, headers=headers, data=data)
        try:
            response = self.opener.open(req, timeout=20)
        except error.HTTPError as err:
            response = err
        with response:
            require(response.getcode() == expected,
                    "HTTP {} for {}; expected {}".format(response.getcode(), path, expected))
            require("basic" not in response.headers.get("WWW-Authenticate", "").lower(),
                    "Unexpected Basic authentication challenge")
            return response.read()


def verify_login(browser, key):
    require(login_page(browser.query("", 200)), "Missing login form at management URL")
    for path in ("/login.css", "/login.js"):
        require(bool(browser.query(path, 200)), "Missing login asset " + path)
    for path in ("/api/overview", "/api/reports", "/api/report?id=" + "0"*32, "/api/detail?id=" + "0"*32, "/app.css", "/app.js"):
        browser.query(path, 401)
    browser.query("/api/login", 401, {"key": secrets.token_urlsafe(28)})
    browser.query("/api/overview", 401)
    browser.query("/api/login", 200, {"key": key})
    cookies = list(browser.cookies)
    require(len(cookies) == 1, "Expected exactly one session cookie")
    cookie = cookies[0]
    require(cookie.secure and cookie.has_nonstandard_attr("HttpOnly"), "Missing session cookie flags")
    require(str(cookie.get_nonstandard_attr("SameSite", "")).lower() in ("strict", "lax"),
            "Missing SameSite restriction")
    require(cookie.value != key, "Session cookie must use a server-issued identifier")
    dashboard = browser.query("", 200)
    require(not login_page(dashboard) and b"/app.js" in dashboard, "Expected management dashboard after login")
    overview = json.loads(browser.query("/api/overview", 200))
    require(overview.get("role") == "maintainer" and "issues" in overview, "Expected management access")
    browser.query("/app.css", 200)
    browser.query("/app.js", 200)
    print("PASS login page, public login assets, protected assets and APIs, rejected key and authenticated dashboard")
    return copy.copy(cookie)


def verify_logout(browser, issued):
    browser.query("/api/logout", 200, {})
    browser.query("/api/overview", 401)
    require(login_page(browser.query("", 200)), "Expected login form after logout")
    previous = Browser()
    previous.cookies.set_cookie(issued)
    previous.query("/api/overview", 401)
    print("PASS sign-out and server-side session revocation")


def replay_fixture(folder, browser):
    public = Browser()
    report = (folder / "id.txt").read_text().strip()
    game = json.loads((folder / "game.json").read_bytes())
    envelope = json.loads((folder / "envelope.json").read_bytes())
    with closing(sqlite3.connect("/var/lib/brcl-inkadmin/experience.sqlite")) as db:
        require(not db.execute("SELECT 1 FROM activity WHERE install=?", (game["install"],)).fetchone(),
                "Fixture installation must be unique")
        require(not db.execute("SELECT 1 FROM incidents WHERE id=?", (report,)).fetchone(),
                "Fixture report ID must be unique")
        issue = None
        try:
            public.query("/collect/activity", 200, game)
            public.query("/collect/activity", 200, game)
            first = json.loads(public.query("/collect/crash", 201, envelope))
            second = json.loads(public.query("/collect/crash", 200, envelope))
            require(first == second and first["id"] == report and first["ok"], "Unexpected signed receipt")
            (folder / "receipt.json").write_text(json.dumps(first))
            public.query("/collect/crash", 400, dict(envelope, mac=base64.b64encode(bytes(32)).decode()))
            public.query("/collect/crash", 400, {"kind": "minecraft"})
            issue = db.execute("SELECT issue FROM incidents WHERE id=?", (report,)).fetchone()[0]
            overview = json.loads(browser.query("/api/overview?view=crashes&days=7", 200))
            row = next(r for r in overview["issues"] if r["id"] == issue)
            require(row["reports"] == 1 and row["affected"] == 1 and "SyntheticInkAdminSmokeException" in row["title"],
                    "Unexpected synthetic report cohort")
            public.query("/api/detail?id="+issue, 401)
            detail = json.loads(browser.query("/api/detail?id="+issue, 200))["report"]
            require((detail["id"], detail["session"], detail["install"]) == (report, game["session"], game["install"]),
                    "Unexpected incident detail")
            list_path = "/api/reports?" + urlencode(dict(days=7, version=game["version"], kind="minecraft", q=report))
            result = json.loads(browser.query(list_path, 200))
            require(result["total"] == 1 and result["items"][0]["id"] == report and result["items"][0]["has_body"],
                    "Could not find exact report")
            exact = json.loads(browser.query("/api/report?id="+report,200))
            require(exact["item"]["id"] == report and exact["report"] == detail, "Individual report content mismatch")
            group_result = json.loads(browser.query("/api/reports?" + urlencode(dict(days=7, issue=issue)),200))
            require(group_result["total"] == row["reports"], "Group count and report results disagree")
            browser.query("/api/report?id=" + secrets.token_hex(16), 404)
            print("PASS scoped crash overview, report ID search, raw attachments, group counts, missing report")
            term = exact["report"]["files"][0]["name"]
            attachments = json.loads(browser.query("/api/reports?" + urlencode(dict(days=7, issue=issue, q=term)),200))
            require(any(item["id"] == report for item in attachments["items"]), "Unable to find retained attachment")
            change = dict(id=issue, status="fixed", owner="fixture", fixed="1.0.2.9")
            public.query("/api/issue", 401, change)
            browser.query("/api/issue", 200, change)
            print("PASS public collector replay deduplication, signed receipts, invalid reports, grouping and triage")
        finally:
            with db:
                db.execute("DELETE FROM bodies WHERE id=?", (report,))
                db.execute("DELETE FROM environments WHERE id=?", (report,))
                db.execute("DELETE FROM incidents WHERE id=?", (report,))
                db.execute("DELETE FROM games WHERE id=? AND install=?", (game["session"], game["install"]))
                db.execute("DELETE FROM activity WHERE install=?", (game["install"],))
                if issue and not db.execute("SELECT 1 FROM incidents WHERE issue=?", (issue,)).fetchone():
                    db.execute("DELETE FROM issues WHERE id=?", (issue,))
                    db.execute("DELETE FROM audit WHERE object=?", (issue,))
            require(not db.execute("SELECT 1 FROM incidents WHERE id=?", (report,)).fetchone()
                    and not db.execute("SELECT 1 FROM activity WHERE install=?", (game["install"],)).fetchone(),
                    "Synthetic fixture cleanup incomplete")
            print("PASS fixture cleanup")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("fixture", nargs="?", type=Path, help="Existing synthetic fixture on the deployment host")
    args = parser.parse_args()
    key = getpass.getpass("InkAdmin key: ") if sys.stdin.isatty() else sys.stdin.readline().rstrip("\r\n")
    require(bool(key), "Missing key on stdin")
    browser = Browser()
    issued = verify_login(browser, key)
    try:
        if args.fixture:
            replay_fixture(args.fixture, browser)
    finally:
        verify_logout(browser, issued)


if __name__ == "__main__":
    main()
