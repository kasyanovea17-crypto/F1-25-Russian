using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.Win32;

sealed class GameLocationCandidate {
 public string Path { get; private set; }
 public string Source { get; private set; }
 internal GameLocationCandidate(string path,string source){Path=path;Source=source;}
}
sealed class GameLocationResult {
 public string Path { get; private set; }
 public string Source { get; private set; }
 public string Diagnostics { get; private set; }
 public ReadOnlyCollection<GameLocationCandidate> Candidates { get; private set; }
 public bool Ambiguous { get { return Candidates.Count>1; } }
 internal GameLocationResult(List<GameLocationCandidate> candidates,string diagnostics){Candidates=candidates.AsReadOnly();Path=candidates.Count==0?"":candidates[0].Path;Source=candidates.Count==0?"":candidates[0].Source;Diagnostics=diagnostics;}
}

// Discovery reads only bounded local metadata. Resource compatibility remains
// Engine's job; finding marker files never prepares or installs the translation.
static class GameLocation {
 const int MaximumTextBytes=1048576,MaximumPath=1024,MaximumRoots=32,MaximumCandidates=64;
 const string AppId="3059520";
 public const string SavedSource="Сохранённая папка",SteamSource="Steam",EaSource="EA app",AdjacentSource="Папка лаунчера";

 public static GameLocationResult Find(string savedFolder,string launcherDirectory){
  var steam=new List<string>();var ea=new List<string>();var diagnostics=new StringBuilder();
  ReadSteamRegistry(steam,diagnostics);ReadEaRegistry(ea,diagnostics);
  foreach(string programFiles in ProgramFiles()){
   AddRoot(steam,System.IO.Path.Combine(programFiles,"Steam"));
   AddRoot(ea,System.IO.Path.Combine(programFiles,"EA Games","F1 25"));
   AddRoot(ea,System.IO.Path.Combine(programFiles,"Origin Games","F1 25"));
  }
  return Discover(savedFolder,launcherDirectory,steam,ea,diagnostics);
 }

 // Explicit sources make fixture tests independent of the machine's registry.
 // steamRoots are Steam/library roots; eaInstallFolders are complete game paths.
 public static GameLocationResult FindFromSources(string savedFolder,string launcherDirectory,IEnumerable<string> steamRoots,IEnumerable<string> eaInstallFolders){
  return Discover(savedFolder,launcherDirectory,steamRoots,eaInstallFolders,new StringBuilder());
 }

 public static bool IsGameDirectory(string path){
  string canonical;if(!TryDirectory(path,out canonical)||!Directory.Exists(canonical)||!OrdinaryPath(canonical,true))return false;
  return OrdinaryFile(System.IO.Path.Combine(canonical,"F1_25.exe"),true)&&OrdinaryFile(System.IO.Path.Combine(canonical,"game.dat"),true);
 }

 static GameLocationResult Discover(string savedFolder,string launcherDirectory,IEnumerable<string> steamRoots,IEnumerable<string> eaFolders,StringBuilder diagnostics){
  var found=new List<GameLocationCandidate>();var seenGames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  AddCandidate(found,seenGames,savedFolder,SavedSource,diagnostics);
  var libraries=new List<string>();var seenLibraries=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int count=0;
  if(steamRoots!=null)foreach(string raw in steamRoots){
   if(count++>=MaximumRoots){Note(diagnostics,"Лимит источников Steam: "+MaximumRoots+".");break;}
   string root;if(!TryDirectory(raw,out root)||!Directory.Exists(root)||!OrdinaryPath(root,true))continue;
   AddUnique(libraries,seenLibraries,root);
   string metadata=System.IO.Path.Combine(root,"steamapps","libraryfolders.vdf");
   if(!File.Exists(metadata))continue;
   try{foreach(string library in ParseSteamLibraries(ReadMetadata(metadata)))AddUnique(libraries,seenLibraries,library);}
   catch(Exception ex){if(!Expected(ex))throw;Note(diagnostics,"libraryfolders.vdf: "+ex.Message);}
  }
  foreach(string library in libraries){
   if(found.Count>=MaximumCandidates)break;
   string manifest=System.IO.Path.Combine(library,"steamapps","appmanifest_"+AppId+".acf");
   if(File.Exists(manifest)){
    try{string name=ParseInstallDirectory(ReadMetadata(manifest));AddCandidate(found,seenGames,System.IO.Path.Combine(library,"steamapps","common",name),SteamSource,diagnostics);}
    catch(Exception ex){if(!Expected(ex))throw;Note(diagnostics,"appmanifest_"+AppId+".acf: "+ex.Message);}
   }else AddCandidate(found,seenGames,System.IO.Path.Combine(library,"steamapps","common","F1 25"),SteamSource,diagnostics);
  }
  count=0;if(eaFolders!=null)foreach(string folder in eaFolders){if(count++>=MaximumRoots){Note(diagnostics,"Лимит источников EA app: "+MaximumRoots+".");break;}AddCandidate(found,seenGames,folder,EaSource,diagnostics);}
  string adjacent;if(TryDirectory(launcherDirectory,out adjacent)){
   AddCandidate(found,seenGames,adjacent,AdjacentSource,diagnostics);
   AddCandidate(found,seenGames,System.IO.Path.Combine(adjacent,"F1 25"),AdjacentSource,diagnostics);
  }
  Note(diagnostics,"Найдено установок: "+found.Count.ToString(CultureInfo.InvariantCulture)+".");
  if(found.Count>1)Note(diagnostics,found[0].Source==SavedSource?"Сохранён ранее выбранный путь; найдены и другие установки.":"Выбрана первая установка в порядке Steam → EA app → папка лаунчера. Путь можно изменить вручную.");
  return new GameLocationResult(found,diagnostics.ToString());
 }

