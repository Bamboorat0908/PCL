"""Encrypted, private crash intake; independent from public update files."""
import base64
import hashlib
import json
import os
import re
from pathlib import Path

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes, hmac, padding, serialization
from cryptography.hazmat.primitives.asymmetric import padding as rsa_padding
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

KEY = serialization.load_pem_private_key(Path(os.environ["BRCL_REPORT_KEY"]).read_bytes(), password=None)

def unseal(body):
    envelope = json.loads(body)
    if not isinstance(envelope, dict) or type(envelope.get('v')) is not int or envelope['v'] != 1:
        raise ValueError('version')
    key = KEY.decrypt(base64.b64decode(envelope['key'], validate=True), rsa_padding.OAEP(mgf=rsa_padding.MGF1(hashes.SHA1()), algorithm=hashes.SHA1(), label=None))
    iv = base64.b64decode(envelope['iv'], validate=True)
    data = base64.b64decode(envelope['data'], validate=True)
    mac = base64.b64decode(envelope['mac'], validate=True)
    if len(key) != 64 or len(iv) != 16 or not data or len(data) > 2 * 1024 * 1024 + 16:
        raise ValueError('envelope size')
    verifier = hmac.HMAC(key[32:], hashes.SHA256())
    verifier.update(b'BRCL-REPORT-V1' + iv + data)
    try:
        verifier.verify(mac)
    except InvalidSignature as err:
        raise ValueError('invalid report signature') from err
    decryptor = Cipher(algorithms.AES(key[:32]), modes.CBC(iv)).decryptor()
    padded = decryptor.update(data) + decryptor.finalize()
    unpadder = padding.PKCS7(128).unpadder()
    raw = unpadder.update(padded) + unpadder.finalize()
    if len(raw) > 2 * 1024 * 1024:
        raise ValueError('report size')
    report = json.loads(raw)
    if not isinstance(report, dict):
        raise ValueError('report')
    if report.get('kind') not in ('launcher', 'minecraft') or not re.fullmatch(r'[a-f0-9]{32}', report.get('id', '')):
        raise ValueError('report identity')
    files = report.get('files')
    if not isinstance(files, list) or not 1 <= len(files) <= 10:
        raise ValueError('files')
    for item in files:
        if not isinstance(item,dict) or not re.fullmatch(r'[a-zA-Z0-9_.-]{1,80}', item.get('name', '')) or not isinstance(item.get('text'), str) or len(item['text']) > 200000:
            raise ValueError('log entry')
    for field in ('version', 'created_utc', 'instance', 'reason', 'os'):
        if not isinstance(report.get(field, ''), str) or len(report.get(field, '')) > 1024:
            raise ValueError('metadata')
    permitted = {"id","kind","reason","version","created_utc","os","instance","install","session","files","mc","java","pack"}
    if set(report) - permitted or any(set(item) != {"name","text"} for item in files):
        raise ValueError("unsupported report fields")
    return report, hashlib.sha256(raw).hexdigest()

def receipt(report_id):
    signature = KEY.sign(('BRCL-RECEIPT-V1|' + report_id).encode(), rsa_padding.PKCS1v15(), hashes.SHA256())
    return {'ok': True, 'id': report_id, 'receipt': base64.b64encode(signature).decode()}
