"""Create clearly synthetic NeFS fixtures from the public selected-WEM payload.

No game installation is read or modified. The fixture executable is inert text
and is never launched. NeFS identities/topology exercise the runtime parser.
"""
from pathlib import Path
import argparse,hashlib,json,struct,zlib

def align(n,a):return (n+a-1)//a*a
def crc(b):
    n=zlib.crc32(b);return ((n&65535)+(n>>16))&65535
def build(pack,out,bank,invalid=False):
    manifest=json.loads((pack/'engineer-voices.json').read_text('utf-8'));blob=(pack/'engineer-voices.bin').read_bytes()
    assert hashlib.sha256(blob).hexdigest()==manifest['literal_sha256']
    rows=[x for x in manifest['items'] if x['bank']==bank];rows.sort(key=lambda x:x['entry'])
    count=2268 if bank.endswith('_startup.nfs') else 4950;terminal='eng_wetness_wetint_06.wem' if count==2268 else 'eng_zho_tm_p_02.wem'
    entries=[(x['entry'],blob[x['english_offset']:x['english_offset']+x['english_bytes']]) for x in rows if x['entry']!=terminal]
    terminal_data=next((blob[x['english_offset']:x['english_offset']+x['english_bytes']] for x in rows if x['entry']==terminal),None)
    tiny=min((x[1] for x in entries),key=len)
    for n in range(count-len(entries)-1):entries.append((f'eng_fixture_{n:05d}.wem',tiny))
    entries.append((terminal,terminal_data or tiny));assert len(entries)==count and len({x[0] for x in entries})==count
    if invalid:
        bad=next(data for _,data in entries if data!=entries[0][1]);entries[0]=(entries[0][0],bad)
    names=bytearray(b'win_000.nfs\0');offsets=[]
    for name,_ in entries:offsets.append(len(names));names.extend(name.encode('ascii')+b'\0')
    es=256;shared=es+count*20;ns=shared+count*20;blocks=align(ns+len(names),4)
    records=bytearray();first=[]
    for _,data in entries:
        first.append(len(records)//8)
        for p in range(0,len(data),32768):part=data[p:p+32768];records.extend(struct.pack('<IHH',p+len(part),0,crc(part)))
    volume=align(blocks+len(records),8);writable=volume+16;sw=writable+count*4;toc=align(sw+count*8,16);start=align(toc,32768);total=start+sum(len(x[1]) for x in entries)
    header=bytearray(toc);header[:4]=b'NeFS';struct.pack_into('<III',header,100,toc,0x10600,count)
    struct.pack_into('<4H',header,128,1,0,1,0);struct.pack_into('<8I',header,136,es,writable,shared,sw,ns,blocks,volume,0)
    header[ns:ns+len(names)]=names;header[blocks:blocks+len(records)]=records;struct.pack_into('<QII',header,volume,total,0,start)
    offset=start
    for n,(_,data) in enumerate(entries):
        struct.pack_into('<QIII',header,es+n*20,offset,n,first[n],n);struct.pack_into('<IIIII',header,shared+n*20,n,n,offsets[n],len(data),n)
        struct.pack_into('<HH',header,writable+n*4,0,16 if n==count-1 else 0);struct.pack_into('<II',header,sw+n*8,min(n+1,count-1),n);offset+=len(data)
    header[4:36]=hashlib.sha256(header[:4]+header[36:]).digest();target=out/'game/audio/2025/speech'/bank;target.parent.mkdir(parents=True,exist_ok=True)
    data=bytes(header)+bytes(start-toc)+b''.join(x[1] for x in entries);assert len(data)==total;target.write_bytes(data)
    return hashlib.sha256(data).hexdigest()
def main():
    a=argparse.ArgumentParser(description=__doc__);a.add_argument('--payload',type=Path,required=True);a.add_argument('--out',type=Path,required=True);a.add_argument('--invalid-selected',action='store_true');args=a.parse_args()
    out=args.out.resolve();out.mkdir(parents=True,exist_ok=True);assert not (out/'game').exists(),'Use a new fixture output directory'
    banks={bank:build(args.payload.resolve(),out,bank,args.invalid_selected) for bank in ('en_engineer1_startup.nfs','en_engineer1.nfs')}
    (out/'game/F1_25.exe').write_bytes(b'INERT ENGINEER VOICE TEST FIXTURE - NEVER EXECUTE\n');(out/'game/unrelated-mod.txt').write_text('preserve exactly\n','utf-8')
    record=dict(synthetic_fixture=True,game_execution=False,banks=banks);(out/'FIXTURE.json').write_text(json.dumps(record,indent=2)+'\n','utf-8');print(json.dumps(record))
if __name__=='__main__':main()
