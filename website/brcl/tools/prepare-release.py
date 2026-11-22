"""Bind the static download information to the exact EXE shipped in dist/."""
import argparse
from datetime import date
import hashlib
import html
import json
import pathlib
import re

dist = pathlib.Path(__file__).resolve().parents[1] / 'dist'
parser = argparse.ArgumentParser()
parser.add_argument('--release-record', type=pathlib.Path)
args = parser.parse_args()
metadata = json.loads((dist / 'release.json').read_text(encoding='utf-8-sig'))
executable = dist / 'download' / 'BRCL.exe'
changelog = dist.parents[2] / 'Plain Craft Launcher 2/Pages/PageSetup/Changelog.json'
entries = json.loads(changelog.read_text(encoding='utf-8-sig'))
if not isinstance(entries, list) or not entries:
    raise ValueError('Changelog needs at least one release')
previous_version = None
for entry in entries:
    version = entry['title']
    if not re.fullmatch(r'[0-9]+(?:\.[0-9]+){2}', version):
        raise ValueError('Changelog headings must use three-part version numbers')
    numeric_version = tuple(map(int, version.split('.')))
    if previous_version is not None and numeric_version >= previous_version:
        raise ValueError('Changelog versions must be unique and ordered newest first')
    previous_version = numeric_version
    if date.fromisoformat(entry['date']).isoformat() != entry['date']:
        raise ValueError(f'Invalid release date for {version}')
    if not entry['sections']:
        raise ValueError(f'Missing sections for {version}')
    for section in entry['sections']:
        if not section['title'].strip() or not section['items']:
            raise ValueError(f'Empty changelog section for {version}')
        for item in section['items']:
            if any(not isinstance(item[field], str) or not item[field].strip()
                   for field in ('summary', 'detail')):
                raise ValueError(f'Empty changelog item for {version}')
entry = entries[0]
metadata['size'] = executable.stat().st_size
metadata['sha256'] = hashlib.sha256(executable.read_bytes()).hexdigest()
if args.release_record:
    record = json.loads(args.release_record.read_text(encoding='utf-8-sig'))
    if record['exe_sha256'] != metadata['sha256'] or record['exe_bytes'] != metadata['size']:
        raise ValueError('Website EXE differs from the test and build record')
    metadata.update(display_version=record['display_version'], version=record['revision'],
                    published_at=entry['date'], site_release=entry['date'], build_date=entry['date'],
                    git_commit=record['git_commit'], test_report_sha256=record['test_report_sha256'])
metadata['download_url'] = '/BRCL/download/BRCL.exe'
label = metadata['display_version'] + ' · 官网构建 ' + metadata['site_release'].replace('-', '.')
values = {
    'release-label': label,
    'release-version': metadata['display_version'],
    'release-size': '{:.2f} MiB'.format(metadata['size'] / 1048576),
    'release-sha': metadata['sha256'].upper(),
}
document = (dist / 'index.html').read_text(encoding='utf-8')
for identifier, value in values.items():
    pattern = r'(<(?:p|dd|code)\b[^>]*\bid="' + identifier + r'"[^>]*>).*?(</(?:p|dd|code)>)'
    document, count = re.subn(pattern, lambda match: match[1] + html.escape(value) + match[2], document, flags=re.S)
    if count != 1:
        raise ValueError('Expected one download field: ' + identifier)

if entry['title'] != metadata['display_version'] or entry['date'] != metadata['published_at']:
    raise ValueError('Launcher changelog and website version/date differ')
metadata['release_notes'] = [
    item['summary'] + '。' + item['detail']
    for section in entry['sections'] for item in section['items']
]


def render_release(release, current=False):
    version = html.escape(release['title'])
    release_date = release['date']
    time = f'<time datetime="{release_date}">{release_date}</time>'
    if current:
        parts = [f'<section class="release-note-version" id="release-current">',
                 f'<h4 class="release-note-heading"><span>{version}</span>{time}</h4>']
    else:
        parts = [f'<details class="release-history-version" id="version-{version}">',
                 f'<summary><span>{version}</span>{time}</summary>']
    for section in release['sections']:
        parts.append(f'<div class="release-note-group"><h5>{html.escape(section["title"])}</h5><ul>')
        for item in section['items']:
            summary = html.escape(item['summary'])
            detail = html.escape(item['detail'])
            parts.append(f'<li><strong>{summary}</strong>。{detail}</li>')
        parts.append('</ul></div>')
    parts.append('</section>' if current else '</details>')
    return '\n'.join(parts)


for region, markup in (
    ('release-current', render_release(entry, current=True)),
    ('release-history', '\n'.join(render_release(release) for release in entries[1:])),
):
    start, end = f'<!-- {region}:start -->', f'<!-- {region}:end -->'
    document, count = re.subn(
        re.escape(start) + r'.*?' + re.escape(end),
        lambda _: f'{start}\n{markup}\n{end}',
        document, flags=re.S,
    )
    if count != 1:
        raise ValueError(f'Expected one changelog region: {region}')
document = re.sub(r'(<button\b[^>]*\bid="copy-sha"[^>]*?)\sdisabled(?=[ >])', r'\1', document)
(dist / 'index.html').write_text(document, encoding='utf-8')
(dist / 'release.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
text_notes = [entry['title'], '发布日期：' + entry['date'], '内部版本：' + metadata['version']]
for section in entry['sections']:
    text_notes.extend(['', section['title']])
    text_notes.extend(f'- {item["summary"]}。{item["detail"]}' for item in section['items'])
text_notes.extend(['', '官网：https://zhushu.homes/BRCL/', '下载：https://zhushu.homes/BRCL/download/BRCL.exe',
                   'SHA-256：' + metadata['sha256']])
(dist / 'release-notes.txt').write_text('\n'.join(text_notes) + '\n', encoding='utf-8')
print('PASS static download fields and visible notes match the shipped release; usable before JavaScript or metadata fetch')
