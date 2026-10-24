"""Build and verify the public single EXE without touching an installed game."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT));sys.dont_write_bytecode=True
from lng import LNG
from verify_package import verify

def run(argv):
    p=subprocess.run(list(map(str,argv)),cwd=ROOT,capture_output=True,encoding='utf-8',errors='replace')
    print(p.stdout.strip(),flush=True)
    if p.returncode:
        print(p.stderr,flush=True)
        raise RuntimeError('Check failed: '+subprocess.list2cmdline(list(map(str,argv))))
    return p.stdout

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--skip-build',action='store_true');a=parser.parse_args()
    ps=Path(os.environ.get('WINDIR',r'C:\Windows'))/'System32/WindowsPowerShell/v1.0/powershell.exe'
    csc=Path(os.environ.get('WINDIR',r'C:\Windows'))/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    if not a.skip_build:run([ps,'-NoProfile','-ExecutionPolicy','Bypass','-File',ROOT/'build.ps1'])
    package=ROOT/'dist/F1_25_RU_v0.31';archive=ROOT/'dist/F1_25_RU_v0.31_package.zip';exe=ROOT/'dist/F1_25_RU_v0.31.exe'
    archive_result=verify(archive)
    reports=ROOT/'dist/ci';reports.mkdir(exist_ok=True)
    counts={}
    for flag,output,key,prefix in [('--self-test','UI.txt','ui_checks','UI_TEST_PASS'),('--speech-self-test','SPEECH_UI.txt','speech_ui_checks','SPEECH_UI_PASS'),('--max-voice-self-test','MAX_UI.txt','max_ui_checks','MAX_VOICE_UI_PASS')]:
        run([exe,flag,reports/output]);text=(reports/output).read_text(encoding='utf-8-sig')
        assert text.startswith(prefix),text
        counts[key]=int(re.search(r'checks=(\d+)',text).group(1))
    run([exe,'--bundle-self-test',reports/'BUNDLE.json'])
    bundle=json.loads((reports/'BUNDLE.json').read_text('utf-8-sig'));assert bundle['status']=='PASS' and bundle['textUpdatesPreserved'] is True
    run([ps,'-NoProfile','-ExecutionPolicy','Bypass','-File',ROOT/'scripts/test_speech_release.ps1','-RepositoryRoot',ROOT,'-ReportPath',reports/'SPEECH_BACKEND.json'])
    speech=json.loads((reports/'SPEECH_BACKEND.json').read_text('utf-8-sig'));assert speech['status']=='PASS' and speech['count']==42,speech
    run([sys.executable,'-B',ROOT/'audio/test_patch.py','--report',reports/'MAX_PATCH.json'])
    patch=json.loads((reports/'MAX_PATCH.json').read_text());assert patch['status']=='PASS' and patch['checks']==2344
    with tempfile.TemporaryDirectory(prefix='profiles-',dir=reports) as temp:
        temp=Path(temp)
        for name in ['GameTextProfileTests','UpdateTests']:
            run([csc,'/nologo','/utf8output','/warnaserror','/target:exe','/reference:System.Web.Extensions.dll','/out:'+str(temp/(name+'.exe')),ROOT/(name+'.cs'),ROOT/'Updater.cs',ROOT/'GameTextProfiles.cs',ROOT/'TranslationVariants.cs'])
        profile=run([temp/'GameTextProfileTests.exe',temp/'profiles']);assert 'PROFILE_GATE_PASS checks=44' in profile
        fixture=temp/'updates';fixture.mkdir();parsed=LNG((package/'payload/language.lng').read_bytes());next_text=parsed.replace({'lng_tooltip_rewind':parsed.values['lng_tooltip_rewind']+' (test)'})
        (fixture/'next.lng').write_bytes(next_text);channel=json.loads((ROOT/'updates/stable.json').read_text('utf-8-sig'));channel.update(version='0.15.7',file='0.15.7/language.lng',sha256=hashlib.sha256(next_text).hexdigest(),bytes=len(next_text));(fixture/'channel.json').write_text(json.dumps(channel),encoding='utf-8')
        updater=run([temp/'UpdateTests.exe',fixture,package]);assert 'UPDATE_TEST_PASS checks=239' in updater
    run([ps,'-NoProfile','-ExecutionPolicy','Bypass','-File',ROOT/'single_exe/source/Test-SingleExe.ps1','-PackageDirectory',ROOT/'dist/bundle-v0.31','-WorkDirectory',reports/'single-exe-tests','-Version','0.31-ci','-UpdaterSourceDirectory',ROOT])
    single=json.loads((reports/'single-exe-tests/results/CI_RESULTS.json').read_text('utf-8-sig'));assert single['status']=='PASS' and single['checks']==50 and single['actualUpdaterIntegration'] is True
    result=dict(status='PASS',version='0.31',**counts,speech_tests=42,max_patch_checks=2344,profile_checks=44,updater_checks=239,single_exe_checks=50,single_exe_verified=True,actual_updater_integration=True,archive_checks=archive_result['checks'],files=archive_result['files'],sha256=hashlib.sha256(exe.read_bytes()).hexdigest(),bytes=exe.stat().st_size,installed_game_written=False,registry_written=False)
    (ROOT/'dist/release-ci-result.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print('CI_VERIFY_PASS '+json.dumps(result,separators=(',',':')))

if __name__=='__main__':main()
