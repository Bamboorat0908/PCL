#!/usr/bin/env python3
"""Read-only HTTPS smoke checks, including actual download bytes and old routes."""
import argparse
import hashlib
import html.parser
import json
import pathlib
import socket
import ssl
import urllib.error
import urllib.parse
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class Assets(html.parser.HTMLParser):
    def __init__(self):
        super().__init__()
        self.urls = set()

    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        value = values.get('src') if tag in ('img', 'script', 'source') else values.get('href') if tag == 'link' else None
        if value:
            self.urls.add(value)


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--local-origin', action='store_true', help='Resolve only zhushu.homes to 127.0.0.1, preserving TLS hostname checks')
    parser.add_argument('--sha256', required=True)
    parser.add_argument('--size', required=True, type=int)
    parser.add_argument('--output', help='Optional local JSON verification receipt')
    options = parser.parse_args()
    if options.local_origin:
        resolve = socket.getaddrinfo

        def local_resolve(host, port, *args, **kwargs):
            return resolve('127.0.0.1' if host == 'zhushu.homes' else host, port, *args, **kwargs)

        socket.getaddrinfo = local_resolve
    origin = 'https://zhushu.homes'
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect(),
                                        urllib.request.HTTPSHandler(context=ssl.create_default_context()))

    def open_path(path, headers=None):
        browser_ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/157.0.0.0 Safari/537.36'
        request = urllib.request.Request(origin + path, headers={'User-Agent': browser_ua, **(headers or {})})
        try:
            return opener.open(request, timeout=25)
        except urllib.error.HTTPError as response:
            return response

    results = []
    with open_path('/BRCL?from=smoke') as response:
        location = urllib.parse.urlparse(response.headers.get('Location', ''))
        require(response.getcode() == 308 and location.path == '/BRCL/' and location.query == 'from=smoke', 'canonical redirect dropped its path or query')
    results.append('canonical 308 preserves query')
    with open_path('/BRCL/') as response:
        html = response.read().decode('utf-8')
        require(response.getcode() == 200 and 'BRCL' in html and 'text/html' in response.headers.get('Content-Type', ''), 'product page is not the expected HTML')
        require(response.headers.get('X-Content-Type-Options') == 'nosniff', 'HTML nosniff header missing')
        require("script-src 'self'" in response.headers.get('Content-Security-Policy', ''), 'HTML script policy missing')
    links = Assets()
    links.feed(html)
    checked_assets = []
    for value in sorted(links.urls):
        url = urllib.parse.urlparse(urllib.parse.urljoin(origin + '/BRCL/', value))
        if url.netloc != 'zhushu.homes' or not url.path.startswith('/BRCL/'):
            continue
        with open_path(url.path + ('?' + url.query if url.query else '')) as response:
            require(response.getcode() == 200, 'asset does not load: ' + url.path)
            response.read()
        checked_assets.append(url.path)
    results.append('HTML and referenced local assets load over verified TLS')
    with open_path('/BRCL/release.json') as response:
        metadata = json.loads(response.read().decode('utf-8-sig'))
        require(response.getcode() == 200 and 'no-store' in response.headers.get('Cache-Control', ''), 'release metadata is not fresh')
        require(metadata.get('sha256') == options.sha256.lower() and metadata.get('size', metadata.get('bytes')) == options.size, 'public metadata differs from verified artifact')
        require(metadata.get('download_url') == '/BRCL/download/BRCL.exe', 'public download URL differs')
    results.append('public release metadata matches verified file')
    digest = hashlib.sha256()
    size, prefix = 0, b''
    with open_path('/BRCL/download/BRCL.exe') as response:
        require(response.getcode() == 200, 'download is not 200')
        require(response.headers.get_content_type() == 'application/octet-stream', 'download MIME type is wrong')
        require('attachment' in response.headers.get('Content-Disposition', '') and 'BRCL.exe' in response.headers.get('Content-Disposition', ''), 'download attachment header missing')
        require(response.headers.get('X-Content-Type-Options') == 'nosniff', 'download nosniff header missing')
        while True:
            chunk = response.read(1024 * 1024)
            if not chunk:
                break
            if not prefix:
                prefix = chunk[:32]
            size += len(chunk)
            digest.update(chunk)
    require(size == options.size and digest.hexdigest() == options.sha256.lower(), 'full HTTPS download size/hash mismatch')
    results.append('full HTTPS executable matches exact size and SHA-256')
    with open_path('/BRCL/download/BRCL.exe', {'Range': 'bytes=0-31'}) as response:
        require(response.getcode() == 206 and response.headers.get('Content-Range') == 'bytes 0-31/' + str(options.size) and response.read() == prefix, 'download byte range is incorrect')
    with open_path('/BRCL/download/BRCL.exe', {'Range': 'bytes=' + str(options.size) + '-'}) as response:
        require(response.getcode() == 416, 'out-of-range download did not return 416')
    results.append('download supports exact byte ranges and rejects invalid ranges')
    for path in ('/BRCL/__not_present__', '/BRCL/download/', '/BRCL/download/other.exe'):
        with open_path(path) as response:
            require(response.getcode() == 404, 'nonpublic website path is accessible: ' + path)
    results.append('missing paths and download directory do not expose a listing')
    baselines = {}
    for path in ('/', '/BRS/', '/stock/', '/BRS/news/', '/BRS/news/api/news'):
        with open_path(path) as response:
            baselines[path] = response.getcode()
            require(response.getcode() == 200, 'existing route regressed: ' + path)
            response.read()
    results.append('blog, BRS, stock and news baseline routes remain 200')
    report = json.dumps({'passed': len(results), 'checks': results, 'assets': checked_assets,
                         'exe_bytes': size, 'exe_sha256': digest.hexdigest(), 'existing_routes': baselines}, ensure_ascii=False)
    if options.output:
        pathlib.Path(options.output).write_text(report + '\n', encoding='utf-8')
    print(report)


if __name__ == '__main__':
    main()
