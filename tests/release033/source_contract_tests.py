"""Assert release033 scope and exercise its actual protected-file guard on pure fixtures."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import subprocess

ROOT = Path(__file__).resolve().parents[2]
OLD_ENGINE_NORMALIZED_SHA = 'd1ffd297abd0f96d2aaf4fc860477eb379c5fcb461571cf4641fab6c1e9a7ae7'
OLD_MANIFEST_CANONICAL_SHA = 'cb6b9c2c247e6580204496a97ee318f1464e087ac82cfbfc637dbca318e31dad'
STEAM_EXE = 'd873a1c8d2044cef2fd76c99c9d5a97649c5b815d055540995105f46619865ae'
EA_REVISIONS = ['7f18801344a49dff2b3bc9cd818ea1a0a6cc50b63d4bcfa68fd62047cadc7d55',
                '63304b515dc5bfd95d23756053b839cb720955ec820af12530f99fa6e6f9a0c6']


def digest(value):
    return hashlib.sha256(value).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work', required=True, type=Path)
    args = parser.parse_args()
    work = Path(os.path.abspath(args.work))
    if work.exists() or not work.is_relative_to(ROOT):
        raise RuntimeError('Use a fresh work directory inside the checkout')
    work.mkdir(parents=True)
    checks = []

    def check(value, label):
        if not value:
            raise AssertionError(label)
        checks.append(label)

    manifest_path = ROOT / 'manifest.base.json'
    manifest = json.loads(manifest_path.read_text('utf-8-sig'))
    check(manifest['version'] == '0.33', 'manifest version033')
    matching = [p for p in manifest['native_profiles'] if p['exe_sha256'] == STEAM_EXE]
    check(len(matching) == 1 and matching[0]['game_version'] == '1.26' and
          matching[0].get('platform', 'steam') == 'steam', 'one exact Steam126 profile')
    approved = matching[0]['protected']['EAAntiCheat.GameServiceLauncher.exe']
    check(all(approved.count(h) == 1 for h in EA_REVISIONS), 'two explicit audited component revisions')
    for profile in manifest['native_profiles']:
        if profile is matching[0]:
            continue
        protected = profile.get('protected', {}).get('EAAntiCheat.GameServiceLauncher.exe', [])
        if isinstance(protected, str):
            protected = [protected]
        check(not set(protected).intersection(EA_REVISIONS), 'other profile does not acquire added revisions ' + profile['exe_sha256'])
    # Reconstruct the prior public metadata; this forbids accidental lobby/product/physics edits.
    manifest['version'] = '0.32'
    for h in EA_REVISIONS:
        approved.remove(h)
    canonical = json.dumps(manifest, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode('utf-8')
    check(digest(canonical) == OLD_MANIFEST_CANONICAL_SHA, 'all other public manifest fields unchanged')

    engine_path = ROOT / 'Engine.ps1'
    engine = engine_path.read_text('utf-8-sig')
    old = "  if(!$profile.micro_compatibility -or !(ValidPublisher (Inside $p.Name) $profile.micro_compatibility.publisher_organization)){throw ('Подпись обновлённого файла не прошла проверку: '+$p.Name)}"
    new = "  if(!$profile.micro_compatibility){throw ('Файл не соответствует профилю совместимости; исходный файл сохранён: '+$p.Name)}\n  if(!(ValidPublisher (Inside $p.Name) $profile.micro_compatibility.publisher_organization)){throw ('Подпись обновлённого файла не прошла проверку: '+$p.Name)}"
    check(engine.count(new) == 1, 'exact diagnostic split present once')
    check(digest(engine.replace(new, old).encode('utf-8')) == OLD_ENGINE_NORMALIZED_SHA,
          'all other engine code and verification remain unchanged')
    begin = engine.index(' $protected=@{}\n')
    end = engine.index(' # Inventory only:', begin)
    guard = engine[begin:end]
    fixture = r'''$ErrorActionPreference='Stop'
$script:HashCalls=0;$script:PublisherCalls=0;$script:HeaderCalls=0
function Inside([string]$name){return $name}
function Hash([string]$name){$script:HashCalls++;if($script:Unknown){return ('b'*64)};return ('a'*64)}
function ValidPublisher([string]$file,[string]$organization){$script:PublisherCalls++;return $script:Publisher}
function HeaderCompatible($profile,[string]$file){$script:HeaderCalls++;return $script:Header}
$guard=[scriptblock]::Create(@'
''' + guard + r'''
'@)
$checks=0
foreach($name in @('F1_25.exe','EAAntiCheat.GameServiceLauncher.exe','EAAntiCheat.GameServiceLauncher.dll')){
 foreach($unknown in @($false,$true)){foreach($micro in @($false,$true)){foreach($publisher in @($false,$true)){foreach($header in @($false,$true)){
  $script:Unknown=$unknown;$script:Publisher=$publisher;$script:Header=$header
  $script:HashCalls=0;$script:PublisherCalls=0;$script:HeaderCalls=0
  $profile=[pscustomobject]@{protected=[pscustomobject]@{$name=@(('a'*64))};micro_compatibility=$null}
  if($micro){$profile.micro_compatibility=[pscustomobject]@{publisher_organization='Fixture Organization'}}
  $accepted=$false;$errorText=''
  try{& $guard | Out-Null;$accepted=$true}catch{$errorText=$_.Exception.Message}
  $wanted=(!$unknown -or ($micro -and $publisher -and ($name-ne'F1_25.exe'-or$header)))
  if($accepted-ne$wanted){throw ('Guard acceptance changed: '+$name+','+$unknown+','+$micro+','+$publisher+','+$header)}
  $checks++
  if($unknown-and!$micro){if($errorText-notlike'*профилю совместимости*'-or$script:PublisherCalls-ne0){throw 'Missing profile misreported or publisher called'};$checks++}
  if($unknown-and$micro-and!$publisher){if($errorText-notlike'*Подпись*'){throw 'Invalid signer diagnostic lost'};$checks++}
  if(!$unknown-and($script:PublisherCalls-ne0-or$script:HeaderCalls-ne0)){throw 'Known pinned file unnecessarily rejected'}
  if($script:HashCalls-ne1){throw 'Guard hash observation changed'}
 }}}}
}
Write-Output ('PROTECTED_GUARD_FIXTURES_PASS checks='+$checks+' matrix_cases=48 filesystem_writes=0 protection_changes=0')
'''
    ps1 = work / 'protected_guard_fixtures.ps1'
    ps1.write_text(fixture, encoding='utf-8-sig')
    powershell = Path(os.environ['WINDIR']) / 'System32/WindowsPowerShell/v1.0/powershell.exe'
    argv = [str(powershell), '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', str(ps1)]
    result = subprocess.run(argv, stdin=subprocess.DEVNULL, capture_output=True, text=True,
                            encoding='utf-8', errors='replace', timeout=60)
    check(result.returncode == 0 and 'PROTECTED_GUARD_FIXTURES_PASS checks=66 matrix_cases=48' in result.stdout,
          'actual guard pure fixture matrix')
    launcher = (ROOT / 'Launcher.cs').read_text('utf-8-sig')
    check('Text="F1 25 · На русском · 0.33 · новый интерфейс"' in launcher, 'UI release version')
    check('"Голос инженера · выбор озвучки"' in launcher and 'engineerVoiceButton.Click+=(a,e)=>ShowEngineerVoiceChild();' in launcher,
          'engineer choice exposed')
    child = launcher[launcher.index(' void ShowEngineerVoiceChild(){'):launcher.index('\n void RefreshVoice()', launcher.index(' void ShowEngineerVoiceChild(){'))]
    check('if(busy)return;' in child and
          'String.IsNullOrWhiteSpace(path.Text)||!Directory.Exists(path.Text.Trim())' in child and
          'ShowPage("settings")' in child and
          'catch(Exception ex)' in child and 'failed=true;' in child and
          'StartupDiagnostics.Message(ex)' in child and 'ShowPage("error")' in child and
          'finally{if(!failed)ShowPage("voice");}' in child,
          'engineer child busy/empty-path/error guards and conditional page restoration')
    check('if(args.Length==2&&args[0]=="--engineer-voice-self-test")return EngineerVoiceWindow.SelfTest' in launcher,
          'engineer automated test entry')
    check(launcher.count('StartupDiagnostics.Message(ex)') >= 3, 'typed startup and operation diagnostics integrated')
    report = dict(status='PASS', checks=len(checks) + 66, source_assertions=checks,
                  guard_fixture_matrix_cases=48, guard_checks=66, game_modified=False,
                  production_data_used=False, protection_changes=0,
                  command=dict(argv=argv, input='pure synthetic hash/publisher/header fixtures; stdin empty',
                               stdout=result.stdout, stderr=result.stderr, exit_status=result.returncode),
                  source_hashes={name: digest((ROOT / name).read_bytes()) for name in
                      ['Launcher.cs', 'Engine.ps1', 'manifest.base.json']})
    out = work / 'artifact.json'
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    check(json.loads(out.read_text('utf-8')) == report, 'result reopened')
    print(result.stdout.strip())
    print('SOURCE_CONTRACTS_PASS checks=' + str(report['checks']) + ' production_data_used=false game_modified=false protection_changes=0')


if __name__ == '__main__':
    main()
