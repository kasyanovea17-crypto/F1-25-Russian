"""Update existing v0.33 from verified 0.33.2; stage and verify before promotion; retain old assets and tag."""
from pathlib import Path
import argparse,hashlib,json,os,re,shutil,subprocess,urllib.error,urllib.parse,urllib.request
ROOT=Path(__file__).resolve().parents[1]
REPOSITORY='kasyanovea17-crypto/F1-25-Russian';TAG='v0.33';ASSET_NAME='F1_25_RU_v0.33.exe'
API='https://api.github.com/repos/'+REPOSITORY
class NoRedirect(urllib.request.HTTPRedirectHandler):
 def redirect_request(self,req,fp,code,msg,headers,newurl):return None
OPENER=urllib.request.build_opener(NoRedirect)
def digest(path):
 with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()
def need(value,message):
 if not value:raise RuntimeError(message)
def request(method,url,token,body=None,content_type='application/json'):
 parsed=urllib.parse.urlsplit(url)
 need(parsed.scheme=='https' and parsed.hostname in {'api.github.com','uploads.github.com'},'Unexpected API destination')
 if body is not None and not isinstance(body,bytes):body=json.dumps(body).encode('utf-8')
 req=urllib.request.Request(url,data=body,method=method,headers={'Authorization':'Bearer '+token,
  'Accept':'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28','User-Agent':'F1-25-Russian-release',
  'Content-Type':content_type})
 with OPENER.open(req,timeout=180) as response:return json.load(response)
def optional(url,token):
 try:return request('GET',url,token)
 except urllib.error.HTTPError as error:
  if error.code==404:return None
  raise
def verify_asset(asset,path):
 need(asset.get('state')=='uploaded' and asset.get('size')==path.stat().st_size and asset.get('digest')=='sha256:'+digest(path),'Uploaded asset mismatch: '+path.name)

def verify_assets(release,files):
 assets={a['name']:a for a in release['assets']}
 need(len(assets)==len(release['assets']),'Duplicate release asset names')
 for path in files:
  need(path.name in assets,'Missing release asset: '+path.name);verify_asset(assets[path.name],path)
 return assets

def download_public(url,path,expected_size,expected_hash):
 prefix='https://github.com/'+REPOSITORY+'/releases/download/'+TAG+'/'
 need(url==prefix+urllib.parse.quote(path.name),'Unexpected public asset URL')
 need(not path.exists(),'Preserve existing public-download evidence')
 path.parent.mkdir(parents=True,exist_ok=True);hash_value=hashlib.sha256();total=0
 req=urllib.request.Request(url,headers={'User-Agent':'F1-25-Russian-release-verifier'})
 with urllib.request.urlopen(req,timeout=180) as response,path.open('xb') as output:
  need(urllib.parse.urlsplit(response.geturl()).hostname in {'github.com','release-assets.githubusercontent.com','objects.githubusercontent.com'},'Unexpected asset redirect')
  while True:
   data=response.read(1024*1024)
   if not data:break
   total+=len(data);need(total<=expected_size,'Public download exceeds tested size');hash_value.update(data);output.write(data)
 need(total==expected_size and hash_value.hexdigest()==expected_hash and digest(path)==expected_hash,'Public download SHA-256 mismatch')
