"""Developer-only packer: publish selected WEMs, never entire game archives.

--installation and --inventory are explicit local developer inputs. Runtime has
no Python dependency and no machine-specific paths in its manifest.
"""
from pathlib import Path
import argparse, hashlib, json, os, sys

def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def load(p): return json.loads(Path(p).read_text('utf-8-sig'))
def need(ok,msg):
    if not ok: raise ValueError(msg)

def build(installation,inventory,out,prior):
    installation=installation.resolve(); inventory=inventory.resolve(); out=out.resolve()
    rows=load(installation/'ENCODED_MANIFEST.json')['items']
    text={(r['bank'],r['entry']):r for r in load(installation/'SELECTED_MANIFEST.json')['items']}
    need(len(rows)==len(text)==1596,'Expected the completed 1596 selected recordings')
    allowed={}
    for source in prior:
        for r in load(source)['items']:
            k=(r['bank'],r['entry'])
            allowed.setdefault(k,set()).update(r[h] for h in ('wem_sha256','baseline_wem_sha256','original_wem_sha256') if r.get(h))
    authorized={(r['bank'],r['entry']):r for r in load(installation/'APPROVED_EXISTING_REPLACEMENTS.json')['items']}
    payload=bytearray();dedup={}; result=[]; inputs={}
    def add(data):
        h=hashlib.sha256(data).hexdigest()
        if h not in dedup: dedup[h]=(len(payload),len(data)); payload.extend(data)
        return h,*dedup[h]
    for r in sorted(rows,key=lambda x:(x['bank'],x['entry'])):
        k=r['bank'],r['entry']; need(k in text,'Selected text entry absent')
        folder='engineer_startup' if r['bank']=='en_engineer1_startup.nfs' else 'engineer'
        original=inventory/folder/r['entry']; russian=Path(r['wem_path'])
        old=original.read_bytes(); new=russian.read_bytes(); eh,eo,en=add(old);rh,ro,rn=add(new)
        need(rh==r['wem_sha256'],'Selected Russian WEM changed')
        expected=authorized[k]['inventory_sha256'] if k in authorized else r['original_wem_sha256']
        need(eh==expected,'Original English inventory digest differs: '+r['entry'])
        hashes={eh,rh,r['original_wem_sha256'],r['baseline_wem_sha256']}|allowed.get(k,set())
        need(len(hashes)<=16,'Historical source digest bound exceeded')
        result.append(dict(bank=k[0],entry=k[1],english_sha256=eh,english_offset=eo,english_bytes=en,
            russian_sha256=rh,russian_offset=ro,russian_bytes=rn,known_sha256=sorted(hashes),
            text_ru=text[k]['text_ru'],test_only=True,runtime_playback_verified=False))
        inputs[str(original)]=eh; inputs[str(russian)]=rh
    need(sum(r['bank']=='en_engineer1_startup.nfs' for r in result)==1138,'Startup selection count differs')
    need(sum(r['bank']=='en_engineer1.nfs' for r in result)==458,'Main selection count differs')
    need(len(payload)<=67108864,'Selected literal payload exceeds64MiB')
    blobsha=hashlib.sha256(payload).hexdigest()
    manifest=dict(schema=1,count=1596,english_choice='original_selected_engineer_recordings',russian_choice='synthetic_test',
        literal_file='engineer-voices.bin',literal_sha256=blobsha,literal_bytes=len(payload),
        test_only=True,runtime_menu_coverage_verified=False,short_numbers_require_listening=True,
        quality_notice_ru='Тестовая синтетическая озвучка. Короткие пять, семь, девять и составные сводки требуют прослушивания в игре.',
        install_scope='Only selected WEMs in two shared English engineer banks. Unselected payloads and driver voices are retained byte-for-byte.',items=result)
    out.mkdir(parents=True,exist_ok=True)
    for name,data in [('engineer-voices.bin',bytes(payload)),('engineer-voices.json',(json.dumps(manifest,ensure_ascii=False,sort_keys=True,separators=(',',':'))+'\n').encode())]:
        target=out/name
        need(not target.exists() or target.read_bytes()==data,'Frozen payload differs: '+name)
        if not target.exists():
            with target.open('xb') as f:f.write(data);f.flush();os.fsync(f.fileno())
        need(target.read_bytes()==data,'Written payload differs')
    for p,h in inputs.items():need(sha(p)==h,'A source changed during packing')
    receipt=dict(count=1596,unique_payloads=len(dedup),literal_bytes=len(payload),manifest_sha256=sha(out/'engineer-voices.json'),literal_sha256=blobsha,
        original_english_verified=1596,russian_verified=1596,full_game_banks_published=False,absolute_paths_in_manifest=False)
    print(json.dumps(receipt,ensure_ascii=False,indent=2))
    return receipt

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--installation',type=Path,required=True);p.add_argument('--inventory',type=Path,required=True)
    p.add_argument('--out',type=Path,default=Path(__file__).parent/'payload');p.add_argument('--prior',type=Path,action='append',default=[])
    a=p.parse_args();build(a.installation,a.inventory,a.out,a.prior)