 static void AddCandidate(List<GameLocationCandidate> found,HashSet<string> seen,string raw,string source,StringBuilder diagnostics){
  if(found.Count>=MaximumCandidates)return;
  string path;if(!TryDirectory(raw,out path)||seen.Contains(path)||!IsGameDirectory(path))return;
  seen.Add(path);found.Add(new GameLocationCandidate(path,source));Note(diagnostics,source+": "+path);
 }
 static void AddUnique(List<string> roots,HashSet<string> seen,string raw){string path;if(roots.Count<MaximumRoots&&TryDirectory(raw,out path)&&seen.Add(path))roots.Add(path);}
 static void AddRoot(List<string> roots,string raw){if(roots.Count>=MaximumRoots)return;string path;if(!TryDirectory(raw,out path))return;foreach(string old in roots)if(String.Equals(old,path,StringComparison.OrdinalIgnoreCase))return;roots.Add(path);}
 static void Note(StringBuilder text,string message){const int maximum=8000;if(text.Length>=maximum)return;int remaining=maximum-text.Length;string line=message+Environment.NewLine;text.Append(line.Length>remaining?line.Substring(0,remaining):line);}
 static bool Expected(Exception ex){return ex is IOException||ex is InvalidDataException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is NotSupportedException||ex is SecurityException;}

