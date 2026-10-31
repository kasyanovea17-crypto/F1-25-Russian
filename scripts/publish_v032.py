"""Publish only the exact CI-verified v0.32 EXE and checksum; never replace a public release."""
from pathlib import Path
import argparse,hashlib,json,os,re,subprocess,urllib.error,urllib.parse,urllib.request
ROOT=Path(__file__).resolve().parents[1]
REPOSITORY='kasyanovea17-crypto/F1-25-Russian';TAG='v0.32';ASSET_NAME='F1_25_RU_v0.32.exe'
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
def verify_assets(release,files):
 assets={a['name']:a for a in release['assets']};need(set(assets)=={p.name for p in files},'Unexpected release assets')
 for p in files:
  a=assets[p.name];need(a.get('state')=='uploaded' and a.get('size')==p.stat().st_size and a.get('digest')=='sha256:'+digest(p),'Uploaded asset mismatch: '+p.name)
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
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--publish',action='store_true');args=parser.parse_args()
 exe=ROOT/'dist'/ASSET_NAME;ci=json.loads((ROOT/'dist/release-ci-result.json').read_text(encoding='utf-8-sig'))
 need(ci['status']=='PASS' and ci['version']=='0.32' and ci['files']==50,'CI did not verify v0.32')
 need(ci['ui_checks']==187 and ci['speech_ui_checks']==48 and ci['max_ui_checks']==36 and ci['profile_checks']==68 and ci['footer_checks']==390 and ci['deep_state_checks']==80 and ci['layout_states']==27,'CI gates differ')
 need(ci['discovery_checks']+ci['discovery_symlink_skips']==87 and ci['context_checks']+ci['context_symlink_skips']==42,'Discovery/context tests differ')
 need(ci['single_exe_verified'] and ci['launch_context_verified'] and ci['installed_game_written'] is False and ci['registry_written'] is False,'CI isolation failed')
 need(ci['single_exe_checks']==50 and ci['rollback_verified'] is True and ci['actual_updater_integration'] is True,'Single-EXE rollback/updater gate failed')
 need(ci['game_profiles']==['1.18','1.20','1.24','1.26'] and ci['text_version']=='0.15.6' and ci['original_names_version']=='0.15.6.1','Compatibility/text changed')
 need(ci['sha256']==digest(exe) and ci['bytes']==exe.stat().st_size,'CI artifact changed')
 checksum=exe.with_suffix(exe.suffix+'.sha256');checksum.write_text(ci['sha256']+'  '+ASSET_NAME+'\n',encoding='ascii')
 notes=(ROOT/'releases/v0.32.md').read_text(encoding='utf-8-sig')+'\n### SHA-256\n```text\n'+checksum.read_text(encoding='ascii')+'```\n'
 files=[exe,checksum];plan=dict(repository=REPOSITORY,tag=TAG,sha256=ci['sha256'],bytes=ci['bytes'],assets=[p.name for p in files])
 (ROOT/'dist/publication-plan.json').write_text(json.dumps(plan,indent=2)+'\n',encoding='utf-8')
 if not args.publish:print('PUBLICATION_PLAN_PASS '+json.dumps(plan));return
 need(os.environ.get('GITHUB_REPOSITORY')==REPOSITORY,'Repository binding mismatch')
 commit=os.environ.get('GITHUB_SHA','');need(re.fullmatch(r'[a-f0-9]{40}',commit),'Missing workflow commit')
 need(subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()==commit,'Checkout does not match workflow commit')
 token=os.environ['GH_TOKEN'];release=optional(API+'/releases/tags/'+TAG,token)
 existing_tag=optional(API+'/git/ref/tags/'+TAG,token)
 if release is None:
  need(existing_tag is None,'Tag already exists without the expected draft; stopped')
  release=request('POST',API+'/releases',token,{'tag_name':TAG,'target_commitish':commit,'draft':True,'prerelease':False,
   'name':'F1 25 на русском — 0.32 · новый лаунчер и автопоиск игры','body':notes})
 need(release['draft'] and not release['prerelease'] and release['target_commitish']==commit,'Existing release must be reviewed; publication stopped')
 if existing_tag is not None:need(existing_tag['object']['type']=='commit' and existing_tag['object']['sha']==commit,'Existing draft tag differs')
 url=API+'/releases/'+str(release['id']);known={a['name']:a for a in release['assets']}
 need(set(known)<={p.name for p in files},'Unexpected draft assets')
 for path in files:
  if path.name not in known:
   upload=release['upload_url'].split('{',1)[0]+'?name='+urllib.parse.quote(path.name)
   asset=request('POST',upload,token,path.read_bytes(),'application/vnd.microsoft.portable-executable' if path==exe else 'text/plain')
  else:asset=known[path.name]
  need(asset.get('state')=='uploaded' and asset.get('size')==path.stat().st_size and asset.get('digest')=='sha256:'+digest(path),'Uploaded asset differs: '+path.name)
 verify_assets(request('GET',url,token),files)
 request('PATCH',url,token,{'draft':False,'prerelease':False,'make_latest':'true','body':notes})
 final=request('GET',API+'/releases/tags/'+TAG,token);need(not final['draft'] and not final['prerelease'],'Release did not become public')
 assets=verify_assets(final,files);tag=request('GET',API+'/git/ref/tags/'+TAG,token)
 need(tag['object']['type']=='commit' and tag['object']['sha']==commit,'Published tag differs from tested commit')
 public=ROOT/'dist/verified-public'/ASSET_NAME;download_public(assets[ASSET_NAME]['browser_download_url'],public,ci['bytes'],ci['sha256'])
 record=dict(plan,release_id=final['id'],commit=commit,url=final['html_url'],published_at=final['published_at'],public_download_verified=True,
  downloaded_sha256=digest(public),assets=[{k:a[k] for k in ('id','name','size','digest','browser_download_url')} for a in final['assets']])
 (ROOT/'dist/PUBLISHED_RELEASE.json').write_text(json.dumps(record,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 print('RELEASE_PUBLISHED '+json.dumps(record,ensure_ascii=False))
if __name__=='__main__':main()
