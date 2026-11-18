"""Bind the static download information to the exact EXE shipped in dist/."""
import hashlib
import html
import json
import pathlib
import re

dist = pathlib.Path(__file__).resolve().parents[1] / 'dist'
metadata = json.loads((dist / 'release.json').read_text(encoding='utf-8-sig'))
executable = dist / 'download' / 'BRCL.exe'
metadata['size'] = executable.stat().st_size
metadata['sha256'] = hashlib.sha256(executable.read_bytes()).hexdigest()
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

changelog = dist.parents[2] / 'Plain Craft Launcher 2/Pages/PageSetup/Changelog.json'
entry = json.loads(changelog.read_text(encoding='utf-8-sig'))[0]
if entry['title'] != 'BRCL ' + metadata['display_version'] or entry['date'] != metadata['published_at']:
    raise ValueError('Launcher changelog and website version/date differ')
notes = entry['items']
metadata['release_notes'] = notes
if not isinstance(notes, list) or not notes or any(not isinstance(note, str) or not note.strip() for note in notes):
    raise ValueError('Release notes must contain nonempty text items')
notes_html = ''.join('<li>' + html.escape(note) + '</li>' for note in notes)
document, count = re.subn(r'(<ul\b[^>]*\bid="release-notes-list"[^>]*>).*?(</ul>)',
                          lambda match: match[1] + notes_html + match[2], document, flags=re.S)
if count != 1:
    raise ValueError('Expected one visible release-notes list')
document = re.sub(r'(<button\b[^>]*\bid="copy-sha"[^>]*?)\sdisabled(?=[ >])', r'\1', document)
(dist / 'index.html').write_text(document, encoding='utf-8')
(dist / 'release.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
text_notes = [entry['title'] + ' 更新日志', '发布日期：' + entry['date'], '内部版本：' + metadata['version'], '']
text_notes.extend(f'{index}. {note}' for index, note in enumerate(notes, 1))
text_notes.extend(['', '官网：https://zhushu.homes/BRCL/', '下载：https://zhushu.homes/BRCL/download/BRCL.exe',
                   'SHA-256：' + metadata['sha256']])
(dist / 'release-notes.txt').write_text('\n'.join(text_notes) + '\n', encoding='utf-8')
print('PASS static download fields and visible notes match the shipped release; usable before JavaScript or metadata fetch')