 static bool TryDirectory(string raw,out string canonical){
  canonical="";if(String.IsNullOrWhiteSpace(raw)||raw.Length>MaximumPath)return false;foreach(char c in raw)if(Char.IsControl(c))return false;
  string value=raw.Trim();if(value.Length<3||!Char.IsLetter(value[0])||value[1]!=':'||(value[2]!='\\'&&value[2]!='/'))return false;
  for(int i=0;i<value.Length;i++)if(Char.IsControl(value[i])||value[i]=='"'||value[i]=='*'||value[i]=='?'||value[i]=='<'||value[i]=='>'||value[i]=='|'||(value[i]==':'&&i!=1))return false;
  foreach(string component in value.Substring(3).Split(new[]{'\\','/'},StringSplitOptions.RemoveEmptyEntries))if(component=="."||component==".."||component.EndsWith(".",StringComparison.Ordinal)||component.EndsWith(" ",StringComparison.Ordinal))return false;
  try{string full=System.IO.Path.GetFullPath(value);string root=System.IO.Path.GetPathRoot(full);canonical=full.Length>root.Length?full.TrimEnd('\\','/'):full;return canonical.Length<=MaximumPath;}
  catch(Exception ex){if(!Expected(ex))throw;return false;}
 }
 static bool OrdinaryPath(string path,bool directory){
  try{
   if(((File.GetAttributes(path)&FileAttributes.Directory)!=0)!=directory)return false;
   for(string current=path;!String.IsNullOrEmpty(current);current=System.IO.Path.GetDirectoryName(current))if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)return false;
   return true;
  }catch(Exception ex){if(!Expected(ex))throw;return false;}
 }
 static bool OrdinaryFile(string path,bool nonempty){
  try{return File.Exists(path)&&OrdinaryPath(path,false)&&(!nonempty||new FileInfo(path).Length>0);}
  catch(Exception ex){if(!Expected(ex))throw;return false;}
 }
 static string ReadMetadata(string path){
  if(!OrdinaryFile(path,false))throw new InvalidDataException("метаданные недоступны или являются ссылкой.");
  using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
   if(stream.Length>MaximumTextBytes)throw new InvalidDataException("метаданные превышают 1 МиБ.");
   using(var reader=new StreamReader(stream,new UTF8Encoding(false,true),true,4096)){
    var buffer=new char[4096];var text=new StringBuilder();int read;
    while((read=reader.Read(buffer,0,buffer.Length))>0){if(text.Length+read>MaximumTextBytes)throw new InvalidDataException("метаданные превышают 1 МиБ.");text.Append(buffer,0,read);}
    return text.ToString();
   }
  }
 }

 public static string[] ParseSteamLibraries(string text){
  var root=Single(new Vdf(text).Parse(),"libraryfolders",true);var result=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var entry in root.Children){int number;if(!Int32.TryParse(entry.Key,NumberStyles.None,CultureInfo.InvariantCulture,out number)||number<0||number>9999)continue;
   string value=entry.Value;if(entry.Children!=null){var item=Single(entry.Children,"path",false);value=item==null?null:item.Value;}
   AddUnique(result,seen,value);
  }
  return result.ToArray();
 }
 public static string ParseInstallDirectory(string text){
  var app=Single(new Vdf(text).Parse(),"AppState",true);var id=Single(app.Children,"appid",false);if(id!=null&&id.Value!=AppId)throw new InvalidDataException("AppState.appid не относится к F1 25.");
  var install=Single(app.Children,"installdir",false);string value=install==null?null:install.Value;
  if(String.IsNullOrWhiteSpace(value)||value.Length>120||value!=value.Trim()||value=="."||value==".."||value.EndsWith(".",StringComparison.Ordinal)||value.IndexOfAny(System.IO.Path.GetInvalidFileNameChars())>=0||value.IndexOf('/')>=0||value.IndexOf('\\')>=0)throw new InvalidDataException("installdir должен содержать одно имя папки.");
  foreach(char c in value)if(Char.IsControl(c))throw new InvalidDataException("installdir содержит управляющий символ.");
  string stem=value.Split('.')[0].ToUpperInvariant();if(stem=="CON"||stem=="PRN"||stem=="AUX"||stem=="NUL"||(stem.Length==4&&(stem.StartsWith("COM",StringComparison.Ordinal)||stem.StartsWith("LPT",StringComparison.Ordinal))&&stem[3]>='0'&&stem[3]<='9'))throw new InvalidDataException("installdir содержит зарезервированное имя.");
  return value;
 }
 static Entry Single(List<Entry> entries,string key,bool objectRequired){
  Entry match=null;foreach(var entry in entries)if(String.Equals(entry.Key,key,StringComparison.OrdinalIgnoreCase)){if(match!=null)throw new InvalidDataException("Повторяющееся поле "+key+".");match=entry;}
  if(objectRequired&&(match==null||match.Children==null))throw new InvalidDataException("Отсутствует объект "+key+".");
  if(!objectRequired&&match!=null&&match.Children!=null)throw new InvalidDataException("Неверное строковое поле "+key+".");
  return match;
 }
 sealed class Entry {public string Key,Value;public List<Entry> Children;}
 sealed class Vdf {
  readonly string text;int at,tokens;const int MaximumTokens=65536;
  internal Vdf(string source){if(source==null||source.Length>MaximumTextBytes)throw new InvalidDataException("Неверный размер VDF.");text=source;}
  internal List<Entry> Parse(){var result=Object(0,false);Skip();if(at!=text.Length)throw new InvalidDataException("Лишние данные VDF.");return result;}
  List<Entry> Object(int depth,bool nested){
   if(depth>24)throw new InvalidDataException("VDF слишком глубоко вложен.");var values=new List<Entry>();
   while(true){Skip();if(at==text.Length){if(nested)throw new InvalidDataException("Незавершённый объект VDF.");return values;}if(text[at]=='}'){if(!nested)throw new InvalidDataException("Лишняя скобка VDF.");at++;return values;}
    string key=Token();Skip();if(at==text.Length)throw new InvalidDataException("Нет значения VDF.");var entry=new Entry{Key=key};
    if(text[at]=='{'){at++;entry.Children=Object(depth+1,true);}else entry.Value=Token();values.Add(entry);
   }
  }
  void Skip(){while(at<text.Length){if(Char.IsWhiteSpace(text[at])||text[at]=='\ufeff'){at++;continue;}if(text[at]=='/'&&at+1<text.Length&&text[at+1]=='/'){at+=2;while(at<text.Length&&text[at]!='\n')at++;continue;}break;}}
  string Token(){
   Skip();if(++tokens>MaximumTokens||at==text.Length||text[at]=='{'||text[at]=='}')throw new InvalidDataException("Неверный токен VDF.");var value=new StringBuilder();
   if(text[at]=='"'){
    at++;while(at<text.Length){char c=text[at++];if(c=='"')return value.ToString();if(c=='\\'){if(at==text.Length)throw new InvalidDataException("Незавершённая escape-последовательность VDF.");char next=text[at++];if(next=='\\'||next=='"')value.Append(next);else if(next=='n')value.Append('\n');else if(next=='r')value.Append('\r');else if(next=='t')value.Append('\t');else{value.Append('\\');value.Append(next);}}else{if(c=='\0')throw new InvalidDataException("Нулевой символ VDF.");value.Append(c);}if(value.Length>32768)throw new InvalidDataException("Строка VDF слишком длинная.");}
    throw new InvalidDataException("Незавершённая строка VDF.");
   }
   while(at<text.Length&&!Char.IsWhiteSpace(text[at])&&text[at]!='{'&&text[at]!='}'){char c=text[at++];if(c=='\0'||c=='"')throw new InvalidDataException("Неверный символ VDF.");value.Append(c);if(value.Length>32768)throw new InvalidDataException("Строка VDF слишком длинная.");}return value.ToString();
  }
 }

 static IEnumerable<string> ProgramFiles(){
  var roots=new List<string>();AddRoot(roots,Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));AddRoot(roots,Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));return roots;
 }
 static void ReadSteamRegistry(List<string> roots,StringBuilder diagnostics){
  ReadSteamKey(RegistryHive.CurrentUser,RegistryView.Default,"SteamPath",roots,diagnostics);
  ReadSteamKey(RegistryHive.LocalMachine,RegistryView.Registry32,"InstallPath",roots,diagnostics);
  if(Environment.Is64BitOperatingSystem)ReadSteamKey(RegistryHive.LocalMachine,RegistryView.Registry64,"InstallPath",roots,diagnostics);
 }
 static void ReadSteamKey(RegistryHive hive,RegistryView view,string field,List<string> roots,StringBuilder diagnostics){
  try{using(var baseKey=RegistryKey.OpenBaseKey(hive,view))using(var key=baseKey.OpenSubKey(@"Software\Valve\Steam",false)){if(key!=null)AddRoot(roots,key.GetValue(field,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string);}}
  catch(Exception ex){if(!Expected(ex))throw;Note(diagnostics,"Реестр Steam: "+ex.Message);}
 }
 static void ReadEaRegistry(List<string> roots,StringBuilder diagnostics){
  foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})foreach(var view in Environment.Is64BitOperatingSystem?new[]{RegistryView.Registry32,RegistryView.Registry64}:new[]{RegistryView.Default}){
   try{using(var baseKey=RegistryKey.OpenBaseKey(hive,view))using(var uninstall=baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall",false)){
    if(uninstall==null)continue;var names=uninstall.GetSubKeyNames();int maximum=Math.Min(names.Length,1024);
    for(int i=0;i<maximum;i++)try{using(var key=uninstall.OpenSubKey(names[i],false)){
     if(key==null)continue;string name=key.GetValue("DisplayName",null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string;if(!F1Name(name))continue;
     string location=key.GetValue("InstallLocation",null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string;if(location!=null&&location.Length<=MaximumPath)AddRoot(roots,Environment.ExpandEnvironmentVariables(location));
    }}catch(Exception ex){if(!Expected(ex))throw;Note(diagnostics,"Реестр установки F1 25: "+ex.Message);}
   }}catch(Exception ex){if(!Expected(ex))throw;Note(diagnostics,"Реестр EA app: "+ex.Message);}
  }
 }
 static bool F1Name(string name){if(String.IsNullOrEmpty(name)||name.Length>512)return false;string value=name.Replace("®","").Replace("™","").Replace('_',' ').ToUpperInvariant();int at=value.IndexOf("F1 25",StringComparison.Ordinal);return at>=0&&(at==0||!Char.IsLetterOrDigit(value[at-1]))&&(at+5==value.Length||!Char.IsDigit(value[at+5]));}
}
