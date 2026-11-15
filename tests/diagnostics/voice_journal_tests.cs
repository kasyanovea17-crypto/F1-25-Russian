using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.IO.Compression;

// Only drawing/launcher shell are stubbed. Voice backends and journal are production classes.
static class Shape { public static GraphicsPath Round(Rectangle r,int n){var p=new GraphicsPath();p.AddRectangle(r);return p;} }
sealed class UiButton : Button { public bool Primary; public int Glyph; }
sealed partial class Launcher : Form {
 bool busy=false,testing=true; string home="",lastLog=""; TextBox path=new TextBox(),logBox=new TextBox();
 public Launcher(){speechButton=new Button();GC.KeepAlive(speechButton);GC.KeepAlive(lastLog);}
}
static class VoiceJournalTests {
 static int checks;
 static void Check(bool b,string why){if(!b)throw new Exception(why);checks++;}
 static object[] Read(string path){return (object[])new JavaScriptSerializer{MaxJsonLength=3000000,RecursionLimit=30}.DeserializeObject(File.ReadAllText(path));}
 static string T(Dictionary<string,object> e,string k){object v;return e.TryGetValue(k,out v)?Convert.ToString(v):"";}
 static object Invoke(Type type,string home,string action,string game){
  if(type==typeof(SpeechWindow))return SpeechWindow.ExecuteHost(home,action,game,null);
  return type.GetMethod("Execute",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{home,action,game});
 }
 static Exception InvokeFailure(Type type,string home,string action,string game){try{Invoke(type,home,action,game);return null;}catch(TargetInvocationException e){return e.InnerException;}catch(Exception e){return e;}}
 [STAThread] public static int Main(string[] args){try{
  string source=Path.GetFullPath(args[0]),root=Path.GetFullPath(args[1]);if(Directory.Exists(root))throw new Exception("Preserve prior evidence");Directory.CreateDirectory(root);
  string store=Path.Combine(root,"journal"),missing=Path.Combine(root,"missing-game"),missingRuntime=Path.Combine(root,"missing-runtime");
  SupportDiagnostics.InitializeForTests(source,store);
  Type[] types={typeof(MaxVoiceWindow),typeof(SpeechWindow),typeof(EngineerVoiceWindow)};
  string[] components={"max_voice","speech_commands","engineer_voice"};
  for(int i=0;i<types.Length;i++){
   foreach(string action in new[]{"status",i==2?"russian":"install"}){
    Exception error=InvokeFailure(types[i],missingRuntime,action,missing);
    Check(error is IOException,"runtime failure retained: "+components[i]+" "+action);
    object[] all=Read(Path.Combine(store,"events.json"));var end=(Dictionary<string,object>)all[all.Length-1];
    Check(T(end,"kind")=="end"&&T(end,"exitCode")=="-1","no invented process exit: "+components[i]);
    Check(T(end,"exception").Contains(error.GetType().FullName)&&T(end,"exception").Contains("HResult=0x"),"typed failure retained");
    string id=T(end,"operation");int begin=0,stage=0;
    foreach(Dictionary<string,object> e in all){if(T(e,"operation")!=id)continue;if(T(e,"kind")=="begin"){begin++;Check(T(e,"component")==components[i]&&T(e,"action")==action,"component/action correlated");}if(T(e,"kind")=="stage"){stage++;Check(T(e,"stage")=="runtime-validation","failure located before host");}}
    Check(begin==1&&stage==1,"exact failed operation sequence");
    Check(!Directory.Exists(missing)&&!Directory.Exists(missingRuntime),"absent game/runtime untouched");
   }
  }
  // Valid runtimes perform only status against an absent fixture game. Never invoke runas.
  for(int i=0;i<types.Length;i++){
   Invoke(types[i],source,"status",missing);
   object[] all=Read(Path.Combine(store,"events.json"));var end=(Dictionary<string,object>)all[all.Length-1];string id=T(end,"operation");
   Check(T(end,"kind")=="end"&&T(end,"exitCode")=="1","actual missing-game backend exit recorded: "+components[i]);
   Check(T(end,"detail").Contains("error")&&!end.ContainsKey("exception"),"backend response distinguished from C# exception");
   var stages=new List<string>();foreach(Dictionary<string,object> e in all)if(T(e,"operation")==id&&T(e,"kind")=="stage")stages.Add(T(e,"stage"));
   Check(String.Join(",",stages.ToArray())=="runtime-validation,temporary-result,host-launch,host-wait,result-validation","complete actual host sequence");
   Check(!Directory.Exists(missing),"status does not create game");
  }
  string zip=Path.Combine(root,"VOICE_REPORT.zip");Check(SupportDiagnostics.Export(zip,source,missing,"Fixture: no installed game") == zip,"export saved");
  using(var stream=File.OpenRead(zip))using(var z=new ZipArchive(stream,ZipArchiveMode.Read)){
   Check(z.GetEntry("SUMMARY.txt")!=null&&z.GetEntry("REPORT.json")!=null&&z.GetEntry("EVENTS.json")!=null,"three export members");
   using(var r=new StreamReader(z.GetEntry("EVENTS.json").Open())){string text=r.ReadToEnd();Check(text.Contains("max_voice")&&text.Contains("speech_commands")&&text.Contains("engineer_voice"),"all voice modules exported");Check(!text.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace("\\","\\\\")),"profile hidden in export");}
  }
  SupportDiagnostics.InitializeForTests(source,store);Check(Read(Path.Combine(store,"events.json")).Length>0,"journal survives reinitialize");
  Console.WriteLine("VOICE_JOURNAL_PASS checks="+checks+" modules=3 prelaunch_failures=6 readonly_host_calls=3 game_writes=0 uac_invoked=false");return 0;
 }catch(Exception e){Console.WriteLine("VOICE_JOURNAL_FAIL "+e);return 1;}}
}
