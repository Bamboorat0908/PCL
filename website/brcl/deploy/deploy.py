#!/usr/bin/env python3
"""Activate a verified BRCL static release on the existing zhushu.homes nginx host.

Run as root on the target host. Authentication is outside this script.
The archive contains only the public site and download/BRCL.exe.
"""
import argparse
import hashlib
import json
import os
import pathlib
import re
import shutil
import subprocess
import tarfile
import tempfile

ROOT = pathlib.Path('/var/www/brcl')
VHOST = pathlib.Path('/etc/nginx/sites-available/mcstatus')
SNIPPET = pathlib.Path('/etc/nginx/snippets/brcl-site.conf')
BACKUPS = pathlib.Path('/var/backups/brcl-site')
INCLUDE = '    include snippets/brcl-site.conf;'
ANCHOR = '    include snippets/brs-news.conf;'

NGINX = '''# BRCL product website: isolated static deployment.
location = /BRCL { return 308 /BRCL/$is_args$args; }

location = /BRCL/download/BRCL.exe {
    alias /var/www/brcl/current/download/BRCL.exe;
    types { }
    default_type application/octet-stream;
    sendfile on;
    max_ranges 1;
    include snippets/security-headers.conf;
    add_header Content-Disposition 'attachment; filename="BRCL.exe"';
    add_header Cache-Control "no-store" always;
}
location ^~ /BRCL/download/ { return 404; }

location = /BRCL/release.json {
    alias /var/www/brcl/current/release.json;
    default_type application/json;
    include snippets/security-headers.conf;
    add_header Cache-Control "no-store" always;
}

location ^~ /BRCL/assets/ {
    alias /var/www/brcl/current/assets/;
    autoindex off;
    include snippets/security-headers.conf;
    expires 1d;
}

location ^~ /BRCL/ {
    alias /var/www/brcl/current/;
    index index.html;
    autoindex off;
    include snippets/security-headers.conf;
    add_header Cache-Control "no-cache" always;
    add_header Content-Security-Policy "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'; upgrade-insecure-requests" always;
}
'''


def sha256(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def atomic_write(path, data, mode=0o644):
    fd, temporary = tempfile.mkstemp(prefix=path.name + '.', dir=str(path.parent))
    try:
        with os.fdopen(fd, 'wb') as stream:
            stream.write(data)
        os.chmod(temporary, mode)
        os.replace(temporary, str(path))
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def current_target():
    current = ROOT / 'current'
    if current.is_symlink():
        return os.readlink(str(current))
    if current.exists():
        raise RuntimeError('current exists and is not a release symlink')
    return None


def point_current(target):
    current = ROOT / 'current'
    temporary = ROOT / 'current.new'
    if temporary.exists() or temporary.is_symlink():
        temporary.unlink()
    if target is None:
        if current.is_symlink():
            current.unlink()
        return
    os.symlink(str(target), str(temporary))
    os.replace(str(temporary), str(current))


def command(*args):
    return subprocess.run(args, check=True, stdout=subprocess.PIPE,
                          stderr=subprocess.STDOUT, universal_newlines=True).stdout.strip()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--archive', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--exe-sha', required=True)
    parser.add_argument('--exe-size', required=True, type=int)
    options = parser.parse_args()
    if os.geteuid() != 0:
        raise RuntimeError('root is required for nginx activation')
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]{1,90}', options.release):
        raise ValueError('invalid release identifier')
    if not re.fullmatch(r'[a-fA-F0-9]{64}', options.exe_sha):
        raise ValueError('invalid executable hash')
    ROOT.mkdir(mode=0o755, parents=True, exist_ok=True)
    releases = ROOT / 'releases'
    releases.mkdir(mode=0o755, exist_ok=True)
    release = releases / options.release
    if release.exists():
        raise RuntimeError('release directory already exists; use a unique release identifier')
    old_target = current_target()
    vhost_before = VHOST.read_bytes()
    text = vhost_before.decode('utf-8')
    if INCLUDE not in text:
        if text.count(ANCHOR) != 1:
            raise RuntimeError('the expected HTTPS include anchor is not unique')
        if re.search(r'location\s+[^\n{]*\/BRCL', text):
            raise RuntimeError('an unrelated BRCL location already exists')
        text = text.replace(ANCHOR, ANCHOR + '\n' + INCLUDE, 1)

    release.mkdir(mode=0o755)
    with tarfile.open(options.archive, 'r:gz') as archive:
        for item in archive.getmembers():
            path = pathlib.PurePosixPath(item.name)
            if (path.is_absolute() or '..' in path.parts or
                    any(part.startswith('.') for part in path.parts) or
                    not (item.isfile() or item.isdir())):
                raise RuntimeError('archive contains an unsupported member: ' + item.name)
        archive.extractall(str(release))
    for directory, subdirs, files in os.walk(str(release)):
        os.chmod(directory, 0o755)
        for filename in files:
            os.chmod(os.path.join(directory, filename), 0o644)
    executable = release / 'download' / 'BRCL.exe'
    if not (release / 'index.html').is_file():
        raise RuntimeError('index.html is missing')
    if executable.stat().st_size != options.exe_size or sha256(executable) != options.exe_sha.lower():
        raise RuntimeError('the prepared executable does not match its verified size/hash')
    metadata = json.loads((release / 'release.json').read_text(encoding='utf-8-sig'))
    if metadata.get('sha256', '').lower() != options.exe_sha.lower():
        raise RuntimeError('public release metadata hash differs from the executable')
    if metadata.get('size', metadata.get('bytes')) != options.exe_size:
        raise RuntimeError('public release metadata size differs from the executable')
    if metadata.get('download_url') != '/BRCL/download/BRCL.exe':
        raise RuntimeError('public release metadata download URL is not the stable HTTPS route')

    backup = BACKUPS / options.release
    backup.mkdir(mode=0o700, parents=True)
    shutil.copy2(str(VHOST), str(backup / 'mcstatus.conf'))
    os.chmod(str(backup / 'mcstatus.conf'), 0o600)
    snippet_before = SNIPPET.read_bytes() if SNIPPET.exists() else None
    if snippet_before is not None:
        atomic_write(backup / 'brcl-site.conf', snippet_before, 0o600)
    receipt = {'release': options.release, 'release_directory': str(release),
               'previous_target': old_target, 'vhost_before_sha256': sha256(VHOST),
               'exe_sha256': options.exe_sha.lower(), 'exe_bytes': options.exe_size,
               'url': 'https://zhushu.homes/BRCL/', 'backup': str(backup)}
    atomic_write(backup / 'receipt.json', json.dumps(receipt, indent=2).encode('utf-8'), 0o600)
    try:
        atomic_write(SNIPPET, NGINX.encode('utf-8'))
        atomic_write(VHOST, text.encode('utf-8'), VHOST.stat().st_mode & 0o777)
        point_current(release)
        test = command('nginx', '-t')
        command('systemctl', 'reload', 'nginx')
    except Exception:
        atomic_write(VHOST, vhost_before)
        if snippet_before is None:
            if SNIPPET.exists():
                SNIPPET.unlink()
        else:
            atomic_write(SNIPPET, snippet_before)
        point_current(old_target)
        command('nginx', '-t')
        command('systemctl', 'reload', 'nginx')
        raise
    receipt['nginx_test'] = test
    receipt['vhost_after_sha256'] = sha256(VHOST)
    atomic_write(backup / 'receipt.json', json.dumps(receipt, indent=2).encode('utf-8'), 0o600)
    print(json.dumps(receipt, ensure_ascii=False))


if __name__ == '__main__':
    main()
