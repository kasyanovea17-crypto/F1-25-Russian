using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

class GameTextProfileTests {
 static int checks,fetches,engineCalls;static string root,home,game;static JavaScriptSerializer json=new JavaScriptSerializer();
 static void Check(bool b,string label){if(!b)throw new Exception(label);checks++;}
 static byte[] Fetch(string url,int maximum){fetches++;throw new IOException("FETCH SENTINEL");}
 static void Reject(Action action,string label){try{action();}catch{checks++;return;}throw new Exception("Expected rejection: "+label);}
 static void Setup(string id,string version,bool unknown){
  home=Path.Combine(root,id);game=Path.Combine(home,"game");Directory.CreateDirectory(game);Directory.CreateDirectory(Path.Combine(home,"payload"));
  File.WriteAllText(Path.Combine(game,"F1_25.exe"),"STATIC IDENTITY FIXTURE "+id);
  File.WriteAllText(Path.Combine(home,"payload","language.lng"),"PAYLOAD SENTINEL");
  var profile=new Dictionary<string,object>{{"exe_sha256",unknown?new string('0',64):Updates.Hash(Path.Combine(game,"F1_25.exe"))},{"game_version",version}};
  if(version!="1.26")profile["language_set"]=version;
  File.WriteAllText(Path.Combine(home,"manifest.json"),json.Serialize(new Dictionary<string,object>{{"native_profiles",new object[]{profile}}}));fetches=engineCalls=0;
 }
 static string Snapshot(){var files=Directory.GetFiles(home,"*",SearchOption.AllDirectories);Array.Sort(files,StringComparer.Ordinal);var text=new StringBuilder();foreach(string file in files)text.Append(file.Substring(home.Length)).Append(' ').Append(Updates.Hash(file)).Append('\n');return text.ToString();}
 static void Apply(bool install){Updates.Apply(home,new TextUpdate{Available=true},Fetch,game,install,(h,g)=>{engineCalls++;return "ENGINE SENTINEL";});}
 static int Main(string[] args){try{root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
#if BASELINE
  Setup("baseline-legacy","1.18",false);Reject(()=>Apply(true),"baseline fetch sentinel");
  Check(fetches==1&&engineCalls==0&&File.Exists(Path.Combine(home,"update.lock")),"baseline reaches fetch and creates lock for legacy");
  Console.WriteLine("BASELINE_GATE observed_fetch_calls="+fetches+" lock_created=true engine_calls="+engineCalls+" legacy_blocked_before_fetch=false");
#else
  foreach(string version in new[]{"1.18","1.24"})foreach(bool install in new[]{false,true}){
   Setup("legacy-"+version+"-"+install,version,false);string before=Snapshot();Reject(()=>Apply(install),"legacy apply");
   Check(fetches==0&&engineCalls==0,"legacy no fetch or engine");Check(!File.Exists(Path.Combine(home,"update.lock")),"legacy no lock");Check(Snapshot()==before,"legacy no mutation");
   Reject(()=>Updates.Check(home,Fetch,game),"legacy check");Check(fetches==0&&Snapshot()==before,"legacy check no fetch/mutation");
  }
  foreach(bool install in new[]{false,true}){
   Setup("unknown-"+install,"1.26",true);string before=Snapshot();Reject(()=>Apply(install),"unknown apply");
   Check(fetches==0&&!File.Exists(Path.Combine(home,"update.lock"))&&Snapshot()==before,"unknown no fetch lock mutation");
  }
  Setup("empty-game","1.26",false);string emptyBefore=Snapshot();Reject(()=>Updates.Apply(home,new TextUpdate(),Fetch,"",true,null),"empty install target");Check(fetches==0&&Snapshot()==emptyBefore,"empty install no fetch/mutation");
  foreach(string version in new[]{"1.18","1.24","unknown"}){
   Setup("recover-"+version,version=="unknown"?"1.26":version,version=="unknown");
   File.WriteAllText(Path.Combine(home,"update-pending.json"),json.Serialize(new Dictionary<string,object>{{"install",true},{"game",game},{"transaction",new string('a',32)}}));
   string before=Snapshot();Reject(()=>Updates.Recover(home,(h,g)=>{engineCalls++;return "BAD";}),"legacy/unknown recover");
   Check(engineCalls==0&&!File.Exists(Path.Combine(home,"update.lock"))&&Snapshot()==before,"recovery rejects before lock/mutation");
  }
  Setup("current","1.26",false);Reject(()=>Apply(false),"current reaches fetch sentinel");Check(fetches==1&&File.Exists(Path.Combine(home,"update.lock")),"current stable accepted");
  Setup("package-only","1.26",false);Reject(()=>Updates.Apply(home,new TextUpdate(),Fetch,"",false,null),"package-only reaches fetch sentinel");Check(fetches==1,"package-only compatibility");
  string context=Path.Combine(root,"profile-context");var stable=GameTextProfiles.Parse("PROFILE game=1.26 records=56134 text=0.15.5 channel=stable\r\nPREPARE PASS",context,"russian");
  Check(stable.Stable&&stable.Matches(context,"russian")&&!stable.Matches(context+"-other","russian")&&!stable.Matches(context,"original_names"),"profile binding");
  Reject(()=>GameTextProfiles.Parse("PROFILE game=1.18 records=56439 text=0.15.5.118 channel=stable",context,"russian"),"legacy stable mismatch");
  Reject(()=>GameTextProfiles.Parse("PROFILE game=1.24 records=56134 text=0.15.5.124 channel=pinned",context,"russian"),"record mismatch");
  Reject(()=>GameTextProfiles.Parse("PREPARE PASS",context,"russian"),"missing profile");
  Console.WriteLine("PROFILE_GATE_PASS checks="+checks+" legacy_fetch_calls=0 unknown_fetch_calls=0 rejected_lock_writes=0 real_game_writes=false");
#endif
  return 0;
 }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
