"""Portable, game-free verification of the distributable Max literal/copy patch."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import struct

ROOT=Path(__file__).resolve().parent
EXPECTED_MANIFEST='24353455483c192129a8cc6b9b6238d481da901d5237b9d16ca6c9d6b789e77b'
EXPECTED_LITERAL='e26fb80e9ee24c335f2bdb1197d49c1104519670f56a619e7fe06e39446bca16'
EXPECTED_OUTPUT='b3202523d49c3cd173a7dd4172b78f071fd5e0b9967bdb59cbcaae9719dd50a5'
EXPECTED_INPUTS={'af47405ce900a44cc9be72f3cc97868f0c3dc179e0d14127087cb106119ba772':67708772,'bf42b070e1277f50646a627a72e5845b672e257863eee38e311ffd705ebe163f':67790983}

def verify(root):
    checks=0
    def check(ok,message):
        nonlocal checks
        if not ok:raise ValueError(message)
        checks+=1
    manifest_path=root/'payload/max-ru-test.json';literal_path=root/'payload/max-ru-test.delta'
    raw=manifest_path.read_bytes();literal=literal_path.read_bytes()
    check(hashlib.sha256(raw).hexdigest()==EXPECTED_MANIFEST,'Pinned manifest SHA256')
    check(hashlib.sha256(literal).hexdigest()==EXPECTED_LITERAL,'Pinned literal SHA256')
    m=json.loads(raw)
    check(m['schema']==1 and m['target']=='audio/2025/speech/en_drivers.nfs','Exact target contract')
    check(m['output_sha256']==EXPECTED_OUTPUT and m['output_bytes']==65471820,'Exact output contract')
    check(m['literal_file']=='max-ru-test.delta' and m['literal_bytes']==len(literal)==3665934 and m['literal_sha256']==EXPECTED_LITERAL,'Literal contract')
    check(m['max_slots']==192 and m['unchanged_non_max_slots']==2646 and m['bank_entries']==2838,'Scope accounting')
    check(m['synthetic_voice'] is True and m['test_only'] is True and m['pronunciation_review_entries']==18,'Explicit testing labels')
    check(len(m['inputs'])==2 and {x['sha256']:x['bytes'] for x in m['inputs']}==EXPECTED_INPUTS,'Exact input allowlist')
    replacements=m['replacements']
    check(len(replacements)==192 and len({x['entry'] for x in replacements})==192,'192 unique replacement entries')
    check(all(re.fullmatch(r'ver_[a-z0-9_]+\.wem',x['entry']) for x in replacements),'Only native Max names')
    check(all(type(x['offset']) is int and type(x['bytes']) is int and 0<=x['offset']<m['output_bytes'] and 0<x['bytes']<=8*1024*1024 and x['offset']+x['bytes']<=m['output_bytes'] for x in replacements),'Replacement bounds')
    check(all(a['offset']+a['bytes']<=b['offset'] for a,b in zip(replacements,replacements[1:])),'Ordered non-overlapping native entries')
    for source in m['inputs']:
        ops=source['ops'];check(1<=len(ops)<=2048,'Operation count')
        output_offset=0;literal_ranges=[]
        for op in ops:
            check(set(op)=={'kind','offset','length'} and op['kind'] in ('copy','literal'),'Exact operation fields')
            offset,length=op['offset'],op['length'];limit=source['bytes'] if op['kind']=='copy' else len(literal)
            check(type(offset) is int and type(length) is int and offset>=0 and length>0 and offset<=limit and length<=limit-offset,'Operation input bounds')
            check(output_offset+length<=m['output_bytes'],'Output bound')
            if op['kind']=='literal':literal_ranges.append((output_offset,output_offset+length,offset))
            output_offset+=length
        check(output_offset==m['output_bytes'],'Exact reconstructed length')
        check(ops[0]['kind']=='literal' and ops[0]['offset']==0 and ops[0]['length']==262144 and literal[:4]==b'NeFS','Pinned native header')
        for row in replacements:
            a,b=row['offset'],row['offset']+row['bytes']
            matching=[(start,end,offset) for start,end,offset in literal_ranges if start<=a<b<=end]
            check(len(matching)==1,'Max replacement entirely literal: '+row['entry'])
            start,_,offset=matching[0];payload=literal[offset+a-start:offset+b-start]
            check(hashlib.sha256(payload).hexdigest()==row['sha256'],'Replacement payload hash: '+row['entry'])
            check(payload[:4]==b'RIFF' and struct.unpack_from('<I',payload,4)[0]+8==len(payload) and payload[8:12]==b'WAVE','Complete WEM: '+row['entry'])
        check(sum(op['length'] for op in ops if op['kind']=='copy')>60_000_000,'Other driver data is copied locally, not redistributed')
    engine=(root/'MaxVoice.ps1').read_text('utf-8-sig')
    check(EXPECTED_MANIFEST in engine and EXPECTED_LITERAL in engine and EXPECTED_OUTPUT in engine,'Runtime constants agree')
    check('FixtureRoot' in engine and 'File]::Replace' in engine and "'RESTORING'" in engine,'Transactional runtime is included')
    return dict(status='PASS',checks=checks,inputs=2,max_replacements=192,non_max_preserved=2646,literal_bytes=len(literal),input_game_required=False,actual_bank_reconstruction='separately verified by local fixture tests',installed_game_writes=0,registry_writes=0)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--root',type=Path,default=ROOT);p.add_argument('--report',type=Path);a=p.parse_args()
    result=verify(a.root)
    if a.report:a.report.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print('MAX_PATCH_VERIFY_PASS '+json.dumps(result,separators=(',',':')))
