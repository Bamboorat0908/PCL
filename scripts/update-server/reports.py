"""Encrypted, private crash intake; independent from public update files."""
import base64
import hashlib
import json
import os
import re
import shutil
import threading
import time
import uuid
from datetime import datetime, timedelta
from pathlib import Path
from zoneinfo import ZoneInfo
from cryptography.hazmat.primitives import hashes, hmac, padding, serialization
from cryptography.hazmat.primitives.asymmetric import padding as rsa_padding
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

ROOT = Path(os.environ.get('BRCL_REPORT_ROOT', '/var/lib/brcl-reports'))
KEY = serialization.load_pem_private_key(Path(os.environ.get('BRCL_REPORT_KEY', '/etc/brcl-reports/upload-private.pem')).read_bytes(), password=None)
MAX_BODY = 3 * 1024 * 1024
SLOTS = threading.BoundedSemaphore(4)
LOCK = threading.Lock()
RATES = {}
DAY_COUNT = [None, 0]
LAST_CLEANUP = 0

def unseal(body):
    envelope = json.loads(body)
    if envelope.get('v') != 1:
        raise ValueError('version')
    key = KEY.decrypt(base64.b64decode(envelope['key'], validate=True), rsa_padding.OAEP(mgf=rsa_padding.MGF1(hashes.SHA1()), algorithm=hashes.SHA1(), label=None))
    iv = base64.b64decode(envelope['iv'], validate=True)
    data = base64.b64decode(envelope['data'], validate=True)
    mac = base64.b64decode(envelope['mac'], validate=True)
    if len(key) != 64 or len(iv) != 16 or not data or len(data) > 2 * 1024 * 1024 + 16:
        raise ValueError('envelope size')
    verifier = hmac.HMAC(key[32:], hashes.SHA256())
    verifier.update(b'BRCL-REPORT-V1' + iv + data)
    verifier.verify(mac)
    decryptor = Cipher(algorithms.AES(key[:32]), modes.CBC(iv)).decryptor()
    padded = decryptor.update(data) + decryptor.finalize()
    unpadder = padding.PKCS7(128).unpadder()
    raw = unpadder.update(padded) + unpadder.finalize()
    if len(raw) > 2 * 1024 * 1024:
        raise ValueError('report size')
    report = json.loads(raw)
    if report.get('kind') not in ('launcher', 'minecraft') or not re.fullmatch(r'[a-f0-9]{32}', report.get('id', '')):
        raise ValueError('report identity')
    files = report.get('files')
    if not isinstance(files, list) or not 1 <= len(files) <= 10:
        raise ValueError('files')
    for item in files:
        if not re.fullmatch(r'[a-zA-Z0-9_.-]{1,80}', item.get('name', '')) or not isinstance(item.get('text'), str) or len(item['text']) > 200000:
            raise ValueError('log entry')
    for field in ('version', 'created_utc', 'instance', 'reason', 'os'):
        if not isinstance(report.get(field, ''), str) or len(report.get(field, '')) > 1024:
            raise ValueError('metadata')
    return report, hashlib.sha256(raw).hexdigest()

def receipt(report_id):
    signature = KEY.sign(('BRCL-RECEIPT-V1|' + report_id).encode(), rsa_padding.PKCS1v15(), hashes.SHA256())
    return {'ok': True, 'id': report_id, 'receipt': base64.b64encode(signature).decode()}

