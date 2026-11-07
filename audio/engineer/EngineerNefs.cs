using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

// Bounded NeFS 1.6 raw-WEM rewriter. Routing/name/opaque fields and unselected
// payloads are retained. Verified all-zero TOC padding is canonicalized to the
// next16-byte boundary, preventing padding growth across language switches.
// No executable, code-loading or anti-cheat path exists.
public static class EngineerNefs {
 const int MaxBank=268435456,MaxWem=8388608,BlockSize=32768;
 sealed class Item { public string Name;public int Index,Shared,Start,Size,First,Blocks; }
 sealed class Bank { public byte[] Data;public int Toc,Count,Entries,Writable,Shared,SW,Names,Blocks,Volume,DataStart; public List<Item> Items=new List<Item>(); }
 sealed class Entry { public string Bank,Name,EnHash,RuHash;public int EnOffset,EnBytes,RuOffset,RuBytes;public HashSet<string> Known=new HashSet<string>(StringComparer.Ordinal); }
 public sealed class Summary { public string Bank,Hash; public int Entries,Selected,English,Russian,Changed; }
 static Dictionary<string,Entry> selected;static byte[] literal;
 static void Need(bool b,string m){if(!b)throw new InvalidDataException(m);}
 static int Int(object o){Need(o is int,"Manifest integer required.");return (int)o;}
 static string Str(Dictionary<string,object>d,string k,int max){Need(d.ContainsKey(k)&&d[k] is string,"Manifest string missing: "+k);string s=(string)d[k];Need(s.Length>0&&s.Length<=max,"Manifest string bound: "+k);return s;}
 static int U32(byte[]b,int p){Need(p>=0&&p<=b.Length-4,"Integer out of bounds.");uint n=BitConverter.ToUInt32(b,p);Need(n<=Int32.MaxValue,"Integer exceeds memory bound.");return (int)n;}
 static int U16(byte[]b,int p){Need(p>=0&&p<=b.Length-2,"Short out of bounds.");return BitConverter.ToUInt16(b,p);}
 static int U64(byte[]b,int p){Need(p>=0&&p<=b.Length-8,"Long out of bounds.");ulong n=BitConverter.ToUInt64(b,p);Need(n<=MaxBank,"Long exceeds bank bound.");return (int)n;}
 static void W32(byte[]b,int p,int n){Buffer.BlockCopy(BitConverter.GetBytes((uint)n),0,b,p,4);}
 static void W64(byte[]b,int p,int n){Buffer.BlockCopy(BitConverter.GetBytes((ulong)n),0,b,p,8);}
 static void W16(Stream s,int n){byte[]b=BitConverter.GetBytes((ushort)n);s.Write(b,0,2);}
 static void W32(Stream s,int n){byte[]b=BitConverter.GetBytes((uint)n);s.Write(b,0,4);}
 static int Align(int n,int a){return checked((n+a-1)/a*a);}
 static bool Equal(byte[]a,int ap,byte[]b,int bp,int count){if(ap<0||bp<0||count<0||ap>a.Length-count||bp>b.Length-count)return false;for(int i=0;i<count;i++)if(a[ap+i]!=b[bp+i])return false;return true;}
 static bool Zero(byte[]b,int a,int z){Need(a>=0&&z>=a&&z<=b.Length,"Zero range rejected.");for(int i=a;i<z;i++)if(b[i]!=0)return false;return true;}
 static byte[] Slice(byte[]b,int p,int n){Need(p>=0&&n>=0&&p<=b.Length-n,"Slice out of bounds.");byte[]r=new byte[n];Buffer.BlockCopy(b,p,r,0,n);return r;}
 public static string HashBytes(byte[]b){using(var h=SHA256.Create())return Hex(h.ComputeHash(b));}
 public static string HashFile(string path){using(var f=File.OpenRead(path))using(var h=SHA256.Create())return Hex(h.ComputeHash(f));}
 static string Hex(byte[]b){return BitConverter.ToString(b).Replace("-","").ToLowerInvariant();}
 static bool Sha(string s){if(s==null||s.Length!=64)return false;foreach(char c in s)if(!(c>='0'&&c<='9'||c>='a'&&c<='f'))return false;return true;}
 static byte[] Read(string p,int bound){var f=new FileInfo(p);Need(f.Exists&&f.Length>0&&f.Length<=bound,"File size rejected: "+Path.GetFileName(p));byte[]b=File.ReadAllBytes(p);Need(b.LongLength==f.Length&&b.Length<=bound,"File changed while reading.");return b;}
 static uint[] CrcTable(){uint[]t=new uint[256];for(uint i=0;i<256;i++){uint c=i;for(int k=0;k<8;k++)c=(c&1)!=0?0xedb88320U^(c>>1):c>>1;t[i]=c;}return t;}
 static readonly uint[] crcTable=CrcTable();
 static int Crc16(byte[]b,int p,int n){uint c=0xffffffff;for(int i=0;i<n;i++)c=crcTable[(c^b[p+i])&255]^(c>>8);c^=0xffffffff;return (int)(((c&65535)+(c>>16))&65535);}
 static string Name(byte[]b,int p,int end){Need(p>=0&&p<end&&end<=b.Length,"Name reference rejected.");int z=p;while(z<end&&z<p+512&&b[z]!=0){Need(b[z]>=32&&b[z]<127,"Non-ASCII bank filename.");z++;}Need(z<end&&z<p+512,"Missing filename terminator.");return Encoding.ASCII.GetString(b,p,z-p);}
 static string Key(string bank,string entry){return bank+"/"+entry;}
 static int Count(string bank){if(bank=="en_engineer1_startup.nfs")return 2268;if(bank=="en_engineer1.nfs")return 4950;throw new InvalidDataException("Unknown engineer bank.");}
 static string Terminal(string bank){return bank=="en_engineer1_startup.nfs"?"eng_wetness_wetint_06.wem":"eng_zho_tm_p_02.wem";}
 static Bank Parse(byte[]d,string name){
  Need(d.Length>=256&&Encoding.ASCII.GetString(d,0,4)=="NeFS","Not a NeFS bank.");var b=new Bank{Data=d,Toc=U32(d,100),Count=U32(d,108)};
  Need(U32(d,104)==0x10600&&b.Count==Count(name),"Engineer bank layout/version changed.");Need(b.Toc>=256&&b.Toc<=16777216&&b.Toc<=d.Length&&b.Toc%16==0,"TOC bounds rejected.");
  byte[]hashed=new byte[b.Toc-32];Buffer.BlockCopy(d,0,hashed,0,4);Buffer.BlockCopy(d,36,hashed,4,b.Toc-36);using(var h=SHA256.Create())Need(Equal(h.ComputeHash(hashed),0,d,4,32),"NeFS header checksum differs.");
  Need(U16(d,128)==1&&U16(d,130)==0&&U16(d,132)==1&&U16(d,134)==0,"Unsupported split/volume/block layout.");
  b.Entries=U32(d,136);b.Writable=U32(d,140);b.Shared=U32(d,144);b.SW=U32(d,148);b.Names=U32(d,152);b.Blocks=U32(d,156);b.Volume=U32(d,160);
  Need(U32(d,164)==0&&b.Entries==256&&b.Shared==256+b.Count*20&&b.Names==b.Shared+b.Count*20,"Entry/name topology changed.");
  Need(b.Names<b.Blocks&&b.Blocks<b.Volume&&b.Volume<b.Writable&&b.Writable<b.SW&&b.SW<b.Toc,"Invalid table order.");
  Need(b.Volume%8==0&&b.Writable==b.Volume+16&&b.SW==b.Writable+b.Count*4&&b.SW+b.Count*8<=b.Toc,"Writable layout changed.");
  b.DataStart=U32(d,b.Volume+12);Need(U64(d,b.Volume)==d.Length&&b.DataStart==Align(b.Toc,BlockSize)&&b.DataStart<=d.Length,"Volume size differs.");
  Need(Zero(d,b.Toc,b.DataStart)&&Zero(d,b.SW+b.Count*8,b.Toc),"Nonzero opaque padding rejected.");
  Need(Name(d,b.Names+U32(d,b.Volume+8),b.Blocks)=="win_000.nfs","Volume identity differs.");
  var names=new HashSet<string>(StringComparer.Ordinal);var shared=new HashSet<int>();int next=b.DataStart,nextBlock=0;
  for(int n=0;n<b.Count;n++){
   int ep=b.Entries+n*20,si=U32(d,ep+8);Need(si<b.Count&&shared.Add(si)&&U32(d,ep+16)==n,"Shared/duplicate entry changed.");int sp=b.Shared+si*20;
   Need(U32(d,sp)==n&&U32(d,sp+4)==n&&U32(d,sp+16)==n,"Directory routing changed.");string entry=Name(d,b.Names+U32(d,sp+8),b.Blocks);
   Need(entry.StartsWith("eng_",StringComparison.Ordinal)&&entry.EndsWith(".wem",StringComparison.Ordinal)&&entry.IndexOfAny(new[]{'/','\\',':'})<0&&names.Add(entry),"Invalid/duplicate engineer filename.");
   int wp=b.Writable+n*4;Need(U16(d,wp)==0&&U16(d,wp+2)==(n==b.Count-1?16:0),"Opaque entry flags changed.");if(n==b.Count-1)Need(entry==Terminal(name),"Terminal identity changed.");
   int sw=b.SW+si*8;Need(U32(d,sw)==Math.Min(n+1,b.Count-1)&&U32(d,sw+4)==n,"Sibling routing changed.");
   var i=new Item{Name=entry,Index=n,Shared=si,Start=U64(d,ep),Size=U32(d,sp+12),First=U32(d,ep+12)};i.Blocks=(i.Size+BlockSize-1)/BlockSize;
   Need(i.Start==next&&i.Size>=12&&i.Size<=MaxWem&&i.Start<=d.Length-i.Size&&i.First==nextBlock,"Entry extent differs.");
   Need(Encoding.ASCII.GetString(d,i.Start,4)=="RIFF"&&Encoding.ASCII.GetString(d,i.Start+8,4)=="WAVE"&&U32(d,i.Start+4)+8==i.Size,"Invalid WEM extent.");
   for(int k=0;k<i.Blocks;k++){int bp=b.Blocks+(i.First+k)*8,end=Math.Min((k+1)*BlockSize,i.Size);Need(bp<=b.Volume-8&&U32(d,bp)==end&&U16(d,bp+4)==0&&U16(d,bp+6)==Crc16(d,i.Start+k*BlockSize,end-k*BlockSize),"Bank payload block checksum differs: "+entry);}
   b.Items.Add(i);next=checked(next+i.Size);nextBlock=checked(nextBlock+i.Blocks);
  }
  int blockEnd=b.Blocks+nextBlock*8;Need(Align(blockEnd,8)==b.Volume&&Zero(d,blockEnd,b.Volume)&&next==d.Length,"Unexpected payload/table tail.");return b;
 }
 static byte[] Fmt(byte[]d){
  Need(d.Length>=12&&d.Length<=MaxWem&&Encoding.ASCII.GetString(d,0,4)=="RIFF"&&Encoding.ASCII.GetString(d,8,4)=="WAVE"&&U32(d,4)+8==d.Length,"Invalid replacement WEM.");
  var chunks=new Dictionary<string,byte[]>();int p=12;while(p<d.Length){Need(p<=d.Length-8,"WEM chunk header truncated.");string tag=Encoding.ASCII.GetString(d,p,4);int n=U32(d,p+4),end=checked(p+8+n);Need((tag=="fmt "||tag=="hash"||tag=="seek"||tag=="data")&&!chunks.ContainsKey(tag)&&end<=d.Length,"Unknown/truncated WEM chunk.");chunks[tag]=Slice(d,p+8,n);p=end==d.Length?end:end+(n&1);}
  Need(p==d.Length&&chunks.ContainsKey("fmt ")&&chunks.ContainsKey("seek")&&chunks.ContainsKey("data"),"Incomplete WEM.");byte[]f=chunks["fmt "];Need(f.Length==36&&U16(f,0)==0x3041&&U16(f,2)==1&&U32(f,4)==48000&&U16(f,16)==18&&f[34]==1&&f[35]==0,"Unsupported Wwise Opus codec.");
  int frames=U32(f,24),packets=U32(f,28);Need(frames>0&&frames<=2880000&&packets>0&&packets<=3001&&chunks["seek"].Length==packets*2,"Invalid Opus sample/packet count.");int sum=0;for(int n=0;n<packets;n++){int s=U16(chunks["seek"],n*2);Need(s>0,"Empty Opus packet.");sum=checked(sum+s);}Need(sum==chunks["data"].Length,"Opus seek data differs.");return f;
 }
 public static void Configure(string manifestPath,string literalPath,string manifestHash,string literalHash){
  byte[]m=Read(manifestPath,4194304);Need(HashBytes(m)==manifestHash&&Sha(manifestHash)&&Sha(literalHash),"Engineer package manifest changed.");literal=Read(literalPath,67108864);Need(HashBytes(literal)==literalHash,"Engineer literal payload changed.");
  var j=new JavaScriptSerializer{MaxJsonLength=4194304,RecursionLimit=16};var d=j.DeserializeObject(Encoding.UTF8.GetString(m))as Dictionary<string,object>;Need(d!=null&&Int(d["schema"])==1&&Int(d["count"])==1596,"Wrong engineer package schema/count.");
  Need(Str(d,"literal_file",64)=="engineer-voices.bin"&&Str(d,"literal_sha256",64)==literalHash&&Int(d["literal_bytes"])==literal.Length,"Literal identity differs.");object[]rows=d["items"]as object[];Need(rows!=null&&rows.Length==1596,"Wrong engineer selection count.");var output=new Dictionary<string,Entry>(StringComparer.Ordinal);int startup=0,main=0;
  foreach(object o in rows){var r=o as Dictionary<string,object>;Need(r!=null,"Bad engineer entry.");var e=new Entry{Bank=Str(r,"bank",64),Name=Str(r,"entry",128),EnHash=Str(r,"english_sha256",64),RuHash=Str(r,"russian_sha256",64),EnOffset=Int(r["english_offset"]),EnBytes=Int(r["english_bytes"]),RuOffset=Int(r["russian_offset"]),RuBytes=Int(r["russian_bytes"])};
   Count(e.Bank);Need(e.Name.StartsWith("eng_",StringComparison.Ordinal)&&e.Name.EndsWith(".wem",StringComparison.Ordinal)&&e.Name.IndexOfAny(new[]{'/','\\',':'})<0&&Sha(e.EnHash)&&Sha(e.RuHash),"Invalid selection identity.");
   foreach(bool ru in new[]{false,true}){int start=ru?e.RuOffset:e.EnOffset,n=ru?e.RuBytes:e.EnBytes;byte[]wem=Slice(literal,start,n);Need(n<=MaxWem&&HashBytes(wem)==(ru?e.RuHash:e.EnHash),"Selected literal differs.");Fmt(wem);}
   object[]known=r["known_sha256"]as object[];Need(known!=null&&known.Length>=2&&known.Length<=16,"Known audio digest list rejected.");foreach(object h in known){Need(h is string&&Sha((string)h),"Known digest malformed.");e.Known.Add((string)h);}Need(e.Known.Contains(e.EnHash)&&e.Known.Contains(e.RuHash),"Language hashes absent from allowlist.");
   string key=Key(e.Bank,e.Name);Need(!output.ContainsKey(key),"Duplicate selection.");output.Add(key,e);if(e.Bank.EndsWith("_startup.nfs",StringComparison.Ordinal))startup++;else main++;
  }
  Need(startup==1138&&main==458,"Engineer selection partition differs.");selected=output;
 }
 static Summary Check(Bank b,string name){Need(selected!=null,"Engineer package not loaded.");var s=new Summary{Bank=name,Hash=HashBytes(b.Data),Entries=b.Count};foreach(var i in b.Items){Entry e;if(!selected.TryGetValue(Key(name,i.Name),out e))continue;string h=HashBytes(Slice(b.Data,i.Start,i.Size));Need(e.Known.Contains(h),"A selected engineer recording was changed by another package: "+i.Name);s.Selected++;if(h==e.EnHash)s.English++;if(h==e.RuHash)s.Russian++;}Need(s.Selected==(name.EndsWith("_startup.nfs",StringComparison.Ordinal)?1138:458),"Selected recording is missing.");return s;}
 public static Summary Inspect(string path,string name){return Check(Parse(Read(path,MaxBank),name),name);}
 public static Summary Build(string source,string name,string language,string destination){
  Need(language=="english"||language=="russian","Unknown engineer voice selection.");Need(!File.Exists(destination)&&!String.Equals(Path.GetFullPath(source),Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase),"Destination must be a new copy.");
  Bank b=Parse(Read(source,MaxBank),name);Summary before=Check(b,name);var payloads=new List<byte[]>();var changed=new HashSet<string>(StringComparer.Ordinal);byte[]prefix=Slice(b.Data,0,b.Blocks);byte[]blocks;
  using(var bs=new MemoryStream()){foreach(var i in b.Items){Entry e;byte[]wem;if(selected.TryGetValue(Key(name,i.Name),out e)){wem=Slice(literal,language=="russian"?e.RuOffset:e.EnOffset,language=="russian"?e.RuBytes:e.EnBytes);byte[]oldFmt=Fmt(Slice(b.Data,i.Start,i.Size)),newFmt=Fmt(wem);foreach(int[]range in new[]{new[]{0,8},new[]{12,18},new[]{20,24},new[]{34,36}})Need(Equal(oldFmt,range[0],newFmt,range[0],range[1]-range[0]),"Codec routing fields differ.");}else wem=Slice(b.Data,i.Start,i.Size);bool diff=!Equal(b.Data,i.Start,wem,0,wem.Length)||i.Size!=wem.Length;payloads.Add(wem);W32(prefix,b.Entries+i.Index*20+12,(int)bs.Length/8);W32(prefix,b.Shared+i.Shared*20+12,wem.Length);if(diff){changed.Add(i.Name);for(int p=0;p<wem.Length;p+=BlockSize){int n=Math.Min(BlockSize,wem.Length-p);W32(bs,p+n);W16(bs,0);W16(bs,Crc16(wem,p,n));}}else bs.Write(b.Data,b.Blocks+i.First*8,i.Blocks*8);}blocks=bs.ToArray();}
  if(changed.Count==0){using(var f=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(b.Data,0,b.Data.Length);f.Flush(true);}var same=Inspect(destination,name);same.Changed=0;return same;}
  int volume=Align(b.Blocks+blocks.Length,8),writable=volume+16,sw=writable+b.Count*4,toc=Align(sw+b.Count*8,16),start=Align(toc,BlockSize),total=start;foreach(var x in payloads)total=checked(total+x.Length);Need(total<=MaxBank,"Repacked bank exceeds size bound.");W32(prefix,100,toc);W32(prefix,140,writable);W32(prefix,148,sw);W32(prefix,160,volume);int offset=start;for(int n=0;n<b.Count;n++){W64(prefix,b.Entries+n*20,offset);offset+=payloads[n].Length;}
  byte[]header=new byte[toc];Buffer.BlockCopy(prefix,0,header,0,prefix.Length);Buffer.BlockCopy(blocks,0,header,b.Blocks,blocks.Length);Buffer.BlockCopy(b.Data,b.Volume,header,volume,16);W64(header,volume,total);W32(header,volume+12,start);Buffer.BlockCopy(b.Data,b.Writable,header,writable,b.Count*4);Buffer.BlockCopy(b.Data,b.SW,header,sw,b.Count*8);byte[]hdata=new byte[toc-32];Buffer.BlockCopy(header,0,hdata,0,4);Buffer.BlockCopy(header,36,hdata,4,toc-36);using(var h=SHA256.Create())Buffer.BlockCopy(h.ComputeHash(hdata),0,header,4,32);
  byte[]output=new byte[total];Buffer.BlockCopy(header,0,output,0,toc);offset=start;foreach(var x in payloads){Buffer.BlockCopy(x,0,output,offset,x.Length);offset+=x.Length;}Bank after=Parse(output,name);Check(after,name);ValidatePreserved(b,after,name,language);
  Need(HashFile(source)==before.Hash,"Source bank changed during build.");using(var f=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(output,0,output.Length);f.Flush(true);}Need(HashFile(destination)==HashBytes(output)&&HashFile(source)==before.Hash,"Written copy/source changed.");Summary result=Check(after,name);result.Changed=changed.Count;return result;
 }
 static void ValidatePreserved(Bank a,Bank b,string name,string language){
  Need(a.Count==b.Count&&Equal(a.Data,a.Names,b.Data,b.Names,a.Blocks-a.Names)&&Equal(a.Data,a.Writable,b.Data,b.Writable,a.Count*4)&&Equal(a.Data,a.SW,b.Data,b.SW,a.Count*8),"Routing/name/flags changed.");byte[]x=Slice(a.Data,0,256),y=Slice(b.Data,0,256);foreach(int[]r in new[]{new[]{4,36},new[]{100,104},new[]{140,144},new[]{148,152},new[]{160,164}})for(int n=r[0];n<r[1];n++){x[n]=0;y[n]=0;}Need(Equal(x,0,y,0,256),"Opaque header changed.");
  for(int n=0;n<a.Count;n++){Item old=a.Items[n],now=b.Items[n];Need(old.Name==now.Name&&old.Shared==now.Shared&&old.Index==now.Index,"Entry identity moved.");Entry e;if(selected.TryGetValue(Key(name,old.Name),out e)){Need(HashBytes(Slice(b.Data,now.Start,now.Size))==(language=="russian"?e.RuHash:e.EnHash),"Selected language verification failed.");}else Need(old.Size==now.Size&&Equal(a.Data,old.Start,b.Data,now.Start,old.Size)&&Equal(a.Data,a.Blocks+old.First*8,b.Data,b.Blocks+now.First*8,old.Blocks*8),"Unselected recording/block was changed.");byte[]ae=Slice(a.Data,a.Entries+n*20,20),be=Slice(b.Data,b.Entries+n*20,20);for(int k=0;k<8;k++){ae[k]=0;be[k]=0;}for(int k=12;k<16;k++){ae[k]=0;be[k]=0;}Need(Equal(ae,0,be,0,20),"Entry routing metadata changed.");byte[]asr=Slice(a.Data,a.Shared+old.Shared*20,20),bsr=Slice(b.Data,b.Shared+now.Shared*20,20);for(int k=12;k<16;k++){asr[k]=0;bsr[k]=0;}Need(Equal(asr,0,bsr,0,20),"Shared routing metadata changed.");}
 }
 public static void VerifyPair(string before,string after,string bank,string language){Bank a=Parse(Read(before,MaxBank),bank),b=Parse(Read(after,MaxBank),bank);Check(a,bank);Check(b,bank);ValidatePreserved(a,b,bank,language);}
}
