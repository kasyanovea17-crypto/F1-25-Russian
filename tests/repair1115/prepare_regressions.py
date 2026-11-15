"""Run the real text engine on bounded synthetic files, never on a real game."""
from pathlib import Path
import argparse, ctypes, hashlib, json, os, shutil, subprocess, sys, uuid
ROOT=Path(__file__).resolve().parents[2]
WORK=ROOT.parent
PS=Path(os.environ['WINDIR'])/'System32/WindowsPowerShell/v1.0/powershell.exe'

def sha(data): return hashlib.sha256(data).hexdigest()
def snapshot(root):
 return {p.relative_to(root).as_posix(): ('directory' if p.is_dir() else sha(p.read_bytes())) for p in sorted(root.rglob('*'))}

def fixture(destination,engine):
 destination.mkdir(parents=True)
 shutil.copy2(engine,destination/'Engine.ps1')
 g=destination/'test-game';g.mkdir()
 def save(base,name,data):
  p=base/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data);return sha(data)
 exe=save(g,'F1_25.exe',b'FIXTURE NOT EXECUTABLE\n')
 guard=save(g,'EAAntiCheat.GameServiceLauncher.exe',b'IMMUTABLE FIXTURE\n')
 original=b'0123456789ABCDEF0123456789ABCDEF'; changed=original[:8]+b'RU'+original[10:]
 save(g,'game.dat',original)
 stock=save(g,'2025_asset_groups/ui_package/fonts_japanese.erp',b'ORIGINAL FONT\n')
 lng=save(destination,'payload/language.lng',b'RUSSIAN TEXT FIXTURE\n')
 font=save(destination,'payload/fonts_russian.erp',b'RUSSIAN FONT FIXTURE\n')
 profile={'exe_sha256':exe,'game_version':'1.26','steam_build':'synthetic', 'backup_dir':'.f1ru-v25-'+sha(original),'dat_original':sha(original),'dat_installed':sha(changed),'accepted_dat':[], 'patches':[{'offset':8,'before':original[8:10].hex(),'after':b'RU'.hex()}], 'protected':{'F1_25.exe':[exe],'EAAntiCheat.GameServiceLauncher.exe':[guard]}, 'stock_files':{'2025_asset_groups\\ui_package\\fonts_japanese.erp':stock},'install_files':[{'target':'localisation\\2025_russian\\language_eng.lng','payload':'language.lng','sha256':lng},{'target':'localisation\\2025_russian\\language_jap.lng','payload':'language.lng','sha256':lng},{'target':'2025_asset_groups\\ui_package\\fonts_russian.erp','payload':'fonts_russian.erp','sha256':font}]}
 m={'version':'0.33.1','text_version':'0.15.6','lng':lng,'previous_lng':[],'language_records':56134,'font_modified':sha(b'OLD MODIFIED FONT'), 'payload':{'language.lng':lng,'fonts_russian.erp':font},'native_profiles':[profile]}
 (destination/'manifest.json').write_text(json.dumps(m),encoding='utf-8')
 return g,m,original,changed

