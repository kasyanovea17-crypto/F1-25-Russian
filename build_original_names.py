"""Build original-name variant on the CURRENT edited Russian release.
Usage: python build_original_names.py PATH_TO_ORIGINAL_ENGLISH_LNG
No old Russian sentences are imported; only name rules are reused.
"""
from pathlib import Path
import collections,hashlib,json,re,struct,sys
from lng import LNG
ROOT=Path(__file__).resolve().parent
read=lambda p:json.loads(p.read_text('utf-8-sig'))
channel=read(ROOT/'updates/stable.json')
base=ROOT/'updates'/channel['file']
assert hashlib.sha256(base.read_bytes()).hexdigest()==channel['sha256']
current=LNG.read(base);RU=current.values;EN=LNG.read(sys.argv[1]).values
assert len(EN)==56134 and set(EN)==set(RU)
catalog=read(ROOT/'original_names_rules.json');entities=catalog['entities'];labels=set(catalog['exact_labels']) & set(EN)
CYR=re.compile('[А-Яа-яЁё]')
TOKEN=re.compile(r'\{[^{}]*\}|\[[^\[\]]*\]|(?<!\d)%(?:\d+\$)?[-+0#]*(?:\d+|\*)?(?:\.(?:\d+|\*))?[sdifuxXge]')
role_rx=re.compile(r'principal|director|designer|officer|engineer|^md,',re.I)
def plain(s): return re.sub(r'\{[^{}]*\}', '', s)

def word_pattern(word):
    if not CYR.search(word): return re.escape(word)
    w=word.lower()
    if len(w)<3: return re.escape(word)
    if w.endswith(('ский','цкий')): return re.escape(w[:-2])+r'(?:ий|ого|ому|им|ом|ая|ой|ую|ие|их|ими)'
    if w.endswith('ый'): return re.escape(w[:-2])+r'(?:ый|ого|ому|ым|ом|ая|ой|ую|ое|ые|ых|ыми)'
    if w.endswith('ий'): return re.escape(w[:-2])+r'(?:ий|ия|ию|ием|ии)'
    if w.endswith('ей'): return re.escape(w[:-2])+r'(?:ей|ея|ею|еем|ее)'
    if w.endswith('а'): return re.escape(w[:-1])+r'(?:а|ы|и|е|у|ой|ою)'
    if w.endswith('я'): return re.escape(w[:-1])+r'(?:я|и|е|ю|ей|ёй|ею)'
    if w.endswith('ь'): return re.escape(w[:-1])+r'(?:ь|я|ю|ем|е|и)'
    if w[-1] in 'оеёиуыю': return re.escape(w)
    return re.escape(w)+r'(?:а|у|ом|ем|е|ы|ов|ым|ами|ах)?'

def alias_pattern(s):
    chunks=re.split(r'([А-Яа-яЁё]+)',s)
    return r'(?<![A-Za-zА-Яа-яЁё])'+''.join(word_pattern(x) if CYR.fullmatch(x[:1]) else re.escape(x) for x in chunks)+r'(?![A-Za-zА-Яа-яЁё])'
rules=[];index=collections.defaultdict(set)
for name,item in entities.items():
    p=plain(name)
    if len(p)<3 or '{' in name: continue
    aliases={a for a in item['aliases'] if CYR.search(a) and '{' not in a and '[' not in a and '\n' not in a}
    if not aliases: continue
    rx=re.compile('|'.join(alias_pattern(a) for a in sorted(aliases,key=len,reverse=True)),re.I)
    src=re.compile(r'(?<!\w)'+re.escape(p)+r'(?!\w)',re.I)
    n=len(rules);rules.append((name,rx,src))
    first=re.search(r'\w+',p).group().casefold();index[first].add(n)

markup_rules=[(name,item) for name,item in sorted(entities.items(),key=lambda pair:len(pair[0]),reverse=True) if '{' in name]

