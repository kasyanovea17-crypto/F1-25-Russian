"""Portable v0.30 build tests; no installed game or external samples are used."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
sys.dont_write_bytecode = True
from lng import LNG
from verify_package import verify


def run(args):
    result = subprocess.run(list(map(str, args)), cwd=ROOT, check=True,
                            capture_output=True, encoding='utf-8', errors='replace')
    print(result.stdout.strip(), flush=True)
    return result.stdout


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--skip-build', action='store_true')
    args = parser.parse_args()
    windir = Path(os.environ.get('WINDIR', r'C:\Windows'))
    ps = windir / 'System32/WindowsPowerShell/v1.0/powershell.exe'
    if not args.skip_build:
        run([ps, '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ROOT / 'build.ps1'])
    package = ROOT / 'dist/F1_25_RU_v0.30'
    archive = ROOT / 'dist/F1_25_RU_v0.30_text0.15.6.zip'
    result = verify(archive)
    initial_hash = result['sha256']
    csc = windir / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    channel = json.loads((ROOT / 'updates/stable.json').read_text('utf-8-sig'))
    with tempfile.TemporaryDirectory(prefix='release-ci-', dir=ROOT / 'dist') as temp:
        temp = Path(temp).resolve()
        assert temp.is_relative_to((ROOT / 'dist').resolve())
        for name in ['GameTextProfileTests', 'UpdateTests']:
            run([csc, '/nologo', '/utf8output', '/warnaserror', '/target:exe',
                 '/reference:System.Web.Extensions.dll', '/out:' + str(temp / (name + '.exe')),
                 ROOT / (name + '.cs'), ROOT / 'Updater.cs',
                 ROOT / 'GameTextProfiles.cs', ROOT / 'TranslationVariants.cs'])
        profile = run([temp / 'GameTextProfileTests.exe', temp / 'profile-fixtures'])
        assert 'PROFILE_GATE_PASS checks=44' in profile, profile
        fixture = temp / 'update-fixtures'
        fixture.mkdir()
        parsed = LNG((package / 'payload/language.lng').read_bytes())
        next_text = parsed.replace({'lng_tooltip_rewind': parsed.values['lng_tooltip_rewind'] + ' (test)'})
        (fixture / 'next.lng').write_bytes(next_text)
        channel.update(version='0.15.7', file='0.15.7/language.lng',
                       sha256=hashlib.sha256(next_text).hexdigest(), bytes=len(next_text))
        (fixture / 'channel.json').write_text(json.dumps(channel, ensure_ascii=False), encoding='utf-8')
        updater = run([temp / 'UpdateTests.exe', fixture, package])
        assert 'UPDATE_TEST_PASS checks=239' in updater, updater
    assert hashlib.sha256(archive.read_bytes()).hexdigest() == initial_hash
    result.update(profile_checks=44, updater_checks=239, installed_game_written=False)
    (ROOT / 'dist/release-ci-result.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print('CI_VERIFY_PASS profile=44 updater=239 archive=' + str(result['checks']) + ' game_written=false')


if __name__ == '__main__':
    main()
