using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

static class GameLocationTests {
 static int checks,skipped;static string root;static readonly Dictionary<string,string> markers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateSymbolicLink(string link,string target,int flags);
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
 static string Hash(string path){using(var hash=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
 static string Folder(string name){string path=Path.Combine(root,name);Directory.CreateDirectory(path);return path;}
 static void Marker(string path){File.WriteAllText(path,"fixture bytes only; not a PE image",new UTF8Encoding(false));markers[path]=Hash(path);}
 static string Game(string name){string game=Folder(name);Marker(Path.Combine(game,"F1_25.exe"));Marker(Path.Combine(game,"game.dat"));return game;}
 static string Steam(string name){string path=Folder(name);Directory.CreateDirectory(Path.Combine(path,"steamapps","common"));return path;}
 static string Escape(string value){return value.Replace("\\","\\\\").Replace("\"","\\\"");}
 static string Manifest(string install,string app="3059520"){return "\"AppState\" { "+(app==null?"":"\"appid\" \""+app+"\" ")+"\"installdir\" \""+Escape(install)+"\" }";}
 static void Put(string file,string text){Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,text,new UTF8Encoding(false));}
 static GameLocationResult Find(string saved,string launcher,string[] steam,string[] ea){return GameLocation.FindFromSources(saved,launcher,steam,ea);}
 static void BadManifest(string body,string label){bool bad=false;try{GameLocation.ParseInstallDirectory(body);}catch(InvalidDataException){bad=true;}Check(bad,label);}
 static void BadLibraries(string body,string label){bool bad=false;try{GameLocation.ParseSteamLibraries(body);}catch(InvalidDataException){bad=true;}Check(bad,label);}
 static int Main(string[] args){
  Console.OutputEncoding=new UTF8Encoding(false);
  if(args.Length>0&&args[0]=="--probe"){
   if(args.Length>2)return 2;var timer=Stopwatch.StartNew();var value=GameLocation.Find(args.Length==2?args[1]:"",AppDomain.CurrentDomain.BaseDirectory);timer.Stop();
   Console.WriteLine(new JavaScriptSerializer().Serialize(new{path=value.Path,source=value.Source,count=value.Candidates.Count,ambiguous=value.Ambiguous,candidates=value.Candidates,diagnostics=value.Diagnostics,elapsedMilliseconds=timer.ElapsedMilliseconds,registryWrites=0,gameWrites=0,gameLaunched=false}));return 0;
  }
  if(args.Length!=2){Console.Error.WriteLine("Usage: GameLocationTests.exe FIXTURE_ROOT RESULT.json | --probe [SAVED_FOLDER]");return 2;}
  string report=Path.GetFullPath(args[1]);try{
   root=Path.Combine(Path.GetFullPath(args[0]),"run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   var empty=Find("","",null,null);Check(empty.Path==""&&empty.Source==""&&empty.Candidates.Count==0&&!empty.Ambiguous,"empty sources");
   string saved=Game("saved");var result=Find(saved,"",null,null);Check(result.Path==saved&&result.Source==GameLocation.SavedSource,"valid saved folder selected");
   Check(GameLocation.IsGameDirectory(saved+Path.DirectorySeparatorChar),"trailing separator normalized");
   Check(!GameLocation.IsGameDirectory(Path.Combine(root,"missing")),"missing path rejected");
   string exeOnly=Folder("exe-only");Marker(Path.Combine(exeOnly,"F1_25.exe"));Check(!GameLocation.IsGameDirectory(exeOnly),"game.dat required");
   string datOnly=Folder("dat-only");Marker(Path.Combine(datOnly,"game.dat"));Check(!GameLocation.IsGameDirectory(datOnly),"executable marker required");
   string zeroExe=Folder("empty-exe");File.WriteAllBytes(Path.Combine(zeroExe,"F1_25.exe"),new byte[0]);Marker(Path.Combine(zeroExe,"game.dat"));Check(!GameLocation.IsGameDirectory(zeroExe),"empty executable rejected");
   string zeroDat=Folder("empty-dat");Marker(Path.Combine(zeroDat,"F1_25.exe"));File.WriteAllBytes(Path.Combine(zeroDat,"game.dat"),new byte[0]);Check(!GameLocation.IsGameDirectory(zeroDat),"empty archive rejected");
   string dirExe=Folder("directory-exe");Directory.CreateDirectory(Path.Combine(dirExe,"F1_25.exe"));Marker(Path.Combine(dirExe,"game.dat"));Check(!GameLocation.IsGameDirectory(dirExe),"directory instead of executable rejected");
   Check(!GameLocation.IsGameDirectory("\\\\127.0.0.1\\game")&&!GameLocation.IsGameDirectory("//127.0.0.1/game"),"UNC sources rejected before filesystem access");
   Check(!GameLocation.IsGameDirectory("C:relative")&&!GameLocation.IsGameDirectory("relative"),"relative sources rejected");
   Check(!GameLocation.IsGameDirectory(saved+"\n")&&!GameLocation.IsGameDirectory(saved+"\0"),"control characters rejected");
   Check(!GameLocation.IsGameDirectory(Path.Combine(saved,"..","saved")),"traversal source rejected");
   Check(!GameLocation.IsGameDirectory("\""+saved+"\""),"quoted source rejected");
   Check(!GameLocation.IsGameDirectory(saved+"\\.. "),"trailing-space component rejected");

   string steam=Steam("steam-default");string defaultGame=Game(Path.Combine("steam-default","steamapps","common","F1 25"));result=Find(Path.Combine(root,"stale"),"",new[]{steam},null);
   Check(result.Path==defaultGame&&result.Source==GameLocation.SteamSource,"stale saved path falls back to Steam");
   result=Find(saved,"",new[]{steam},null);Check(result.Path==saved&&result.Ambiguous&&result.Candidates.Count==2,"saved path wins over another detected installation");
   result=Find("","",new[]{steam,steam.ToUpperInvariant(),steam.Replace('\\','/')},null);Check(result.Candidates.Count==1&&!result.Ambiguous,"duplicate libraries deduplicated case-insensitively");

   string customSteam=Steam("steam-custom");string customName="Formula 25 — Тест";string customGame=Game(Path.Combine("steam-custom","steamapps","common",customName));Put(Path.Combine(customSteam,"steamapps","appmanifest_3059520.acf"),Manifest(customName));
   result=Find("","",new[]{customSteam},null);Check(result.Path==customGame,"manifest-specific install directory");
   string rootSteam=Steam("steam-root");string library=Steam("external-library");string externalGame=Game(Path.Combine("external-library","steamapps","common","F1 25 Custom"));Put(Path.Combine(library,"steamapps","appmanifest_3059520.acf"),Manifest("F1 25 Custom"));
   Put(Path.Combine(rootSteam,"steamapps","libraryfolders.vdf"),"// retained comment\n\"libraryfolders\" { \"0\" { \"path\" \""+Escape(rootSteam)+"\" } \"1\" {\"path\" \""+Escape(library)+"\" \"apps\" {\"3059520\" \"123456\"}} }");
   result=Find("","",new[]{rootSteam},null);Check(result.Path==externalGame&&result.Candidates.Count==1,"modern external Steam library");
   string legacy=Steam("legacy-root");Put(Path.Combine(legacy,"steamapps","libraryfolders.vdf"),"\"LibraryFolders\" { \"TimeNextStatsReport\" \"0\" \"1\" \""+Escape(library)+"\" }");result=Find("","",new[]{legacy},null);Check(result.Path==externalGame,"legacy external Steam library");
   var parsed=GameLocation.ParseSteamLibraries("\"libraryfolders\" {\"0\" {\"path\" \""+Escape(library)+"\"} \"1\" \""+Escape(library.ToUpperInvariant())+"\"}");Check(parsed.Length==1&&parsed[0]==library,"library parser deduplicates expanded paths");
   parsed=GameLocation.ParseSteamLibraries("\"libraryfolders\" {\"0\" \"\\\\\\\\server\\\\game\" \"1\" \"relative\" \"2\" \"C:\\\\bad\\\\..\\\\game\"}");Check(parsed.Length==0,"invalid network/relative/traversal library paths ignored");
   parsed=GameLocation.ParseSteamLibraries("libraryfolders { 0 {path \""+Escape(library)+"\" } }");Check(parsed.Length==1,"bare-key Valve metadata accepted");
   BadLibraries("\"libraryfolders\" { \"1\" {\"path\" \"C:\\\\Games\" \"path\" \"D:\\\\Games\"}}","duplicate path field rejected");
   BadLibraries("\"libraryfolders\" { \"0\" \"C:\\\\Steam\"","unterminated VDF object rejected");
   BadLibraries("\"libraryfolders\" { \"0\" \"C:\\\\Steam }","unterminated VDF string rejected");
   BadLibraries("\"libraryfolders\" {} }","unmatched closing brace rejected");
   BadLibraries("\"libraryfolders\"","missing object rejected");
   BadLibraries("\"libraryfolders\" {} \"libraryfolders\" {}","duplicate root rejected");
   BadLibraries(new string('x',1048577),"oversized VDF rejected");
   BadLibraries("\"libraryfolders\" {\"0\" \""+new string('x',32769)+"\"}","oversized token rejected");
   string nesting="libraryfolders {";for(int i=0;i<30;i++)nesting+=" x {";nesting+="x y";for(int i=0;i<31;i++)nesting+="}";BadLibraries(nesting,"excessive nesting rejected");

   Check(GameLocation.ParseInstallDirectory(Manifest("F1 25"))=="F1 25","manifest AppState accepted");
   Check(GameLocation.ParseInstallDirectory(Manifest(customName,null))==customName,"optional appid permitted with valid folder");
   BadManifest(Manifest("F1 25","1"),"wrong appid rejected");
   BadManifest("AppState {appid 3059520 appid 3059520 installdir \"F1 25\"}","duplicate appid rejected");
   BadManifest("AppState {installdir \"F1 25\" installdir other}","duplicate installdir rejected");
   BadManifest("AppState {appid 3059520}","missing installdir rejected");
   foreach(string unsafeName in new[]{"..",".","../escape","..\\escape","C:\\Games","with/slash","with\\slash","with:colon","CON","NUL.txt","LPT1","COM0","trailing."," trailing","trailing ","line\nbreak"})BadManifest(Manifest(unsafeName),"unsafe manifest folder: "+unsafeName.Replace('\n',' '));
   BadManifest("AppState {installdir {a b}}","object instead of installdir rejected");
   BadManifest("AppState \"value\"","string instead of AppState rejected");
   string wrongSteam=Steam("wrong-app");Game(Path.Combine("wrong-app","steamapps","common","F1 25"));Put(Path.Combine(wrongSteam,"steamapps","appmanifest_3059520.acf"),Manifest("F1 25","100"));result=Find("","",new[]{wrongSteam},null);Check(result.Path==""&&result.Diagnostics.Contains("appid"),"present contradictory manifest does not fall back to default folder");
   string hugeSteam=Steam("huge-metadata");Put(Path.Combine(hugeSteam,"steamapps","appmanifest_3059520.acf"),new string('x',1048577));result=Find("","",new[]{hugeSteam},null);Check(result.Path==""&&result.Diagnostics.Contains("1 МиБ"),"oversized metadata skipped without discovery failure");
   string badEncoding=Steam("bad-encoding");File.WriteAllBytes(Path.Combine(badEncoding,"steamapps","appmanifest_3059520.acf"),new byte[]{0xff,0xff,0xff});result=Find("","",new[]{badEncoding},null);Check(result.Path=="","invalid UTF-8 metadata skipped");
   string ea=Game("EA install custom");result=Find("","",null,new[]{ea});Check(result.Path==ea&&result.Source==GameLocation.EaSource,"EA install location");
   result=Find("","",new[]{steam},new[]{ea});Check(result.Ambiguous&&result.Path==defaultGame&&result.Candidates.Count==2,"ambiguity deterministic Steam before EA");
   result=Find("",ea,null,null);Check(result.Path==ea&&result.Source==GameLocation.AdjacentSource,"launcher beside game markers");
   string adjacent=Folder("adjacent");string adjacentGame=Game(Path.Combine("adjacent","F1 25"));result=Find("",adjacent,null,null);Check(result.Path==adjacentGame,"launcher explicit neighboring F1 25 folder");
   var roots=new CountingRoots();result=GameLocation.FindFromSources("","",roots,null);Check(roots.Reads<=33&&result.Path=="","source enumeration bounded");

   string linked=Path.Combine(root,"linked-game");bool linkCreated=CreateSymbolicLink(linked,saved,3);if(linkCreated)Check(!GameLocation.IsGameDirectory(linked),"directory symlink/reparse path rejected");else skipped++;
   string linkFiles=Folder("linked-marker");Marker(Path.Combine(linkFiles,"game.dat"));linkCreated=CreateSymbolicLink(Path.Combine(linkFiles,"F1_25.exe"),Path.Combine(saved,"F1_25.exe"),2);if(linkCreated)Check(!GameLocation.IsGameDirectory(linkFiles),"file symlink/reparse marker rejected");else skipped++;
   foreach(var marker in markers)Check(File.Exists(marker.Key)&&Hash(marker.Key)==marker.Value,"fixture marker bytes unchanged");
   var reportData=new{result="GAME_LOCATION_PASS",checks=checks,skippedSymlinkTests=skipped,fixtureRoot=root,registryCalls=0,registryWrites=0,gameWrites=0,gameLaunched=false,fixtureMarkersUnchanged=markers.Count};
   Directory.CreateDirectory(Path.GetDirectoryName(report));File.WriteAllText(report,new JavaScriptSerializer().Serialize(reportData),new UTF8Encoding(false));Console.WriteLine("GAME_LOCATION_PASS checks="+checks+" symlink_skipped="+skipped+" registry_calls=0 game_writes=0 game_launched=false markers_unchanged="+markers.Count);return 0;
  }catch(Exception ex){Directory.CreateDirectory(Path.GetDirectoryName(report));File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{result="GAME_LOCATION_FAIL",checks=checks,error=ex.ToString()}),new UTF8Encoding(false));Console.Error.WriteLine(ex);return 1;}
 }
 sealed class CountingRoots:IEnumerable<string>{public int Reads;public IEnumerator<string> GetEnumerator(){while(true){Reads++;yield return "";}}IEnumerator IEnumerable.GetEnumerator(){return GetEnumerator();}}
}
