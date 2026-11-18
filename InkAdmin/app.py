"""InkAdmin dashboard and public client intake behind the site's HTTPS proxy."""
import hashlib
import json
import os
import re
import shutil
import socketserver
import sqlite3
import time
from contextlib import closing, contextmanager
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler
from pathlib import Path
from urllib.parse import urlsplit, parse_qs
import auth
from codec import unseal, receipt

BASE = "/BRCL/InkAdmin"
ROOT = Path(__file__).parent
DATABASE = os.environ.get("INK_DB", "/var/lib/brcl-inkadmin/experience.sqlite")
CST = timezone(timedelta(hours=8))
IDENT = re.compile(r"[a-f0-9]{32}")
VERSION = re.compile(r"[0-9]{1,4}(?:\.[0-9]{1,4}){1,4}")
STAMP = re.compile(r"([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2})(?:\.([0-9]{1,7}))?(Z|[+-][0-9]{2}:[0-9]{2})")
BODY_LIFETIME = 30 * 86400

class Conflict(ValueError):
    """A retried event must keep its original contents."""

class CapacityError(Exception):
    """Intake is temporarily full; existing receipts can still be returned."""
SCHEMA = """
CREATE TABLE IF NOT EXISTS activity(install TEXT,day TEXT,version TEXT,PRIMARY KEY(install,day,version));
CREATE INDEX IF NOT EXISTS active_day ON activity(day,version);
CREATE TABLE IF NOT EXISTS games(id TEXT PRIMARY KEY,install TEXT,day TEXT,version TEXT,diagnostic INTEGER);
CREATE INDEX IF NOT EXISTS game_day ON games(day);
CREATE TABLE IF NOT EXISTS issues(id TEXT PRIMARY KEY,kind TEXT,title TEXT,status TEXT DEFAULT 'open',
 owner TEXT DEFAULT '',fixed TEXT DEFAULT '');
CREATE TABLE IF NOT EXISTS incidents(id TEXT PRIMARY KEY,issue TEXT,install TEXT,game TEXT,version TEXT,
 day TEXT,received INTEGER,digest TEXT);
CREATE INDEX IF NOT EXISTS incident_received ON incidents(received);
CREATE INDEX IF NOT EXISTS incident_issue ON incidents(issue,received);
CREATE INDEX IF NOT EXISTS incident_day ON incidents(day,version);
CREATE TABLE IF NOT EXISTS environments(id TEXT PRIMARY KEY,mc TEXT,java TEXT,pack TEXT);
CREATE INDEX IF NOT EXISTS incidents_game ON incidents(game,install);
CREATE TABLE IF NOT EXISTS bodies(id TEXT PRIMARY KEY,body TEXT);
CREATE TABLE IF NOT EXISTS audit(at INTEGER,actor TEXT,action TEXT,object TEXT);
"""

@contextmanager
def database():
    with closing(sqlite3.connect(DATABASE, timeout=8)) as db:
        db.row_factory = sqlite3.Row
        db.execute("PRAGMA secure_delete=ON")
        with db:
            yield db

def initialize():
    with database() as db:
        db.execute("PRAGMA journal_mode=WAL")
        db.executescript(SCHEMA + auth.SCHEMA)

def purge(db):
    db.execute("DELETE FROM admin_sessions WHERE expires <= ?", (int(time.time()),))
    cutoff = int(time.time()) - BODY_LIFETIME
    db.execute("DELETE FROM bodies WHERE id IN (SELECT id FROM incidents WHERE received <= ?)", (cutoff,))
    db.execute("DELETE FROM audit WHERE at <= ?", (int(time.time()) - 180 * 86400,))

