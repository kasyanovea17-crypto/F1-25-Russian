"""Windows integration tests using public synthetic banks, never an installed game."""
from pathlib import Path
import argparse,hashlib,json,os,subprocess,sys,tempfile
sys.path.insert(0,str(Path(__file__).resolve().parent))
import make_engineer_fixture as fixture

def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def need(ok,msg):
    if not ok:raise AssertionError(msg)

def run(package,out):
    package=package.resolve();out=out.resolve();out.mkdir(parents=True,exist_ok=True)
    need(not (out/'voice-tests.json').exists(),'Use a fresh output directory')
    ps=Path(os.environ['WINDIR'])/'System32/WindowsPowerShell/v1.0/powershell.exe'
    engine=package/'audio/engineer/EngineerVoice.ps1';payload=package/'audio/engineer/payload'
    banks=('en_engineer1_startup.nfs','en_engineer1.nfs');checks=[];receipts=[]
    def test(ok,label):need(ok,label);checks.append(label)
    def create(name,invalid=False):
        root=out/name;root.mkdir(); game=root/'game'
        for b in banks:fixture.build(payload,root,b,invalid)
        (game/'F1_25.exe').write_bytes(b'INERT ENGINEER VOICE TEST FIXTURE - NEVER EXECUTE\n');(game/'unrelated-mod.txt').write_bytes(b'preserve-me\x00\x11\xff')
        return root
    def hashes(root):return {b:sha(root/'game/audio/2025/speech'/b) for b in banks}
    def action(root,mode,code=0,fail='None'):
        args=[str(ps),'-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',str(engine),'-PackageRoot',str(engine.parent),'-GamePath',str(root/'game'),'-FixtureRoot',str(root),'-Action',mode,'-FixtureFailPoint',fail]
        p=subprocess.run(args,capture_output=True,encoding='utf-8',errors='replace',timeout=180)
        receipts.append(dict(action=mode,fixture=root.name,failpoint=fail,exit_status=p.returncode,stdout=p.stdout,stderr=p.stderr))
        need(p.returncode==code,f'{root.name}/{mode}/{fail}: exit {p.returncode}, expected{code}: {p.stdout} {p.stderr}')
        need(not p.stderr.strip(),'Unexpected stderr: '+p.stderr)
        d=json.loads(p.stdout.strip().lstrip('\ufeff'));need(d['schema']==1 and d['engineerSlots']==1596 and d['testOnly'] is True,'Result schema differs');return d
    root=create('roundtrip');original=hashes(root);unrelated=sha(root/'game/unrelated-mod.txt');exe=sha(root/'game/F1_25.exe')
    s=action(root,'status');test(s['selection']=='english' and s['canApply'] and not s['canRestore'],'original status')
    test(not (root/'state').exists(),'status writes no state')
    s=action(root,'russian');ru=hashes(root);test(s['selection']=='russian' and s['installed'] and s['canRestore'],'install Russian')
    test(ru!=original,'Russian modifies selected banks')
    test(sum(x['russian'] for x in s['banks'])==1596,'all1596 Russian payloads verified')
    s=action(root,'verify');test(s['selection']=='russian','verify installed Russian')
    s=action(root,'english');test(s['selection']=='english' and hashes(root)==original,'English removes Russian including previously-installed set')
    test(sum(x['english'] for x in s['banks'])==1596,'all1596 English payloads verified')
    s=action(root,'russian');test(hashes(root)==ru,'language roundtrip is byte-stable')
    s=action(root,'restore');test(s['selection']=='english' and hashes(root)==original and not s['canRestore'],'copy rollback restores exact previous banks')
    test(sha(root/'game/unrelated-mod.txt')==unrelated and sha(root/'game/F1_25.exe')==exe,'unrelated files and EXE unchanged')
    s=action(root,'english');test(s['code']=='already_selected' and hashes(root)==original,'already-selected language is no-op')
    (root/'game.running').write_bytes(b'closed-game guard fixture')
    s=action(root,'status');test(s['gameRunning'] and not s['canApply'] and not s['canRestore'],'running state disables mutations')
    s=action(root,'russian',1);test(hashes(root)==original,'running game blocks bank writes')
    (root/'game.running').unlink()
    for point in ('AfterJournal','AfterStage','AfterFirstReplace','AfterBothReplace'):
        s=action(root,'russian',1,point)
        test(hashes(root)==original,'atomic two-bank recovery '+point)
        states=list((root/'state/installations').glob('*/state.json'));need(len(states)==1,'Fixture state absent')
        test(json.loads(states[0].read_text('utf-8-sig'))['status']=='RESTORED','recovery journal '+point)
    empty=create('no-owned-backup');before=hashes(empty);s=action(empty,'restore',2)
    test(s['code']=='no_owned_backup' and hashes(empty)==before,'no backup is not fabricated')
    unknown=create('unknown-selected',True);before=hashes(unknown);s=action(unknown,'russian',1)
    test(not s['canApply'] and hashes(unknown)==before,'unknown selected recording fails closed')
    bogus=create('not-inert');(bogus/'game/F1_25.exe').write_bytes(b'MZ-not-the-exact-fixture-marker');before=hashes(bogus);s=action(bogus,'russian',1)
    test(hashes(bogus)==before and 'инертный' in s['message'],'fixture mode rejects nonfixture EXE')
    external=create('external-change');action(external,'russian');first=external/'game/audio/2025/speech'/banks[0]
    first.write_bytes(first.read_bytes()+b'external-change');before=hashes(external);s=action(external,'restore',1)
    test(hashes(external)==before,'restore refuses externally-modified bank without clobber')
    forged=create('forged-backup');action(forged,'russian');states=list((forged/'state/installations').glob('*/state.json'));state=json.loads(states[0].read_text('utf-8-sig'))
    backup=states[0].parent/'transactions'/state['transaction']/('before-'+banks[0]);backup.write_bytes(b'not-a-NeFS-bank');state['banks'][0]['beforeHash']=sha(backup);states[0].write_text(json.dumps(state),'utf-8')
    before=hashes(forged);s=action(forged,'restore',1);test(hashes(forged)==before,'restore validates backup structure even if state digest was edited')
    result=dict(status='PASS',checks=len(checks),labels=checks,receipts=receipts,real_game_writes=False,game_execution=False,registry_writes=False,
                baseline_banks=original,russian_banks=ru,rollback_exact=True,unselected_payloads_verified_by_engine=True)
    report=out/'voice-tests.json';report.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n','utf-8');need(json.loads(report.read_text('utf-8'))==result,'Report reopen differs')
    print('ENGINEER_VOICE_INTEGRATION_PASS checks='+str(len(checks))+' selected=1596 languages=2 rollback_exact=true unknown_selected_blocked=true unselected_preserved=true real_game_writes=false')
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--package',type=Path,default=Path(__file__).resolve().parents[2]);p.add_argument('--out',type=Path)
    a=p.parse_args();out=a.out or Path(tempfile.mkdtemp(prefix='f1-engineer-ci-'));run(a.package,out)
