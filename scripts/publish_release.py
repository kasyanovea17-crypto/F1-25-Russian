"""Publish only the verified v0.30 archive to this repository's GitHub release."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import re
import subprocess
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = 'kasyanovea17-crypto/F1-25-Russian'
TAG = 'v0.30'
ASSET_NAME = 'F1_25_RU_v0.30_text0.15.6.zip'
API = 'https://api.github.com/repos/' + REPOSITORY


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def request(method, url, token, body=None, content_type='application/json'):
    allowed = {'api.github.com', 'uploads.github.com'}
    parsed = urllib.parse.urlsplit(url)
    if parsed.scheme != 'https' or parsed.hostname not in allowed:
        raise ValueError('Unexpected API destination')
    if body is not None and not isinstance(body, bytes):
        body = json.dumps(body).encode('utf-8')
    headers = {'Authorization': 'Bearer ' + token, 'Accept': 'application/vnd.github+json',
               'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'F1-25-Russian-release',
               'Content-Type': content_type}
    req = urllib.request.Request(url, data=body, method=method, headers=headers)
    with urllib.request.urlopen(req, timeout=180) as response:
        return json.load(response)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publish', action='store_true')
    args = parser.parse_args()
    archive = ROOT / 'dist' / ASSET_NAME
    result = json.loads((ROOT / 'dist/release-ci-result.json').read_text('utf-8'))
    assert result['status'] == 'PASS' and result['checks'] == 88 and result['files'] == 28
    assert result['profile_checks'] == 44 and result['updater_checks'] == 239
    assert result['sha256'] == digest(archive) and result['bytes'] == archive.stat().st_size
    checksum = archive.with_suffix(archive.suffix + '.sha256')
    checksum.write_text(digest(archive) + '  ' + ASSET_NAME + '\n', encoding='ascii')
    notes = (ROOT / 'releases/v0.30.md').read_text('utf-8')
    notes += '\n### SHA-256\n```text\n' + checksum.read_text('ascii') + '```\n'
    plan = dict(repository=REPOSITORY, tag=TAG, sha256=digest(archive), bytes=archive.stat().st_size,
                assets=[archive.name, checksum.name])
    (ROOT / 'dist/publication-plan.json').write_text(json.dumps(plan, indent=2) + '\n', encoding='utf-8')
    if not args.publish:
        print('PUBLICATION_PLAN_PASS ' + json.dumps(plan))
        return
    if os.environ.get('GITHUB_REPOSITORY') != REPOSITORY:
        raise ValueError('Repository binding mismatch')
    commit = os.environ.get('GITHUB_SHA', '')
    if not re.fullmatch(r'[a-f0-9]{40}', commit):
        raise ValueError('Missing workflow commit')
    if subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip() != commit:
        raise ValueError('Checkout does not match workflow commit')
    token = os.environ['GH_TOKEN']
    # Never overwrite an existing public release or a draft for another commit.
    try:
        release = request('GET', API + '/releases/tags/' + TAG, token)
    except urllib.error.HTTPError as error:
        if error.code != 404:
            raise
        release = request('POST', API + '/releases', token, {
            'tag_name': TAG, 'target_commitish': commit, 'draft': True, 'prerelease': False,
            'name': 'F1 25 на русском — лаунчер 0.30 · кириллица и исправления', 'body': notes})
    if not release['draft'] or release['target_commitish'] != commit:
        raise ValueError('Existing release must be reviewed; publication stopped')
    release_url = API + '/releases/' + str(release['id'])
    known = {item['name']: item for item in release['assets']}
    for path in [archive, checksum]:
        expected = 'sha256:' + digest(path)
        if path.name in known:
            asset = known[path.name]
        else:
            upload = release['upload_url'].split('{', 1)[0] + '?name=' + urllib.parse.quote(path.name)
            asset = request('POST', upload, token, path.read_bytes(),
                            'application/zip' if path == archive else 'text/plain')
        if asset.get('state') != 'uploaded' or asset.get('size') != path.stat().st_size or asset.get('digest') != expected:
            raise ValueError('Uploaded asset did not match: ' + path.name)
    draft = request('GET', release_url, token)
    if set(item['name'] for item in draft['assets']) != {archive.name, checksum.name}:
        raise ValueError('Unexpected assets in draft')
    release = request('PATCH', release_url, token, {'draft': False, 'prerelease': False, 'make_latest': 'true', 'body': notes})
    final = request('GET', API + '/releases/tags/' + TAG, token)
    assert not final['draft'] and not final['prerelease']
    tag = request('GET', API + '/git/ref/tags/' + TAG, token)
    assert tag['object']['type'] == 'commit' and tag['object']['sha'] == commit
    record = dict(plan, release_id=final['id'], commit=commit, url=final['html_url'],
                  published_at=final['published_at'], assets=[{
                      key: item[key] for key in ['id', 'name', 'size', 'digest', 'browser_download_url']
                  } for item in final['assets']])
    (ROOT / 'dist/PUBLISHED_RELEASE.json').write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
    print('RELEASE_PUBLISHED ' + json.dumps(record))


if __name__ == '__main__':
    main()
