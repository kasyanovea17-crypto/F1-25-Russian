using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;
class UpdateTests {
 static int checks=0;static string root,baseHome,home;static Dictionary<string,object> channel;static byte[] next;static JavaScriptSerializer json=new JavaScriptSerializer();
 static void Check(bool b,string m){if(!b)throw new Exception(m);checks++;}
 static void Reset(string id){home=Path.Combine(root,id);Directory.CreateDirectory(Path.Combine(home,"payload"));File.Copy(Path.Combine(baseHome,"manifest.json"),Path.Combine(home,"manifest.json"));File.Copy(Path.Combine(baseHome,"payload","language.lng"),Path.Combine(home,"payload","language.lng"));channel=Updates.Read(Path.Combine(root,"channel.json"));next=File.ReadAllBytes(Path.Combine(root,"next.lng"));}
 static byte[] Fetch(string u,int max){var b=u.EndsWith("stable.json")?Encoding.UTF8.GetBytes(json.Serialize(channel)):next;Check(b.Length<=max,"fetch limit");return b;}
 static void Reject(Action a,string label){try{a();}catch{checks++;Console.WriteLine("PASS "+label);return;}throw new Exception("Expected rejection: "+label);}
 static string P(){return Path.Combine(home,"payload","language.lng");}
 static int Main(string[] args){try{root=Path.GetFullPath(args[0]);baseHome=Path.GetFullPath(args[1]);
  Reset("same");channel["version"]="0.15";channel["sha256"]=Updates.Hash(P());channel["file"]="0.15/language.lng";Check(!Updates.Check(home,Fetch).Available,"same version");
  Reset("local");var u=Updates.Check(home,Fetch);Check(u.Available,"new version");string text=Updates.Apply(home,u,Fetch,"",false,(h,g)=>{throw new Exception("must not run engine");});Check(text.StartsWith("UPDATE PASS")&&Updates.Hash(P())==u.Hash&&Updates.LocalVersion(home)=="0.15.1","local update");Check(!File.Exists(Path.Combine(home,"update-pending.json")),"committed journal");
  Reset("game");u=Updates.Check(home,Fetch);int calls=0;Updates.Apply(home,u,Fetch,"test-game",true,(h,g)=>{Check(Updates.Hash(P())==u.Hash&&g=="test-game","engine receives new text");calls++;return "INSTALL PASS (fixture)";});Check(calls==1,"one install");
  Reset("failure");u=Updates.Check(home,Fetch);string old=Updates.Hash(P());Reject(()=>Updates.Apply(home,u,Fetch,"test-game",true,(h,g)=>{throw new IOException("simulated engine failure");}),"engine error");Check(Updates.Hash(P())==old&&Updates.LocalVersion(home)=="0.15","local rollback");Reject(()=>Updates.Check(home,Fetch),"pending operation");Check(Updates.Recover(home,(h,g)=>{Check(Updates.Hash(P())==old,"previous text recovered");return "RESTORE FIXTURE";}).StartsWith("RECOVERY PASS"),"explicit recovery");Check(!File.Exists(Path.Combine(home,"update-pending.json")),"recovery journal");
  Reset("hash");u=Updates.Check(home,Fetch);next[100]^=1;Reject(()=>Updates.Apply(home,u,Fetch,"",false,null),"bad SHA256");Check(Updates.LocalVersion(home)=="0.15","bad hash no change");
  Reset("format");u=Updates.Check(home,Fetch);next[0]=0;File.WriteAllBytes(Path.Combine(root,"bad.lng"),next);channel["sha256"]=Updates.Hash(Path.Combine(root,"bad.lng"));u=Updates.Check(home,Fetch);Reject(()=>Updates.Apply(home,u,Fetch,"",false,null),"invalid LNG with matching hash");
  Reset("traversal");channel["file"]="../../Engine.ps1";Reject(()=>Updates.Check(home,Fetch),"path traversal");
  Reset("min");channel["min_launcher"]="99.0";Reject(()=>Updates.Check(home,Fetch),"minimum launcher");
  Reset("game-version");channel["dat_original"]=new string('0',64);Reject(()=>Updates.Check(home,Fetch),"game compatibility");
  Reset("schema");channel["schema"]=5;Reject(()=>Updates.Check(home,Fetch),"unsupported schema");
  Reset("length");channel["bytes"]=99999999;Reject(()=>Updates.Check(home,Fetch),"oversize manifest");
  Reset("downgrade");channel["version"]="0.14";channel["file"]="0.14/language.lng";Reject(()=>Updates.Check(home,Fetch),"downgrade");
  Reset("same-mutation");channel["version"]="0.15";channel["file"]="0.15/language.lng";Reject(()=>Updates.Check(home,Fetch),"mutated published version");
  Reset("manual");u=Updates.Check(home,Fetch);using(var s=File.OpenWrite(P()))s.WriteByte(1);Reject(()=>Updates.Apply(home,u,Fetch,"",false,null),"manual edits preserved");Check(File.ReadAllBytes(P())[0]==1,"manual content retained");
  Reset("stale");u=Updates.Check(home,Fetch);channel["version"]="0.15.2";channel["file"]="0.15.2/language.lng";Reject(()=>Updates.Apply(home,u,Fetch,"",false,null),"stale UI selection");
  Reset("network");Reject(()=>Updates.Check(home,(url,max)=>{throw new IOException("offline fixture");}),"offline");
  Reject(()=>Updates.Download("http://localhost/Engine.ps1",100),"HTTP source");Reject(()=>Updates.Download("https://example.com/evil.lng",100),"foreign source");
  Console.WriteLine("UPDATE_TEST_PASS checks="+checks+" real_game_writes=false network_fixture=true");return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}
}
