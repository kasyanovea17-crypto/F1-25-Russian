using System;
using System.IO;
using System.Security.Cryptography;

// Read-only compatibility analysis. Never patches an executable or contacts a server.
public static class ResourceCompatibility {
 static string Hash(byte[] b,int start,int count){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(b,start,count)).Replace("-","").ToLowerInvariant();}
 static byte[] Hex(string s){if(s==null||s.Length%2!=0)throw new InvalidDataException("Invalid patch bytes");var b=new byte[s.Length/2];for(int i=0;i<b.Length;i++)b[i]=Convert.ToByte(s.Substring(i*2,2),16);return b;}
 static bool Match(byte[] b,int pos,byte[] v){if(pos<0||pos>b.Length-v.Length)return false;for(int i=0;i<v.Length;i++)if(b[pos+i]!=v[i])return false;return true;}
 public static bool HeaderMatches(string path,int length,string expected){
  if(length<256||length>16777216||new FileInfo(path).Length>1073741824)return false;
  var b=File.ReadAllBytes(path);int count=0;
  for(int i=0;i<=b.Length-length;i++)if(b[i]==78&&b[i+1]==101&&b[i+2]==70&&b[i+3]==83&&Hash(b,i,length)==expected)count++;
  return count==1;
 }
 public static string[] Analyze(string path,long size,int[] offsets,string[] before,string[] after,int[] rangeOffsets,int[] rangeLengths,string[] rangeHashes){
  if(size<1||size>1073741824||new FileInfo(path).Length!=size)throw new InvalidDataException("Archive size changed; a new compatibility profile is required.");
  if(offsets.Length==0||before.Length!=offsets.Length||after.Length!=offsets.Length||rangeOffsets.Length==0||rangeOffsets.Length!=rangeLengths.Length||rangeOffsets.Length!=rangeHashes.Length)throw new InvalidDataException("Invalid compatibility profile.");
  var data=File.ReadAllBytes(path);var original=Hash(data,0,data.Length);var beforeBytes=new byte[offsets.Length][];var afterBytes=new byte[offsets.Length][];int end=0;
  for(int i=0;i<offsets.Length;i++){
   var a=Hex(before[i]);var z=Hex(after[i]);int p=offsets[i];bool covered=false;
   for(int j=0;j<rangeOffsets.Length;j++)if(p>=rangeOffsets[j]&&(long)p+a.Length<=(long)rangeOffsets[j]+rangeLengths[j])covered=true;
   if(a.Length==0||a.Length!=z.Length||p<end||p>data.Length-a.Length||!covered)throw new InvalidDataException("Patch outside verified resource ranges.");
   if(!Match(data,p,a)&&!Match(data,p,z))throw new InvalidDataException("Translation resource bytes changed; no writes performed.");
   Buffer.BlockCopy(a,0,data,p,a.Length);beforeBytes[i]=a;afterBytes[i]=z;end=p+a.Length;
  }
  for(int j=0;j<rangeOffsets.Length;j++){
   int p=rangeOffsets[j],n=rangeLengths[j];if(p<0||n<=0||p>data.Length-n||Hash(data,p,n)!=rangeHashes[j])throw new InvalidDataException("Required game resource changed; a new compatibility profile is required.");
  }
  string clean=Hash(data,0,data.Length);
  for(int i=0;i<offsets.Length;i++)Buffer.BlockCopy(afterBytes[i],0,data,offsets[i],afterBytes[i].Length);
  return new[]{clean,Hash(data,0,data.Length),original};
 }
}
