"""Read-only BRCL release endpoint. No directory listings or private files."""
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler
from pathlib import Path
from urllib.parse import urlsplit
import os
import re
import sys
import threading
from reports import handle_upload

ROOT = Path('/opt/brcl-updates/public')

def range_number(digits, size):
    # Range numbers have no protocol length limit. Clamp before int() so a valid
    # enormous suffix/end cannot hit Python's integer-string conversion limit.
    normalized = digits.lstrip('0')
    return size if len(normalized) > len(str(size)) else int(normalized or '0')

class Handler(SimpleHTTPRequestHandler):
    def setup(self):
        super().setup()
        # Bound stalled connections without timing out ordinary slow downloads.
        self.connection.settimeout(120)

    def do_POST(self):
        handle_upload(self)

    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(ROOT), **kwargs)

    def send_head(self):
        self.remaining = None
        path = urlsplit(self.path).path
        if not re.fullmatch(r'/latest\.(json|sig)|/releases/BRCL-[0-9.]+\.(zip|exe)|/modpacks/TRM-(?:1\.10-high|1\.11-(?:high|low))\.zip', path):
            self.send_error(404)
            return None
        header = self.headers.get('Range')
        if header:
            try:
                stream = open(ROOT / path.lstrip('/'), 'rb')
            except FileNotFoundError:
                self.send_error(404)
                return None
            size = os.fstat(stream.fileno()).st_size
            match = re.fullmatch(r'bytes=(\d*)-(\d*)', header.strip())
            start, end = 0, size - 1
            if match and (match[1] or match[2]):
                if match[1]:
                    start = range_number(match[1], size)
                    end = min(range_number(match[2], size), size - 1) if match[2] else size - 1
                else:
                    start = max(0, size - range_number(match[2], size))
            else:
                start = size
            if start > end or start >= size:
                stream.close()
                self.send_response(416)
                self.send_header('Content-Range', f'bytes */{size}')
                self.send_header('Content-Length', '0')
                self.end_headers()
                return None
            self.remaining = end - start + 1
            stream.seek(start)
            self.send_response(206)
            self.send_header('Content-Type', self.guess_type(path))
            self.send_header('Content-Length', str(self.remaining))
            self.send_header('Content-Range', f'bytes {start}-{end}/{size}')
            self.end_headers()
            return stream
        return super().send_head()

    def copyfile(self, source, outputfile):
        if self.remaining is None:
            return super().copyfile(source, outputfile)
        while self.remaining:
            block = source.read(min(self.remaining, 256 * 1024))
            if not block:
                break
            outputfile.write(block)
            self.remaining -= len(block)

    def end_headers(self):
        self.send_header('Accept-Ranges', 'bytes')
        if re.fullmatch(r'/releases/BRCL-[0-9.]+\.exe', urlsplit(self.path).path):
            self.send_header('Content-Disposition', 'attachment; filename="BRCL.exe"')
        self.send_header('Cache-Control', 'no-store' if '/latest.' in self.path or '/api/' in self.path else 'public, max-age=31536000, immutable')
        self.send_header('X-Content-Type-Options', 'nosniff')
        super().end_headers()

class DownloadServer(ThreadingHTTPServer):
    # A launcher can open dozens of Range requests for a single large file.
    # Bound workers below the service task limit, and queue bursts of requests.
    request_queue_size = 256
    daemon_threads = True
    block_on_close = False

    def __init__(self, *args, **kwargs):
        self.workers = threading.BoundedSemaphore(128)
        super().__init__(*args, **kwargs)

    def process_request(self, request, client_address):
        self.workers.acquire()
        try:
            super().process_request(request, client_address)
        except BaseException:
            self.workers.release()
            raise

    def process_request_thread(self, request, client_address):
        try:
            super().process_request_thread(request, client_address)
        finally:
            self.workers.release()

    def handle_error(self, request, client_address):
        # Canceling a parallel download closes in-flight sockets by design.
        if isinstance(sys.exc_info()[1], (BrokenPipeError, ConnectionResetError, TimeoutError)):
            return
        super().handle_error(request, client_address)


if __name__ == '__main__':
    DownloadServer(('0.0.0.0', 25261), Handler).serve_forever()
