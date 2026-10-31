using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

// Tests only: tiny owned directories/markers and process-local environment.
static class LaunchContextTests {
 static int checks,skipped;static string fixture;
 static readonly Dictionary<string,string> markers=new Dictionary<string,string>();
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateSymbolicLink(string link,string target,int flags);
 static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
 static string Canonical(string value){return Path.GetFullPath(value).TrimEnd('\\','/');}
 static bool Same(string left,string right){return String.Equals(Canonical(left),Canonical(right),StringComparison.OrdinalIgnoreCase);}
 static string Hash(string path){using(var sha=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
 static string Folder(string name){string p=Path.Combine(fixture,name);System.IO.Directory.CreateDirectory(p);return p;}
 static string Game(string name){string p=Folder(name);foreach(string file in new[]{"F1_25.exe","game.dat"}){string path=Path.Combine(p,file);File.WriteAllText(path,"tiny fixture marker; never executed",new UTF8Encoding(false));markers[path]=Hash(path);}return p;}
 static void Fallback(string cache,string flag,string input,string label){Check(Same(LaunchContext.SelectDirectory(cache,flag,input),cache),label);}
 static int Main(string[] args){
  Console.OutputEncoding=new UTF8Encoding(false);if(args.Length!=2){Console.Error.WriteLine("Usage: LaunchContextTests.exe FIXTURE_ROOT RESULT.json");return 2;}
  string output=Path.GetFullPath(args[1]);
  try{
   fixture=Path.Combine(Path.GetFullPath(args[0]),"run-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(fixture);
   string cache=Folder("inner-cache"),outer=Game("outer-game — Тест"),saved=Game("saved-game");
   Check(Same(LaunchContext.SelectDirectory(cache,"1",outer),outer),"single EXE selects actual outer directory");
   Check(Same(LaunchContext.SelectDirectory(cache,"1",outer.Replace('\\','/')),outer),"forward slash absolute outer path");
   Check(Same(LaunchContext.SelectDirectory(cache,"1",outer+Path.DirectorySeparatorChar),outer),"trailing separator normalized");
   Check(Same(LaunchContext.SelectDirectory(cache,"1",outer.ToUpperInvariant()),outer),"case-insensitive existing outer directory");
   foreach(string flag in new string[]{null,"","0","true","01"," 1","1 ","2"})Fallback(cache,flag,outer,"outer ignored without exact single EXE flag: "+(flag??"null"));
   foreach(string value in new string[]{null,"",Path.Combine(fixture,"missing"),"relative-folder","C:relative","\\\\127.0.0.1\\game","//127.0.0.1/game","\""+outer+"\"",outer+"\n",outer+"\r",outer+"\0",Path.Combine(outer,"..",Path.GetFileName(outer)),outer+"\\.. ","C:\\"+new string('x',1025)})Fallback(cache,"1",value,"invalid outer path falls back: "+(value==null?"null":value.Replace('\n','?').Replace('\r','?').Replace('\0','?')));
   string notFolder=Path.Combine(fixture,"not-a-folder.txt");File.WriteAllText(notFolder,"fixture",new UTF8Encoding(false));Fallback(cache,"1",notFolder,"file cannot be outer directory");
   string link=Path.Combine(fixture,"outer-symlink");
   if(CreateSymbolicLink(link,outer,3))Fallback(cache,"1",link,"reparse outer directory rejected");else skipped++;
   foreach(string missing in new string[]{null,"",Path.Combine(fixture,"missing-cache")}){
    bool rejected=false;try{LaunchContext.SelectDirectory(missing,null,null);}catch(ArgumentException){rejected=true;}Check(rejected,"invalid fallback is rejected explicitly");
   }
   var outerFound=GameLocation.FindFromSources("",LaunchContext.SelectDirectory(cache,"1",outer),null,null);
   Check(Same(outerFound.Path,outer)&&outerFound.Source==GameLocation.AdjacentSource,"integration: single-EXE context discovers adjacent game markers");
   var ignored=GameLocation.FindFromSources("",LaunchContext.SelectDirectory(cache,"0",outer),null,null);
   Check(ignored.Path==""&&ignored.Candidates.Count==0,"integration: unflagged outer environment does not change discovery");
   var savedFound=GameLocation.FindFromSources(saved,LaunchContext.SelectDirectory(cache,"1",outer),null,null);
   Check(Same(savedFound.Path,saved)&&savedFound.Source==GameLocation.SavedSource&&savedFound.Candidates.Count==2,"saved user selection retains precedence over outer game directory");
   string oldFlag=Environment.GetEnvironmentVariable("F1RU_SINGLE_EXE",EnvironmentVariableTarget.Process);
   string oldOuter=Environment.GetEnvironmentVariable("F1RU_LAUNCH_DIRECTORY",EnvironmentVariableTarget.Process);
   try{
    Environment.SetEnvironmentVariable("F1RU_SINGLE_EXE","1",EnvironmentVariableTarget.Process);Environment.SetEnvironmentVariable("F1RU_LAUNCH_DIRECTORY",outer,EnvironmentVariableTarget.Process);
    Check(Same(LaunchContext.Directory(cache),outer),"Directory reads process-local wrapper context");
    Environment.SetEnvironmentVariable("F1RU_SINGLE_EXE",null,EnvironmentVariableTarget.Process);
    Check(Same(LaunchContext.Directory(cache),cache),"Directory ignores unflagged process context");
    Environment.SetEnvironmentVariable("F1RU_SINGLE_EXE","1",EnvironmentVariableTarget.Process);Environment.SetEnvironmentVariable("F1RU_LAUNCH_DIRECTORY",outer+"\n",EnvironmentVariableTarget.Process);
    Check(Same(LaunchContext.Directory(cache),cache),"Directory rejects malformed process context");
   }finally{Environment.SetEnvironmentVariable("F1RU_SINGLE_EXE",oldFlag,EnvironmentVariableTarget.Process);Environment.SetEnvironmentVariable("F1RU_LAUNCH_DIRECTORY",oldOuter,EnvironmentVariableTarget.Process);}
   Check(Environment.GetEnvironmentVariable("F1RU_SINGLE_EXE",EnvironmentVariableTarget.Process)==oldFlag&&Environment.GetEnvironmentVariable("F1RU_LAUNCH_DIRECTORY",EnvironmentVariableTarget.Process)==oldOuter,"process environment restored");
   foreach(var marker in markers)Check(File.Exists(marker.Key)&&Hash(marker.Key)==marker.Value,"fixture marker unchanged");
   var data=new {result="LAUNCH_CONTEXT_PASS",checks=checks,skippedSymlinkTests=skipped,fixtureRoot=fixture,fixtureMarkersUnchanged=markers.Count,gameWrites=0,registryWrites=0,gameLaunched=false,processEnvironmentRestored=true};
   System.IO.Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,new JavaScriptSerializer().Serialize(data),new UTF8Encoding(false));
   Console.WriteLine("LAUNCH_CONTEXT_PASS checks="+checks+" symlink_skipped="+skipped+" game_writes=0 registry_writes=0 game_launched=false process_environment_restored=true");return 0;
  }catch(Exception ex){System.IO.Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,new JavaScriptSerializer().Serialize(new{result="LAUNCH_CONTEXT_FAIL",checks=checks,error=ex.ToString()}),new UTF8Encoding(false));Console.Error.WriteLine(ex);return 1;}
 }
}