def save_report(report, digest):
    global LAST_CLEANUP
    now = datetime.now(ZoneInfo('Asia/Shanghai'))
    with LOCK:
        ROOT.mkdir(mode=0o700, parents=True, exist_ok=True)
        index_dir = ROOT / 'index'
        index_dir.mkdir(mode=0o700, exist_ok=True)
        index = index_dir / (report['id'] + '.json')
        if index.exists():
            if json.loads(index.read_text())['sha256'] != digest:
                return 409, {'error': 'duplicate id with different content'}
            return 200, receipt(report['id'])
        # Remove only this service's dated report folders after 30 days.
        if time.time() - LAST_CLEANUP > 3600:
            cutoff = (now - timedelta(days=30)).strftime('%Y-%m-%d')
            for kind in ('launcher', 'minecraft'):
                parent = ROOT / kind
                if parent.exists():
                    for folder in parent.iterdir():
                        if folder.is_dir() and re.fullmatch(r'\d{4}-\d{2}-\d{2}', folder.name) and folder.name < cutoff:
                            shutil.rmtree(folder)
            for entry in index_dir.glob('*.json'):
                if entry.stat().st_mtime < time.time() - 31 * 86400:
                    entry.unlink()
            LAST_CLEANUP = time.time()
        if DAY_COUNT[0] != now.date():
            DAY_COUNT[:] = [now.date(), 0]
        if DAY_COUNT[1] >= 500 or sum(f.stat().st_size for f in ROOT.rglob('*') if f.is_file()) > 2 * 1024 ** 3:
            return 507, {'error': 'storage limit'}
        folder = ROOT / report['kind'] / now.strftime('%Y-%m-%d') / report['id']
        folder.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
        temporary = folder.parent / ('.incoming-' + uuid.uuid4().hex)
        temporary.mkdir(mode=0o700)
        try:
            metadata = {k: report.get(k, '') for k in ('id', 'kind', 'version', 'created_utc', 'instance', 'reason', 'os')}
            metadata.update(received_at=now.isoformat(), sha256=digest, file_count=len(report['files']))
            (temporary / 'metadata.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding='utf-8')
            for number, entry in enumerate(report['files']):
                (temporary / f'{number:02d}-{entry["name"]}').write_text(entry['text'], encoding='utf-8')
            # A retry can recover an interrupted write before the index was committed.
            if folder.exists():
                if json.loads((folder / 'metadata.json').read_text())['sha256'] != digest:
                    return 409, {'error': 'conflicting report'}
                shutil.rmtree(temporary)
            else:
                os.replace(temporary, folder)
            temp_index = index.with_suffix('.tmp')
            temp_index.write_text(json.dumps({'sha256': digest, 'path': str(folder.relative_to(ROOT))}), encoding='utf-8')
            os.replace(temp_index, index)
            DAY_COUNT[1] += 1
            return 201, receipt(report['id'])
        finally:
            if temporary.exists():
                shutil.rmtree(temporary)

def handle_upload(handler):
    def reply(status, value):
        body = json.dumps(value).encode()
        handler.send_response(status)
        handler.send_header('Content-Type', 'application/json')
        handler.send_header('Content-Length', str(len(body)))
        handler.end_headers()
        handler.wfile.write(body)
    if handler.path != '/api/crash-reports':
        reply(404, {'error': 'not found'})
        return
    if not SLOTS.acquire(blocking=False):
        reply(429, {'error': 'busy'})
        return
    try:
        if handler.headers.get('Transfer-Encoding') or not (handler.headers.get('Content-Type') or '').startswith('application/json'):
            reply(415, {'error': 'expected encrypted JSON'})
            return
        length = int(handler.headers.get('Content-Length', '0'))
        if not 0 < length <= MAX_BODY:
            reply(413, {'error': 'size limit'})
            return
        hour = int(time.time() // 3600)
        with LOCK:
            if len(RATES) > 1024:
                RATES.clear()
            key = (handler.client_address[0], hour)
            count = RATES.get(key, 0)
            RATES[key] = count + 1
        if count >= 120:
            reply(429, {'error': 'rate limit'})
            return
        handler.connection.settimeout(15)
        body = handler.rfile.read(length)
        if len(body) != length:
            raise ValueError('incomplete body')
        report, digest = unseal(body)
        status, value = save_report(report, digest)
        reply(status, value)
    except (ValueError, KeyError, TypeError, UnicodeError):
        reply(400, {'error': 'invalid encrypted report'})
    except Exception:
        # Never echo decrypted data, credentials or crypto errors back to clients.
        reply(400, {'error': 'report rejected'})
    finally:
        SLOTS.release()
