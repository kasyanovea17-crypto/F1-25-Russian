using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

internal static class BootstrapTests {
 static List<object> results=new List<object>();static string root;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);results.Add(new{test=name,status="PASS"});}
 static void Reject(Action action,string name){bool failed=false;try{action();}catch(InvalidDataException){failed=true;}Check(failed,name);}
 static string NewFolder(string name){string path=Path.Combine(root,name+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
 static BundleManifest OneFile(string name,byte[] bytes){return new BundleManifest{schema=1,version=BundlePins.Version,entryPoint="Launcher.exe",archiveSha256=BundlePins.ArchiveSha256,totalBytes=bytes.Length,textUpdates=false,files=new[]{new BundleFile{path=name,bytes=bytes.Length,sha256=BundleRules.Hash(bytes)}}};}
 static byte[] Zip(params KeyValuePair<string,byte[]>[] entries){using(var output=new MemoryStream()){
  using(var archive=new ZipArchive(output,ZipArchiveMode.Create,true))foreach(var item in entries){var entry=archive.CreateEntry(item.Key);using(var s=entry.Open())s.Write(item.Value,0,item.Value.Length);}return output.ToArray();
 }}
 static void BadZip(string path,string name){byte[] bytes=Encoding.ASCII.GetBytes("test");byte[] zip=Zip(new KeyValuePair<string,byte[]>(path,bytes));var m=OneFile("Launcher.exe",bytes);m.archiveSha256=BundleRules.Hash(zip);string dir=NewFolder(name);Reject(()=>{using(var stream=new MemoryStream(zip))Bootstrap.Extract(stream,m,dir);},name);Check(Directory.GetFileSystemEntries(dir).Length==0,name+"_no_writes");}
 static string OriginalDirectory(BundleManifest m,string label){string dir=NewFolder(label);using(var stream=Bootstrap.Resource("F1RU.Bundle.zip"))Bootstrap.Extract(stream,m,dir);return dir;}
 static Dictionary<string,object> Clone(Dictionary<string,object> d){return BundleRules.ReadObject(BundleRules.Utf8.GetBytes(BundleRules.Json.Serialize(d)));}
 static void WriteObject(string path,Dictionary<string,object> d){File.WriteAllText(path,BundleRules.Json.Serialize(d),new UTF8Encoding(false));}
 static void UpdatedText(string directory,Dictionary<string,object> original,byte[] baseline){
  byte[] next=(byte[])baseline.Clone();int at=8;while(Encoding.ASCII.GetString(next,at,4)!="LNGB"){string tag=Encoding.ASCII.GetString(next,at,4);at+=8+(tag=="SIDA"?4:0)+checked((int)BundleRules.BE(next,at+4));}int start=at+8;bool changed=false;for(int i=start;i<next.Length;i++)if(next[i]>=(byte)'a'&&next[i]<=(byte)'z'){next[i]=next[i]==(byte)'a'?(byte)'b':(byte)'a';changed=true;break;}Check(changed,"text_fixture_changed");
  string hash=BundleRules.Hash(next);var manifest=Clone(original);manifest["lng"]=hash;manifest["text_version"]="99.0.0";((Dictionary<string,object>)manifest["payload"])["language.lng"]=hash;foreach(var entry in BundleRules.StableLanguageEntries(manifest))entry["sha256"]=hash;
  var history=new List<object>();foreach(object item in (IEnumerable)manifest["previous_lng"])history.Add(item);if(!history.Contains(BundleRules.Text(original,"lng")))history.Add(BundleRules.Text(original,"lng"));manifest["previous_lng"]=history;
  WriteObject(Path.Combine(directory,"manifest.json"),manifest);File.WriteAllBytes(Path.Combine(directory,"payload","language.lng"),next);
 }
 static void PendingTest(BundleManifest m,Dictionary<string,object> original,byte[] baseline){
  string dir=OriginalDirectory(m,"pending");string transaction=new string('a',32),updates=Path.Combine(dir,".updates",transaction);Directory.CreateDirectory(updates);
  File.Copy(Path.Combine(dir,"manifest.json"),Path.Combine(updates,"old.json"));File.Copy(Path.Combine(dir,"payload","language.lng"),Path.Combine(updates,"old.lng"));UpdatedText(dir,original,baseline);
  File.Copy(Path.Combine(dir,"manifest.json"),Path.Combine(updates,"new.json"));File.Copy(Path.Combine(dir,"payload","language.lng"),Path.Combine(updates,"new.lng"));
  var next=BundleRules.ReadObject(Path.Combine(updates,"new.json"));File.Copy(Path.Combine(updates,"old.lng"),Path.Combine(dir,"payload","language.lng"),true);
  var pending=new Dictionary<string,object>{{"transaction",transaction},{"game",""},{"install",false},{"new_hash",BundleRules.Text(next,"lng")}};WriteObject(Path.Combine(dir,"update-pending.json"),pending);
  Bootstrap.ValidateDirectory(dir,m);Check(true,"interrupted_update_pair_accepted_for_recovery");
  pending["transaction"]="../../outside";WriteObject(Path.Combine(dir,"update-pending.json"),pending);Reject(()=>Bootstrap.ValidateDirectory(dir,m),"pending_path_traversal_rejected");
 }
 [STAThread]static int Main(string[] args){
  root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);string report=Path.Combine(root,"UNIT_RESULTS.json");try{
   BundleManifest m=Bootstrap.Manifest();Check(m.files.Length>=3,"embedded_manifest_valid");string original=OriginalDirectory(m,"original");Bootstrap.ValidateDirectory(original,m);Check(true,"baseline_extract_and_hash_validation");
   if(args.Length>1&&args[1]=="baseline"){File.WriteAllText(Path.Combine(root,"BASELINE_RESULTS.json"),BundleRules.Json.Serialize(new{status="PASS",files=m.files.Length,originalArchiveSha256=m.archiveSha256,gameModified=false}),new UTF8Encoding(false));Console.WriteLine("BASELINE_PASS embedded_files=3 original_hashes_valid=true game_modified=false");return 0;}
   if(args.Length>1&&args[1]=="rollback"){
    var baselineManifest=BundleRules.ReadObject(Path.Combine(original,"manifest.json"));byte[] baselineLng=File.ReadAllBytes(Path.Combine(original,"payload","language.lng"));string restored=OriginalDirectory(m,"rollback-restored"),retained=OriginalDirectory(m,"rollback-retained");UpdatedText(restored,baselineManifest,baselineLng);UpdatedText(retained,baselineManifest,baselineLng);Bootstrap.ValidateDirectory(restored,m);Bootstrap.ValidateDirectory(retained,m);
    File.Copy(Path.Combine(original,"manifest.json"),Path.Combine(restored,"manifest.json"),true);File.Copy(Path.Combine(original,"payload","language.lng"),Path.Combine(restored,"payload","language.lng"),true);Bootstrap.ValidateDirectory(restored,m);
    bool exact=BundleRules.HashFile(Path.Combine(original,"manifest.json"))==BundleRules.HashFile(Path.Combine(restored,"manifest.json"))&&BundleRules.HashFile(Path.Combine(original,"payload","language.lng"))==BundleRules.HashFile(Path.Combine(restored,"payload","language.lng"));bool retainedChanged=BundleRules.HashFile(Path.Combine(retained,"payload","language.lng"))!=BundleRules.HashFile(Path.Combine(original,"payload","language.lng"));Check(exact&&retainedChanged,"rollback_copy_and_modified_retained");File.WriteAllText(Path.Combine(root,"ROLLBACK_RESULTS.json"),BundleRules.Json.Serialize(new{status="PASS",isolatedOriginalRestored=exact,modifiedCopyRetained=retainedChanged,gameModified=false}),new UTF8Encoding(false));Console.WriteLine("ROLLBACK_PASS isolated_original_restored=true modified_copy_retained=true game_modified=false");return 0;
   }
   foreach(string bad in new[]{"../outside","/absolute","C:/drive","a\\b","a//b","a/./b","a/../b","CON.txt","aux","LPT1.dat","x. ","x.","x:y","a\u0000b"})Reject(()=>BundleRules.SafeRelative(bad),"reject_path_"+bad.Replace('\0','?'));
   foreach(string good in new[]{"Launcher.exe","payload/language.lng","speech/commands.ru.json","assets/Шрифт.txt"})Check(BundleRules.SafeRelative(good)==good,"accept_path_"+good);
   BadZip("../outside","zip_slip_rejected");BadZip("Launcher.exe/../../outside","nested_zip_slip_rejected");BadZip("C:/outside","zip_drive_rejected");
   byte[] data=Encoding.ASCII.GetBytes("test"),duplicate=Zip(new KeyValuePair<string,byte[]>("Launcher.exe",data),new KeyValuePair<string,byte[]>("launcher.exe",data));var dup=OneFile("Launcher.exe",data);dup.archiveSha256=BundleRules.Hash(duplicate);string dd=NewFolder("duplicate");Reject(()=>{using(var s=new MemoryStream(duplicate))Bootstrap.Extract(s,dup,dd);},"duplicate_archive_rejected");
   var badSize=OneFile("Launcher.exe",data);badSize.files[0].bytes=BundleRules.MaxFileBytes+1;Reject(()=>Bootstrap.ValidateManifest(badSize),"oversized_manifest_rejected");
   var manifest=BundleRules.ReadObject(Path.Combine(original,"manifest.json"));byte[] lng=File.ReadAllBytes(Path.Combine(original,"payload","language.lng"));BundleRules.ValidateLanguage(lng,lng);Check(true,"baseline_lng_valid");
   string changed=OriginalDirectory(m,"updated");UpdatedText(changed,manifest,lng);Bootstrap.ValidateDirectory(changed,m);Check(true,"valid_persistent_text_update_accepted");
   var updated=BundleRules.ReadObject(Path.Combine(changed,"manifest.json"));updated["patch_offset"]=1234;WriteObject(Path.Combine(changed,"manifest.json"),updated);Reject(()=>Bootstrap.ValidateDirectory(changed,m),"changed_install_contract_rejected");
   string modified=OriginalDirectory(m,"modified-exe");File.AppendAllText(Path.Combine(modified,"Launcher.exe"),"x");Reject(()=>Bootstrap.ValidateDirectory(modified,m),"modified_executable_rejected");
   string extra=OriginalDirectory(m,"extra-exe");File.WriteAllText(Path.Combine(extra,"inject.dll"),"test");Reject(()=>Bootstrap.ValidateDirectory(extra,m),"extra_dll_rejected");
   string extraScript=OriginalDirectory(m,"extra-script");Directory.CreateDirectory(Path.Combine(extraScript,"evidence"));File.WriteAllText(Path.Combine(extraScript,"evidence","run.ps1"),"test");Reject(()=>Bootstrap.ValidateDirectory(extraScript,m),"evidence_script_rejected");
   string evidence=OriginalDirectory(m,"evidence");Directory.CreateDirectory(Path.Combine(evidence,"evidence","unit"));File.WriteAllText(Path.Combine(evidence,"evidence","unit","result.json"),"{}");Bootstrap.ValidateDirectory(evidence,m);Check(true,"benign_evidence_persisted");
   string junctionRoot=NewFolder("junction-test"),junctionTarget=NewFolder("junction-target"),junction=Path.Combine(junctionRoot,"linked");
   using(var p=Process.Start(new ProcessStartInfo("cmd.exe","/d /c mklink /J "+BundleRules.Quote(junction)+" "+BundleRules.Quote(junctionTarget)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){string stdout=p.StandardOutput.ReadToEnd(),stderr=p.StandardError.ReadToEnd();p.WaitForExit();if(p.ExitCode!=0)throw new IOException("Junction fixture creation failed: "+stdout+stderr);}
   Reject(()=>BundleRules.RealPath(Path.Combine(junction,"child.txt")),"reparse_parent_rejected");Reject(()=>Bootstrap.TreeFiles(junctionRoot).ToArray(),"reparse_tree_rejected_before_traversal");
   PendingTest(m,manifest,lng);
   string rollback=OriginalDirectory(m,"rollback-copy");UpdatedText(rollback,manifest,lng);Bootstrap.ValidateDirectory(rollback,m);File.Copy(Path.Combine(original,"manifest.json"),Path.Combine(rollback,"manifest.json"),true);File.Copy(Path.Combine(original,"payload","language.lng"),Path.Combine(rollback,"payload","language.lng"),true);Bootstrap.ValidateDirectory(rollback,m);Check(BundleRules.HashFile(Path.Combine(original,"manifest.json"))==BundleRules.HashFile(Path.Combine(rollback,"manifest.json"))&&BundleRules.HashFile(Path.Combine(original,"payload","language.lng"))==BundleRules.HashFile(Path.Combine(rollback,"payload","language.lng")),"isolated_rollback_exact_original");Check(BundleRules.HashFile(Path.Combine(changed,"payload","language.lng"))!=BundleRules.HashFile(Path.Combine(original,"payload","language.lng")),"modified_copy_retained");
   var argsCases=new Dictionary<string,string>{{"","\"\""},{"plain","\"plain\""},{"a b","\"a b\""},{"tail\\","\"tail\\\\\""},{"a\"b","\"a\\\"b\""}};foreach(var pair in argsCases)Check(BundleRules.Quote(pair.Key)==pair.Value,"quote_"+pair.Key);
   Reject(()=>BundleRules.Quote("x\r\ny"),"argument_newline_rejected");
   File.WriteAllText(report,BundleRules.Json.Serialize(new{status="PASS",checks=results.Count,tests=results,gameModified=false,registryModified=false}),new UTF8Encoding(false));Console.WriteLine("BUNDLE_UNIT_PASS checks="+results.Count+" rollback_copy_restored=true modified_copy_retained=true game_modified=false");return 0;
  }catch(Exception ex){File.WriteAllText(report,BundleRules.Json.Serialize(new{status="FAIL",checks=results.Count,tests=results,error=ex.ToString()}),new UTF8Encoding(false));Console.Error.WriteLine(ex);return 1;}
 }
}
