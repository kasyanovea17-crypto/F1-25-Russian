using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

// Local support evidence only. Never changes game files or protection settings.
internal static class SupportDiagnostics {
 const int MaxEvents=160, MaxDetail=12288, MaxJournalBytes=4194304;
 static readonly object Gate=new object();
 static readonly List<Dictionary<string,object>> Events=new List<Dictionary<string,object>>();
 static readonly string Session=Guid.NewGuid().ToString("N");
 static bool enabled;
 static string storage="", journalError="", homeDirectory="";
 static readonly UTF8Encoding Utf8=new UTF8Encoding(false);
 static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=16777216,RecursionLimit=32};}
 static string Utc(){return DateTime.UtcNow.ToString("o");}
 static string Limit(string value,int max){if(value==null)return "";return value.Length<=max?value:value.Substring(0,max)+" [TRUNCATED]";}
 internal static string Redact(string value){
  if(String.IsNullOrEmpty(value))return "";
  string text=Limit(value,262144);
  try {
   string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
   if(!String.IsNullOrEmpty(profile)){text=Regex.Replace(text,Regex.Escape(profile.Replace("\\","\\\\")),"%USERPROFILE%",RegexOptions.IgnoreCase);text=Regex.Replace(text,Regex.Escape(profile),"%USERPROFILE%",RegexOptions.IgnoreCase);}
   text=Regex.Replace(text,@"(?i)([a-z]:[\\/]+(?:Users|Documents and Settings)[\\/]+)[^\\/\r\n""<>]+","$1<USER>");
   text=Regex.Replace(text,@"(?i)(/Users/|/home/)[^/\s""<>]+","$1<USER>");
   text=Regex.Replace(text,@"(?i)\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b","<EMAIL>");
   text=Regex.Replace(text,@"(?i)(https?://)[^/\s:@]+:[^/\s@]+@","$1<REDACTED>@");
   // Escaped JSON embedded in a log string has ambiguous quoting. Omit the
   // remaining line rather than risk exposing part of a credential value.
   text=Regex.Replace(text,@"(?i)(\b(?:authorization|password|passwd|token|access_token|refresh_token|api[_-]?key|secret|steamid|accountid|username)\b\\+[""']\s*[:=]\s*)[^\r\n]*","$1<REDACTED escaped credential line>");
   text=Regex.Replace(text,@"(?i)(\b(?:authorization|password|passwd|token|access_token|refresh_token|api[_-]?key|secret|steamid|accountid|username)\b[""']?\s*[:=]\s*)""(?:\\.|[^""\\])*""","$1\"<REDACTED>\"");
   text=Regex.Replace(text,@"(?i)(\b(?:authorization|password|passwd|token|access_token|refresh_token|api[_-]?key|secret|steamid|accountid|username)\b[""']?\s*[:=]\s*)'[^\r\n]*","$1<REDACTED quoted credential line>");
   text=Regex.Replace(text,@"(?i)(\b(?:authorization|password|passwd|token|access_token|refresh_token|api[_-]?key|secret|steamid|accountid)\b[""']?\s*[:=]\s*[""']?)(?:Bearer\s+)?[^\s,""';&}\r\n]+","$1<REDACTED>");
   text=Regex.Replace(text,@"(?i)\bBearer\s+[A-Za-z0-9._~+/=\-]+","Bearer <REDACTED>");
   text=Regex.Replace(text,@"(?i)\b(?:gh[pousr]_[A-Za-z0-9_]{12,}|github_pat_[A-Za-z0-9_]{12,}|sk-[A-Za-z0-9_-]{16,})\b","<REDACTED>");
   var cleaned=new StringBuilder(text.Length);foreach(char c in text)if(!Char.IsControl(c)||c=='\n'||c=='\r'||c=='\t')cleaned.Append(c);return cleaned.ToString();
  }catch{return "[Redaction failed: content omitted]";}
 }
 internal static void Initialize(string home){
  try{InitializeForTests(home,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Karsvein","F1RU","diagnostics"));}catch{lock(Gate){enabled=true;journalError="Journal initialization failed.";}}
 }
 // Explicit owned storage makes self-tests independent of the user's profile.
 internal static void InitializeForTests(string home,string ownedStorage){
  lock(Gate){
   enabled=true;homeDirectory=home??"";Events.Clear();journalError="";storage="";
   try{
    storage=LocalPath(ownedStorage,false);string file=Path.Combine(storage,"events.json");
    FileAttributes existing;if(TryAttributes(file,out existing)){
     LocalPath(file,true);var info=new FileInfo(file);if(info.Length>MaxJournalBytes)throw new InvalidDataException("Journal exceeds size limit.");
     object[] old=Json().DeserializeObject(File.ReadAllText(file,Utf8)) as object[];if(old==null)throw new InvalidDataException("Journal shape invalid.");
     for(int i=Math.Max(0,old.Length-MaxEvents);i<old.Length;i++){
      var item=old[i] as Dictionary<string,object>;if(item==null)continue;
      // Previous files are untrusted: retain only the known bounded scalar schema.
      var safe=new Dictionary<string,object>();
      foreach(string key in new[]{"utc","session","operation","component","action","gameDirectory","kind","stage","lastStage","detail","exception","exceptionType","hResult"}){object v;if(item.TryGetValue(key,out v)&&v is string)safe[key]=Limit(Redact((string)v),key=="detail"||key=="exception"?MaxDetail:1024);}
      object code;if(item.TryGetValue("exitCode",out code)&&code is int)safe["exitCode"]=code;
      if(item.TryGetValue("nativeErrorCode",out code)&&code is int)safe["nativeErrorCode"]=code;
      if(item.TryGetValue("failed",out code)&&code is bool)safe["failed"]=code;
      if(safe.ContainsKey("operation")&&safe.ContainsKey("kind"))Events.Add(safe);
     }
    }
   }catch(Exception e){journalError=Limit(Redact(e.GetType().Name+": "+e.Message),1024);}
  }
 }
 internal static string Begin(string component,string action,string game){
  string id=Guid.NewGuid().ToString("N");
  try{Add(id,"begin","start","",null,new Dictionary<string,object>{{"component",Limit(Redact(component),128)},{"action",Limit(Redact(action),128)},{"gameDirectory",Limit(Redact(game),1024)}});}catch{}
  return id;
 }
 internal static void Stage(string id,string stage,string message){try{Add(id,"stage",stage,message,null,null);}catch{}}
 internal static void End(string id,int code,string detail,Exception error){
  try{bool failed=code!=0||error!=null;var fields=new Dictionary<string,object>{{"exitCode",code},{"failed",failed}};if(error!=null){fields["exception"]=ExceptionText(error);fields["exceptionType"]=error.GetType().FullName;fields["hResult"]="0x"+error.HResult.ToString("X8");Exception at=error;for(int i=0;at!=null&&i<8;i++,at=at.InnerException){var native=at as Win32Exception;if(native!=null){fields["nativeErrorCode"]=native.NativeErrorCode;break;}}}Add(id,"end",failed?"failed":"completed",detail,error,fields);}catch{}
 }
 static string ExceptionText(Exception error){
  var s=new StringBuilder();for(int i=0;error!=null&&i<8;i++,error=error.InnerException){s.Append(error.GetType().FullName).Append(" HResult=0x").Append(error.HResult.ToString("X8"));var native=error as Win32Exception;if(native!=null)s.Append(" NativeErrorCode=").Append(native.NativeErrorCode);s.Append(": ").Append(error.Message).AppendLine();s.AppendLine(error.StackTrace??"");}return Limit(Redact(s.ToString()),MaxDetail);
 }
 static void Add(string id,string kind,string stage,string detail,Exception ignored,Dictionary<string,object> fields){
  lock(Gate){if(!enabled)return;var item=new Dictionary<string,object>{{"utc",Utc()},{"session",Session},{"operation",Limit(id,128)},{"kind",kind},{"stage",Limit(Redact(stage),128)},{"detail",Limit(Redact(detail),MaxDetail)}};
   for(int i=Events.Count-1;i>=0;i--){object previousId;if(Events[i].TryGetValue("operation",out previousId)&&String.Equals(Convert.ToString(previousId),id,StringComparison.Ordinal)){foreach(string key in new[]{"component","action","gameDirectory"}){object v;if(Events[i].TryGetValue(key,out v))item[key]=v;}if(kind=="end"){object previousStage;if(Events[i].TryGetValue("stage",out previousStage))item["lastStage"]=previousStage;}break;}}
   if(fields!=null)foreach(var f in fields)item[f.Key]=f.Value;Events.Add(item);while(Events.Count>MaxEvents)Events.RemoveAt(0);Persist();}
 }
 static void Persist(){
  if(String.IsNullOrEmpty(storage))return;
  string temp="";
  try{
   LocalPath(storage,false);Directory.CreateDirectory(storage);LocalPath(storage,true);
   string path=Path.Combine(storage,"events.json");temp=Path.Combine(storage,"events."+Guid.NewGuid().ToString("N")+".tmp");
   byte[] bytes=Utf8.GetBytes(Json().Serialize(Events));while(bytes.Length>MaxJournalBytes&&Events.Count>1){Events.RemoveAt(0);bytes=Utf8.GetBytes(Json().Serialize(Events));}if(bytes.Length>MaxJournalBytes)throw new InvalidDataException("Journal exceeds size limit.");
   using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}
   if(File.Exists(path)){LocalPath(path,true);File.Replace(temp,path,null);}else File.Move(temp,path);temp="";
  }catch(Exception e){journalError=Limit(Redact(e.GetType().Name+": "+e.Message),1024);}finally{if(temp.Length>0)try{File.Delete(temp);}catch{}}
 }
 static string LocalPath(string raw,bool mustExist){
  if(String.IsNullOrWhiteSpace(raw)||raw.Length>1024)throw new ArgumentException("Local path is missing or too long.");
  string path=Path.GetFullPath(raw);if(path.StartsWith(@"\\",StringComparison.Ordinal)||path.StartsWith("//",StringComparison.Ordinal))throw new IOException("Remote path skipped.");
  string root=Path.GetPathRoot(path);if(String.IsNullOrEmpty(root)||new DriveInfo(root).DriveType==DriveType.Network)throw new IOException("Remote drive skipped.");
  string at=path;int depth=0;
  while(!String.IsNullOrEmpty(at)&&depth++<64){FileAttributes attributes;if(TryAttributes(at,out attributes)&&(attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Reparse path skipped.");at=Path.GetDirectoryName(at);}
  FileAttributes found;if(mustExist&&!TryAttributes(path,out found))throw new FileNotFoundException("Path not found.",path);return path;
 }
 static bool TryAttributes(string path,out FileAttributes attributes){try{attributes=File.GetAttributes(path);return true;}catch(FileNotFoundException){attributes=0;return false;}catch(DirectoryNotFoundException){attributes=0;return false;}}
 static object Section(Func<object> collect){try{return collect();}catch(Exception e){return new Dictionary<string,object>{{"status","error"},{"error",ExceptionText(e)}};}}
 static string Hash(string path,long maxBytes){
  if(new FileInfo(path).Length>maxBytes)return "skipped_size_limit";
  var watch=Stopwatch.StartNew();using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var sha=SHA256.Create()){
   byte[] buffer=new byte[65536];int n;long total=0;while((n=file.Read(buffer,0,buffer.Length))>0){total+=n;if(total>maxBytes||watch.ElapsedMilliseconds>2000)return "skipped_time_or_size_limit";sha.TransformBlock(buffer,0,n,null,0);}sha.TransformFinalBlock(new byte[0],0,0);return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
  }
 }
 static object FileEvidence(string raw,bool hash,long maxBytes){
  return Section(delegate{
   string path=LocalPath(raw,false);var data=new Dictionary<string,object>{{"path",Redact(path)}};FileAttributes attributes;if(!TryAttributes(path,out attributes)){data["status"]="not_found";return data;}if((attributes&FileAttributes.Directory)!=0)throw new IOException("Expected file is a directory.");
   LocalPath(path,true);var info=new FileInfo(path);data["status"]="read";data["bytes"]=info.Length;data["lastWriteUtc"]=info.LastWriteTimeUtc.ToString("o");
   if(path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)){var v=FileVersionInfo.GetVersionInfo(path);data["fileVersion"]=Redact(v.FileVersion);data["productVersion"]=Redact(v.ProductVersion);}
   if(hash){data["sha256"]=Hash(path,maxBytes);var after=new FileInfo(path);data["stableSnapshot"]=after.Exists&&after.Length==info.Length&&after.LastWriteTimeUtc==info.LastWriteTimeUtc;}return data;
  });
 }
 static object EnvironmentEvidence(){
  return Section(delegate{bool admin=false;using(var identity=WindowsIdentity.GetCurrent())admin=new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);return new Dictionary<string,object>{{"status","read"},{"os",Environment.OSVersion.VersionString},{"os64bit",Environment.Is64BitOperatingSystem},{"process64bit",Environment.Is64BitProcess},{"clr",Environment.Version.ToString()},{"elevatedAdministrator",admin},{"uiCulture",System.Globalization.CultureInfo.CurrentUICulture.Name},{"antivirusVerdict","not_obtained"},{"securitySettingsRead",false}};});
 }
 static object GameEvidence(string game){
  return Section(delegate{
   string directory=LocalPath(game,true);if(!Directory.Exists(directory))throw new IOException("Game directory is not a directory.");
   var data=new Dictionary<string,object>{{"status","read"},{"directory",Redact(directory)},{"actualPlaybackLanguage","not_measured"},{"runtimePlaybackVerified",false}};
   var files=new List<object>();foreach(string name in new[]{"F1_25.exe","F1_25_dx12.exe","EAAntiCheat.GameServiceLauncher.exe"})files.Add(FileEvidence(Path.Combine(directory,name),true,268435456));
   foreach(string name in new[]{"en_engineer1_startup.nfs","en_engineer1.nfs"})files.Add(FileEvidence(Path.Combine(directory,"audio","2025","speech",name),false,0));data["files"]=files;
   data["steam"]=Section(delegate{
    var parent=Directory.GetParent(directory);if(parent==null||!String.Equals(parent.Name,"common",StringComparison.OrdinalIgnoreCase)||parent.Parent==null)return new Dictionary<string,object>{{"status","not_in_standard_library"}};
    string manifest=LocalPath(Path.Combine(parent.Parent.FullName,"appmanifest_3059520.acf"),true);if(new FileInfo(manifest).Length>262144)throw new InvalidDataException("Steam manifest exceeds size limit.");
    string text=File.ReadAllText(manifest);var result=new Dictionary<string,object>{{"status","read"},{"path",Redact(manifest)},{"configuredLanguage","unknown"},{"buildid","unknown"},{"actualPlaybackLanguage","not_measured"}};
    Match config=Regex.Match(text,"\"UserConfig\"\\s*\\{([^{}]{0,65536})\\}",RegexOptions.IgnoreCase);Match language=Regex.Match(config.Success?config.Groups[1].Value:"","\"language\"\\s*\"([^\"\\r\\n]{1,80})\"",RegexOptions.IgnoreCase);if(language.Success)result["configuredLanguage"]=Redact(language.Groups[1].Value);result["languageSource"]=language.Success?"AppState.UserConfig.language":"not_found";Match build=Regex.Match(text,"\"buildid\"\\s*\"([0-9]{1,30})\"",RegexOptions.IgnoreCase);if(build.Success)result["buildid"]=build.Groups[1].Value;return result;
   });return data;
  });
 }
 static object ProcessesEvidence(){
  return Section(delegate{var rows=new List<object>();foreach(string name in new[]{"F1_25","F1_25_dx12"}){Process[] processes=Process.GetProcessesByName(name);foreach(Process p in processes)using(p){var row=new Dictionary<string,object>{{"name",name},{"pid",p.Id}};try{row["path"]=Redact(p.MainModule.FileName);}catch(Exception e){row["pathStatus"]=e.GetType().Name;}rows.Add(row);}}return rows;});
 }
 static object PackageEvidence(string home){
  return Section(delegate{string dir=LocalPath(home,true);var files=new List<object>();foreach(string name in new[]{"Launcher.exe","manifest.json","Engine.ps1","Compatibility.cs","audio/MaxVoiceHost.ps1","audio/MaxVoice.ps1","audio/engineer/EngineerVoiceHost.ps1","audio/engineer/EngineerVoice.ps1","audio/engineer/payload/engineer-voices.json","speech/SpeechHost.ps1"})files.Add(FileEvidence(Path.Combine(dir,name.Replace('/',Path.DirectorySeparatorChar)),true,167772160));var result=new Dictionary<string,object>{{"status","read"},{"directory",Redact(dir)},{"members",files}};
   result["manifestVersions"]=Section(delegate{string path=LocalPath(Path.Combine(dir,"manifest.json"),true);if(new FileInfo(path).Length>1048576)throw new InvalidDataException("Manifest exceeds size limit.");var m=Json().DeserializeObject(File.ReadAllText(path,Utf8)) as Dictionary<string,object>;if(m==null)throw new InvalidDataException("Manifest shape invalid.");var versions=new Dictionary<string,object>();foreach(string key in new[]{"version","text_version"}){object v;if(m.TryGetValue(key,out v)&&v is string)versions[key]=Limit(Redact((string)v),80);}return versions;});return result;});
 }
 static void WriteEntry(ZipArchive zip,string name,string text){var entry=zip.CreateEntry(name,CompressionLevel.Optimal);using(var writer=new StreamWriter(entry.Open(),Utf8))writer.Write(text);}
 internal static string Export(string destination,string home,string game,string latest){
  string target=LocalPath(destination,false);if(File.Exists(target)||Directory.Exists(target))throw new IOException("Report destination already exists. Choose a new name.");
  foreach(string forbidden in new[]{home,game,storage})if(!String.IsNullOrWhiteSpace(forbidden)){string root;try{root=Path.GetFullPath(forbidden).TrimEnd('\\','/');}catch{continue;}if(String.Equals(target,root,StringComparison.OrdinalIgnoreCase)||target.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Choose a report destination outside the game, package and journal directories.");}
  string parent=Path.GetDirectoryName(target);LocalPath(parent,true);if(!Directory.Exists(parent))throw new DirectoryNotFoundException("Report destination directory not found.");
  List<Dictionary<string,object>> snapshot;string persistence;lock(Gate){snapshot=new List<Dictionary<string,object>>(Events);persistence=journalError;}
  var report=new Dictionary<string,object>{{"schema",1},{"reportKind","launcher_support"},{"createdUtc",Utc()},{"session",Session},{"privacy",new Dictionary<string,object>{{"localOnly",true},{"automaticUpload",false},{"gameWrites",0},{"registryWrites",0},{"networkCalls",0},{"audioIncluded",false},{"credentialsIncluded",false},{"redaction","known profile paths, user path segments, email addresses, credential-shaped values"}}},{"journal",new Dictionary<string,object>{{"events",snapshot.Count},{"limit",MaxEvents},{"persistenceError",persistence}}},{"environment",EnvironmentEvidence()},{"package",PackageEvidence(String.IsNullOrEmpty(home)?homeDirectory:home)},{"game",GameEvidence(game)},{"runningGameProcesses",ProcessesEvidence()},{"latestDisplayedLog",Limit(Redact(latest),65536)},{"actualPlaybackLanguage","not_measured"},{"runtimePlaybackVerified",false},{"antivirusVerdict","not_obtained"}};
  report["runningLauncher"]=Section(delegate{using(var process=Process.GetCurrentProcess())return FileEvidence(process.MainModule.FileName,true,167772160);});
  Dictionary<string,object> failed=null;for(int i=snapshot.Count-1;i>=0;i--){object code,hasFailure;if((snapshot[i].TryGetValue("exitCode",out code)&&code is int&&(int)code!=0)||(snapshot[i].TryGetValue("failed",out hasFailure)&&hasFailure is bool&&(bool)hasFailure)||snapshot[i].ContainsKey("exception")){failed=snapshot[i];break;}}
  report["lastFailure"]=failed;
  var summary=new StringBuilder("F1 25 — диагностический отчёт лаунчера\r\n");summary.AppendLine("Создан (UTC): "+report["createdUtc"]);summary.AppendLine("Сеанс: "+Session);summary.AppendLine("Отчёт сохранён локально. Отправьте ZIP разработчику вручную после просмотра.");summary.AppendLine("Игровые файлы, записи голоса, пароли и содержимое аккаунтов не включаются.");summary.AppendLine("Проверка установки не доказывает язык реально звучащих реплик. Антивирусный вердикт не получен.");
  summary.AppendLine("\r\nПоследняя зарегистрированная ошибка:");
  if(failed==null)summary.AppendLine("В сохранённой истории ошибки не зарегистрированы; воспроизведите проблему и сохраните новый отчёт.");
  else{
   string[] keys={"utc","component","action","lastStage","exitCode","exceptionType","hResult","nativeErrorCode","gameDirectory","detail","exception"};
   string[] labels={"Время UTC","Компонент","Действие","Этап сбоя","Код завершения","Тип исключения","HResult","Код Windows","Папка игры","Сообщение","Подробности исключения"};
   for(int i=0;i<keys.Length;i++){object value;if(failed.TryGetValue(keys[i],out value))summary.AppendLine(labels[i]+": "+Convert.ToString(value));}
   string id=Convert.ToString(failed["operation"]);summary.AppendLine("Номер операции: "+id);summary.AppendLine("\r\nЭтапы этой операции:");foreach(var e in snapshot){object op;if(e.TryGetValue("operation",out op)&&String.Equals(Convert.ToString(op),id,StringComparison.Ordinal)){object time,stage,detail;e.TryGetValue("utc",out time);e.TryGetValue("stage",out stage);e.TryGetValue("detail",out detail);summary.AppendLine(Convert.ToString(time)+" | "+Convert.ToString(stage)+" | "+Limit(Convert.ToString(detail),1500));}}
  }
  summary.AppendLine("\r\nПоследний журнал окна:");summary.AppendLine(Limit(Redact(latest),65536));if(!String.IsNullOrEmpty(persistence))summary.AppendLine("\r\nОшибка сохранения истории: "+persistence);
  summary.AppendLine("\r\nREPORT.json — версии, пути, контрольные суммы, ошибки сбора. EVENTS.json — ограниченная история этапов.");summary.AppendLine("Ошибки inventory/сбора не означают ошибку установки. Причина блокировки защиты без её собственного вердикта не определяется.");
  string temporary=Path.Combine(parent,".f1-report-"+Guid.NewGuid().ToString("N")+".tmp");
  try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,false)){WriteEntry(zip,"SUMMARY.txt",summary.ToString());WriteEntry(zip,"REPORT.json",Json().Serialize(report));WriteEntry(zip,"EVENTS.json",Json().Serialize(snapshot));}LocalPath(parent,true);File.Move(temporary,target);temporary="";return target;}finally{if(temporary.Length>0)try{File.Delete(temporary);}catch{}}
 }
}
