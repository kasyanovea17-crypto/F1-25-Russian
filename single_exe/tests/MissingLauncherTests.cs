using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

internal static class MissingLauncherTests {
 static readonly List<object> Results=new List<object>();
 static string root;
 static BundleManifest manifest;
 static string nestedImmutable;
 static void Check(bool value,string test){if(!value)throw new Exception(test);Results.Add(new{test=test,status="PASS"});}
 static string Hash(string path){return BundleRules.HashFile(path);}
 static string Relative(string directory,string path){return path.Substring(directory.TrimEnd(Path.DirectorySeparatorChar).Length+1).Replace(Path.DirectorySeparatorChar,'/');}
 static Dictionary<string,string> Snapshot(string directory){
  var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  foreach(string path in Bootstrap.TreeFiles(directory))result.Add(Relative(directory,path),Hash(path));
  return result;
 }
 static bool Same(Dictionary<string,string> first,Dictionary<string,string> second){return first.Count==second.Count&&first.All(p=>second.ContainsKey(p.Key)&&second[p.Key]==p.Value);}
 static string Case(string name){string path=Path.Combine(root,name);Directory.CreateDirectory(path);string cache=Path.Combine(path,"cache");Directory.CreateDirectory(cache);using(var s=Bootstrap.Resource("F1RU.Bundle.zip"))Bootstrap.Extract(s,manifest,cache);return cache;}
 static void Remove(string cache,string relative){File.Delete(BundleRules.Inside(cache,relative));Check(!File.Exists(BundleRules.Inside(cache,relative)),"fixture_removed_"+relative);}
 static void Ensure(string cache){
#if FIXED
  Bootstrap.EnsureCache(cache,manifest);
#else
  Bootstrap.ValidateDirectory(cache,manifest);
#endif
 }
 static void NoStages(string cache,string test){Check(Directory.GetDirectories(Path.GetDirectoryName(cache),".stage-*").Length==0,test);}
 static void RejectNoWrites(string cache,string test){
  var before=Snapshot(cache);string[] dirs=Directory.GetDirectories(cache,"*",SearchOption.AllDirectories);bool rejected=false;
  try{Ensure(cache);}catch(InvalidDataException){rejected=true;}catch(IOException){rejected=true;}catch(UnauthorizedAccessException){rejected=true;}
  Check(rejected,test+"_rejected");Check(Same(before,Snapshot(cache)),test+"_files_unchanged");
  Check(dirs.OrderBy(s=>s,StringComparer.OrdinalIgnoreCase).SequenceEqual(Directory.GetDirectories(cache,"*",SearchOption.AllDirectories).OrderBy(s=>s,StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase),test+"_directories_unchanged");NoStages(cache,test+"_no_stage_left");
 }
 static void WriteObject(string path,Dictionary<string,object> value){File.WriteAllText(path,BundleRules.Json.Serialize(value),new UTF8Encoding(false));}
 static void UpdateText(string cache){
  string manifestPath=Path.Combine(cache,"manifest.json"),languagePath=Path.Combine(cache,"payload","language.lng");var value=BundleRules.ReadObject(manifestPath);string oldHash=BundleRules.Text(value,"lng");byte[] bytes=File.ReadAllBytes(languagePath);int at=8;
  while(Encoding.ASCII.GetString(bytes,at,4)!="LNGB"){string tag=Encoding.ASCII.GetString(bytes,at,4);at=checked(at+8+(tag=="SIDA"?4:0)+(int)BundleRules.BE(bytes,at+4));}
  bool changed=false;for(int i=at+8;i<bytes.Length;i++)if(bytes[i]>=(byte)'a'&&bytes[i]<=(byte)'z'){bytes[i]=bytes[i]==(byte)'a'?(byte)'b':(byte)'a';changed=true;break;}Check(changed,"text_fixture_mutated");
  string hash=BundleRules.Hash(bytes);value["lng"]=hash;value["text_version"]="99.0.0";((Dictionary<string,object>)value["payload"])["language.lng"]=hash;foreach(var entry in BundleRules.StableLanguageEntries(value))entry["sha256"]=hash;
  var previous=new List<object>();foreach(object item in (IEnumerable)value["previous_lng"])previous.Add(item);if(!previous.Contains(oldHash))previous.Add(oldHash);value["previous_lng"]=previous;
  File.WriteAllBytes(languagePath,bytes);WriteObject(manifestPath,value);Bootstrap.ValidateDirectory(cache,manifest);
 }
 static void Pending(string cache){
  string transaction=new string('a',32),updates=Path.Combine(cache,".updates",transaction);Directory.CreateDirectory(updates);
  File.Copy(Path.Combine(cache,"manifest.json"),Path.Combine(updates,"old.json"));File.Copy(Path.Combine(cache,"payload","language.lng"),Path.Combine(updates,"old.lng"));UpdateText(cache);
  File.Copy(Path.Combine(cache,"manifest.json"),Path.Combine(updates,"new.json"));File.Copy(Path.Combine(cache,"payload","language.lng"),Path.Combine(updates,"new.lng"));File.Copy(Path.Combine(updates,"old.lng"),Path.Combine(cache,"payload","language.lng"),true);
  var next=BundleRules.ReadObject(Path.Combine(updates,"new.json"));WriteObject(Path.Combine(cache,"update-pending.json"),new Dictionary<string,object>{{"transaction",transaction},{"game",""},{"install",false},{"new_hash",BundleRules.Text(next,"lng")}});Bootstrap.ValidateDirectory(cache,manifest);
 }
 static void MissingOnly(string name,string relative){
  string cache=Case(name);var expected=Snapshot(cache);Remove(cache,relative);Ensure(cache);Bootstrap.ValidateDirectory(cache,manifest);Check(Same(expected,Snapshot(cache)),name+"_exactly_restored");NoStages(cache,name+"_no_stage_left");
 }
 static void ReparseCase(){
  string cache=Case("reparse"),target=Path.Combine(root,"reparse-target"),junction=Path.Combine(cache,"evidence");Directory.CreateDirectory(target);string sentinel=Path.Combine(target,"keep.txt");File.WriteAllText(sentinel,"outside-cache");string hash=Hash(sentinel);Remove(cache,"Launcher.exe");
  using(var p=Process.Start(new ProcessStartInfo("cmd.exe","/d /c mklink /J "+BundleRules.Quote(junction)+" "+BundleRules.Quote(target)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){string stdout=p.StandardOutput.ReadToEnd(),stderr=p.StandardError.ReadToEnd();p.WaitForExit();if(p.ExitCode!=0)throw new IOException("Junction fixture creation failed: "+stdout+stderr);}
  bool rejected=false;try{Ensure(cache);}catch(InvalidDataException){rejected=true;}catch(IOException){rejected=true;}catch(UnauthorizedAccessException){rejected=true;}
  Check(rejected,"reparse_rejected");Check(Hash(sentinel)==hash&&Directory.GetFileSystemEntries(target).Length==1,"reparse_target_untouched");Check(!File.Exists(Path.Combine(cache,"Launcher.exe")),"reparse_no_repair_write");NoStages(cache,"reparse_no_stage_left");
 }
 static void LockedCase(){
  string cache=Case("locked-existing-and-missing");Remove(cache,"Launcher.exe");var before=Snapshot(cache);bool rejected=false;
  using(var locked=new FileStream(BundleRules.Inside(cache,nestedImmutable),FileMode.Open,FileAccess.Read,FileShare.None)){
   try{Ensure(cache);}catch(InvalidDataException){rejected=true;}catch(IOException){rejected=true;}catch(UnauthorizedAccessException){rejected=true;}
   Check(rejected,"locked_existing_rejected");Check(!File.Exists(Path.Combine(cache,"Launcher.exe")),"locked_existing_no_repair_write");
  }
  Check(Same(before,Snapshot(cache)),"locked_existing_files_unchanged");NoStages(cache,"locked_existing_no_stage_left");
 }
 [STAThread]static int Main(string[] args){
  if(args.Length!=2){Console.Error.WriteLine("Usage: MissingLauncherTests.exe <fresh-evidence-directory> baseline|modified");return 2;}
  root=Path.GetFullPath(args[0]);if(Directory.Exists(root)||File.Exists(root)){Console.Error.WriteLine("Evidence directory already exists; choose a fresh path.");return 2;}Directory.CreateDirectory(root);
  try{
   manifest=Bootstrap.Manifest();Check(manifest.textUpdates,"fixture_text_updates_enabled");nestedImmutable=manifest.files.Select(f=>f.path).FirstOrDefault(p=>p.Contains("/")&&p!="payload/language.lng");Check(nestedImmutable!=null,"fixture_nested_immutable_present");
   if(args[1]=="baseline"){
    string cache=Case("missing-launcher");Remove(cache,"Launcher.exe");var before=Snapshot(cache);string message="";try{Ensure(cache);}catch(InvalidDataException ex){message=ex.Message;}
    Check(message=="Файл комплекта отсутствует: Launcher.exe","baseline_missing_error_reproduced");Check(Same(before,Snapshot(cache)),"baseline_existing_files_untouched");
    File.WriteAllText(Path.Combine(root,"RESULTS.json"),BundleRules.Json.Serialize(new{status="REPRODUCED",behavior="FAIL",error=message,tests=Results,gameModified=false,registryModified=false}),new UTF8Encoding(false));Console.WriteLine("BASELINE_REPRODUCED missing=Launcher.exe repair=false existing_untouched=true");Console.WriteLine(message);return 0;
   }
   if(args[1]!="modified")throw new InvalidDataException("Unknown phase.");
   string valid=Case("valid-cache");var validBefore=Snapshot(valid);Ensure(valid);Check(Same(validBefore,Snapshot(valid)),"valid_cache_no_changes");NoStages(valid,"valid_cache_no_stage");
   MissingOnly("missing-launcher","Launcher.exe");MissingOnly("missing-nested-immutable",nestedImmutable);
   string updated=Case("updated-text-and-evidence");UpdateText(updated);Directory.CreateDirectory(Path.Combine(updated,"evidence"));File.WriteAllText(Path.Combine(updated,"evidence","kept.json"),"{\"keep\":true}");var updatedExpected=Snapshot(updated);Remove(updated,"Launcher.exe");Ensure(updated);Bootstrap.ValidateDirectory(updated,manifest);Check(Same(updatedExpected,Snapshot(updated)),"updated_text_and_evidence_exactly_preserved");NoStages(updated,"updated_no_stage_left");
   string pending=Case("pending-update");Pending(pending);var pendingExpected=Snapshot(pending);Remove(pending,"Launcher.exe");Ensure(pending);Bootstrap.ValidateDirectory(pending,manifest);Check(Same(pendingExpected,Snapshot(pending)),"pending_journal_and_payloads_exactly_preserved");NoStages(pending,"pending_no_stage_left");
   string tamper=Case("tamper-and-missing");File.AppendAllText(BundleRules.Inside(tamper,nestedImmutable),"tamper");Remove(tamper,"Launcher.exe");RejectNoWrites(tamper,"tamper_and_missing");
   string unknown=Case("unknown-dll-and-missing");File.WriteAllText(Path.Combine(unknown,"unknown.dll"),"unknown");Remove(unknown,"Launcher.exe");RejectNoWrites(unknown,"unknown_dll_and_missing");
   foreach(string mutable in new[]{"manifest.json","payload/language.lng"}){string cache=Case("missing-mutable-"+Path.GetFileName(mutable));Remove(cache,mutable);Remove(cache,"Launcher.exe");RejectNoWrites(cache,"mutable_missing_"+mutable);}
   string directoryAtTarget=Case("directory-at-target");Remove(directoryAtTarget,"Launcher.exe");Directory.CreateDirectory(Path.Combine(directoryAtTarget,"Launcher.exe"));RejectNoWrites(directoryAtTarget,"directory_at_target");
   string invalidText=Case("invalid-text-and-missing");File.AppendAllText(Path.Combine(invalidText,"payload","language.lng"),"invalid");Remove(invalidText,"Launcher.exe");RejectNoWrites(invalidText,"invalid_text_and_missing");ReparseCase();LockedCase();
   File.WriteAllText(Path.Combine(root,"RESULTS.json"),BundleRules.Json.Serialize(new{status="PASS",checks=Results.Count,tests=Results,gameModified=false,registryModified=false,productionCacheModified=false}),new UTF8Encoding(false));Console.WriteLine("MODIFIED_PASS checks="+Results.Count+" missing_immutable_restored=true text_and_pending_preserved=true reject_before_writes=true game_modified=false");return 0;
  }catch(Exception ex){File.WriteAllText(Path.Combine(root,"RESULTS.json"),BundleRules.Json.Serialize(new{status="FAIL",checks=Results.Count,tests=Results,error=ex.ToString()}),new UTF8Encoding(false));Console.Error.WriteLine(ex);return 1;}
 }
}