def day_of(value):
    match = STAMP.fullmatch(value) if isinstance(value, str) else None
    if not match:
        raise ValueError("timestamp")
    # .NET round-trip timestamps have seven fractional digits; Python 3.8 accepts six.
    fraction = (match[2] or "").ljust(6, "0")[:6]
    offset = "+00:00" if match[3] == "Z" else match[3]
    moment = datetime.fromisoformat(match[1] + "." + fraction + offset)
    now = time.time()
    delta = now - moment.timestamp()
    if not -900 <= delta <= 16 * 86400:
        raise ValueError("timestamp")
    return datetime.fromtimestamp(min(now, moment.timestamp()), CST).date().isoformat()

def check_space(db):
    # The website shares this filesystem; existing events can obtain receipts at capacity.
    dbpath = Path(DATABASE)
    wal = Path(str(dbpath) + "-wal")
    try:
        wal_bytes = wal.stat().st_size
    except FileNotFoundError:
        wal_bytes = 0
    allocated = dbpath.stat().st_size + wal_bytes
    if allocated >= 512 * 1024**2 or shutil.disk_usage(dbpath.parent).free < 768 * 1024**2:
        raise CapacityError("storage")


def identifier(value):
    if not isinstance(value,str) or not IDENT.fullmatch(value):
        raise ValueError("identifier")
    return value

def version(value):
    if not isinstance(value,str) or not VERSION.fullmatch(value):
        raise ValueError("version")
    return value

def activity(db, item):
    install, ver = identifier(item["install"]), version(item["version"])
    day = day_of(item["created_utc"])
    if item["type"] == "game":
        game = identifier(item["session"])
        if type(item["diagnostic"]) is not bool:
            raise ValueError("diagnostic")
        event = (install, day, ver, int(item["diagnostic"]))
        previous = db.execute("SELECT install,day,version,diagnostic FROM games WHERE id=?", (game,)).fetchone()
        if previous:
            if tuple(previous) != event:
                raise Conflict("session")
            return
        check_space(db)
        db.execute("INSERT INTO games VALUES(?,?,?,?,?)", (game,) + event)
    elif item["type"] == "open":
        if db.execute("SELECT 1 FROM activity WHERE install=? AND day=? AND version=?", (install,day,ver)).fetchone():
            return
        check_space(db)
    else:
        raise ValueError("event")
    db.execute("INSERT OR IGNORE INTO activity VALUES(?,?,?)", (install,day,ver))


def signature(report):
    files = {x["name"]: x["text"] for x in report["files"]}
    kind = report["kind"]
    if report["reason"] == "unexpected_exit":
        return "unexpected", "上次启动器会话未正常结束", "unexpected"
    if "jvm-crash.log" in files:
        kind = "jvm"
    ordered = sorted(files,key=lambda f:(0 if f=="jvm-crash.log" or f=="exception.log" or f.startswith("crash-") else 1))
    text = "\n".join(files[f] for f in ordered)
    exceptions = re.findall(r"(?:Caused by:\s*)?\b([A-Za-z_][\w.$]{1,160}(?:Exception|Error))\b",text)
    frames = re.findall(r"(?m)^\s*(?:at|在)\s+([\w.$<>:+]{3,140})",text)
    native = re.search(r"(?im)problematic frame:\s*\n#?\s*\w?\s*\[([a-z0-9_.-]{2,90})",text)
    title = native[1] if native else (exceptions[0] if exceptions else {"launcher":"启动器异常","minecraft":"游戏异常退出","jvm":"JVM 异常"}[kind])
    return kind,title,"|".join([kind,title]+frames[:3])

