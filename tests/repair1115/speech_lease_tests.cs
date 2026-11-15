using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// UI-only stubs: compile the real SpeechUI.cs without starting the launcher.
static class Shape { public static GraphicsPath Round(Rectangle r,int n){var p=new GraphicsPath();p.AddRectangle(r);return p;} }
sealed class UiButton : Button { public bool Primary; public int Glyph; }
sealed partial class Launcher : Form {
 bool busy=false,testing=true; string home="",lastLog=""; TextBox path=new TextBox(),logBox=new TextBox();
 public Launcher(){speechButton=new Button();GC.KeepAlive(speechButton);GC.KeepAlive(lastLog);}
}

static class SpeechLeaseTests {
 static int checks;
 static readonly string[] Files={"SpeechHost.ps1","SpeechEngine.ps1","ModelBootstrap.ps1","commands.ru.json"};
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
 static string Copy(string package,string root,string name){string p=Path.Combine(root,name);Directory.CreateDirectory(Path.Combine(p,"speech"));foreach(string n in Files)File.Copy(Path.Combine(package,"speech",n),Path.Combine(p,"speech",n));return p;}
 static bool Rejected(Action a){try{a();return false;}catch(IOException){return true;}catch(TargetInvocationException e){if(e.InnerException is IOException)return true;throw;}}
 static IDisposable Lease(Type t,string home){return (IDisposable)Activator.CreateInstance(t,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{home},null);}
 static IDisposable Lease(Type t,string home,string action){return (IDisposable)Activator.CreateInstance(t,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{home,action},null);}
 public static int Main(string[] args){try{
  string package=Path.GetFullPath(args[0]),root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);
  // This harmless substituted host writes only fixture output, never game/registry data.
  string changed=Copy(package,root,"substituted-host"),marker=Path.Combine(changed,"marker.txt");
  string fake="param([string]$Action,[string]$GamePath,[string]$ResultPath)\r\n"+
   "[IO.File]::WriteAllText((Join-Path $PSScriptRoot '..\\marker.txt'),'fixture host ran')\r\n"+
   "[IO.File]::WriteAllText($ResultPath,'{\"status\":\"ready\",\"code\":\"fixture\",\"message\":\"fixture\",\"modelInstalled\":true,\"installed\":false,\"needsRestart\":false,\"gameRunning\":false,\"canInstall\":true,\"canRestore\":false,\"commandCount\":37,\"details\":null}')\r\nexit 0\r\n";
  File.WriteAllText(Path.Combine(changed,"speech","SpeechHost.ps1"),fake,new UTF8Encoding(true));
  bool rejected=Rejected(()=>{SpeechWindow.ExecuteHost(changed,"status",root,null);});
  if(!rejected||File.Exists(marker)){Console.WriteLine("SPEECH_INTEGRITY_FAIL substituted_host_executed="+File.Exists(marker).ToString().ToLowerInvariant()+" game_writes=0 registry_writes=0");return 1;}
  Check(rejected&&!File.Exists(marker),"substituted host must be rejected before any process");
  Type type=typeof(SpeechWindow).GetNestedType("RuntimeLease",BindingFlags.NonPublic);
  Check(type!=null,"runtime lease exists");
  string valid=Copy(package,root,"valid");
  using(Lease(type,valid)){
   Check(Rejected(()=>Directory.Move(Path.Combine(valid,"speech"),Path.Combine(valid,"speech-moved"))),"lease blocks runtime directory rename");
   foreach(string f in Files){
   string p=Path.Combine(valid,"speech",f);
   Check(File.ReadAllBytes(p).Length>0,"lease permits child read "+f);
   Check(Rejected(()=>{using(var x=new FileStream(p,FileMode.Open,FileAccess.Write,FileShare.ReadWrite)){};}),"lease blocks writes "+f);
   Check(Rejected(()=>File.Delete(p)),"lease blocks delete "+f);
   Check(Rejected(()=>File.Move(p,p+".moved")),"lease blocks rename "+f);
   }
  }
  foreach(string f in Files){
   string p=Path.Combine(valid,"speech",f);byte[] original=File.ReadAllBytes(p);File.AppendAllText(p,"\nfixture alteration");
   Check(Rejected(()=>{using(Lease(type,valid)){};}),"pin detects changed dependency "+f);
   File.WriteAllBytes(p,original);
   // Failed acquisition must release all already opened streams.
   using(var x=new FileStream(Path.Combine(valid,"speech","SpeechHost.ps1"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(x.Length>0,"failed lease releases streams "+f);
  }
  Check(Rejected(()=>{using(Lease(type,Path.Combine(root,"missing"))){};}),"missing package rejected");
  string recovery=Copy(package,root,"restore-no-unused-dependencies");
  File.Delete(Path.Combine(recovery,"speech","commands.ru.json"));
  using(Lease(type,recovery,"restore")){checks++;Check(Rejected(()=>File.Delete(Path.Combine(recovery,"speech","SpeechEngine.ps1"))),"restore still locks engine with missing dictionary");}
  File.Delete(Path.Combine(recovery,"speech","ModelBootstrap.ps1"));
  using(Lease(type,recovery,"restore")){checks++;Check(Rejected(()=>File.Delete(Path.Combine(recovery,"speech","SpeechHost.ps1"))),"restore still locks host with missing model installer");}
  Check(Rejected(()=>{using(Lease(type,recovery,"install")){};}),"install still requires dictionary");
  File.AppendAllText(Path.Combine(recovery,"speech","SpeechEngine.ps1"),"\nfixture alteration");
  Check(Rejected(()=>{using(Lease(type,recovery,"restore")){};}),"restore rejects changed executable dependency");
  string model=Copy(package,root,"model-no-unused-dependencies");
  File.Delete(Path.Combine(model,"speech","SpeechEngine.ps1"));File.Delete(Path.Combine(model,"speech","commands.ru.json"));
  using(Lease(type,model,"model")){checks++;Check(Rejected(()=>File.Delete(Path.Combine(model,"speech","ModelBootstrap.ps1"))),"model action still locks installer");}
  MethodInfo validate=typeof(SpeechWindow).GetMethod("ValidateExitCode",BindingFlags.Static|BindingFlags.NonPublic);
  Check(validate!=null,"exit validation exists");
  foreach(string status in new[]{"ready","installed","restored"}){
   var response=new SpeechState{Status=status,Message="fixture"};validate.Invoke(null,new object[]{response,0});checks++;
   Check(Rejected(()=>validate.Invoke(null,new object[]{response,1})),"failure exit cannot claim "+status);
  }
  foreach(string status in new[]{"error","failed","conflict","rolled_back"}){
   var response=new SpeechState{Status=status,Message="fixture"};validate.Invoke(null,new object[]{response,1});checks++;
   Check(Rejected(()=>validate.Invoke(null,new object[]{response,0})),"success exit cannot claim "+status);
  }
  var pending=new SpeechState{Status="not_ready",Message="fixture"};validate.Invoke(null,new object[]{pending,2});checks++;
  Check(Rejected(()=>validate.Invoke(null,new object[]{pending,7})),"unknown exit rejected");
  Console.WriteLine("SPEECH_INTEGRITY_PASS checks="+checks+" runtime_files=4 substituted_host_executed=false game_writes=0 registry_writes=0");return 0;
 }catch(Exception e){Console.WriteLine("SPEECH_INTEGRITY_FAIL "+e);return 1;}}
}
