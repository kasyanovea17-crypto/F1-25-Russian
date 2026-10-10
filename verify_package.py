"""Reopen a release ZIP and verify its allowlist, checksums and six LNG tables."""
from pathlib import Path
import argparse
import hashlib
import json
import sys
import zipfile

sys.dont_write_bytecode = True
from lng import LNG


def verify(archive):
    checks = 0

    def check(value, message):
        nonlocal checks
        if not value:
            raise ValueError(message)
        checks += 1

    prefix = 'F1_25_RU_v0.30/'
    with zipfile.ZipFile(archive) as z:
        check(z.testzip() is None, 'ZIP CRC')
        names = z.namelist()
        check(len(names) == len(set(names)), 'Duplicate ZIP entry')
        check(all(n.startswith(prefix) and '..' not in n.split('/') and '\\' not in n for n in names), 'ZIP paths')
        check(not any(n.endswith(('/F1_25.exe', '/game.dat')) or '/test-game' in n or '/evidence/' in n for n in names), 'Game fixture included')
        sums = {}
        for line in z.read(prefix + 'SHA256SUMS.txt').decode('utf-8-sig').splitlines():
            digest, name = line.split('  ', 1)
            check(name not in sums, 'Duplicate checksum')
            sums[name] = digest
            check(hashlib.sha256(z.read(prefix + name)).hexdigest() == digest, 'Checksum: ' + name)
        check(set(names) == {prefix + n for n in sums} | {prefix + 'SHA256SUMS.txt'}, 'Unlisted archive content')
        m = json.loads(z.read(prefix + 'manifest.json').decode('utf-8-sig'))
        check(m['version'] == '0.30' and m['language_records'] == 56134, 'Version contract')
        check(len(m['native_profiles']) == 4 and set(m['language_sets']) == {'1.18', '1.24'}, 'Profile contract')
        check(len(m['payload']) == 8, 'Payload count')
        for name, digest in m['payload'].items():
            check(hashlib.sha256(z.read(prefix + 'payload/' + name)).hexdigest() == digest, 'Payload: ' + name)
        sets = [('1.26', 56134, 'language.lng', m['translation_variants']['original_names']['payload'])]
        for version, count in [('1.18', 56439), ('1.24', 55991)]:
            s = m['language_sets'][version]
            check(s['records'] == count, 'Record contract: ' + version)
            sets.append((version, count, s['payload'], s['translation_variants']['original_names']['payload']))
        for version, count, russian, original_names in sets:
            a = LNG(z.read(prefix + 'payload/' + russian))
            b = LNG(z.read(prefix + 'payload/' + original_names))
            check(len(a.rows) == len(b.rows) == count, 'LNG count: ' + version)
            check([x[0] for x in a.rows] == [x[0] for x in b.rows], 'LNG key order: ' + version)
            for tag in (b'HSHS', b'HSHT', b'SIDB'):
                _, alen, apos = a.sections[tag]
                _, blen, bpos = b.sections[tag]
                check(a.data[apos:apos + alen] == b.data[bpos:bpos + blen], 'LNG lookup table: ' + version)
        check(z.read(prefix + 'UI_VERIFICATION.txt').decode('utf-8-sig').startswith('UI_TEST_PASS'), 'UI verification')
    digest = hashlib.sha256(Path(archive).read_bytes()).hexdigest()
    result = dict(status='PASS', checks=checks, files=len(names), sha256=digest, bytes=Path(archive).stat().st_size)
    print('ARCHIVE_VERIFY_PASS ' + json.dumps(result, ensure_ascii=False))
    return result


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('archive', type=Path)
    args = p.parse_args()
    verify(args.archive)