def convert(key,text):
    source=EN[key]
    if key in labels: return source
    for name,item in markup_rules:
        if name in source:
            for alias in sorted(item['aliases'],key=len,reverse=True):
                if '{' in alias and TOKEN.findall(alias)==TOKEN.findall(name):text=text.replace(alias,name)
    ids=set()
    for w in re.findall(r'\w+',plain(source).casefold()): ids.update(index.get(w,()))
    # Full names precede components; source spelling/case is authoritative.
    for i in sorted(ids,key=lambda i:len(rules[i][0]),reverse=True):
        name,rx,src=rules[i]; matches=list(src.finditer(plain(source)))
        if not matches:continue
        spelling=matches[0].group()
        text=re.sub(r'(?<![A-Za-z])('+re.escape(spelling)+r')(?=[А-Яа-яЁё])',r'\1 ',text,flags=re.I)
        if name.casefold()=='button' and not ('Jenson' in source or '_btn_' in key or key=='lng_button' or (key.startswith('lng_subtitle_com') and 'Button' in source)):
            rx=re.compile(alias_pattern('Баттон'),re.I)
        # Do not turn lowercase ordinary English words into people names.
        if spelling.islower() and 'person' in entities[name]['categories']:continue
        pieces=[];pos=0
        for token in TOKEN.finditer(text):
            pieces.append(rx.sub(lambda m:spelling,text[pos:token.start()]))
            pieces.append(token.group());pos=token.end()
        pieces.append(rx.sub(lambda m:spelling,text[pos:]))
        text=''.join(pieces)
    if key.endswith('_stats_body') or key.startswith('lng_compendium_champions_stats_'):
        es=re.split(r'([\r\n]+)',source);ts=re.split(r'([\r\n]+)',text)
        for i in range(2,len(es),2):
            prev=es[i-2].strip().lower().rstrip(':')
            if prev in {'teams','associated teams','chassis','engine','tyres'} or (role_rx.search(es[i-2]) and es[i-2].rstrip().endswith(':')):
                ts[i]=es[i]
            elif prev=='active in formula one':
                ts[i]=re.sub(r'\bOn\b','н. в.',re.sub(r'^As\b','Как',es[i]),flags=re.I)
        text=''.join(ts)
    return text

values={k:convert(k,v) for k,v in RU.items()}
# Preserve every service tag and newline from the edited Russian baseline.
for k,v in values.items():
    assert TOKEN.findall(v)==TOKEN.findall(RU[k]),('tokens',k)
    assert re.findall(r'[\r\n]+',v)==re.findall(r'[\r\n]+',RU[k]),('newlines',k)
changes={k:{'english':EN[k],'russian':RU[k],'original_names':v} for k,v in values.items() if v!=RU[k]}
vp,_,vo=current.sections[b'LNGB'];_,_,so=current.sections[b'SIDA'];out=bytearray(current.data[:vo])
for i,(key,_) in enumerate(current.rows):
    struct.pack_into('>I',out,so+i*8+4,len(out)-vo);out.extend(values[key].encode('utf-8')+b'\0')
struct.pack_into('>I',out,vp+4,len(out)-vo);struct.pack_into('>I',out,4,len(out))
assert LNG(out).values==values
file=ROOT/'payload/language-original-names.lng';file.write_bytes(out)
hash=hashlib.sha256(out).hexdigest()
m=read(ROOT/'manifest.base.json');m['payload'][file.name]=hash
m['translation_variants']={'original_names':{'payload':file.name,'sha256':hash,'version':channel['version']+'.1','base_text_version':channel['version'],'records':len(values),'changed':len(changes),'previous_sha256':[]}}
(ROOT/'manifest.base.json').write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf-8')
audit=ROOT.parent/'f1_launcher_release_audit'
audit.mkdir(parents=True,exist_ok=True)
(audit/'translation_diff.json').write_text(json.dumps(changes,ensure_ascii=False,indent=2),encoding='utf-8')
(audit/'translation_build.json').write_text(json.dumps({'entries':len(values),'changed':len(changes),'base_sha256':channel['sha256'],'alternative_sha256':hash,'standard_unchanged':True},indent=2),encoding='utf-8')
print('TRANSLATION_BUILD_PASS records='+str(len(values))+' changed='+str(len(changes))+' base='+channel['version']+' standard_unchanged=true')
