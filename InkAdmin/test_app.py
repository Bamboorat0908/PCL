import os
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace
import hashlib
import base64
import json
from datetime import datetime, timezone, timedelta
from pathlib import Path
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import rsa

tmp=tempfile.TemporaryDirectory()
key=rsa.generate_private_key(public_exponent=65537,key_size=2048)
keypath=Path(tmp.name)/"test.pem"
keypath.write_bytes(key.private_bytes(serialization.Encoding.PEM,serialization.PrivateFormat.PKCS8,serialization.NoEncryption()))
os.environ["BRCL_REPORT_KEY"]=str(keypath)
import app

class ExperienceTests(unittest.TestCase):
    def setUp(self):
        self.folder=tempfile.TemporaryDirectory()
        app.DATABASE=str(Path(self.folder.name)/"test.db")
        app.initialize()
        self.now=datetime.now(timezone.utc)
        self.install="1"*32
        self.game="2"*32
        self.event=dict(type="game",install=self.install,session=self.game,diagnostic=True,version="1.0.2.9",created_utc=self.now.isoformat())
        self.report=dict(id="3"*32,kind="minecraft",reason="game_crash",version="1.0.2.9",install=self.install,
                         session=self.game,created_utc=self.now.isoformat(),files=[dict(name="console.log",text="java.lang.OutOfMemoryError")])
    def tearDown(self):
        self.folder.cleanup()
    def test_cohort_and_duplicates(self):
        with app.database() as db:
            for _ in range(2): app.activity(db,self.event)
            app.activity(db,dict(self.event,type="open",install="4"*32,created_utc=(self.now-timedelta(days=3)).isoformat()))
            self.assertEqual(201,app.save(db,self.report,"test"))
            self.assertEqual(200,app.save(db,self.report,"test"))
            with self.assertRaises(ValueError): app.save(db,self.report,"other")
            stats=app.overview(db,7,"")
            self.assertEqual([2,1,2,1,1],[stats[k] for k in ("total","dau","wau","games","crashed")])
            self.assertEqual(1,stats["issues"][0]["affected"])
    def test_unlinked_and_unexpected(self):
        with app.database() as db:
            app.save(db,dict(self.report,kind="launcher",install=None,session=None,reason="unexpected_exit"),"a")
            s=app.overview(db,7,"")
            self.assertEqual(("unexpected",0,0), (s["issues"][0]["kind"],s["issues"][0]["affected"],s["crashed"]))
    def test_fix_regression_and_expiry(self):
        with app.database() as db:
            app.save(db,self.report,"old")
            db.execute("UPDATE issues SET status='fixed',fixed='1.0.2.10'")
            app.save(db,dict(self.report,id="5"*32),"second")
            self.assertEqual("fixed",db.execute("SELECT status FROM issues").fetchone()[0])
            app.save(db,dict(self.report,id="6"*32,version="1.0.2.10"),"new")
            self.assertEqual("reopened",db.execute("SELECT status FROM issues").fetchone()[0])
            db.execute("UPDATE incidents SET received=0")
            app.purge(db)
            self.assertEqual(0,db.execute("SELECT count(*) FROM bodies").fetchone()[0])
            self.assertEqual(3,db.execute("SELECT count(*) FROM incidents").fetchone()[0])
    def test_dotnet_roundtrip_and_midnight(self):
        clock=datetime(2026,11,18,15,59,59,tzinfo=timezone.utc)
        with patch("app.time.time",return_value=clock.timestamp()):
            self.assertEqual("2026-11-18",app.day_of("2026-11-18T15:59:59.1234567Z"))
            self.assertEqual("2026-11-18",app.day_of("2026-11-18T16:00:01.1234567+00:00"))
            self.assertEqual("2026-11-18",app.day_of("2026-11-18T23:59:00.12+08:00"))
            for value in ["2026-11-18T15:59:59","2026-11-18T17:00:00Z","2026-10-01T01:00:00Z","",None]:
                with self.assertRaises(ValueError): app.day_of(value)
        with patch("app.time.time",return_value=clock.timestamp()+2):
            self.assertEqual("2026-11-19",app.day_of("2026-11-18T16:00:00.0000001Z"))

    def test_windows_version_and_filtered_detail(self):
        day=self.now.astimezone(app.CST).date()
        with app.database() as db:
            for i,age in enumerate([0,6,7,29,30]):
                db.execute("INSERT INTO activity VALUES(?,?,?)",(str(i),str(day-timedelta(days=age)),"1.2"))
            db.execute("INSERT INTO activity VALUES(?,?,?)",("2",str(day),"1.3"))
            stats=app.overview(db,7,"")
            self.assertEqual((5,2,3,4,1),(stats["total"],stats["dau"],stats["wau"],stats["mau"],stats["returning"]))
            self.assertEqual(0, app.overview(db,7,"1.3")["new"])
            app.save(db,self.report,"a")
            other=dict(self.report,id="c"*32,version="1.0.3",created_utc=(self.now-timedelta(days=2)).isoformat())
            app.save(db,other,"b")
            issue=app.overview(db,7,"")["issues"][0]["id"]
            self.assertIsNone(app.representative(db,issue,1,"1.0.3"))
            self.assertEqual(other["id"],app.representative(db,issue,7,"1.0.3")["id"])
            db.execute("UPDATE incidents SET received=0")
            self.assertIsNone(app.representative(db,issue,7,""))

    def test_session_conflict_and_confirmation_denominator(self):
        with app.database() as db:
            app.activity(db,self.event)
            with self.assertRaises(app.Conflict):
                app.activity(db,dict(self.event,install="e"*32))
            app.save(db,dict(self.report,version="1.0.2.10"),"a")
            self.assertEqual(0,app.overview(db,7,"")["crashed"])
            self.assertEqual(1,db.execute("SELECT count(*) FROM activity").fetchone()[0])

    def test_capacity_and_receipts(self):
        with app.database() as db:
            app.activity(db,self.event)
            app.save(db,self.report,"a")
            with patch("app.shutil.disk_usage",return_value=SimpleNamespace(free=768*1024**2-1)):
                self.assertEqual(200,app.save(db,self.report,"a"))
                app.activity(db,self.event)
                with self.assertRaises(app.CapacityError): app.save(db,dict(self.report,id="5"*32),"b")
                with self.assertRaises(app.CapacityError): app.activity(db,dict(self.event,session="f"*32))
            with patch("pathlib.Path.stat",return_value=SimpleNamespace(st_size=256*1024**2)):
                with self.assertRaises(app.CapacityError): app.check_space(db)

    def test_retention_boundary(self):
        with app.database() as db:
            app.save(db,self.report,"a")
            now=int(self.now.timestamp())
            cutoff=now-app.BODY_LIFETIME
            issue=db.execute("SELECT id FROM issues").fetchone()[0]
            with patch("app.time.time",return_value=now):
                db.execute("UPDATE incidents SET received=?",(cutoff+1,))
                self.assertIsNotNone(app.representative(db,issue,1,""))
                db.execute("UPDATE incidents SET received=?",(cutoff,))
                self.assertIsNone(app.representative(db,issue,1,""))
                app.purge(db)
            self.assertEqual(0,db.execute("SELECT COUNT(*) FROM bodies").fetchone()[0])

    def test_reopened_workflow(self):
        with app.database() as db: app.save(db,self.report,"a")
        with app.database() as db:
            issue=db.execute("SELECT id FROM issues").fetchone()[0]
            item=dict(id=issue,owner="开发",status="reopened",fixed="")
            with self.assertRaises(ValueError): app.update_issue(db,item,"maintainer")
        with app.database() as db: app.update_issue(db,dict(item,status="fixed",fixed="1.0.2.9"),"maintainer")
        with app.database() as db: app.save(db,dict(self.report,id="6"*32),"b")
        with app.database() as db: app.update_issue(db,item,"maintainer")
        with app.database() as db:
            self.assertEqual(("reopened","1.0.2.9"),tuple(db.execute("SELECT status,fixed FROM issues").fetchone()))

    def test_envelope_and_signed_receipt(self):
        import codec
        from cryptography.hazmat.primitives import hashes,hmac,padding
        from cryptography.hazmat.primitives.asymmetric import padding as asym
        from cryptography.hazmat.primitives.ciphers import Cipher,algorithms,modes
        raw=json.dumps(self.report).encode()
        keys=os.urandom(64)
        iv=os.urandom(16)
        pad=padding.PKCS7(128).padder()
        aes=Cipher(algorithms.AES(keys[:32]),modes.CBC(iv)).encryptor()
        data=aes.update(pad.update(raw)+pad.finalize())+aes.finalize()
        mac=hmac.HMAC(keys[32:],hashes.SHA256())
        mac.update(b"BRCL-REPORT-V1"+iv+data)
        wrapped=key.public_key().encrypt(keys,asym.OAEP(mgf=asym.MGF1(hashes.SHA1()),algorithm=hashes.SHA1(),label=None))
        env={k:base64.b64encode(v).decode() for k,v in (("key",wrapped),("iv",iv),("data",data),("mac",mac.finalize()))}
        env["v"]=1
        report,digest=codec.unseal(json.dumps(env).encode())
        self.assertEqual(self.report,report)
        self.assertEqual(hashlib.sha256(raw).hexdigest(),digest)
        signed=codec.receipt(report["id"])
        key.public_key().verify(base64.b64decode(signed["receipt"]),("BRCL-RECEIPT-V1|"+report["id"]).encode(),asym.PKCS1v15(),hashes.SHA256())
        with self.assertRaises(ValueError): codec.unseal(json.dumps(dict(env,mac=base64.b64encode(bytes(32)).decode())).encode())
        for body in [b"null",b"[]",b"{}",b"not json"]:
            with self.assertRaises(ValueError): codec.unseal(body)

if __name__=="__main__":
    unittest.main()