def update_existing(release,files,token,commit,notes,evidence):
 """Promote exact assets only after staging verification. Never deletes assets or moves tags."""
 need(release is not None and not release['draft'] and not release['prerelease'],'Expected existing public v0.33 release; stopped')
 need(release['tag_name']==TAG,'Release tag mismatch')
 tag_before=request('GET',API+'/git/ref/tags/'+TAG,token)
 need(tag_before.get('object',{}).get('sha'),'Existing release tag missing')
 url=API+'/releases/'+str(release['id']);evidence=Path(evidence)
 need(not evidence.exists(),'Preserve prior publication evidence; choose a fresh directory')
 evidence.mkdir(parents=True)
 state=dict(schema=1,status='staging',commit=commit,release_id=release['id'],tagBefore=tag_before['object'],staged=[],renamedPrevious=[],promoted=[],deletedAssets=0)
 def journal():
  (evidence/'TRANSACTION.json').write_text(json.dumps(state,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 def rename(asset_id,name):
  return request('PATCH',API+'/releases/assets/'+str(asset_id),token,{'name':name})
 known={a['name']:a for a in release['assets']};need(len(known)==len(release['assets']),'Duplicate existing asset names')
 staged={};journal()
 # This phase never changes/deletes an existing canonical download.
 for path in files:
  current=known.get(path.name)
  if current and current.get('digest')=='sha256:'+digest(path) and current.get('size')==path.stat().st_size:
   verify_asset(current,path);staged[path.name]=current
   download_public(current['browser_download_url'],evidence/'staged-verified'/path.name,path.stat().st_size,digest(path));continue
  stage_name=path.name+'.staged-'+digest(path)[:16]+'-'+commit[:12]
  asset=known.get(stage_name)
  if asset is None:
   upload=release['upload_url'].split('{',1)[0]+'?name='+urllib.parse.quote(stage_name)
   asset=request('POST',upload,token,path.read_bytes(),'application/vnd.microsoft.portable-executable' if path.suffix=='.exe' else 'text/plain')
  verify_asset(asset,path)
  need(asset['name']==stage_name,'Uploaded staging name changed')
  # Anonymous download verifies the bytes actually served by the public release.
  download_public(asset['browser_download_url'],evidence/'staged-verified'/stage_name,path.stat().st_size,digest(path))
  staged[path.name]=asset;state['staged'].append({'id':asset['id'],'name':stage_name,'targetName':path.name,'sha256':digest(path)});journal()
 # Fresh read before mutation prevents silently replacing someone else's concurrent update.
 fresh=request('GET',url,token);fresh_names={a['name']:a for a in fresh['assets']}
 need(not fresh['draft'] and not fresh['prerelease'] and fresh['tag_name']==TAG,'Release state changed concurrently')
 need(all(fresh.get(k)==release.get(k) for k in ('name','body')),'Release notes changed concurrently')
 for path in files:
  before=known.get(path.name);now=fresh_names.get(path.name)
  need((before is None and now is None) or (before is not None and now is not None and all(before.get(k)==now.get(k) for k in ('id','name','size','digest'))),'Canonical asset changed concurrently: '+path.name)
 for asset in staged.values():
  observed=fresh_names.get(asset['name']);need(observed is not None and observed['id']==asset['id'] and observed.get('digest')==asset.get('digest'),'Staged asset changed concurrently')
 need(request('GET',API+'/git/ref/tags/'+TAG,token)['object']==tag_before['object'],'Release tag changed concurrently')
 state['status']='promoting';state['metadataAttempted']=False;journal()
 new_name='F1 25 на русском — 0.33.2 · исправления и диагностические отчёты'
 try:
  for path in files:
   asset=staged[path.name]
   if asset['name']==path.name:continue
   previous=known.get(path.name)
   if previous:
    backup_name=path.name+'.previous-'+str(previous['id'])
    need(backup_name not in fresh_names,'Historical asset name already exists: '+backup_name)
    entry={'id':previous['id'],'name':path.name,'backupName':backup_name};state['renamedPrevious'].append(entry);journal()
    changed=rename(previous['id'],backup_name);need(changed['name']==backup_name,'Previous asset rename failed')
   entry={'id':asset['id'],'stagedName':asset['name'],'name':path.name};state['promoted'].append(entry);journal()
   changed=rename(asset['id'],path.name);verify_asset(changed,path);need(changed['name']==path.name,'Staged asset promotion failed')
  final=request('GET',url,token);assets=verify_assets(final,files)
  for path in files:download_public(assets[path.name]['browser_download_url'],evidence/'canonical-verified'/path.name,path.stat().st_size,digest(path))
  need(request('GET',API+'/git/ref/tags/'+TAG,token)['object']==tag_before['object'],'Existing release tag was changed externally')
  before_notes=request('GET',url,token);need(all(before_notes.get(k)==release.get(k) for k in ('name','body')),'Release notes changed during asset promotion')
  state['metadataAttempted']=True;journal()
  request('PATCH',url,token,{'name':new_name,'body':notes,'make_latest':'true'})
  final=request('GET',url,token);assets=verify_assets(final,files)
  need(not final['draft'] and not final['prerelease'] and final['tag_name']==TAG and final['body']==notes,'Release metadata verification failed')
  state['status']='published';state['tagAfter']=request('GET',API+'/git/ref/tags/'+TAG,token)['object'];need(state['tagAfter']==tag_before['object'],'Tag history changed')
  state['public_download_verified']=True;state['assets']=[{k:assets[p.name][k] for k in ('id','name','size','digest','browser_download_url')} for p in files];state['url']=final['html_url'];journal();return state
 except Exception:
  # Attempt rollback by exact IDs; do not delete even on failed recovery.
  errors=[]
  for entry in reversed(state['promoted']):
   try:rename(entry['id'],entry['stagedName'])
   except Exception as e:errors.append('staged rename '+str(entry['id'])+': '+type(e).__name__)
  for entry in reversed(state['renamedPrevious']):
   try:rename(entry['id'],entry['name'])
   except Exception as e:errors.append('previous rename '+str(entry['id'])+': '+type(e).__name__)
  if state['metadataAttempted']:
   try:
    current=request('GET',url,token)
    if current.get('name')==new_name and current.get('body')==notes:request('PATCH',url,token,{'name':release['name'],'body':release['body']})
    else:need(all(current.get(k)==release.get(k) for k in ('name','body')),'Release notes changed externally; not overwritten during rollback')
   except Exception as e:errors.append('metadata restore: '+type(e).__name__)
  try:
   restored=request('GET',url,token);restored_names={a['name']:a for a in restored['assets']}
   for path in files:
    before=known.get(path.name);after=restored_names.get(path.name)
    need((before is None and after is None) or (before and after and before['id']==after['id'] and before.get('digest')==after.get('digest')),'Previous canonical asset not restored')
  except Exception as e:errors.append('rollback verification: '+type(e).__name__)
  state['status']='rollback_failed' if errors else 'rolled_back';state['rollbackErrors']=errors;journal();raise

def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--publish',action='store_true');args=parser.parse_args()
 ci=json.loads((ROOT/'dist/release-ci-result.json').read_text(encoding='utf-8-sig'));version='0.33.2';built=ROOT/'dist'/('F1_25_RU_v'+version+'.exe');assetdir=ROOT/'dist'/('publication-assets-'+version);exe=assetdir/ASSET_NAME
 need(ci['status']=='PASS' and ci['version']==version and ci['files']==len([n for n in (ROOT/'.release/package-v0.33.txt').read_text(encoding='utf-8-sig').splitlines() if n]),'CI did not verify 0.33.2')
 for key,count in {'ui_checks':205,'speech_ui_checks':48,'max_ui_checks':52,'engineer_ui_checks':41,'profile_checks':80,'footer_checks':390,'deep_state_checks':80,'layout_states':27,'startup_checks':80,'source_contract_checks':84,'engineer_transaction_checks':27,'engineer_process_checks':44,'engineer_privilege_checks':43,'diagnostic_core_checks':43,'diagnostic_ui_checks':46,'diagnostic_voice_checks':59,'diagnostic_text_checks':19}.items():need(ci.get(key)==count,'CI gate differs: '+key)
 need(ci['discovery_checks']+ci['discovery_symlink_skips']==87 and ci['context_checks']+ci['context_symlink_skips']==42,'Discovery/context tests differ')
 need(ci['single_exe_verified'] and ci['launch_context_verified'] and ci['installed_game_written'] is False and ci['registry_written'] is False,'CI isolation failed')
 need(ci['single_exe_checks']==50 and ci['rollback_verified'] is True and ci['actual_updater_integration'] is True,'Single-EXE rollback/updater gate failed')
 need(ci['game_profiles']==['1.18','1.20','1.24','1.26','1.27'] and ci['text_version']=='0.15.6' and ci['original_names_version']=='0.15.6.1','Compatibility/text changed')
 need(ci['sha256']==digest(built) and ci['bytes']==built.stat().st_size,'CI artifact changed')
 assetdir.mkdir(parents=True,exist_ok=True)
 if exe.exists():need(digest(exe)==ci['sha256'],'Preserve existing differently hashed publication file')
 else:shutil.copy2(built,exe)
 checksum=exe.with_suffix(exe.suffix+'.sha256');checksum_text=ci['sha256']+'  '+ASSET_NAME+'\n'
 if checksum.exists():need(checksum.read_text(encoding='ascii')==checksum_text,'Preserve existing differing checksum')
 else:checksum.write_text(checksum_text,encoding='ascii')
 notes=(ROOT/'releases/v0.33.md').read_text(encoding='utf-8-sig')+'\n### SHA-256\n```text\n'+checksum.read_text(encoding='ascii')+'```\n'
 files=[exe,checksum];plan=dict(repository=REPOSITORY,tag=TAG,version=version,sha256=ci['sha256'],bytes=ci['bytes'],assets=[p.name for p in files],preserve_tag=True,preserve_previous_assets=True)
 (ROOT/'dist/publication-plan.json').write_text(json.dumps(plan,indent=2)+'\n',encoding='utf-8')
 if not args.publish:print('PUBLICATION_PLAN_PASS '+json.dumps(plan));return
 need(os.environ.get('GITHUB_REPOSITORY')==REPOSITORY,'Repository binding mismatch')
 commit=os.environ.get('GITHUB_SHA','');need(re.fullmatch(r'[a-f0-9]{40}',commit),'Missing workflow commit')
 need(subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()==commit,'Checkout does not match workflow commit')
 notes+='\n### Исходный код этой сборки\nТег v0.33 сохранён без переноса. Проверенные исходники обновления: ['+commit[:12]+'](https://github.com/'+REPOSITORY+'/tree/'+commit+').\n'
 token=os.environ['GH_TOKEN'];release=optional(API+'/releases/tags/'+TAG,token)
 record=update_existing(release,files,token,commit,notes,ROOT/'dist'/('publication-'+commit[:12]))
 (ROOT/'dist/PUBLISHED_RELEASE.json').write_text(json.dumps(record,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 print('RELEASE_UPDATED '+json.dumps(record,ensure_ascii=False))
if __name__=='__main__':main()
