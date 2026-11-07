"""Exercise current bootstrap recovery and startup errors with synthetic resources only."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import stat
import struct
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def safe(path):
    path = Path(os.path.abspath(path))
    for parent in [path, *path.parents]:
        if os.path.lexists(parent):
            info = parent.lstat()
            if info.st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
                raise RuntimeError('Reparse output path rejected: ' + str(parent))
    return path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work', required=True, type=Path)
    args = parser.parse_args()
    work = safe(args.work)
    if work.exists():
        raise RuntimeError('Use a fresh work directory; prior evidence is retained')
    if not work.is_relative_to(ROOT):
        raise RuntimeError('Work directory must remain inside the checkout')
    work.mkdir(parents=True)
    commands = []

    def run(argv):
        completed = subprocess.run([str(x) for x in argv], stdin=subprocess.DEVNULL,
                                   capture_output=True, text=True, encoding='utf-8',
                                   errors='replace', timeout=120)
        commands.append(dict(argv=[str(x) for x in argv], stdin='empty',
                             stdout=completed.stdout, stderr=completed.stderr,
                             exit_status=completed.returncode))
        (work / 'COMMAND_RESULTS.json').write_text(json.dumps(commands, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        print(completed.stdout.rstrip(), flush=True)
        if completed.returncode:
            raise RuntimeError('Command failed: ' + completed.stderr)
        return completed

    csc = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    common = [csc, '/nologo', '/target:exe', '/platform:x64', '/optimize+', '/warn:4', '/warnaserror+']
    fixture = work / 'fixture'
    (fixture / 'payload').mkdir(parents=True)
    (fixture / 'assets').mkdir()
    run(common + ['/reference:System.Web.Extensions.dll', '/out:' + str(fixture / 'Launcher.exe'),
                  ROOT / 'single_exe/tests/FixtureLauncher.cs'])

    def section(tag, value):
        return tag.encode('ascii') + struct.pack('>I', len(value)) + value

    # Structural fixture, not game text: deterministic numbered keys and one ASCII string.
    count = 56134
    sida = b''.join(struct.pack('>II', n, 0) for n in range(count))
    sections = (section('HSHS', bytes(4)) + section('HSHT', bytes(4)) +
                b'SIDA' + struct.pack('>II', len(sida), count) + sida +
                section('SIDB', bytes(4)) + section('LNGB', b'fixture\0'))
    language = b'LNGT' + struct.pack('>I', 8 + len(sections)) + sections
    (fixture / 'payload/language.lng').write_bytes(language)
    language_hash = sha(fixture / 'payload/language.lng')
    text_manifest = dict(text_version='0.1', lng=language_hash, previous_lng=[],
                         payload={'language.lng': language_hash},
                         native_profiles=[dict(game_version='1.26', install_files=[
                             dict(payload='language.lng', sha256=language_hash)])])
    (fixture / 'manifest.json').write_text(json.dumps(text_manifest), encoding='utf-8')
    (fixture / 'assets/reference.txt').write_text('synthetic immutable nested file', encoding='utf-8')
    files = sorted(p for p in fixture.rglob('*') if p.is_file())
    archive = work / 'bundle.zip'
    with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED) as zipped:
        for file in files:
            zipped.write(file, file.relative_to(fixture).as_posix())
    manifest = dict(schema=1, version='0.33-startup-ci', entryPoint='Launcher.exe',
                    archiveSha256=sha(archive), totalBytes=sum(f.stat().st_size for f in files),
                    textUpdates=True, files=[dict(path=f.relative_to(fixture).as_posix(),
                        bytes=f.stat().st_size, sha256=sha(f)) for f in files])
    manifest_path = work / 'bundle.json'
    manifest_path.write_text(json.dumps(manifest), encoding='utf-8')
    pins = work / 'BundlePins.cs'
    pins.write_text('internal static class BundlePins { internal const string ManifestSha256="' +
                    sha(manifest_path) + '", ArchiveSha256="' + sha(archive) +
                    '", Version="0.33-startup-ci"; }\n', encoding='utf-8')
    bootstrap = ROOT / 'single_exe/source/Bootstrap.cs'
    contract = ROOT / 'single_exe/source/BundleContract.cs'
    diagnostics = ROOT / 'StartupDiagnostics.cs'
    test = ROOT / 'single_exe/tests/MissingLauncherTests.cs'
    recovery = work / 'MissingLauncherTests.exe'
    run(common + ['/define:FIXED', '/main:MissingLauncherTests',
                  '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll',
                  '/reference:System.Web.Extensions.dll', '/reference:System.IO.Compression.dll',
                  '/reference:System.IO.Compression.FileSystem.dll', '/out:' + str(recovery),
                  '/resource:' + str(archive) + ',F1RU.Bundle.zip',
                  '/resource:' + str(manifest_path) + ',F1RU.Bundle.json',
                  bootstrap, contract, diagnostics, pins, test])
    run([recovery, work / 'recovery', 'modified'])
    recovery_result = json.loads((work / 'recovery/RESULTS.json').read_text('utf-8'))
    assert recovery_result['status'] == 'PASS' and recovery_result['checks'] >= 60
    diagnostic_test = ROOT / 'tests/StartupDiagnosticsTests.cs'
    diagnostic_exe = work / 'StartupDiagnosticsTests.exe'
    run(common + ['/out:' + str(diagnostic_exe), diagnostics, diagnostic_test])
    output = run([diagnostic_exe]).stdout
    assert output.strip() == 'STARTUP_DIAGNOSTICS_PASS checks=20 filesystem_writes=0 protection_changes=0'
    report = dict(status='PASS', checks=recovery_result['checks'] + 20,
                  cache_recovery_checks=recovery_result['checks'], diagnostic_checks=20,
                  inputs={str(f.relative_to(ROOT)): sha(f) for f in [bootstrap, contract, diagnostics, test, diagnostic_test, Path(__file__)]},
                  synthetic_only=True, game_modified=False, registry_modified=False,
                  protection_changes=0, production_cache_modified=False)
    report_path = work / 'artifact.json'
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    assert json.loads(report_path.read_text('utf-8')) == report
    print('STARTUP_REGRESSIONS_PASS checks=' + str(report['checks']) + ' synthetic_only=true game_modified=false protection_changes=0')


if __name__ == '__main__':
    main()
