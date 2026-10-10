"""Read-only final fixture inventory: optional component absence and sentinels."""
from pathlib import Path
import argparse
import hashlib
import json
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
NAMES = ('EAAntiCheat.GameServiceLauncher.exe', 'EAAntiCheat.cfg')
SENTINEL = hashlib.sha256(b'IMMUTABLE_TEST_SENTINEL_NOT_A_REAL_COMPONENT\n').hexdigest()


def digest(path):
    if not path.exists():
        return None
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('matrix_evidence', type=Path)
    args = parser.parse_args()
    report = json.loads(args.matrix_evidence.read_text(encoding='utf-8'))
    assert report['status'] == 'passed'
    records = []
    for fixture in report['fixtures']:
        version = fixture['version']
        for role in ('primary', 'rollback', 'migration'):
            if role not in fixture:
                continue
            folder = Path(fixture[role])
            assert folder.resolve().is_relative_to(ROOT)
            for name in NAMES:
                if version in ('1.18', '1.24'):
                    expected = None if role == 'primary' else SENTINEL
                else:
                    source = Path(r'C:\Program Files (x86)\Steam\steamapps\common\F1 25') / name
                    expected = digest(source)
                actual = digest(folder / name)
                row = {'version': version, 'fixture_role': role, 'file': str(folder / name),
                       'expected_sha256_or_absent': expected, 'actual_sha256_or_absent': actual,
                       'pass': expected == actual}
                records.append(row)
    assert len(records) == 24 and all(r['pass'] for r in records), records
    result = {'command': subprocess.list2cmdline([sys.executable, *sys.argv]),
              'input': str(args.matrix_evidence.resolve()), 'checks': len(records),
              'status': 'passed', 'records': records, 'game_files_written': False,
              'stdout': 'OPTIONAL_PRESENCE_PASS checks=24\n', 'exit_status': 0}
    output = ROOT / 'evidence' / ('OPTIONAL_PRESENCE_' + report['attempt'] + '.json')
    if output.exists():
        assert json.loads(output.read_text(encoding='utf-8')) == result
    else:
        output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    assert json.loads(output.read_text(encoding='utf-8')) == result
    print(result['stdout'], end='')


if __name__ == '__main__':
    main()
