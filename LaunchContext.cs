using System;
using System.IO;
using System.Security;

// The outer single EXE supplies its directory before starting the verified
// cached entry point. The hint affects discovery only, never compatibility.
static class LaunchContext {
 public static string Directory(string fallback){return SelectDirectory(fallback,Environment.GetEnvironmentVariable("F1RU_SINGLE_EXE"),Environment.GetEnvironmentVariable("F1RU_LAUNCH_DIRECTORY"));}
 public static string SelectDirectory(string fallback,string singleExeFlag,string outerDirectory){
  string selected;if(singleExeFlag=="1"&&Valid(outerDirectory,out selected))return selected;
  if(Valid(fallback,out selected))return selected;
  throw new ArgumentException("Каталог лаунчера не определён.");
 }
 static bool Valid(string raw,out string path){
  path="";if(String.IsNullOrWhiteSpace(raw)||raw.Length>1024)return false;
  foreach(char c in raw)if(Char.IsControl(c))return false;
  string value=raw.Trim();if(value.Length<3||!Char.IsLetter(value[0])||value[1]!=':'||(value[2]!='\\'&&value[2]!='/'))return false;
  for(int i=0;i<value.Length;i++)if(value[i]=='"'||value[i]=='*'||value[i]=='?'||value[i]=='<'||value[i]=='>'||value[i]=='|'||(value[i]==':'&&i!=1))return false;
  foreach(string part in value.Substring(3).Split(new[]{'\\','/'},StringSplitOptions.RemoveEmptyEntries))if(part=="."||part==".."||part.EndsWith(".",StringComparison.Ordinal)||part.EndsWith(" ",StringComparison.Ordinal))return false;
  try{
   string full=Path.GetFullPath(value);if(!System.IO.Directory.Exists(full))return false;
   for(string at=full;!String.IsNullOrEmpty(at);at=Path.GetDirectoryName(at))if((File.GetAttributes(at)&FileAttributes.ReparsePoint)!=0)return false;
   string root=Path.GetPathRoot(full);path=full.Length>root.Length?full.TrimEnd('\\','/'):full;return true;
  }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}catch(ArgumentException){return false;}catch(NotSupportedException){return false;}catch(SecurityException){return false;}
 }
}
