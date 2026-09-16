"""Author-only publishing preparation; does not push or modify an installed game."""
from pathlib import Path
import sys,json,hashlib,struct,re
from lng import LNG
root=Path(__file__).resolve().parent
version=sys.argv[1];notes=sys.argv[2] if len(sys.argv)>2 else 'Исправления перевода.'
if not re.fullmatch(r'\d+\.\d+(?:\.\d+){0,2}',version):raise SystemExit('Invalid version')
channel=json.loads((root/'updates/stable.json').read_text(encoding='utf-8'))
def sem(v):return tuple(map(int,v.split('.')))+(0,)*(4-len(v.split('.')))
if sem(version)<=sem(channel['version']):raise SystemExit('Use a new, higher version')
source=root/'updates'/channel['file'];data=LNG.read(source)
values=json.loads((root/'translations.json').read_text(encoding='utf-8'))
if set(values)!=set(data.values):raise SystemExit('Translation keys differ')
token=re.compile(r'\[[^\]\r\n]+\]|\{[^}\r\n]+\}')
for k,v in values.items():
    if not isinstance(v,str) or '\0' in v:raise SystemExit('Invalid text: '+k)
    if sorted(t.replace(' и ', ' and ') if t in ['[1994, 1995, 2005 и 2006]', '[1995, 2005 и 2006]'] else t for t in token.findall(v))!=sorted(t.replace(' и ', ' and ') if t in ['[1994, 1995, 2005 и 2006]', '[1995, 2005 и 2006]'] else t for t in token.findall(data.values[k])):raise SystemExit('Placeholders/tags changed: '+k)
# Compact values, keeping the original lookup tables and key order intact.
vp,_,vo=data.sections[b'LNGB'];out=bytearray(data.data[:vo]);_,_,so=data.sections[b'SIDA']
for i,(k,_) in enumerate(data.rows):
    struct.pack_into('>I',out,so+i*8+4,len(out)-vo);out.extend(values[k].encode('utf-8')+b'\0')
struct.pack_into('>I',out,vp+4,len(out)-vo);struct.pack_into('>I',out,4,len(out))
if LNG(out).values!=values:raise SystemExit('Roundtrip failed')
if len(out)>33554432:raise SystemExit('Payload exceeds launcher limit')
dest=root/'updates'/version;dest.mkdir() # Never overwrite a published version.
(dest/'language.lng').write_bytes(out)
channel.update(version=version,file=version+'/language.lng',sha256=hashlib.sha256(out).hexdigest(),bytes=len(out),notes=notes)
(root/'updates/stable.json').write_text(json.dumps(channel,ensure_ascii=False,indent=2),encoding='utf-8')
print('RELEASE_READY version='+version+' records='+str(len(values))+' sha256='+channel['sha256'])
