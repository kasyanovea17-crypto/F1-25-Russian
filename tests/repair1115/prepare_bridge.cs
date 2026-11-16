using System;using System.IO;using System.Collections.Generic;using System.Text;using System.Web.Script.Serialization;using System.Diagnostics;
class PrepareBridgeTests {
 static int checks;static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
 static void Reject(Action action,string label){bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}Check(rejected,label);}
 static void Write(string file,string action,string game,int code,string output){File.WriteAllText(file,new JavaScriptSerializer().Serialize(new{schema=1,action=action,gamePath=game,translation="russian",exitCode=code,output=output}),Encoding.UTF8);}
 static int Main(string[] args){try{
  string source=Path.GetFullPath(args[0]),work=Path.GetFullPath(args[1]);if(Directory.Exists(work))throw new Exception("Fresh fixture required");Directory.CreateDirectory(work);
  Check(!TextEngineBridge.Mutation("prepare")&&TextEngineBridge.Mutation("install")&&TextEngineBridge.Mutation("restore"),"mutation matrix");
  bool bad=false;try{TextEngineBridge.Mutation("arbitrary");}catch(ArgumentException){bad=true;}Check(bad,"action allowlist");
  var read=TextEngineBridge.StartInfo(work,"prepare",work,"russian",null);Check(!read.UseShellExecute&&read.Verb==""&&read.RedirectStandardOutput&&read.CreateNoWindow,"prepare no elevation");
  foreach(string action in new[]{"install","restore"}){var start=TextEngineBridge.StartInfo(work,action,work,"russian",Path.Combine(work,"result.json"));Check(start.UseShellExecute&&start.Verb=="runas"&&!start.RedirectStandardOutput,"consent "+action);Check(Path.IsPathRooted(start.FileName)&&start.FileName==TextEngineBridge.SystemPowerShell(),"system executable "+action);}
  bad=false;try{TextEngineBridge.StartInfo(work,"install",work,"unknown",Path.Combine(work,"result.json"));}catch(ArgumentException){bad=true;}Check(bad,"variant rejected");
  bad=false;try{TextEngineBridge.StartInfo(work,"install",work+"\"bad","russian",Path.Combine(work,"result.json"));}catch(ArgumentException){bad=true;}Check(bad,"argument injection rejected");
  string result=Path.Combine(work,"result.json");
  Write(result,"install",work,0,"PROFILE game=1.26 records=56134 text=0.15.6 channel=stable\r\nINSTALL PASS; fixture");Check(TextEngineBridge.ParseResult(result,"install",work,"russian",0).Code==0,"success correlated");
  Reject(()=>TextEngineBridge.ParseResult(result,"restore",work,"russian",0),"wrong action rejected");
  Reject(()=>TextEngineBridge.ParseResult(result,"install",work+"2","russian",0),"wrong game rejected");
  Reject(()=>TextEngineBridge.ParseResult(result,"install",work,"russian",1),"exit mismatch rejected");
  Write(result,"install",work,0,"PROFILE game=1.26 records=56134 text=0.15.6 channel=stable");Reject(()=>TextEngineBridge.ParseResult(result,"install",work,"russian",0),"missing commit token rejected");
  foreach(int code in new[]{1,3,5}){Write(result,"install",work,code,"ERROR fixture");Check(TextEngineBridge.ParseResult(result,"install",work,"russian",code).Code==code,"error preserved "+code);}
  Write(result,"install",work,7,"ERROR fixture");Reject(()=>TextEngineBridge.ParseResult(result,"install",work,"russian",7),"unknown code rejected");

  foreach(string invalid in new[]{"{}","{","[]","null","{\"schema\":\"1\",\"action\":\"install\",\"gamePath\":\"C:\\\\fixture\",\"translation\":\"russian\",\"exitCode\":0,\"output\":\"INSTALL PASS; fixture\"}"}){File.WriteAllText(result,invalid);Reject(()=>TextEngineBridge.ParseResult(result,"install",work,"russian",0),"malformed or wrongly typed result");}
  Write(result,"install",work,0,"INSTALL PASS; fixture");string validJson=File.ReadAllText(result);File.WriteAllText(result,validJson.Replace("\"exitCode\":0","\"exitCode\":\"0\""));Reject(()=>TextEngineBridge.ParseResult(result,"install",work,"russian",0),"exit code string rejected");
  File.WriteAllText(result,validJson.Replace("\"output\":\"INSTALL PASS; fixture\"","\"output\":42"));Reject(()=>TextEngineBridge.ParseResult(result,"install",work,"russian",0),"output number rejected");
  File.Delete(result);
  File.Copy(Path.Combine(source,"Engine.ps1"),Path.Combine(work,"Engine.ps1"));File.Copy(Path.Combine(source,"Compatibility.cs"),Path.Combine(work,"Compatibility.cs"));Directory.CreateDirectory(Path.Combine(work,"payload"));
  var manifest=Updates.Read(Path.Combine(source,"manifest.base.json"));var payload=(Dictionary<string,object>)manifest["payload"];
  foreach(var item in payload)File.Copy(Path.Combine(source,"payload",item.Key),Path.Combine(work,"payload",item.Key));
  string manifestPath=Path.Combine(work,"manifest.json");File.WriteAllText(manifestPath,new JavaScriptSerializer().Serialize(manifest));string originalManifest=File.ReadAllText(manifestPath);
  using(var lease=new TextEngineBridge.RuntimeLease(work)){
   foreach(string name in new[]{"Engine.ps1","Compatibility.cs","manifest.json",@"payload\language.lng"}){bool denied=false;try{using(var stream=new FileStream(Path.Combine(work,name),FileMode.Open,FileAccess.Write,FileShare.ReadWrite)){} }catch(IOException){denied=true;}Check(denied,"held through operation "+name);Check(File.ReadAllBytes(Path.Combine(work,name)).Length>0,"child may read "+name);}
  }
  string engine=Path.Combine(work,"Engine.ps1");byte[] original=File.ReadAllBytes(engine);File.AppendAllText(engine,"\n# modified fixture");bad=false;try{using(var lease=new TextEngineBridge.RuntimeLease(work)){} }catch(IOException){bad=true;}Check(bad,"changed immutable script rejected");File.WriteAllBytes(engine,original);
  string language=Path.Combine(work,"payload","language.lng"),fixedFont=Path.Combine(work,"payload","fonts_russian.erp");byte[] languageBytes=File.ReadAllBytes(language),fontBytes=File.ReadAllBytes(fixedFont);
  File.WriteAllText(language,"changed");Reject(()=>{using(var lease=new TextEngineBridge.RuntimeLease(work)){}},"changed payload rejected");File.WriteAllBytes(language,languageBytes);
  // A matching forged payload hash must not authenticate altered fixed assets.
  File.WriteAllText(fixedFont,"changed fixed asset");payload["fonts_russian.erp"]=Updates.Hash(fixedFont);File.WriteAllText(manifestPath,new JavaScriptSerializer().Serialize(manifest));Reject(()=>{using(var lease=new TextEngineBridge.RuntimeLease(work)){}},"manifest plus matching fixed payload rejected");File.WriteAllBytes(fixedFont,fontBytes);File.WriteAllText(manifestPath,originalManifest);
  var tampered=Updates.Read(manifestPath);var profiles=(System.Collections.IEnumerable)tampered["native_profiles"];foreach(Dictionary<string,object> profile in profiles){profile["backup_dir"]="forged";break;}File.WriteAllText(manifestPath,new JavaScriptSerializer().Serialize(tampered));Reject(()=>{using(var lease=new TextEngineBridge.RuntimeLease(work)){}},"changed profile destination rejected");File.WriteAllText(manifestPath,originalManifest);
  // A structurally compatible text-only update retains all immutable settings.
  var updated=Updates.Read(manifestPath);string originalHash=(string)updated["lng"];File.Copy(Path.Combine(work,"payload","language-original-names.lng"),language,true);string updateHash=Updates.Hash(language);
  var history=new System.Collections.ArrayList();foreach(object old in (System.Collections.IEnumerable)updated["previous_lng"])history.Add(old);if(!history.Contains(originalHash))history.Add(originalHash);updated["previous_lng"]=history;updated["lng"]=updateHash;updated["text_version"]="0.15.7";((Dictionary<string,object>)updated["payload"])["language.lng"]=updateHash;
  foreach(Dictionary<string,object> profile in (System.Collections.IEnumerable)updated["native_profiles"]){object set;if(profile.TryGetValue("language_set",out set)&&!String.IsNullOrEmpty(Convert.ToString(set)))continue;object version;if(profile.TryGetValue("game_version",out version)&&Convert.ToString(version)!="1.26"&&Convert.ToString(version)!="1.27")continue;foreach(Dictionary<string,object> item in (System.Collections.IEnumerable)profile["install_files"])if((string)item["payload"]=="language.lng")item["sha256"]=updateHash;}
  Check(Updates.InstallContractHash(updated)==Updates.InstallContractHash(Updates.Read(Path.Combine(source,"manifest.base.json"))),"valid text update keeps immutable contract");File.WriteAllText(manifestPath,new JavaScriptSerializer().Serialize(updated));using(var lease=new TextEngineBridge.RuntimeLease(work)){Check(true,"valid text update lease accepted");}
  updated["text_version"]="0.1";File.WriteAllText(manifestPath,new JavaScriptSerializer().Serialize(updated));Reject(()=>{using(var lease=new TextEngineBridge.RuntimeLease(work)){}},"downgraded mutable text rejected");
  File.WriteAllBytes(language,languageBytes);File.WriteAllText(manifestPath,originalManifest);
  File.Delete(language);using(var lease=new TextEngineBridge.RuntimeLease(work,"restore")){Check(true,"restore remains available without unused language payload");}
  bool missing=false;try{using(var lease=new TextEngineBridge.RuntimeLease(work,"install")){} }catch(FileNotFoundException){missing=true;}Check(missing,"install still requires payload");File.WriteAllBytes(language,languageBytes);

  Console.WriteLine("TEXT_BRIDGE_PASS checks="+checks+" read_only_no_elevation=true mutations_explicit_runas=true dependency_lease=true result_correlation=true actual_uac_invoked=false real_game_writes=0");return 0;
 }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
