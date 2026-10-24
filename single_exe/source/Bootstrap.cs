using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

internal static class Bootstrap {
 const string ArchiveResource="F1RU.Bundle.zip",ManifestResource="F1RU.Bundle.json";
 internal static Assembly ResourceAssembly=Assembly.GetExecutingAssembly();
 internal static Stream Resource(string name){Stream s=ResourceAssembly.GetManifestResourceStream(name);BundleRules.Need(s!=null,"Встроенный комплект отсутствует.");return s;}
 internal static BundleManifest Manifest(){
  byte[] bytes;using(var s=Resource(ManifestResource))bytes=BundleRules.ReadBounded(s,BundleRules.MaxManifestBytes);
  BundleRules.Need(BundleRules.Hash(bytes)==BundlePins.ManifestSha256,"Встроенное описание комплекта повреждено.");
  var m=BundleRules.Json.Deserialize<BundleManifest>(BundleRules.Utf8.GetString(bytes));ValidateManifest(m);return m;
 }
 internal static void ValidateManifest(BundleManifest m){
  BundleRules.Need(m!=null&&m.schema==1&&m.version==BundlePins.Version&&Regex.IsMatch(m.version,"\\A[0-9]+(?:\\.[0-9]+){1,3}(?:-[A-Za-z0-9.-]+)?\\z"),"Неверная версия комплекта.");
  BundleRules.Need(m.archiveSha256==BundlePins.ArchiveSha256&&BundleRules.IsHash(m.archiveSha256),"Неверная сумма встроенного архива.");
  BundleRules.SafeRelative(m.entryPoint);BundleRules.Need(m.entryPoint=="Launcher.exe","Неверная точка запуска.");
  BundleRules.Need(m.files!=null&&m.files.Length>=1&&m.files.Length<=BundleRules.MaxFiles,"Неверное число файлов комплекта.");
  var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
  foreach(var f in m.files){BundleRules.Need(f!=null&&BundleRules.IsHash(f.sha256)&&f.bytes>=0&&f.bytes<=BundleRules.MaxFileBytes,"Неверный файл комплекта.");BundleRules.SafeRelative(f.path);BundleRules.Need(names.Add(f.path),"Повторяющийся путь комплекта.");total=checked(total+f.bytes);}
  BundleRules.Need(total==m.totalBytes&&total<=BundleRules.MaxTotalBytes&&names.Contains(m.entryPoint),"Неверный размер комплекта.");
  if(m.textUpdates)BundleRules.Need(names.Contains("manifest.json")&&names.Contains("payload/language.lng"),"Нет исходных файлов текстового обновления.");
 }
 internal static Dictionary<string,BundleFile> FileMap(BundleManifest m){return m.files.ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);}
 internal static void VerifyArchive(Stream stream,BundleManifest m){BundleRules.Need(stream.CanSeek&&stream.Length<=BundleRules.MaxArchiveBytes,"Встроенный архив слишком велик.");BundleRules.Need(BundleRules.Hash(stream)==m.archiveSha256,"Встроенный архив поврежден.");stream.Position=0;}
 internal static void Extract(Stream source,BundleManifest m,string directory){
  BundleRules.RealPath(directory);BundleRules.Need(Directory.Exists(directory)&&Directory.GetFileSystemEntries(directory).Length==0,"Временная папка комплекта не пуста.");
  VerifyArchive(source,m);var expected=FileMap(m);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
  using(var archive=new ZipArchive(source,ZipArchiveMode.Read,true)){
   BundleRules.Need(archive.Entries.Count==expected.Count,"Состав встроенного архива отличается от описания.");
   foreach(var entry in archive.Entries){string name=BundleRules.SafeRelative(entry.FullName);BundleFile file;
    BundleRules.Need(expected.TryGetValue(name,out file)&&seen.Add(name),"Неизвестный или повторяющийся файл архива.");
    BundleRules.Need((entry.ExternalAttributes>>16&0xF000)!=0xA000,"Ссылка в архиве не поддерживается.");
    BundleRules.Need(entry.Length==file.bytes&&entry.Length<=BundleRules.MaxFileBytes,"Размер файла в архиве отличается.");total=checked(total+entry.Length);BundleRules.Need(total<=m.totalBytes,"Распакованный архив превышает предел.");
    string target=BundleRules.Inside(directory,name);Directory.CreateDirectory(Path.GetDirectoryName(target));BundleRules.RealPath(target);
    using(var input=entry.Open())using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
     long copied=0;byte[] buffer=new byte[65536];int count;while((count=input.Read(buffer,0,buffer.Length))>0){copied+=count;BundleRules.Need(copied<=file.bytes,"Распакованный файл превышает предел.");output.Write(buffer,0,count);}BundleRules.Need(copied==file.bytes,"Файл архива обрезан.");output.Flush(true);
    }BundleRules.Need(BundleRules.HashFile(target)==file.sha256,"Распакованный файл не прошёл проверку.");
   }
  }BundleRules.Need(total==m.totalBytes&&seen.Count==expected.Count,"Архив распакован не полностью.");
 }
 internal static byte[] OriginalFile(string name,BundleManifest m){
  var file=m.files.Single(f=>f.path.Equals(name,StringComparison.OrdinalIgnoreCase));using(var source=Resource(ArchiveResource))using(var archive=new ZipArchive(source,ZipArchiveMode.Read,false)){
   var entry=archive.GetEntry(name);BundleRules.Need(entry!=null&&entry.Length==file.bytes,"Встроенный исходный файл отсутствует.");using(var stream=entry.Open()){byte[] bytes=BundleRules.ReadBounded(stream,file.bytes);BundleRules.Need(bytes.LongLength==file.bytes&&BundleRules.Hash(bytes)==file.sha256,"Исходный файл поврежден.");return bytes;}
  }
 }
 internal static void ValidateTextPair(string directory,string manifestFile,string languageFile,Dictionary<string,object> original,byte[] originalLanguage){
  var current=BundleRules.ReadObject(manifestFile);BundleRules.ValidateTextManifest(original,current);BundleRules.RealPath(languageFile);BundleRules.Need(new FileInfo(languageFile).Length<=33554432,"Файл перевода слишком велик.");
  byte[] language=File.ReadAllBytes(languageFile);BundleRules.Need(BundleRules.Hash(language)==BundleRules.Text(current,"lng"),"Файл перевода не соответствует описанию.");BundleRules.ValidateLanguage(originalLanguage,language);
 }
 internal static bool IsUpdateAuxiliary(string relative){
  if(relative=="update.lock")return true;
  return Regex.IsMatch(relative,"\\A\\.updates/[a-f0-9]{32}/(?:new\\.lng|new\\.json|old\\.lng|old\\.json|recovery\\.json)\\z");
 }
 internal static void ValidatePending(string directory,Dictionary<string,object> original,byte[] language){
  string pending=Path.Combine(directory,"update-pending.json");var p=BundleRules.ReadObject(pending);BundleRules.Need(p.Keys.Count==4&&p.ContainsKey("transaction")&&p.ContainsKey("game")&&p.ContainsKey("install")&&p.ContainsKey("new_hash"),"Неверный журнал текстового обновления.");
  string id=BundleRules.Text(p,"transaction");BundleRules.Need(Regex.IsMatch(id,"\\A[a-f0-9]{32}\\z")&&p["install"] is bool,"Неверный журнал текстового обновления.");
  string game=BundleRules.Text(p,"game");BundleRules.Need(game.Length<=4096&&game.IndexOfAny(new[]{'\0','\r','\n','\"'})<0&&(!(bool)p["install"]||Path.IsPathRooted(game)),"Неверная папка в журнале обновления.");
  string root=BundleRules.Inside(directory,".updates/"+id);BundleRules.RealPath(root);string oldManifest=Path.Combine(root,"old.json"),newManifest=Path.Combine(root,"new.json"),oldLng=Path.Combine(root,"old.lng"),newLng=Path.Combine(root,"new.lng");
  ValidateTextPair(directory,oldManifest,oldLng,original,language);ValidateTextPair(directory,newManifest,newLng,original,language);
  var next=BundleRules.ReadObject(newManifest);BundleRules.Need(BundleRules.Text(p,"new_hash")==BundleRules.Text(next,"lng"),"Сумма журнала обновления отличается.");
  string currentManifest=BundleRules.HashFile(Path.Combine(directory,"manifest.json"));string currentLanguage=BundleRules.HashFile(Path.Combine(directory,"payload","language.lng"));
  BundleRules.Need(currentManifest==BundleRules.HashFile(oldManifest)||currentManifest==BundleRules.HashFile(newManifest),"Текущее описание не принадлежит прерванному обновлению.");
  BundleRules.Need(currentLanguage==BundleRules.HashFile(oldLng)||currentLanguage==BundleRules.HashFile(newLng),"Текущий перевод не принадлежит прерванному обновлению.");
 }
 internal static void ValidateDirectory(string directory,BundleManifest m){
  BundleRules.RealPath(directory);var expected=FileMap(m);long total=0;int count=0;
  foreach(var f in m.files){string path=BundleRules.Inside(directory,f.path);BundleRules.RealPath(path);BundleRules.Need(File.Exists(path),"Файл комплекта отсутствует: "+f.path);
   if(m.textUpdates&&(f.path=="manifest.json"||f.path=="payload/language.lng"))continue;
   BundleRules.Need(new FileInfo(path).Length==f.bytes&&BundleRules.HashFile(path)==f.sha256,"Файл комплекта изменён: "+f.path+". Существующие данные сохранены.");
  }
  foreach(string path in TreeFiles(directory)){
   BundleRules.RealPath(path);var info=new FileInfo(path);total=checked(total+info.Length);BundleRules.Need(++count<=8192&&total<=BundleRules.MaxTotalBytes*2,"Папка комплекта превышает предел.");
   string relative=path.Substring(directory.TrimEnd(Path.DirectorySeparatorChar).Length+1).Replace(Path.DirectorySeparatorChar,'/');if(expected.ContainsKey(relative))continue;
   bool evidence=relative.StartsWith("evidence/",StringComparison.Ordinal)&&!Regex.IsMatch(Path.GetExtension(relative),"\\A\\.(?:exe|dll|ps1|cmd|bat|com|vbs|js|msi|scr)\\z",RegexOptions.IgnoreCase);
   BundleRules.Need(evidence||(m.textUpdates&&(IsUpdateAuxiliary(relative)||relative=="update-pending.json")),"Неизвестный файл в папке комплекта: "+relative);
   BundleRules.Need(info.Length<=(evidence?67108864:33554432),"Служебный файл комплекта слишком велик.");if(relative=="update.lock")BundleRules.Need(info.Length==0,"Файл блокировки обновления поврежден.");
  }
  if(m.textUpdates){var original=BundleRules.ReadObject(OriginalFile("manifest.json",m));byte[] language=OriginalFile("payload/language.lng",m);
   if(File.Exists(Path.Combine(directory,"update-pending.json")))ValidatePending(directory,original,language);
   else ValidateTextPair(directory,Path.Combine(directory,"manifest.json"),Path.Combine(directory,"payload","language.lng"),original,language);
  }
 }
 internal static IEnumerable<string> TreeFiles(string root){
  var directories=new Stack<string>();directories.Push(root);int count=0;
  while(directories.Count>0){string directory=directories.Pop();BundleRules.RealPath(directory);BundleRules.Need(++count<=16384,"Слишком много папок комплекта.");
   foreach(string child in Directory.EnumerateFileSystemEntries(directory,"*",SearchOption.TopDirectoryOnly)){BundleRules.RealPath(child);if(Directory.Exists(child))directories.Push(child);else{BundleRules.Need(File.Exists(child),"Неизвестный тип файла комплекта.");yield return child;}}
  }
 }
 internal static string CacheParent(){
  string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);BundleRules.Need(!String.IsNullOrEmpty(local)&&Path.IsPathRooted(local),"Локальная папка пользователя недоступна.");
  string parent=Path.Combine(local,"Karsvein","F1RU","packages");BundleRules.RealPath(parent);Directory.CreateDirectory(parent);BundleRules.RealPath(parent);return parent;
 }
 internal static FileStream Acquire(string path){
  BundleRules.RealPath(path);var timer=Stopwatch.StartNew();while(true){try{return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){if(timer.Elapsed.TotalSeconds>=60)throw;Thread.Sleep(100);}}
 }
 internal static void DeleteStage(string directory,string parent){
  string full=Path.GetFullPath(directory),basePath=Path.GetFullPath(parent);BundleRules.Need(Path.GetDirectoryName(full).Equals(basePath,StringComparison.OrdinalIgnoreCase)&&Regex.IsMatch(Path.GetFileName(full),"\\A\\.stage-[a-f0-9]{32}\\z"),"Неверная временная папка.");
  if(!Directory.Exists(full))return;BundleRules.RealPath(full);foreach(string item in TreeFiles(full))BundleRules.RealPath(item);Directory.Delete(full,true);
 }
 internal static string Prepare(BundleManifest m){
  string parent=CacheParent(),name=m.version+"-"+m.archiveSha256,cache=Path.Combine(parent,name);BundleRules.RealPath(cache);
  using(var gate=Acquire(Path.Combine(parent,name+".lock"))){
   if(Directory.Exists(cache)){ValidateDirectory(cache,m);return cache;}
   string stage=Path.Combine(parent,".stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
   try{using(var source=Resource(ArchiveResource))Extract(source,m,stage);ValidateDirectory(stage,m);Directory.Move(stage,cache);ValidateDirectory(cache,m);return cache;}
   finally{DeleteStage(stage,parent);}
  }
 }
 internal static int Launch(string cache,BundleManifest m,string[] args){
  string entry=BundleRules.Inside(cache,m.entryPoint);BundleRules.RealPath(entry);BundleRules.Need(BundleRules.HashFile(entry)==m.files.Single(f=>f.path==m.entryPoint).sha256,"Лаунчер изменён перед запуском.");
  string arguments=String.Join(" ",args.Select(BundleRules.Quote).ToArray());var start=new ProcessStartInfo(entry,arguments){WorkingDirectory=cache,UseShellExecute=false,CreateNoWindow=args.Length>0,WindowStyle=args.Length>0?ProcessWindowStyle.Hidden:ProcessWindowStyle.Normal};
  start.EnvironmentVariables["F1RU_SINGLE_EXE"]="1";start.EnvironmentVariables["F1RU_BUNDLE_VERSION"]=m.version;
  using(var process=Process.Start(start)){BundleRules.Need(process!=null,"Процесс лаунчера не создан.");process.WaitForExit();return process.ExitCode;}
 }
 [STAThread]internal static int Main(string[] args){
  bool test=args.Length==2&&args[0]=="--bundle-self-test";try{
   BundleManifest m=Manifest();string cache=Prepare(m);
   if(test){string report=Path.GetFullPath(args[1]);BundleRules.RealPath(report);File.WriteAllText(report,BundleRules.Json.Serialize(new{status="PASS",version=m.version,cache=cache,files=m.files.Length,totalBytes=m.totalBytes,archiveSha256=m.archiveSha256,gameModified=false,registryModified=false,textUpdatesPreserved=m.textUpdates}),new UTF8Encoding(false));return 0;}
   return Launch(cache,m,args);
  }catch(Exception ex){
   if(test){try{File.WriteAllText(Path.GetFullPath(args[1]),BundleRules.Json.Serialize(new{status="FAIL",error=ex.ToString(),gameModified=false,registryModified=false}),new UTF8Encoding(false));}catch(IOException){}catch(UnauthorizedAccessException){}}
   else if(args.Length==0)MessageBox.Show("Операция не завершена. Существующие файлы сохранены.\n\n"+ex.Message,"F1 25 на русском",MessageBoxButtons.OK,MessageBoxIcon.Error);
   try{Console.Error.WriteLine(ex);}catch(IOException){}return 1;
  }
 }
}
