"""Offline build of a bounded literal/copy patch; no game files are written."""
from pathlib import Path
import argparse
import hashlib
import importlib.util
import json
import sys

ORIGINAL='af47405ce900a44cc9be72f3cc97868f0c3dc179e0d14127087cb106119ba772'
PREVIOUS='bf42b070e1277f50646a627a72e5845b672e257863eee38e311ffd705ebe163f'
OUTPUT='b3202523d49c3cd173a7dd4172b78f071fd5e0b9967bdb59cbcaae9719dd50a5'

def sha(data): return hashlib.sha256(data).hexdigest()

def build(parser_path, original, previous, modified, output):
    spec=importlib.util.spec_from_file_location('max_nfs_copy',parser_path)
    module=importlib.util.module_from_spec(spec);sys.modules[spec.name]=module;spec.loader.exec_module(module)
    target=modified.read_bytes(); assert sha(target)==OUTPUT
    new=module.parse_bank(target)
    sources=[]
    for path,pin in [(original,ORIGINAL),(previous,PREVIOUS)]:
        raw=path.read_bytes();assert sha(raw)==pin
        bank=module.parse_bank(raw)
        assert [(x.name,x.index,x.shared_index) for x in bank.items]==[(x.name,x.index,x.shared_index) for x in new.items]
        sources.append((raw,bank,pin))
    literals=bytearray();cache={}
    def literal(data):
        key=sha(data)
        if key not in cache:
            cache[key]=(len(literals),len(data));literals.extend(data)
        offset,length=cache[key]
        assert bytes(literals[offset:offset+length])==data
        return dict(kind='literal',offset=offset,length=length)
    inputs=[]
    for raw,bank,pin in sources:
        ops=[literal(target[:new.data_start])];non_max=0
        for i,(old,item) in enumerate(zip(bank.items,new.items)):
            end=new.items[i+1].start if i+1<len(new.items) else len(target)
            data=target[item.start:end]
            if item.name.startswith('ver_'):
                ops.append(literal(data))
            else:
                # Preserve the game's own audio, including existing alignment bytes.
                assert raw[old.start:old.start+len(data)]==data
                op=dict(kind='copy',offset=old.start,length=len(data));non_max+=1
                if ops[-1]['kind']=='copy' and ops[-1]['offset']+ops[-1]['length']==op['offset']:
                    ops[-1]['length']+=op['length']
                else:ops.append(op)
        assert non_max==2646 and sum(x['length'] for x in ops)==len(target)
        rebuilt=b''.join(raw[x['offset']:x['offset']+x['length']] if x['kind']=='copy' else bytes(literals[x['offset']:x['offset']+x['length']]) for x in ops)
        assert rebuilt==target
        inputs.append(dict(sha256=pin,bytes=len(raw),ops=ops))
    replacements=[dict(entry=item.name,offset=item.start,bytes=item.size,sha256=sha(target[item.start:item.start+item.size])) for item in new.items if item.name.startswith('ver_')]
    assert len(replacements)==192
    manifest=dict(schema=1,target='audio/2025/speech/en_drivers.nfs',output_sha256=OUTPUT,output_bytes=len(target),literal_file='max-ru-test.delta',literal_sha256=sha(literals),literal_bytes=len(literals),max_slots=192,unchanged_non_max_slots=2646,bank_entries=2838,synthetic_voice=True,test_only=True,pronunciation_review_entries=18,replacements=replacements,inputs=inputs)
    output.mkdir(parents=True,exist_ok=True)
    for name,data in [('max-ru-test.delta',bytes(literals)),('max-ru-test.json',(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n').encode())]:
        path=output/name
        if path.exists():assert path.read_bytes()==data,'Existing patch differs'
        else:path.write_bytes(data)
        assert path.read_bytes()==data
    print(f'MAX_PATCH_BUILD_PASS inputs=2 slots=192 unchanged=2646 literal_bytes={len(literals)} rebuilt_hash={OUTPUT}')

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    for name in ['parser','original','previous','modified','output']:p.add_argument('--'+name,type=Path,required=True)
    a=p.parse_args();build(a.parser,a.original,a.previous,a.modified,a.output)