def save(db, report, digest):
    old = db.execute("SELECT digest FROM incidents WHERE id=?", (report["id"],)).fetchone()
    if old:
        if old[0] != digest: raise Conflict("report")
        return 200
    if db.execute("SELECT COUNT(*) FROM incidents WHERE received>=?",(int(time.time())-86400,)).fetchone()[0] >= 1000:
        raise CapacityError("report intake at capacity")
    check_space(db)
    version(report["version"])
    day = day_of(report["created_utc"])
    install = identifier(report["install"]) if report.get("install") else None
    game = identifier(report["session"]) if report.get("session") else None
    if game and (install is None or report["kind"] != "minecraft"):
        raise ValueError("session")
    mc, java, pack = (report.get(k, "") for k in ("mc", "java", "pack"))
    if not (isinstance(mc, str) and re.fullmatch(r"[0-9a-zA-Z.\-]{0,40}", mc)
            and isinstance(java, str) and re.fullmatch(r"[0-9.]{0,40}", java)
            and pack in ("", "TRM", "其他")):
        raise ValueError("environment")
    kind,title,key = signature(report)
    issue = hashlib.sha256(key.encode()).hexdigest()[:32]
    db.execute("INSERT OR IGNORE INTO issues(id,kind,title) VALUES(?,?,?)",(issue,kind,title))
    row = db.execute("SELECT fixed,status FROM issues WHERE id=?",(issue,)).fetchone()
    if row["status"]=="fixed" and tuple(map(int,report["version"].split("."))) >= tuple(map(int,row["fixed"].split("."))):
        db.execute("UPDATE issues SET status='reopened' WHERE id=?", (issue,))
    db.execute("INSERT INTO incidents VALUES(?,?,?,?,?,?,?,?)",(report["id"],issue,install,game,report["version"],day,int(time.time()),digest))
    db.execute("INSERT INTO environments VALUES(?,?,?,?)",(report["id"],mc,java,pack))
    db.execute("INSERT INTO bodies VALUES(?,?)",(report["id"],json.dumps(report, ensure_ascii=False)))
    return 201

def rows(db, query, args=()):
    return [dict(r) for r in db.execute(query,args)]

def period(days):
    today = datetime.now(CST).date()
    return ((today - timedelta(days=days - 1)).isoformat(), today.isoformat())

def filters(params):
    days = int(params.get("days", ["7"])[0])
    if days not in (1, 7, 30, 90):
        raise ValueError("period")
    ver = params.get("version", [""])[0]
    return days, version(ver) if ver else ""

def crash_summary(db, days, ver):
    start, end = period(days)
    args = (start, end, ver, ver)
    total = db.execute("SELECT COUNT(*) FROM (SELECT DISTINCT install FROM activity)").fetchone()[0]
    sessions = db.execute("""SELECT COUNT(*) AS games, COALESCE(SUM(EXISTS (
            SELECT 1 FROM incidents i JOIN issues q ON q.id=i.issue
            WHERE i.game=g.id AND i.install=g.install AND i.version=g.version
            AND q.kind IN ('minecraft','jvm'))),0) AS crashed
        FROM games g WHERE g.diagnostic=1 AND g.day BETWEEN ? AND ? AND (?='' OR g.version=?)""", args).fetchone()
    versions = rows(db, """SELECT version FROM activity WHERE day BETWEEN ? AND ?
        UNION SELECT version FROM incidents WHERE day BETWEEN ? AND ? ORDER BY version""", (start,end,start,end))
    issues = rows(db, """SELECT q.*,COUNT(*) AS reports,COUNT(DISTINCT i.install) AS affected,
        MIN(i.day) AS first,MAX(i.day) AS last,GROUP_CONCAT(DISTINCT i.version) AS versions
        FROM incidents i JOIN issues q ON q.id=i.issue WHERE i.day BETWEEN ? AND ?
        AND (?='' OR i.version=?) GROUP BY q.id ORDER BY reports DESC,last DESC,q.id LIMIT 500""", args)
    return dict(total=total, games=sessions["games"], crashed=sessions["crashed"], versions=versions,
                issues=issues, period_start=start, period_end=end)


