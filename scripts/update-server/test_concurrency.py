"""Hold 64 large Range requests open and verify another download still works."""
from concurrent.futures import ThreadPoolExecutor
from http.client import HTTPConnection
from pathlib import Path
import time

PATH = '/modpacks/TRM-1.10-high.zip'
EXPECTED_SIZE = 170920685

def hold_request(_):
    client = HTTPConnection('127.0.0.1', 25261, timeout=15)
    client.request('GET', PATH, headers={'Range': 'bytes=0-8388607'})
    response = client.getresponse()
    assert response.status == 206, response.status
    assert response.getheader('Content-Length') == '8388608'
    assert response.read(1) == b'P'
    return client, response

connections = []
try:
    with ThreadPoolExecutor(max_workers=64) as executor:
        connections = list(executor.map(hold_request, range(64)))
    time.sleep(0.5)
    tasks = int(Path('/sys/fs/cgroup/system.slice/brcl-updates.service/pids.current').read_text())
    assert tasks > 32, f'Expected sustained load above old limit: {tasks}'
    client = HTTPConnection('127.0.0.1', 25261, timeout=15)
    client.request('GET', PATH, headers={'Range': 'bytes=170920173-'})
    response = client.getresponse()
    assert response.status == 206
    assert response.getheader('Content-Range') == f'bytes 170920173-170920684/{EXPECTED_SIZE}'
    data = response.read()
    with open('/opt/brcl-updates/public/modpacks/TRM-1.10-high.zip', 'rb') as source:
        source.seek(-512, 2)
        assert data == source.read()
    client.close()
    print(f'PASS: 64 held parallel downloads, {tasks} server tasks; another Range request completed correctly')
finally:
    for client, response in connections:
        response.close()
        client.close()

client = HTTPConnection('127.0.0.1', 25261, timeout=15)
client.request('GET', '/latest.json')
response = client.getresponse()
assert response.status == 200
response.read()
client.close()
print('PASS: service remained available after clients canceled all parallel downloads')
