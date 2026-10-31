"""Build/test the explicit v0.32 source package; never starts an installed game."""
from pathlib import Path
import argparse,hashlib,json,os,re,subprocess,sys,uuid,zipfile
ROOT=Path(__file__).resolve().parents[1]
VERSION='0.32';PACKAGE=ROOT/'dist/F1_25_RU_v0.32';EXE=ROOT/'dist/F1_25_RU_v0.32.exe'
REPORTS=ROOT/'dist/ci-v0.32';TESTS=ROOT/'tests/release032'
CSC=Path(os.environ.get('WINDIR',r'C:\Windows'))/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
PS=Path(os.environ.get('WINDIR',r'C:\Windows'))/'System32/WindowsPowerShell/v1.0/powershell.exe'
SOURCES=('Launcher.cs','GameLocation.cs','LaunchContext.cs','SpeechUI.cs','MaxVoiceUI.cs','TranslationVariants.cs','GameTextProfiles.cs','Updater.cs','Compatibility.cs')
COMMANDS=[]
sys.stdout.reconfigure(encoding='utf-8');sys.stderr.reconfigure(encoding='utf-8')
def digest(path):
 with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
def need(ok,message):
 if not ok:raise RuntimeError(message)
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def run(label,args,result=None):
 argv=list(map(str,args));p=subprocess.run(argv,cwd=ROOT,stdin=subprocess.DEVNULL,capture_output=True,encoding='utf-8-sig',errors='replace',env=dict(os.environ,PYTHONUTF8='1',PYTHONIOENCODING='utf-8'),creationflags=subprocess.CREATE_NO_WINDOW)
 record=dict(label=label,argv=argv,stdin='empty',stdout=p.stdout,stderr=p.stderr,exit_status=p.returncode)
 if result is not None and result.exists():record['literal_result']=result.read_text(encoding='utf-8-sig')
 COMMANDS.append(record);REPORTS.mkdir(parents=True,exist_ok=True)
 (REPORTS/'COMMANDS.json').write_text(json.dumps(COMMANDS,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 print(label+': '+record.get('literal_result',p.stdout).strip(),flush=True)
 need(p.returncode==0,label+' failed: '+p.stdout+p.stderr)
 return record.get('literal_result',p.stdout)
def compile_test(name,sources,target='exe'):
 output=PACKAGE/(name+('.dll' if target=='library' else '.exe'))
 run('COMPILE_'+name,[CSC,'/nologo','/utf8output','/target:'+target,'/platform:x64','/optimize+','/warn:4','/warnaserror+','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/out:'+str(output)]+list(sources));return output
def hook(exe,flag,name):
 output=REPORTS/(name+('.json' if flag in ('--bundle-self-test','--launch-context-self-test') else '.txt'))
 return output,run(name,[exe,flag,output],output)
def validate_contract():
 m=read(PACKAGE/'manifest.json');need(m['version']==VERSION,'Manifest version mismatch')
 need(m['text_version']=='0.15.6' and m['translation_variants']['original_names']['version']=='0.15.6.1','Text versions changed')
 need(set(p.get('game_version','1.26') for p in m['native_profiles'])=={'1.18','1.20','1.24','1.26'},'Profile set changed')
 legacy=[p for p in m['native_profiles'] if p.get('game_version')=='1.20']
 need(len(legacy)==1 and legacy[0].get('language_set')=='1.20' and not legacy[0].get('micro_compatibility'),'Exact 1.20 profile missing')
 need(m['language_sets']['1.20']['records']==55991 and m['language_sets']['1.20']['version']=='0.15.6.120','Pinned 1.20 text changed')
 for name,value in m['payload'].items():need(digest(PACKAGE/'payload'/name)==value,'Payload hash mismatch: '+name)
 channel=read(ROOT/'updates/stable.json');need(channel['version']=='0.15.6' and channel['sha256']==m['lng'],'Stable channel changed')
 def strings(value):
  if isinstance(value,str):yield value
  elif isinstance(value,list):
   for item in value:yield from strings(item)
  elif isinstance(value,dict):
   for item in value.values():yield from strings(item)
 for name in ('assets/redesign/PROVENANCE.json','audit/TEXT_CHECKS_1.20.json'):
  need(not any(re.search(r'[A-Za-z]:[\\/]',s) for s in strings(read(PACKAGE/name))),'Private absolute path in '+name)
 return m
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--skip-build',action='store_true');args=parser.parse_args()
 REPORTS.mkdir(parents=True,exist_ok=True);need(not EXE.exists(),'Preserve existing EXE; use a fresh checkout')
 if not args.skip_build:run('STAGE',[PS,'-NoProfile','-ExecutionPolicy','Bypass','-File',ROOT/'build.ps1','-PackageOnly'])
 need((PACKAGE/'Launcher.exe').is_file(),'Package not staged');m=validate_contract();counts={};native=[]
 for flag,name,key,count in [('--self-test','UI','ui_checks',187),('--speech-self-test','SPEECH_UI','speech_ui_checks',48),('--max-voice-self-test','MAX_UI','max_ui_checks',36)]:
  _,text=hook(PACKAGE/'Launcher.exe',flag,'NATIVE_'+name);match=re.search(r'checks=(\d+)',text)
  need(match and int(match[1])==count,'Unexpected '+name+' result');counts[key]=count;native.append(text.strip())
 profile=compile_test('ReleaseProfileTests',[TESTS/'Profile120GateTests.cs',ROOT/'Updater.cs',ROOT/'GameTextProfiles.cs',ROOT/'TranslationVariants.cs'])
 text=run('PROFILE_GATES',[profile,REPORTS/'profile-fixtures']);need('PROFILE_GATE_PASS checks=68' in text,'Profile gate count');counts['profile_checks']=68
 discovery=compile_test('ReleaseDiscoveryTests',[TESTS/'GameLocationTests.cs',ROOT/'GameLocation.cs']);output=REPORTS/'DISCOVERY.json'
 run('DISCOVERY',[discovery,REPORTS/'discovery-fixtures',output]);data=read(output)
 need(data['result']=='GAME_LOCATION_PASS' and data['checks']+data['skippedSymlinkTests']==87,'Discovery cases changed')
 need(data['gameWrites']==data['registryWrites']==data['registryCalls']==0 and not data['gameLaunched'],'Discovery side effects')
 counts.update(discovery_checks=data['checks'],discovery_symlink_skips=data['skippedSymlinkTests'])
 context=compile_test('ReleaseContextTests',[TESTS/'LaunchContextTests.cs',ROOT/'LaunchContext.cs',ROOT/'GameLocation.cs']);output=REPORTS/'CONTEXT.json'
 run('LAUNCH_CONTEXT',[context,REPORTS/'context-fixtures',output]);data=read(output)
 need(data['result']=='LAUNCH_CONTEXT_PASS' and data['checks']+data['skippedSymlinkTests']==42 and data['processEnvironmentRestored'],'Context cases changed')
 counts.update(context_checks=data['checks'],context_symlink_skips=data['skippedSymlinkTests'])
 footer=compile_test('ReleaseFooterAudit',[TESTS/'FooterAudit.cs']);text=run('FOOTER',[footer,PACKAGE/'Launcher.exe',REPORTS/'FOOTER.txt'])
 need('FOOTER_AUDIT_PASS checks=390' in text and 'control_intersections=0' in text,'Footer regression');counts['footer_checks']=390
 layout=compile_test('ReleaseLayoutAudit',[TESTS/'LayoutAudit.cs']);text=run('LAYOUT',[layout,REPORTS/'LAYOUT.txt'])
 need(all(v in text for v in ('states=27','clipped=0','overlaps=0','outside=0','errors=0')),'Layout regression');counts['layout_states']=27
 fixture=REPORTS/('deep-'+uuid.uuid4().hex);pref=fixture/'preferences';pref.mkdir(parents=True);game=fixture/'steamapps/common/F1 25';game.mkdir(parents=True)
 (game/'F1_25.exe').write_text('Fixture marker, not executable',encoding='utf-8');(game/'game.dat').write_text('Fixture marker, not game resources',encoding='utf-8')
 (fixture/'steamapps/appmanifest_3059520.acf').write_text('"AppState" {"appid" "3059520" "installdir" "F1 25" "UserConfig" {"language" "japanese"}}',encoding='utf-8');(pref/'game-folder.txt').write_text(str(game),encoding='utf-8')
 original='Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Karsvein","F1RU")'
 source=(ROOT/'Launcher.cs').read_text(encoding='utf-8-sig');need(source.count(original)==1,'Preferences fixture hook changed')
 patched=fixture/'Launcher.fixture.cs';patched.write_text(source.replace(original,'@"'+str(pref).replace('"','""')+'"'),encoding='utf-8-sig')
 library=compile_test('ReleaseDeepFixture',[patched]+[ROOT/name for name in SOURCES if name!='Launcher.cs'],'library')
 deep=compile_test('ReleaseDeepAudit',[TESTS/'DeepStateAudit.cs']);text=run('DEEP_STATE',[deep,library,REPORTS/'DEEP_STATE.txt'])
 need('DEEP_STATE_AUDIT checks=80 findings=0' in text,'Deep-state regression');counts['deep_state_checks']=80
 run('SINGLE_EXE_TRANSACTIONS',[PS,'-NoProfile','-ExecutionPolicy','Bypass','-File',ROOT/'single_exe/source/Test-SingleExe.ps1',
  '-PackageDirectory',PACKAGE,'-WorkDirectory',REPORTS/'single-exe-tests','-Version','0.32-ci','-UpdaterSourceDirectory',ROOT])
 single=read(REPORTS/'single-exe-tests/results/CI_RESULTS.json')
 need(single['status']=='PASS' and single['checks']==50 and single['rollbackCopyRestored'] and single['actualUpdaterIntegration'] and single['argumentForwarding'] and single['exitCodePropagation'],'Single-EXE transaction checks failed')
 need(single['gameModified'] is False and single['registryModified'] is False,'Single-EXE test isolation failed')
 counts['single_exe_checks']=single['checks']
 (PACKAGE/'UI_VERIFICATION.txt').write_text('\n'.join(native)+'\nDeep-state80; footer390; profiles68; layout27; discovery/context results recorded by release CI.\nVisual screenshots and live game tests remain pending.\n',encoding='utf-8')
 names=[name for name in (ROOT/'.release/package-v0.32.txt').read_text(encoding='utf-8-sig').splitlines() if name]
 (PACKAGE/'SHA256SUMS.txt').write_text('\n'.join(digest(PACKAGE/name)+'  '+name for name in names if name!='SHA256SUMS.txt')+'\n',encoding='utf-8')
 run('SINGLE_EXE_BUILD',[PS,'-NoProfile','-ExecutionPolicy','Bypass','-File',ROOT/'single_exe/source/Build-SingleExe.ps1','-PackageDirectory',PACKAGE,'-OutputExe',EXE,'-Version',VERSION,'-AllowList',ROOT/'.release/package-v0.32.txt','-EnableTextUpdates'])
 work=EXE.parent/(EXE.stem+'.build');receipt=read(work/'BUILD_RECEIPT.json');manifest=read(work/'bundle.json')
 need(receipt['status']=='PASS' and receipt['files']==50 and receipt['exeSha256']==digest(EXE),'Receipt mismatch')
 need(set(item['path'] for item in manifest['files'])==set(names),'Manifest/allowlist mismatch')
 with zipfile.ZipFile(work/'bundle.zip') as archive:
  need(set(archive.namelist())==set(names),'ZIP/allowlist mismatch')
  for item in manifest['files']:need(hashlib.sha256(archive.read(item['path'])).hexdigest()==item['sha256'],'ZIP hash mismatch')
  need(not any(name.lower() in ('game.dat','f1_25.exe') or name.lower().endswith('/game.dat') or any(part in ('test','tests','fixtures') for part in name.lower().split('/')[:-1]) for name in names),'Test or game binary in package')
 for flag,name,key in [('--self-test','PUBLIC_UI','ui_checks'),('--speech-self-test','PUBLIC_SPEECH','speech_ui_checks'),('--max-voice-self-test','PUBLIC_MAX','max_ui_checks')]:
  _,text=hook(EXE,flag,name);need('checks='+str(counts[key]) in text,'Public '+name+' mismatch')
 output,_=hook(EXE,'--bundle-self-test','BUNDLE');bundle=read(output);need(bundle['status']=='PASS' and bundle['files']==50 and bundle['textUpdatesPreserved'],'Bundle self-test')
 output,_=hook(EXE,'--launch-context-self-test','PUBLIC_CONTEXT');context=read(output)
 need(context['status']=='PASS' and context['singleExe'] and Path(context['launcherDirectory'])==EXE.parent and Path(context['packageDirectory'])!=EXE.parent,'Outer EXE directory lost')
 need(context['gameWrites']==context['registryWrites']==0 and not context['gameLaunched'],'Context side effects')
 validate_contract()
 result=dict(status='PASS',version=VERSION,**counts,files=50,single_exe_verified=True,launch_context_verified=True,rollback_verified=single['rollbackCopyRestored'],actual_updater_integration=single['actualUpdaterIntegration'],sha256=digest(EXE),bytes=EXE.stat().st_size,installed_game_written=False,registry_written=False,text_version=m['text_version'],original_names_version=m['translation_variants']['original_names']['version'],game_profiles=['1.18','1.20','1.24','1.26'],visual_comparison='pending',live_game_test='not performed')
 (ROOT/'dist/release-ci-result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');print('CI_VERIFY_PASS '+json.dumps(result,separators=(',',':')),flush=True)
if __name__=='__main__':main()