def overview(db, days, ver):
    start, end = period(days)
    today = datetime.fromisoformat(end).date()
    previous = (today - timedelta(days=2 * days - 1)).isoformat()
    args = (start, end, ver, ver)
    def distinct(since):
        return db.execute("""SELECT COUNT(DISTINCT install) FROM activity
            WHERE day BETWEEN ? AND ? AND (?='' OR version=?)""", (since, end, ver, ver)).fetchone()[0]

    new = db.execute("""SELECT COUNT(DISTINCT a.install) FROM activity a
        WHERE day=? AND (?='' OR version=?) AND NOT EXISTS (
            SELECT 1 FROM activity prev WHERE prev.install=a.install AND prev.day<a.day)""", (end, ver, ver)).fetchone()[0]
    returning = db.execute("""SELECT COUNT(DISTINCT a.install) FROM activity a
        WHERE a.day BETWEEN ? AND ? AND (?='' OR a.version=?) AND EXISTS (
            SELECT 1 FROM activity earlier WHERE earlier.install=a.install AND earlier.day<?)""", args + (start,)).fetchone()[0]
    versions = rows(db, """SELECT version, COUNT(DISTINCT install) AS active FROM activity
        WHERE day BETWEEN ? AND ? GROUP BY version ORDER BY active DESC, version""", (start, end))
    environments = rows(db, """SELECT e.mc,e.java,e.pack,COUNT(*) AS reports,COUNT(DISTINCT i.install) AS affected
        FROM environments e JOIN incidents i ON i.id=e.id WHERE i.day BETWEEN ? AND ? AND (?='' OR i.version=?)
        AND (e.mc<>'' OR e.java<>'' OR e.pack<>'') GROUP BY e.mc,e.java,e.pack ORDER BY reports DESC LIMIT 100""", args)
    total = db.execute("SELECT COUNT(*) FROM (SELECT DISTINCT install FROM activity)").fetchone()[0]
    return dict(period_start=start, period_end=end, total=total, new=new, returning=returning, environments=environments,
        dau=distinct(end), wau=distinct((today-timedelta(days=6)).isoformat()), mau=distinct((today-timedelta(days=29)).isoformat()),
        games=db.execute("""SELECT COUNT(*) FROM games WHERE diagnostic=1 AND day BETWEEN ? AND ?
            AND (?='' OR version=?)""", args).fetchone()[0],
        crashed=db.execute("""SELECT COUNT(*) FROM games g WHERE diagnostic=1 AND day BETWEEN ? AND ? AND (?='' OR version=?)
            AND EXISTS(SELECT 1 FROM incidents i JOIN issues q ON i.issue=q.id WHERE i.game=g.id AND i.install=g.install
            AND i.version=g.version AND q.kind IN ('minecraft','jvm'))""", args).fetchone()[0],
        trend=rows(db, """SELECT day,COUNT(DISTINCT install) AS active FROM activity WHERE day BETWEEN ? AND ?
            AND (?='' OR version=?) GROUP BY day ORDER BY day DESC""", args),
        versions=versions,
        issues=rows(db, """SELECT q.*,(SELECT COUNT(*) FROM incidents p WHERE p.issue=q.id AND p.day>=? AND p.day<?
            AND (?='' OR p.version=?)) AS previous,
            (SELECT MIN(b.day) FROM incidents b WHERE b.issue=q.id) AS first_seen,
            COUNT(*) AS reports,COUNT(DISTINCT i.install) AS affected,MIN(i.day) AS first,MAX(i.day) AS last,
            GROUP_CONCAT(DISTINCT i.version) AS versions FROM incidents i JOIN issues q ON q.id=i.issue
            WHERE i.day BETWEEN ? AND ? AND (?='' OR i.version=?) GROUP BY q.id
            ORDER BY affected DESC,reports DESC,last DESC,q.id LIMIT 500""", (previous, start, ver, ver) + args))

def representative(db, issue, days, ver):
    start, end = period(days)
    row = db.execute("""SELECT body FROM bodies JOIN incidents USING(id)
        WHERE issue=? AND day BETWEEN ? AND ? AND (?='' OR version=?) AND received>?
        ORDER BY received DESC, incidents.rowid DESC LIMIT 1""",
        (issue, start, end, ver, ver, int(time.time())-BODY_LIFETIME)).fetchone()
    return json.loads(row[0]) if row else None


