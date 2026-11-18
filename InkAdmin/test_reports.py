import json
import tempfile
import time
import unittest
from pathlib import Path
from unittest.mock import patch

from test_app import app


class ReportCenterTests(unittest.TestCase):
    def setUp(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.database = patch.object(app, "DATABASE", str(Path(folder.name)/"test.db"))
        self.database.start()
        self.addCleanup(self.database.stop)
        app.initialize()
        self.now = int(time.time())
        self.day = app.period(1)[1]
        self.issue = "a"*32
        with app.database() as db:
            db.execute("INSERT INTO issues(id,kind,title) VALUES (?,?,?)",
                       (self.issue,"minecraft","java.lang.SampleException"))
            for n in range(23):
                rid = format(n,"032x")
                db.execute("INSERT INTO incidents VALUES(?,?,?,?,?,?,?,?)",
                           (rid,self.issue,None,None,"1.2",self.day,self.now-n,""))
                body={"id":rid,"install":"fixture-private-id",
                      "files":[{"name":"sample.log","text":"Render Thread\n罕见报错 20%_load" if n==0 else "其他"}]}
                db.execute("INSERT INTO bodies VALUES(?,?)",(rid,json.dumps(body)))

    def search(self, db, **params):
        return app.list_reports(db,*app.report_filters({k:[str(v)] for k,v in params.items()}))

    def test_pagination_and_issue_filter(self):
        with app.database() as db:
            first = self.search(db, issue=self.issue)
            second = self.search(db, issue=self.issue, page=2)
            self.assertEqual((23,20,3),(first["total"],len(first["items"]),len(second["items"])))
            self.assertEqual(23,len({x["id"] for x in first["items"]+second["items"]}))
            self.assertEqual(23,self.search(db,page=3)["total"])
            self.assertEqual(0,self.search(db,kind="unexpected")["total"])
            self.assertEqual(0,self.search(db,version="9.8")["total"])
            self.assertEqual(0,self.search(db,issue="f"*32)["total"])
            self.assertNotIn("files",first["items"][0])

    def test_literal_search_retention_and_detail(self):
        with app.database() as db:
            for term in ("RENDER THREAD","20%_load","罕见报错"):
                self.assertEqual(1,self.search(db,q=term)["total"])
            for term in ("sampleexception",self.issue.upper(),"sample.log","1.2"):
                self.assertEqual(23,self.search(db,q=term)["total"])
            self.assertEqual(0,self.search(db,q="fixture-private-id")["total"])
            rid="0"*32
            item=app.read_report(db,rid,7,"1.2")
            self.assertEqual(23,item["issue"]["reports"])
            self.assertEqual(rid,item["report"]["id"])
            self.assertTrue(item["item"]["has_body"])
            db.execute("UPDATE incidents SET received=? WHERE id=?",(self.now-app.BODY_LIFETIME,rid))
            self.assertEqual(0,self.search(db,q="罕见报错")["total"])
            match=self.search(db,q=rid)
            self.assertEqual(1,match["total"])
            self.assertFalse(match["items"][0]["has_body"])
            self.assertIsNone(app.read_report(db,rid,7,"")["report"])
            self.assertIsNone(app.read_report(db,"f"*32,7,""))

    def test_scope_of_rate_and_cumulative_installs(self):
        with app.database() as db:
            db.executemany("INSERT INTO activity VALUES(?,?,?)",
                           [("A",self.day,"1.2"),("B","2020-01-01","1.1"),("A",self.day,"1.1")])
            for n,(ver,diagnostic) in enumerate([("1.2",1),("1.2",1),("1.2",0),("1.1",1)]):
                db.execute("INSERT INTO games VALUES(?,?,?,?,?)",(str(n),"A",self.day,ver,diagnostic))
            db.execute("UPDATE incidents SET game='0',install='A' WHERE id IN (?,?)",("0"*32,format(1,"032x")))
            s=app.crash_summary(db,1,"1.2")
            self.assertEqual((2,1,2),(s["total"],s["crashed"],s["games"]))
            db.execute("UPDATE incidents SET version='9.9' WHERE id=?",(format(5,"032x"),))
            self.assertIn("9.9",[r["version"] for r in app.crash_summary(db,1,"")["versions"]])
            self.assertEqual(2,app.overview(db,1,"9.9")["total"])

    def test_input_bounds(self):
        for params in ({"kind":["unknown"]},{"days":["31"]},{"page":["0"]},
                       {"q":["x"*129]},{"issue":["bad"]},{"q":["term\x00"]}):
            with self.assertRaises(ValueError):
                app.report_filters(params)


if __name__=="__main__":
    unittest.main()
