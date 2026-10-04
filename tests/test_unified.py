"""Independent unified-launcher integration checks on isolated game copies.

Usage: python test_unified.py ABSOLUTE_PACKAGE_HOME --attempt unique_label
Fixtures are exclusively below packagehome/test-game-*. Existing fixtures are
never removed or reused. Received game executables are hashed/read, never run.
The baseline and standalone engines are copied into test-only driver harnesses.
"""
from pathlib import Path
import argparse
import hashlib
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import traceback

sys.dont_write_bytecode = True
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT.parent
OLD = WORK.parent / 'f1'
LEGACY = WORK / 'f1_legacy_support_20261003'
BASE = WORK / 'f1_translation_release/dist/F1_25_RU_v0.28'
AUDIT = WORK / 'f1_launcher_release_audit'
PS = Path(r'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe')
PYTHON = Path(sys.executable).resolve()
FONTS = ('fonts_japanese.erp', 'fonts_efigs_r_p.erp')
SENTINELS = ('EAAntiCheat.GameServiceLauncher.exe', 'EAAntiCheat.cfg')
SENTINEL_CONTENT = b'IMMUTABLE_TEST_SENTINEL_NOT_A_REAL_COMPONENT\n'


def sha(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def json_write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def snapshot(folder, include_backups=False):
    result = {}
    for file in sorted(Path(folder).rglob('*')):
        rel = file.relative_to(folder)
        if file.is_file() and (include_backups or not any(s.startswith('.f1ru') for s in rel.parts)):
            result[str(rel)] = sha(file)
    return result


def language_count(path):
    data = Path(path).read_bytes()
    assert data[:4] == b'LNGT' and struct.unpack_from('>I', data, 4)[0] == len(data)
    pos = 8
    for expected in (b'HSHS', b'HSHT', b'SIDA'):
        assert data[pos:pos + 4] == expected
        size = struct.unpack_from('>I', data, pos + 4)[0]
        if expected == b'SIDA':
            count = struct.unpack_from('>I', data, pos + 8)[0]
            assert size == 8 * count
            return count
        pos += 8 + size
    raise AssertionError('Missing SIDA')


class Integration:
    def __init__(self, package, attempt, selected):
        self.package = package.resolve()
        self.attempt = attempt
        self.selected = selected
        self.evidence = ROOT / 'evidence' / ('UNIFIED_TEST_' + attempt + '.json')
        self.manifest = read_json(self.package / 'manifest.json')
        self.inputs = {}
        self.records = []
        self.checks = []
        self.profiles = []
        self.fixture_notes = []
        self.input_hashes_preserved = None
        self.failure = None
        self.harness_source_sha256 = sha(Path(__file__))
        self.package_hashes = {str(f.relative_to(self.package)): sha(f)
                               for f in sorted(self.package.rglob('*')) if f.is_file()
                               and not any(s.startswith('test-game-') for s in f.relative_to(self.package).parts)}
        self.evidence.parent.mkdir(parents=True, exist_ok=True)
        assert not self.evidence.exists(), 'Evidence already exists; choose a new --attempt'

    def persist(self, status='running'):
        json_write(self.evidence, {
            'status': status, 'command': subprocess.list2cmdline([str(PYTHON), *sys.argv]),
            'package': str(self.package), 'attempt': self.attempt,
            'execution_order': self.selected, 'checks_passed': len(self.checks),
            'harness_source_sha256': self.harness_source_sha256,
            'checks': self.checks, 'records': self.records, 'profiles': self.profiles,
            'fixtures': self.fixture_notes, 'source_hashes_before': self.inputs,
            'source_hashes_preserved': self.input_hashes_preserved,
            'package_hashes_before': self.package_hashes,
            'received_exe_executed': False, 'real_game_read': False,
            'real_game_written': False, 'gameplay_tested': False,
            'failure': self.failure,
        })

    def check(self, condition, label):
        if not condition:
            raise AssertionError(label)
        self.checks.append(label)

    def remember(self, file):
        file = Path(file).resolve()
        digest = sha(file)
        if str(file) in self.inputs:
            self.check(self.inputs[str(file)] == digest, 'Stable reused input: ' + str(file))
        else:
            self.inputs[str(file)] = digest
        return digest

    def fixture_path(self, label, parent=None):
        parent = self.package if parent is None else Path(parent)
        path = parent / ('test-game-' + label + '-' + self.attempt)
        rel = path.resolve().relative_to(self.package)
        assert rel.parts[0].startswith('test-game-')
        assert not path.exists(), 'Preserve previous fixture: ' + str(path)
        return path

    def harness(self, source, label):
        """Copy just the trusted installer sources; no launcher/game exe execution."""
        dest = self.fixture_path('driver-' + label)
        dest.mkdir()
        for name in ('Engine.ps1', 'Compatibility.cs', 'manifest.json', 'ROLLBACK.sh'):
            original = source / name
            self.remember(original)
            shutil.copy2(original, dest / name)
        for file in sorted((source / 'payload').rglob('*')):
            if file.is_file():
                self.remember(file)
                target = dest / file.relative_to(source)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(file, target)
        return dest

    def command(self, label, args, fixture, expected=0):
        args = [str(a) for a in args]
        # Explicitly guard the only processes this integration harness may launch.
        self.check(Path(args[0]).resolve() in (PS.resolve(), PYTHON), 'Allowed test runner: ' + label)
        result = subprocess.run(args, capture_output=True, encoding='utf-8', errors='replace')
        row = {'label': label, 'argv': args, 'command': subprocess.list2cmdline(args),
               'input': str(fixture), 'stdout': result.stdout, 'stderr': result.stderr,
               'exit_status': result.returncode, 'expected_exit_status': expected}
        self.records.append(row)
        self.persist()
        print(label + ': ' + result.stdout.strip() + ' [exit ' + str(result.returncode) + ']', flush=True)
        self.check(result.returncode == expected, 'Exit status: ' + label + '\n' + json.dumps(row, ensure_ascii=False))
        return result

    def engine(self, label, action, fixture, variant='russian', expected=0, home=None):
        return self.command(label, [PS, '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
                                   (home or self.package) / 'Engine.ps1', '-Action', action,
                                   '-GamePath', fixture, '-Translation', variant, '-TestMode'], fixture, expected)

    def source_files(self, version):
        if version in ('1.24', '1.18'):
            src = WORK / ('f1_received_second_20261003' if version == '1.24' else 'f1_received_build_20261003')
            files = {'F1_25.exe': src / 'source/F1_25.exe', 'game.dat': src / 'source/game.dat'}
            files.update({str(Path('2025_asset_groups/ui_package') / name): src / 'additional_assets' / name for name in FONTS})
            standalone = LEGACY / ('build124' if version == '1.24' else 'build118') / ('F1_25_RU_' + version)
        else:
            client = version.split('-')[1]
            src = AUDIT / ('engine-' + client) / 'test-game'
            files = {'F1_25.exe': (src / 'F1_25.exe' if client == 'Steam'
                                  else OLD / 'ea-adaptation-20260921/source/F1_25.exe'),
                     'game.dat': OLD / ('launcher-v25-build/ORIGINAL.dat' if client == 'Steam'
                                       else 'ea-adaptation-20260921/source/game.dat')}
            files.update({str(Path('2025_asset_groups/ui_package') / name): src / '2025_asset_groups/ui_package' / name for name in FONTS})
            for name in SENTINELS:
                if (src / name).is_file():
                    files[name] = src / name
            standalone = None
        for file in files.values():
            self.remember(file)
        return files, standalone

    def seed(self, folder, files, sentinels=False):
        folder.mkdir()
        for rel, source in files.items():
            dest = folder / rel
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, dest)
        if sentinels:
            for name in SENTINELS:
                assert name not in files
                (folder / name).write_bytes(SENTINEL_CONTENT)
        return snapshot(folder)

    def resolve_profile(self, files, version):
        digest = sha(files['F1_25.exe'])
        matches = [p for p in self.manifest['native_profiles'] if p['exe_sha256'] == digest]
        self.check(len(matches) == 1, version + ': unique exact EXE profile')
        profile = matches[0]
        self.check(sha(files['game.dat']) == profile['dat_original'], version + ': baseline archive hash')
        for name, digest in profile['stock_files'].items():
            self.check(sha(files[name]) == digest, version + ': baseline stock font ' + name)
        for name, known in profile['protected'].items():
            self.check(sha(files[name]) in ([known] if isinstance(known, str) else known), version + ': baseline protected ' + name)
        selected = profile.get('language_set')
        language_set = self.manifest['language_sets'][selected] if selected else self.manifest
        records = language_set.get('records', language_set.get('language_records', 56134))
        russian_payload = language_set.get('payload', 'language.lng') if selected else 'language.lng'
        original = language_set['translation_variants']['original_names']
        variants = {'russian': (russian_payload, language_set['lng']),
                    'original_names': (original['payload'], original['sha256'])}
        for variant, (payload, digest) in variants.items():
            self.check(self.manifest['payload'][payload] == digest == sha(self.package / 'payload' / payload),
                       version + ': packaged selected-set hash ' + variant)
            self.check(language_count(self.package / 'payload' / payload) == records,
                       version + ': selected-set LNG records ' + variant)
        self.profiles.append({'version': version, 'exe_sha256': sha(files['F1_25.exe']),
                              'dat_original': profile['dat_original'], 'dat_installed': profile['dat_installed'],
                              'language_set': selected or '1.26', 'records': records, 'variants': variants,
                              'backup_dir': profile['backup_dir']})
        return profile, variants

    def installed(self, folder, profile, variants, variant, stock, label):
        self.check(sha(folder / 'game.dat') == profile['dat_installed'], label + ': installed archive')
        for rel in stock:
            if rel != 'game.dat':
                self.check(sha(folder / rel) == stock[rel], label + ': original/sentinel preserved ' + rel)
        for item in profile['install_files']:
            digest = variants[variant][1] if item['payload'] == 'language.lng' else self.manifest['payload'][item['payload']]
            self.check(sha(folder / item['target']) == digest, label + ': installed resource ' + item['target'])
        state = read_json(folder / profile['backup_dir'] / 'state.json')
        self.check(state['translation_variant'] == variant and state['language_sha256'] == variants[variant][1],
                   label + ': selected variant state')
        if state['version'] == self.manifest['version']:
            set_id = profile.get('language_set')
            text_set = self.manifest['language_sets'][set_id] if set_id else self.manifest
            count = text_set.get('records', text_set.get('language_records', 56134))
            text_version = (text_set['translation_variants']['original_names']['version']
                            if variant == 'original_names' else text_set.get('version') if set_id
                            else text_set['text_version'])
            self.check(state['game_version'] == (set_id or '1.26'), label + ': automatic game version state')
            self.check(state['language_records'] == count, label + ': selected record count state')
            self.check(state['text_version'] == text_version, label + ': selected text version state')
            self.check(state['update_channel'] == ('pinned' if set_id else 'stable'), label + ': version-safe update channel')
        self.check(sha(folder / profile['backup_dir'] / 'game.dat') == profile['dat_original'], label + ': preserved original backup')
        self.check(not list(folder.glob('.f1ru-transaction-*')), label + ': no pending transaction directories')
        self.check(not (folder / profile['backup_dir'] / 'pending.json').exists(), label + ': no pending journal')

    def restored(self, folder, profile, stock, label):
        self.check(snapshot(folder) == stock, label + ': byte-exact original files and added resources absent')
        self.check(sha(folder / 'game.dat') == profile['dat_original'], label + ': original archive hash')
        self.check(not list(folder.glob('.f1ru-transaction-*')), label + ': no pending transactions')
        self.check(read_json(folder / profile['backup_dir'] / 'state.json')['status'] == 'restore', label + ': restore state')

    def rollback(self, label, fixture):
        script = self.package / 'ROLLBACK.sh'
        os.chmod(script, 0o755)
        self.command(label, [PYTHON, script, fixture, '--test'], fixture)

    def unknown_checks(self, version, folder, profile, files, stock):
        critical = profile.get('micro_compatibility', {}).get('critical_ranges', [])
        offset = critical[0]['offset'] + 32 if critical else 128
        # Mutate Authenticode-covered bytes, not optional certificate-table
        # padding at EOF which may legitimately retain a valid publisher signature.
        for name, pos, label in [('game.dat', offset, 'ARCHIVE'), ('F1_25.exe', 128, 'EXE'),
                                 (r'2025_asset_groups\ui_package\fonts_japanese.erp', 0, 'FONT')]:
            file = folder / name
            with file.open('r+b') as stream:
                stream.seek(pos, 2 if pos < 0 else 0)
                actual = stream.tell()
                byte = stream.read(1)
                stream.seek(actual)
                stream.write(bytes([byte[0] ^ 1]))
            before = snapshot(folder, True)
            self.engine(version + '_REJECT_UNKNOWN_' + label, 'install', folder, expected=1)
            self.check(snapshot(folder, True) == before, version + ': rejected unknown ' + label + ' without any file change')
            shutil.copy2(files[name], file)
        self.restored(folder, profile, stock, version + '_AFTER_REJECTIONS')

    def cross_version_checks(self, version, folder, profile, variants, stock):
        active = [i for i in profile['install_files'] if i['payload'] == 'language.lng']
        assert active
        # Inject a valid text belonging to each other supported game version.
        wrong_sets = [s for s in ('1.26', '1.24', '1.18') if s != version.split('-')[0]]
        for wrong in wrong_sets:
            if wrong == '1.26':
                source_name = 'language.lng'
            else:
                source_name = self.manifest['language_sets'][wrong]['payload']
            target = folder / active[0]['target']
            shutil.copy2(self.package / 'payload' / source_name, target)
            before = snapshot(folder, True)
            self.engine(version + '_REJECT_TEXT_FOR_' + wrong, 'install', folder, expected=1)
            self.check(snapshot(folder, True) == before, version + ': cross-version ' + wrong + ' text rejected unchanged')
            shutil.copy2(self.package / 'payload' / variants['russian'][0], target)
        self.installed(folder, profile, variants, 'russian', stock, version + '_AFTER_CROSS_VERSION_REJECTION')

    def run_version(self, version):
        files, standalone = self.source_files(version)
        profile, variants = self.resolve_profile(files, version)
        label = version.replace('.', '').lower()
        primary = self.fixture_path(label + '-primary')
        rollback = self.fixture_path(label + '-rollback')
        stock = self.seed(primary, files)
        self.fixture_notes.append({'version': version, 'primary': str(primary), 'rollback': str(rollback), 'state': 'running'})
        if standalone:
            # The baseline fixture is below its copied Engine's TestMode root.
            baseline_home = self.harness(BASE, label + '-baseline028')
            baseline = self.fixture_path('baseline-input', baseline_home)
            baseline_stock = self.seed(baseline, files)
            before = snapshot(baseline, True)
            self.engine(version + '_BASELINE_028_REJECT', 'prepare', baseline, expected=1, home=baseline_home)
            self.check(snapshot(baseline, True) == before == baseline_stock, version + ': baseline rejection byte-exact')
        self.engine(version + '_PREPARE', 'prepare', primary)
        self.check(snapshot(primary) == stock, version + ': prepare leaves original game files unchanged')
        self.engine(version + '_INSTALL_RUSSIAN', 'install', primary)
        self.installed(primary, profile, variants, 'russian', stock, version + '_RUSSIAN')
        self.engine(version + '_SWITCH_ORIGINAL_NAMES', 'install', primary, 'original_names')
        self.installed(primary, profile, variants, 'original_names', stock, version + '_ORIGINAL_NAMES')
        self.engine(version + '_REINSTALL_ORIGINAL_NAMES', 'install', primary, 'original_names')
        self.installed(primary, profile, variants, 'original_names', stock, version + '_REINSTALL')
        self.engine(version + '_SWITCH_BACK_RUSSIAN', 'install', primary)
        self.installed(primary, profile, variants, 'russian', stock, version + '_SWITCH_BACK')
        self.cross_version_checks(version, primary, profile, variants, stock)
        rollback_stock = self.seed(rollback, files, sentinels=standalone is not None)
        self.engine(version + '_ROLLBACK_COPY_INSTALL', 'install', rollback, 'original_names')
        self.installed(rollback, profile, variants, 'original_names', rollback_stock, version + '_ROLLBACK_COPY')
        self.rollback(version + '_ROLLBACK', rollback)
        self.restored(rollback, profile, rollback_stock, version + '_ROLLBACK')
        self.unknown_checks(version, rollback, profile, files, rollback_stock)
        if standalone:
            standalone_manifest = read_json(standalone / 'manifest.json')
            standalone_profile = standalone_manifest['native_profiles'][0]
            self.check(standalone_profile['backup_dir'] == profile['backup_dir'], version + ': legacy backup identity retained')
            self.check(standalone_profile['dat_installed'] == profile['dat_installed'], version + ': legacy archive patch identity retained')
            driver = self.harness(standalone, label + '-standalone028')
            migration = self.fixture_path('migration-input', driver)
            migration_stock = self.seed(migration, files, sentinels=True)
            self.engine(version + '_STANDALONE_028_INSTALL', 'install', migration, 'original_names', home=driver)
            self.installed(migration, profile, variants, 'original_names', migration_stock, version + '_STANDALONE_BASELINE')
            baseline_state = read_json(migration / profile['backup_dir'] / 'state.json')
            self.check(baseline_state['version'] == standalone_manifest['version'], version + ': genuine standalone state')
            self.engine(version + '_MIGRATION_PREPARE', 'prepare', migration)
            self.engine(version + '_MIGRATION_INSTALL', 'install', migration)
            self.installed(migration, profile, variants, 'russian', migration_stock, version + '_MIGRATION')
            self.check(read_json(migration / profile['backup_dir'] / 'state.json')['version'] == self.manifest['version'],
                       version + ': state upgraded to unified launcher version')
            self.engine(version + '_MIGRATION_SWITCH', 'install', migration, 'original_names')
            self.installed(migration, profile, variants, 'original_names', migration_stock, version + '_MIGRATION_SWITCH')
            self.rollback(version + '_MIGRATION_ROLLBACK', migration)
            self.restored(migration, profile, migration_stock, version + '_MIGRATION_ROLLBACK')
            self.fixture_notes[-1]['migration'] = str(migration)
            self.fixture_notes[-1]['migration_final'] = 'byte-exact stock; optional-presence sentinels preserved'
        self.engine(version + '_FINAL_PREPARE', 'prepare', primary)
        self.installed(primary, profile, variants, 'russian', stock, version + '_FINAL')
        self.fixture_notes[-1]['state'] = 'primary Russian installed; separate rollback copy exact stock'
        self.persist()

    def preservation(self):
        self.check(sha(Path(__file__)) == self.harness_source_sha256, 'Test harness source unchanged during this run')
        remaining = {str(Path(f)): sha(Path(f)) for f in self.inputs}
        self.input_hashes_preserved = remaining == self.inputs
        self.check(self.input_hashes_preserved, 'All original inputs/reference packages unchanged')
        after = {name: sha(self.package / name) for name in self.package_hashes}
        self.check(after == self.package_hashes, 'Unified package bytes unchanged by integration tests')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('package', type=Path)
    parser.add_argument('--attempt', required=True)
    parser.add_argument('--profiles', nargs='+', choices=['1.24', '1.18', '1.26-EA', '1.26-Steam'],
                        default=['1.24', '1.18', '1.26-EA', '1.26-Steam'])
    args = parser.parse_args()
    assert re.fullmatch(r'[a-zA-Z0-9_-]+', args.attempt), 'Use a simple nonempty unique label'
    assert args.package.is_absolute() and args.package.is_dir()
    assert args.package.resolve().is_relative_to(ROOT), 'Package must be inside unified workspace'
    assert (args.package / 'Engine.ps1').is_file()
    test = Integration(args.package, args.attempt, args.profiles)
    try:
        for version in args.profiles:
            test.run_version(version)
        test.preservation()
        test.persist('passed')
        # Reopen the actual evidence artifact before reporting it.
        result = read_json(test.evidence)
        assert result['status'] == 'passed' and result['checks_passed'] == len(test.checks)
        print('UNIFIED_TEST_PASS checks=' + str(len(test.checks)) + ' profiles=' + ','.join(args.profiles)
              + ' rollback=byte_exact source_hashes_preserved=true real_game_written=false', flush=True)
        print('EVIDENCE=' + str(test.evidence), flush=True)
    except BaseException as exc:
        test.failure = {'type': type(exc).__name__, 'message': str(exc), 'traceback': traceback.format_exc()}
        try:
            test.preservation()
        except BaseException as preservation_error:
            test.failure['preservation_error'] = repr(preservation_error)
        test.persist('failed')
        raise


if __name__ == '__main__':
    main()
