"""Offline publication transaction tests; never reads credentials or calls GitHub."""
from pathlib import Path
import argparse,copy,hashlib,importlib.util,json,tempfile,urllib.parse

ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('publisher',ROOT/'scripts/publish_v033.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

def sha(data):return hashlib.sha256(data).hexdigest()

class Remote:
 def __init__(self,files,fail=None):
  self.fail=fail;self.triggered=False;self.calls=[];self.downloads=[];self.tag={'type':'commit','sha':'a'*40};self.initial_tag=copy.deepcopy(self.tag);self.nextid=10;self.data={};self.files=files
  self.release={'id':9,'tag_name':m.TAG,'draft':False,'prerelease':False,'name':'Previous release','body':'Previous notes','html_url':'https://github.com/'+m.REPOSITORY+'/releases/tag/'+m.TAG,'upload_url':'https://uploads.github.com/repos/'+m.REPOSITORY+'/releases/9/assets{?name,label}','assets':[]}
  for index,path in enumerate(files):self.add(index+1,path.name,('old:'+path.name).encode())
  self.add(3,'unrelated.txt',b'keep unrelated artifact')
  self.original=copy.deepcopy(self.release);self.initial_ids={a['name']:a['id'] for a in self.release['assets']}
 def add(self,id,name,content):
  self.data[id]=content;asset={'id':id,'name':name,'size':len(content),'state':'uploaded','digest':'sha256:'+sha(content),'browser_download_url':self.url(name)};self.release['assets'].append(asset);return asset
 def url(self,name):return 'https://github.com/'+m.REPOSITORY+'/releases/download/'+m.TAG+'/'+urllib.parse.quote(name)
 def request(self,method,url,token,body=None,content_type=None):
  self.calls.append([method,url,copy.deepcopy(body) if isinstance(body,dict) else 'bytes' if body else None])
  if method=='DELETE':raise AssertionError('Deletion is forbidden')
  if method=='GET' and url.endswith('/git/ref/tags/'+m.TAG):return {'object':copy.deepcopy(self.tag)}
  if method=='GET' and url==m.API+'/releases/9':
   if self.fail=='concurrent' and not self.triggered:
    self.triggered=True;self.release['assets'][0]['digest']='sha256:'+'c'*64
   if self.fail=='notes_concurrent' and not self.triggered:self.triggered=True;self.release['body']='External notes'
   if self.fail=='notes_after_promote' and not self.triggered and any(a['id']>10 and a['name']==self.files[1].name for a in self.release['assets']):self.triggered=True;self.release['body']='External notes'
   return copy.deepcopy(self.release)
  if method=='POST':
   assert url.startswith('https://uploads.github.com/');name=urllib.parse.parse_qs(urllib.parse.urlsplit(url).query)['name'][0]
   if self.fail=='upload2' and len([a for a in self.release['assets'] if '.staged-' in a['name']])==1:raise IOError('simulated upload interruption')
   self.nextid+=1;a=self.add(self.nextid,name,body)
   if self.fail=='metadata':a['digest']='sha256:'+'b'*64
   return copy.deepcopy(a)
  if method=='PATCH' and '/releases/assets/' in url:
   id=int(url.rsplit('/',1)[1]);asset=next(a for a in self.release['assets'] if a['id']==id);name=body['name']
   if id in (1,2):assert len([n for n in self.downloads if '.staged-' in n])==2,'Old asset changed before both staged downloads verified'
   if self.fail=='promote2' and id>10 and name==self.files[1].name and not self.triggered:self.triggered=True;raise IOError('simulated promotion interruption')
   assert not any(a['id']!=id and a['name']==name for a in self.release['assets']),'duplicate asset name'
   asset['name']=name;asset['browser_download_url']=self.url(name);return copy.deepcopy(asset)
  if method=='PATCH' and url==m.API+'/releases/9':
   if self.fail=='notes' and body.get('body')=='New notes' and not self.triggered:self.triggered=True;raise IOError('simulated metadata update interruption')
   self.release.update({k:v for k,v in body.items() if k in ('name','body')})
   if self.fail=='notes_reply_lost' and body.get('body')=='New notes' and not self.triggered:self.triggered=True;raise IOError('simulated response lost after applying metadata')
   return copy.deepcopy(self.release)
  raise AssertionError((method,url))
 def download(self,url,path,expected_size,expected_hash):
  asset=next(a for a in self.release['assets'] if a['browser_download_url']==url);data=self.data[asset['id']]
  if (self.fail=='download' and '.staged-' in asset['name']) or (self.fail=='canonical_download' and asset['id']>10 and '.staged-' not in asset['name']):raise IOError('simulated downloaded hash mismatch')
  assert len(data)==expected_size and sha(data)==expected_hash
  assert url==self.url(path.name)
  path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(data);self.downloads.append(asset['name'])
 def old_canonical_intact(self):
  current={a['name']:a for a in self.release['assets']}
  return all(current.get(p.name,{}).get('id')==i+1 and current[p.name]['digest']==self.original['assets'][i]['digest'] for i,p in enumerate(self.files))

def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--work',required=True,type=Path);a=parser.parse_args();a.work.mkdir(parents=True,exist_ok=False)
 files=[a.work/m.ASSET_NAME,a.work/(m.ASSET_NAME+'.sha256')]
 for p in files:p.write_bytes(('new:'+p.name).encode())
 checks=[];records=[]
 def check(ok,name):
  if not ok:raise AssertionError(name)
  checks.append(name)
 for failure in [None,'upload2','metadata','download','promote2','canonical_download','notes','concurrent','notes_concurrent','notes_after_promote','notes_reply_lost']:
  remote=Remote(files,failure);m.request=remote.request;m.download_public=remote.download;evidence=a.work/(failure or 'success');error=None
  try:result=m.update_existing(copy.deepcopy(remote.release),files,'inert-test-token','f'*40,'New notes',evidence)
  except (RuntimeError,IOError,AssertionError) as e:error=type(e).__name__+': '+str(e)
  check((error is None)==(failure is None),'expected outcome '+str(failure))
  check(all(c[0]!='DELETE' for c in remote.calls),'no deletion '+str(failure))
  check(remote.tag==remote.initial_tag,'tag retained '+str(failure))
  check(any(x['id']==3 and x['name']=='unrelated.txt' for x in remote.release['assets']),'unrelated asset preserved '+str(failure))
  if failure is None:
   check(result['status']=='published' and result['public_download_verified'],'published verified')
   check(all(any(a['id']==i and '.previous-' in a['name'] for a in remote.release['assets']) for i in (1,2)),'old bytes preserved historically')
   check(all(next(a for a in remote.release['assets'] if a['name']==p.name)['digest']=='sha256:'+sha(p.read_bytes()) for p in files),'new canonical exact hashes')
   check(len(remote.downloads)==4,'both staging and canonical downloads verified')
  elif failure not in ('concurrent','notes_concurrent'):
   check(remote.old_canonical_intact(),'failure retains previous downloads '+failure)
   check(remote.release['body']==('External notes' if failure=='notes_after_promote' else 'Previous notes'),'failure retains appropriate notes '+failure)
   if failure in ('upload2','metadata','download'):check(not any(c[0]=='PATCH' and '/releases/assets/' in c[1] for c in remote.calls),'no mutation before staged verification '+failure)
   else:check(json.loads((evidence/'TRANSACTION.json').read_text('utf-8'))['status']=='rolled_back','rollback verified '+failure)
  else:
   check(not any(c[0]=='PATCH' for c in remote.calls),'concurrent change stops promotion '+failure)
   if failure=='notes_concurrent':check(remote.release['body']=='External notes','concurrent notes not overwritten')
  if failure is None:
   count_before=len(remote.calls);before_ids=[(a['id'],a['name']) for a in remote.release['assets']]
   repeated=m.update_existing(copy.deepcopy(remote.release),files,'inert-test-token','f'*40,'New notes',a.work/'idempotent')
   check(repeated['status']=='published','idempotent update verified')
   check(before_ids==[(a['id'],a['name']) for a in remote.release['assets']],'idempotent update does not duplicate assets')
   check(not any(c[0]=='POST' or (c[0]=='PATCH' and '/releases/assets/' in c[1]) for c in remote.calls[count_before:]),'idempotent update does not upload or rename')
  records.append({'failure':failure,'error':error,'calls':remote.calls,'downloads':remote.downloads})
 # Drafts are deliberately excluded: the updater must not repurpose a draft or create a new tag.
 remote=Remote(files);remote.release['draft']=True;m.request=remote.request;m.download_public=remote.download
 try:m.update_existing(remote.release,files,'inert-test-token','f'*40,'New notes',a.work/'draft');rejected=False
 except RuntimeError:rejected=True
 check(rejected and not remote.calls,'draft rejected before network')
 check('inert-test-token' not in json.dumps(records),'credential value absent from evidence')
 result={'status':'PASS','checks':len(checks),'cases':12,'real_network_calls':0,'credential_reads':0,'deletions':0,'tag_writes':0,'checksPerformed':checks,'records':records}
 (a.work/'PUBLISH_TESTS.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n','utf-8');assert json.loads((a.work/'PUBLISH_TESTS.json').read_text('utf-8'))==result
 print('PUBLICATION_TRANSACTIONS_PASS checks='+str(len(checks))+' cases=12 real_network_calls=0 credential_reads=0 deletions=0 tag_writes=0')
if __name__=='__main__':main()