def main():
 ap=argparse.ArgumentParser();ap.add_argument('--label',required=True);a=ap.parse_args()
 work=WORK/('prepare-test-'+a.label)
 if work.exists(): raise RuntimeError('Use a fresh label; evidence is preserved')
 work.mkdir();results=[];checks=[]
 def run(argv):
  cp=subprocess.run([str(v) for v in argv],capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=90)
  results.append({'command':[str(v) for v in argv],'stdin':'empty','stdout':cp.stdout,'stderr':cp.stderr,'exit_status':cp.returncode})
  (work/'COMMANDS.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
  return cp
 def check(ok,label):
  if not ok: raise AssertionError(label)
  checks.append(label)
 def engine(home,g,action,extra=()):
  return run([PS,'-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',home/'Engine.ps1','-Action',action,'-GamePath',g,'-TestMode',*extra])
 def deny(g,enabled):
  assert g.is_relative_to(work) and g.name=='test-game'
  args=['icacls.exe',g,'/deny','*S-1-1-0:(WD,AD)'] if enabled else ['icacls.exe',g,'/remove:d','*S-1-1-0']
  cp=run(args);check(cp.returncode==0,'fixture ACL '+str(enabled))
 for kind in ('baseline','source'):
  h=work/kind;g,m,original,changed=fixture(h,WORK/kind/'Engine.ps1')
  deny(g,True)
  try:
   before=snapshot(g);cp=engine(h,g,'prepare');after=snapshot(g)
   check(after==before,kind+' denied-folder inspection preserves all files')
   if kind=='baseline':check(cp.returncode==1 and 'CreateDirectory' in cp.stdout,'baseline reproduces denied CreateDirectory')
   else:check(cp.returncode==0 and 'PREPARE PASS' in cp.stdout,'modified prepare works with no create permissions')
  finally:deny(g,False)
  if kind=='baseline':continue
  before=snapshot(g);cp=engine(h,g,'prepare');check(cp.returncode==0 and snapshot(g)==before,'prepare creates zero backup files/directories')
  cp=engine(h,g,'install');check(cp.returncode==0 and 'INSTALL PASS' in cp.stdout,'install transaction succeeds')
  check((g/'game.dat').read_bytes()==changed,'install changes intended resource')
  before=snapshot(g);cp=engine(h,g,'prepare');check(cp.returncode==0 and snapshot(g)==before,'prepare existing lock read only')
  # Existing exclusive writer lock is respected without creating a new lock.
  lock=g/m['native_profiles'][0]['backup_dir']/'operation.lock'
  kernel=ctypes.WinDLL('kernel32',use_last_error=True)
  kernel.CreateFileW.argtypes=[ctypes.c_wchar_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p];kernel.CreateFileW.restype=ctypes.c_void_p
  kernel.CloseHandle.argtypes=[ctypes.c_void_p];kernel.CloseHandle.restype=ctypes.c_int
  handle=kernel.CreateFileW(str(lock),0x80000000,0,None,3,0,None)
  check(handle!=ctypes.c_void_p(-1).value,'fixture exclusive writer lock opened')
  try:
   before=(g/'game.dat').read_bytes();cp=engine(h,g,'prepare');check(cp.returncode==1 and (g/'game.dat').read_bytes()==before,'prepare rejects concurrent operation without writes')
  finally:kernel.CloseHandle(handle)
  cp=engine(h,g,'restore');check(cp.returncode==0 and 'RESTORE PASS' in cp.stdout,'restore transaction succeeds')
  check((g/'game.dat').read_bytes()==original,'restore exact original bytes')
  check(not (g/'localisation/2025_russian/language_eng.lng').exists(),'restore removes only installed text')
  # A valid interrupted transaction is inspected but not recovered by prepare.
  profile=m['native_profiles'][0];backup=g/profile['backup_dir'];txn='.f1ru-transaction-'+uuid.uuid4().hex;(g/txn).mkdir()
  names=['game.dat',*profile['stock_files'],*[f['target'] for f in profile['install_files']],profile['backup_dir']+'\\state.json'];entries=[]
  for n,name in enumerate(names):
   path=g/name;exists=path.is_file();old=sha(path.read_bytes()) if exists else None
   if exists:shutil.copy2(path,g/txn/('snapshot-'+str(n)))
   if name=='game.dat':new=sha(changed)
   elif name in profile['stock_files']:new=profile['stock_files'][name]
   elif name.endswith('state.json'):new=sha(b'NEW STATE')
   else:new=next(f['sha256'] for f in profile['install_files'] if f['target']==name)
   entries.append({'target':name,'snapshot':'snapshot-'+str(n),'existed':exists,'before':old,'after':new})
  j={'schema':1,'root':str(g),'backup_dir':profile['backup_dir'],'transaction':txn,'action':'install','entries':entries}
  (backup/'pending.json').write_text(json.dumps(j),encoding='utf-8');(g/'game.dat').write_bytes(changed)
  before=snapshot(g);cp=engine(h,g,'prepare')
  check(cp.returncode==3 and '[RECOVERY_REQUIRED]' in cp.stdout,'prepare gives explicit recovery status')
  check(snapshot(g)==before,'pending journal and partial files left untouched')
  cp=engine(h,g,'restore');check(cp.returncode==0 and 'RECOVERY PASS' in cp.stdout and 'RESTORE PASS' in cp.stdout,'explicit restore performs pending recovery then restore')
  check((g/'game.dat').read_bytes()==original and not (backup/'pending.json').exists(),'recovery restores original and clears committed journal')
  # All existing executable/publisher/hash guards remain unchanged in source.
  previous=(WORK/'baseline/Engine.ps1').read_text('utf-8-sig');current=(ROOT/'Engine.ps1').read_text('utf-8-sig')
  guard=lambda text:text[text.index(' $protected=@{}'):text.index(' # Inventory only:')].replace('Emit (','Write-Output (')
  check(guard(previous)==guard(current),'complete executable and publisher guard unchanged')
  check('Get-Process -Name F1_25' in current and current.count('AssertClosed')==previous.count('AssertClosed'),'running game guard retained')
  # Result protocol: invalid destination must fail before first game write.
  before=snapshot(g);bad=work/'not-an-owned-result.json';cp=engine(h,g,'install',['-ResultPath',bad])
  check(cp.returncode!=0 and snapshot(g)==before and not bad.exists(),'arbitrary elevated output path rejected before write')
  temp=Path(os.environ['TEMP'])/('F1RU-Text-'+uuid.uuid4().hex);temp.mkdir()
  try:
   result=temp/'result.json';cp=engine(h,g,'prepare',['-ResultPath',result]);report=json.loads(result.read_text('utf-8-sig'))
   check(cp.returncode==0 and report['exitCode']==0 and report['action']=='prepare' and report['gamePath']==str(g) and 'PREPARE PASS' in report['output'],'result protocol correlates action path exit and output')
   result.unlink()
  finally:temp.rmdir()
 report={'checks':checks,'commands':results,'source_sha256':sha((ROOT/'Engine.ps1').read_bytes()),'gameplay_tested':False,'real_game_writes':0,'security_settings_writes':0,'fixture_acl_restored':True}
 (work/'RESULT.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
 print('PREPARE_REGRESSIONS_PASS checks='+str(len(checks))+' baseline_access_denied_reproduced=true readonly_prepare=true pending_preserved=true install_restore=true game_writes=0')
 print(work)
if __name__=='__main__':main()
