"""Real Max runtime install/restore tests on owned inert fixtures; no game writes."""
import argparse, hashlib, json, os, pathlib, shutil, subprocess, sys, uuid

def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    p=argparse.ArgumentParser();p.add_argument('--source',type=pathlib.Path,required=True);p.add_argument('--output',type=pathlib.Path,required=True);p.add_argument('--original',type=pathlib.Path,required=True);a=p.parse_args()
    before='bf42b070e1277f50646a627a72e5845b672e257863eee38e311ffd705ebe163f'
    after='b3202523d49c3cd173a7dd4172b78f071fd5e0b9967bdb59cbcaae9719dd50a5'
    assert sha(a.original)==before
    assert not a.output.exists(), 'Preserve prior evidence'
    a.output.mkdir(parents=True)
    ps=pathlib.Path(os.environ['WINDIR'])/'System32/WindowsPowerShell/v1.0/powershell.exe'
    script=a.source/'audio/MaxVoice.ps1';events=[]
    def fixture(name,marker=True):
        root=a.output/name;game=root/'game';bank=game/'audio/2025/speech/en_drivers.nfs';bank.parent.mkdir(parents=True)
        shutil.copy2(a.original,bank);(game/'F1_25.exe').write_bytes(b'INERT MAX VOICE TEST FIXTURE - NEVER EXECUTE\n' if marker else b'NOT AN ALLOWED FIXTURE')
        return root,game,bank
    core_only=a.output/'core-only-package';core_only.mkdir();shutil.copy2(script,core_only/'MaxVoice.ps1');shutil.copy2(a.source/'audio/MaxVoiceHost.ps1',core_only/'MaxVoiceHost.ps1')
    def call(f,action,exit_code,status,fail=None,package=None):
        root,game,bank=f
        cmd=[str(ps),'-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',str(script if package is None else package/'MaxVoice.ps1'),'-Action',action,'-GamePath',str(game),'-PackageRoot',str(a.source/'audio' if package is None else package),'-FixtureRoot',str(root)]
        if fail:cmd+=['-FixtureFailPoint',fail]
        result=subprocess.run(cmd,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
        output=result.stdout.decode('utf-8-sig').strip();data=json.loads(output)
        events.append({'command':subprocess.list2cmdline(cmd),'input':str(bank),'stdout':output,'stderr':result.stderr.decode('utf-8-sig'),'exit':result.returncode})
        (a.output/'COMMANDS.json').write_text(json.dumps(events,ensure_ascii=False,indent=2),encoding='utf-8')
        assert result.returncode==exit_code,(action,output,result.returncode)
        assert data['status']==status,(action,data)
        return data
    installed=fixture('installed-retained');call(installed,'status',0,'ready');call(installed,'install',0,'installed');assert sha(installed[2])==after
    call(installed,'verify',0,'installed');call(installed,'install',0,'installed')
    rollback=fixture('rollback-copy');call(rollback,'install',0,'installed');call(rollback,'status',0,'installed',package=core_only);call(rollback,'restore',0,'restored',package=core_only);assert sha(rollback[2])==before
    call(rollback,'restore',0,'restored',package=core_only)
    for fail in ('AfterJournal','AfterStage','AfterReplace'):
        f=fixture('fault-'+fail);call(f,'install',1,'error',fail);assert sha(f[2])==before
    f=fixture('bad-marker',False);call(f,'install',1,'error');assert sha(f[2])==before and not (f[0]/'state').exists()
    f=fixture('running');(f[0]/'game.running').write_text('inert test marker');r=call(f,'status',0,'ready');assert r['gameRunning'] and not r['canInstall'];call(f,'install',1,'error');assert sha(f[2])==before
    assert sha(a.original)==before and sha(installed[2])==after
    report={'pass':True,'sourceSha256':sha(script),'events':events,'originalSourceUnchanged':True,'installedCopyRetained':str(installed[2]),'installedSha256':after,'rollbackCopy':str(rollback[2]),'rollbackSha256':before,'realGameWrites':0,'uacInvoked':False,'securitySettingsWrites':0}
    (a.output/'RESULTS.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print('MAX_TRANSACTION_PASS calls=%d install_verified=true rollback_verified=true failure_points=3 core_only_restore=true real_game_writes=0'%len(events))

if __name__=='__main__':main()