REPORT_FIELDS = """i.id,i.issue,q.title,q.kind,i.version,i.day,i.received,
    (i.received>:cutoff AND EXISTS(SELECT 1 FROM bodies b WHERE b.id=i.id)) AS has_body"""
REPORT_TABLES = " FROM incidents i JOIN issues q ON q.id=i.issue "
REPORT_PAGE_SIZE = 20


def report_filters(params):
    days, ver = filters(params)
    kind = params.get("kind", [""])[0]
    if kind not in ("", "minecraft", "jvm", "launcher", "unexpected"):
        raise ValueError("kind")
    issue = params.get("issue", [""])[0]
    if issue:
        identifier(issue)
    query = params.get("q", [""])[0].strip()
    if len(query) > 128 or any(ord(char)<32 for char in query):
        raise ValueError("search")
    page = int(params.get("page", ["1"])[0])
    if not 1 <= page <= 1000000:
        raise ValueError("page")
    return days, ver, kind, issue, query, page


def list_reports(db, days, ver, kind, issue, query, page):
    start, end = period(days)
    values = dict(start=start, end=end, version=ver, kind=kind, issue=issue, q=query,
                  cutoff=int(time.time())-BODY_LIFETIME, limit=REPORT_PAGE_SIZE, offset=(page-1)*REPORT_PAGE_SIZE)
    conditions = ["i.day BETWEEN :start AND :end"]
    if ver:
        conditions.append("i.version=:version")
    if kind:
        conditions.append("q.kind=:kind")
    if issue:
        conditions.append("i.issue=:issue")
    if query:
        matches = ["substr(i.id,1,length(:q))=lower(:q)", "substr(i.issue,1,length(:q))=lower(:q)",
                   "instr(lower(q.title),lower(:q))>0", "instr(i.version,:q)>0"]
        if len(query) >= 2:
            # Search only retained attachment names/text, not IDs or other private JSON metadata.
            matches.append("""(i.received>:cutoff AND EXISTS(
                SELECT 1 FROM bodies b, json_each(b.body,'$.files') f WHERE b.id=i.id AND
                (instr(lower(json_extract(f.value,'$.text')),lower(:q))>0 OR
                 instr(lower(json_extract(f.value,'$.name')),lower(:q))>0)))""")
        conditions.append("(" + " OR ".join(matches) + ")")
    source = REPORT_TABLES + "WHERE " + " AND ".join(conditions)
    # A window count scans retained text once and keeps attachment contents out of list responses.
    items = rows(db, "SELECT " + REPORT_FIELDS + ", COUNT(*) OVER() AS matched" + source +
                 " ORDER BY i.received DESC,i.id DESC LIMIT :limit OFFSET :offset", values)
    total = items[0]["matched"] if items else 0
    if not items and page > 1:
        total = db.execute("SELECT COUNT(*)" + source, values).fetchone()[0]
    for item in items:
        del item["matched"]
        item["has_body"] = bool(item["has_body"])
    return dict(items=items, total=total, page=page, page_size=REPORT_PAGE_SIZE)


def read_report(db, report_id, days, ver):
    cutoff = int(time.time()) - BODY_LIFETIME
    row = db.execute("SELECT " + REPORT_FIELDS + REPORT_TABLES + "WHERE i.id=:id",
                     {"id": report_id, "cutoff": cutoff}).fetchone()
    if not row:
        return None
    item = dict(row)
    item["has_body"] = bool(item["has_body"])
    body = None
    if item["has_body"]:
        body = json.loads(db.execute("SELECT body FROM bodies WHERE id=?", (report_id,)).fetchone()[0])
    start,end = period(days)
    issue = dict(db.execute("""SELECT q.*,(SELECT COUNT(*) FROM incidents i
        WHERE i.issue=q.id AND i.day BETWEEN ? AND ? AND (?='' OR i.version=?)) AS reports
        FROM issues q WHERE q.id=?""", (start,end,ver,ver,item["issue"])).fetchone())
    return dict(item=item, report=body, issue=issue)

