"""Conservative EGO LNGT reader/editor; structure: EgoEngineModding/Ego-Engine-Modding."""
import struct
from pathlib import Path

class LNG:
    def __init__(self, data):
        self.data=bytes(data)
        assert data[:4]==b'LNGT'
        assert struct.unpack_from('>I',data,4)[0]==len(data)
        pos=8; self.sections={}
        for tag in (b'HSHS',b'HSHT',b'SIDA',b'SIDB',b'LNGB'):
            assert data[pos:pos+4]==tag,(pos,tag,data[pos:pos+4])
            size=struct.unpack_from('>I',data,pos+4)[0]
            extra=4 if tag==b'SIDA' else 0
            self.sections[tag]=(pos,size,pos+8+extra)
            pos+=8+size+extra
        assert pos==len(data)
        sp,ss,so=self.sections[b'SIDA']
        count=struct.unpack_from('>I',data,sp+8)[0]
        assert ss==count*8
        self.rows=[]; self.index={}
        _,ks,ko=self.sections[b'SIDB']; _,vs,vo=self.sections[b'LNGB']
        def string(start,offset,length):
            assert 0<=offset<length
            end=data.index(b'\0',start+offset,start+length)
            return data[start+offset:end].decode('utf-8')
        for i in range(count):
            k,v=struct.unpack_from('>II',data,so+i*8)
            key=string(ko,k,ks);value=string(vo,v,vs)
            assert key not in self.index,key
            self.index[key]=i;self.rows.append((key,value))
        self.values=dict(self.rows)

    @classmethod
    def read(cls,path): return cls(Path(path).read_bytes())

    def replace(self, replacements):
        result=bytearray(self.data)
        _,_,so=self.sections[b'SIDA']; vp,vs,vo=self.sections[b'LNGB']
        for key,value in replacements.items():
            assert key in self.index,key
            assert '\0' not in value
            struct.pack_into('>I',result,so+self.index[key]*8+4,len(result)-vo)
            result.extend(value.encode('utf-8')+b'\0')
        struct.pack_into('>I',result,vp+4,len(result)-vo)
        struct.pack_into('>I',result,4,len(result))
        parsed=LNG(result)
        assert parsed.values=={**self.values,**replacements}
        return bytes(result)