def update_issue(db, item, actor):
    issue = identifier(item["id"])
    state, owner = item["status"], item["owner"]
    if state not in ("open", "working", "fixed", "reopened") or not isinstance(owner, str) or len(owner)>60:
        raise ValueError("issue state")
    db.execute("BEGIN IMMEDIATE")
    previous = db.execute("SELECT status,fixed FROM issues WHERE id=?", (issue,)).fetchone()
    if previous is None or (state=="reopened" and previous["status"]!="reopened"):
        raise ValueError("issue state")
    fixed = previous["fixed"] if state=="reopened" else version(item["fixed"]) if state=="fixed" else ""
    db.execute("UPDATE issues SET status=?,owner=?,fixed=? WHERE id=?", (state, owner, fixed, issue))
    db.execute("INSERT INTO audit VALUES(?,?,?,?)", (int(time.time()), actor, "update", issue))

class Handler(BaseHTTPRequestHandler):
    def log_message(self,*args):
        pass
    def reply(self,status,value,mime="application/json; charset=utf-8",cookie=None):
        data=value if isinstance(value,bytes) else json.dumps(value,ensure_ascii=False).encode()
        self.send_response(status)
        self.send_header("Content-Type",mime)
        self.send_header("Content-Length",str(len(data)))
        self.send_header("Cache-Control","no-store")
        self.send_header("X-Content-Type-Options","nosniff")
        if cookie:
            self.send_header("Set-Cookie", cookie)
        self.end_headers()
        self.wfile.write(data)
    def do_GET(self):
        self.dispatch(False)
    def do_POST(self):
        self.dispatch(True)
    def dispatch(self,post):
        try:
            path=urlsplit(self.path).path
            landing = not post and path in (BASE, BASE+"/")
            public_asset = not post and path in (BASE+"/login.css", BASE+"/login.js")
            login = post and path == BASE+"/api/login"
            logout = post and path == BASE+"/api/logout"
            intake = post and path in (BASE+"/collect/activity", BASE+"/collect/crash")
            actor = None
            digest = auth.digest_cookie(self.headers.get("Cookie")) if not intake else None
            if digest and not public_asset and not login:
                with database() as db:
                    actor = auth.claim(db, digest)
            if landing:
                page = ROOT / ("admin.html" if actor else "login.html")
                return self.reply(200, page.read_bytes(), "text/html; charset=utf-8")
            if not (public_asset or login or intake or actor):
                return self.reply(401, {"error":"请先登录管理后台"})
            if post:
                if not intake and self.headers.get("Origin") != "https://zhushu.homes":
                    return self.reply(403, {"error":"请求来源无效"})
                if logout:
                    with database() as db:
                        auth.revoke(db, digest)
                    return self.reply(200, {"ok":True}, cookie=auth.session_cookie("", 0))
                if self.headers.get_content_type() != "application/json" or self.headers.get("Transfer-Encoding"):
                    return self.reply(415,{"error":"需要 JSON"})
                size=int(self.headers.get("Content-Length",0))
                if not 0<size<=(3*1024*1024 if path.endswith("/crash") else 4096):
                    return self.reply(413,{"error":"大小超限"})
                raw=self.rfile.read(size)
                if len(raw) != size: raise ValueError("incomplete body")
                if path==BASE+"/collect/crash":
                    report,digest=unseal(raw)
                    with database() as db:
                        db.execute("BEGIN IMMEDIATE")
                        status=save(db,report,digest)
                    return self.reply(status,receipt(report["id"]))
                item=json.loads(raw)
                if login:
                    tag = auth.authenticate(item.get("key"))
                    if tag is None:
                        return self.reply(401, {"error":"密钥不正确"})
                    with database() as db:
                        cookie = auth.issue(db, digest, tag)
                    return self.reply(200, {"ok":True}, cookie=cookie)
                if path==BASE+"/collect/activity":
                    with database() as db:
                        db.execute("BEGIN IMMEDIATE")
                        activity(db,item)
                    return self.reply(200,{"ok":True})
                if path==BASE+"/api/issue" and actor=="maintainer":
                    with database() as db: update_issue(db,item,actor)
                    return self.reply(200,{"ok":True})
                return self.reply(403,{"error":"操作被拒绝"})
            if public_asset or path in (BASE+"/app.js",BASE+"/app.css"):
                file=ROOT/path.rsplit("/",1)[1]
                return self.reply(200,file.read_bytes(),"text/javascript; charset=utf-8" if file.suffix==".js" else "text/css; charset=utf-8")
            params=parse_qs(urlsplit(self.path).query)
            if path==BASE+"/api/reports" and actor=="maintainer":
                options=report_filters(params)
                with database() as db:
                    if options[4]:
                        db.execute("INSERT INTO audit VALUES(?,?,?,?)",(int(time.time()),actor,"search_reports",""))
                        db.commit()
                    db.execute("BEGIN")
                    result=list_reports(db,*options)
                return self.reply(200,result)
            if path==BASE+"/api/report" and actor=="maintainer":
                report_id=identifier(params["id"][0])
                days,ver=filters(params)
                with database() as db:
                    db.execute("INSERT INTO audit VALUES(?,?,?,?)",(int(time.time()),actor,"read_report",report_id))
                    result=read_report(db,report_id,days,ver)
                if result is None:
                    return self.reply(404,{"error":"报告不存在"})
                return self.reply(200,result)
            if path==BASE+"/api/overview":
                days,ver=filters(params)
                with database() as db:
                    db.execute("BEGIN")
                    result=crash_summary(db,days,ver) if params.get("view")==["crashes"] else overview(db,days,ver)
                result["role"]=actor
                return self.reply(200,result)
            if path==BASE+"/api/detail" and actor=="maintainer":
                issue=identifier(params["id"][0])
                with database() as db:
                    days,ver=filters(params)
                    report=representative(db,issue,days,ver)
                    db.execute("INSERT INTO audit VALUES(?,?,?,?)",(int(time.time()),actor,"read",issue))
                return self.reply(200,{"report":report})
            return self.reply(404,{"error":"无此资源"})
        except Conflict:
            self.reply(409,{"error":"事件编号已被使用"})
        except auth.LoginBusy:
            self.reply(429,{"error":"登录请求较多，请稍后重试"})
        except CapacityError:
            self.reply(429,{"error":"接收容量已满，请稍后重试"})
        except (ValueError,KeyError,TypeError,AttributeError,UnicodeError,OverflowError,RecursionError):
            self.reply(400,{"error":"数据格式无效"})
        except Exception:
            self.reply(503,{"error":"请求暂时无法处理"})

if hasattr(socketserver, 'UnixStreamServer'):
    class Service(socketserver.ThreadingMixIn,socketserver.UnixStreamServer):
        daemon_threads=True
        def get_request(self):
            conn,_=super().get_request()
            conn.settimeout(15)
            return conn,("local",0)

if __name__=="__main__":
    import sys
    initialize()
    with database() as db:
        purge(db)
        db.commit()
        db.execute("PRAGMA wal_checkpoint(TRUNCATE)")
    if "--purge" not in sys.argv:
        sock=Path(os.environ.get("INK_SOCKET","/run/brcl-inkadmin/api.sock"))
        if sock.is_socket(): sock.unlink()
        with Service(str(sock),Handler) as server:
            sock.chmod(0o660)
            server.serve_forever()
