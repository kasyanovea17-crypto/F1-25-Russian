using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal sealed class BundleFile {
 public string path { get; set; }
 public long bytes { get; set; }
 public string sha256 { get; set; }
}
internal sealed class BundleManifest {
 public int schema { get; set; }
 public string version { get; set; }
 public string entryPoint { get; set; }
 public string archiveSha256 { get; set; }
 public long totalBytes { get; set; }
 public bool textUpdates { get; set; }
 public BundleFile[] files { get; set; }
}
internal static class BundleRules {
 internal const long MaxArchiveBytes=536870912,MaxFileBytes=268435456,MaxTotalBytes=1073741824;
 internal const int MaxFiles=4096,MaxManifestBytes=2097152;
 internal static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
 internal static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=MaxManifestBytes,RecursionLimit=64};
 internal static void Need(bool ok,string text){if(!ok)throw new InvalidDataException(text);}
 internal static string Hash(byte[] bytes){using(var h=SHA256.Create())return Hex(h.ComputeHash(bytes));}
 internal static string Hash(Stream stream){using(var h=SHA256.Create())return Hex(h.ComputeHash(stream));}
 internal static string HashFile(string path){using(var stream=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read))return Hash(stream);}
 static string Hex(byte[] b){return BitConverter.ToString(b).Replace("-","").ToLowerInvariant();}
 internal static bool IsHash(string text){return text!=null&&Regex.IsMatch(text,"\\A[a-f0-9]{64}\\z");}
 internal static string SafeRelative(string name){
  Need(!String.IsNullOrEmpty(name)&&name.Length<=220&&name.IndexOf('\\')<0&&!name.StartsWith("/",StringComparison.Ordinal),"Неверный путь в комплекте.");
  foreach(string component in name.Split('/')){
   Need(component.Length>0&&component!="."&&component!=".."&&!component.EndsWith(".",StringComparison.Ordinal)&&!component.EndsWith(" ",StringComparison.Ordinal),"Путь комплекта содержит недопустимый компонент.");
   Need(component.IndexOfAny(Path.GetInvalidFileNameChars())<0&&component.IndexOf(':')<0,"Путь комплекта содержит недопустимый символ.");
   string device=component.Split('.')[0];Need(!Regex.IsMatch(device,"\\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\\z",RegexOptions.IgnoreCase),"Служебное имя Windows в комплекте.");
  }
  return name;
 }
 internal static string Inside(string root,string name){
  SafeRelative(name);string target=Path.GetFullPath(Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)));
  Need(target.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Путь выходит из папки комплекта.");return target;
 }
 internal static void RealPath(string path){
  string full=Path.GetFullPath(path);Need(!full.StartsWith("\\\\",StringComparison.Ordinal),"Сетевая папка комплекта не поддерживается.");
  for(string cursor=full;!String.IsNullOrEmpty(cursor);cursor=Path.GetDirectoryName(cursor)){
   if(File.Exists(cursor)||Directory.Exists(cursor))Need((File.GetAttributes(cursor)&FileAttributes.ReparsePoint)==0,"Папка комплекта содержит ссылку: "+cursor);
  }
 }
 internal static byte[] ReadBounded(Stream input,long limit){
  using(var output=new MemoryStream()){
   byte[] buffer=new byte[65536];int count;while((count=input.Read(buffer,0,buffer.Length))>0){Need(output.Length+count<=limit,"Размер данных превышает предел.");output.Write(buffer,0,count);}return output.ToArray();
  }
 }
 internal static Dictionary<string,object> ReadObject(byte[] data){Need(data.Length<=MaxManifestBytes,"Описание слишком велико.");string text=Utf8.GetString(data);if(text.Length>0&&text[0]=='\uFEFF')text=text.Substring(1);var d=Json.DeserializeObject(text) as Dictionary<string,object>;Need(d!=null,"Неверное описание комплекта.");return d;}
 internal static Dictionary<string,object> ReadObject(string path){RealPath(path);Need(new FileInfo(path).Length<=MaxManifestBytes,"Описание слишком велико.");return ReadObject(File.ReadAllBytes(path));}
 internal static string Text(Dictionary<string,object> d,string key){object value;Need(d.TryGetValue(key,out value)&&value is string,"В описании отсутствует поле "+key);return (string)value;}
 internal static string Canonical(object value){
  var d=value as Dictionary<string,object>;if(d!=null){var keys=new List<string>(d.Keys);keys.Sort(StringComparer.Ordinal);return "{"+String.Join(",",keys.Select(k=>Json.Serialize(k)+":"+Canonical(d[k])).ToArray())+"}";}
  var a=value as IEnumerable;if(a!=null&&!(value is string)){var fields=new List<string>();foreach(object entry in a)fields.Add(Canonical(entry));return "["+String.Join(",",fields.ToArray())+"]";}return Json.Serialize(value);
 }
 internal static IEnumerable<Dictionary<string,object>> StableLanguageEntries(Dictionary<string,object> manifest){
  object raw;if(!manifest.TryGetValue("native_profiles",out raw))yield break;var profiles=raw as IEnumerable;Need(profiles!=null,"Неверные профили установки.");
  foreach(object item in profiles){var profile=item as Dictionary<string,object>;Need(profile!=null,"Неверный профиль установки.");object value;
   if(profile.TryGetValue("language_set",out value)&&!String.IsNullOrEmpty(Convert.ToString(value)))continue;
   if(profile.TryGetValue("game_version",out value)&&Convert.ToString(value)!="1.26")continue;
   if(!profile.TryGetValue("install_files",out raw))continue;
   foreach(object file in (IEnumerable)raw){var entry=file as Dictionary<string,object>;Need(entry!=null,"Неверный файл профиля.");if(entry.TryGetValue("payload",out value)&&Convert.ToString(value)=="language.lng")yield return entry;}
  }
 }
 internal static string InstallContract(Dictionary<string,object> manifest){
  var copy=ReadObject(Utf8.GetBytes(Json.Serialize(manifest)));copy.Remove("previous_lng");copy.Remove("lng");copy.Remove("text_version");object raw;
  Need(copy.TryGetValue("payload",out raw)&&raw is Dictionary<string,object>,"Неверное описание файлов перевода.");((Dictionary<string,object>)raw).Remove("language.lng");foreach(var entry in StableLanguageEntries(copy))entry.Remove("sha256");return Canonical(copy);
 }
 internal static void ValidateTextManifest(Dictionary<string,object> original,Dictionary<string,object> current){
  Need(InstallContract(original)==InstallContract(current),"Параметры установки изменились. Существующие данные сохранены.");
  string hash=Text(current,"lng");Need(IsHash(hash),"Неверная сумма перевода.");object raw;Need(current.TryGetValue("payload",out raw)&&raw is Dictionary<string,object>,"Неверное описание перевода.");Need(Text((Dictionary<string,object>)raw,"language.lng")==hash,"Суммы перевода в описании расходятся.");
  Version a,b;Need(Version.TryParse(Text(original,"text_version"),out a)&&Version.TryParse(Text(current,"text_version"),out b)&&b>=a,"Неверная или устаревшая версия текста.");
  foreach(var entry in StableLanguageEntries(current))Need(Text(entry,"sha256")==hash,"Профиль установки не соответствует тексту.");
  object history;Need(current.TryGetValue("previous_lng",out history)&&history is IEnumerable&&!(history is string),"Неверная история перевода.");
  var hashes=new HashSet<string>(StringComparer.Ordinal);int count=0;foreach(object entry in (IEnumerable)history){Need(entry is string&&IsHash((string)entry)&&++count<=1024,"Неверная история перевода.");Need(hashes.Add((string)entry),"Повтор в истории перевода.");}
  object originalHistory;if(original.TryGetValue("previous_lng",out originalHistory))foreach(object entry in (IEnumerable)originalHistory)Need(hashes.Contains((string)entry),"История исходного перевода потеряна.");
  if(hash!=Text(original,"lng"))Need(hashes.Contains(Text(original,"lng")),"Исходная сумма отсутствует в истории обновления.");
 }
 internal static uint BE(byte[] b,int p){Need(p>=0&&(long)p+4<=b.Length,"Повреждён заголовок LNG.");return ((uint)b[p]<<24)|((uint)b[p+1]<<16)|((uint)b[p+2]<<8)|b[p+3];}
 static Dictionary<string,int[]> Sections(byte[] b){
  Need(b.Length>=8&&b.Length<=33554432&&Encoding.ASCII.GetString(b,0,4)=="LNGT"&&BE(b,4)==b.Length,"Повреждён файл перевода.");
  var sections=new Dictionary<string,int[]>();int p=8;foreach(string tag in new[]{"HSHS","HSHT","SIDA","SIDB","LNGB"}){
   Need((long)p+8<=b.Length&&Encoding.ASCII.GetString(b,p,4)==tag,"Повреждена структура LNG.");int size=checked((int)BE(b,p+4)),extra=tag=="SIDA"?4:0;Need((long)p+8+extra+size<=b.Length,"Секция LNG выходит за пределы файла.");sections[tag]=new[]{p+8+extra,size};
   if(tag=="SIDA")Need(BE(b,p+8)==56134&&size==56134*8,"Неверное количество строк LNG.");p=checked(p+8+extra+size);
  }Need(p==b.Length,"Лишние данные в LNG.");return sections;
 }
 internal static void ValidateLanguage(byte[] original,byte[] candidate){
  var a=Sections(original);var b=Sections(candidate);foreach(string tag in new[]{"HSHS","HSHT","SIDB"}){Need(a[tag][1]==b[tag][1],"Изменилась таблица ключей LNG.");for(int i=0;i<a[tag][1];i++)Need(original[a[tag][0]+i]==candidate[b[tag][0]+i],"Изменились ключи LNG.");}
  Utf8.GetString(candidate,b["LNGB"][0],b["LNGB"][1]);for(int i=0;i<56134;i++){
   Need(BE(original,a["SIDA"][0]+i*8)==BE(candidate,b["SIDA"][0]+i*8),"Изменился порядок ключей LNG.");int offset=checked((int)BE(candidate,b["SIDA"][0]+i*8+4));Need(offset<b["LNGB"][1]&&Array.IndexOf(candidate,(byte)0,b["LNGB"][0]+offset,b["LNGB"][1]-offset)>=0,"Некорректная строка LNG.");
  }
 }
 internal static string Quote(string value){
  Need(value!=null&&value.IndexOf('\0')<0&&value.IndexOf('\r')<0&&value.IndexOf('\n')<0,"Неверный аргумент запуска.");var output=new StringBuilder("\"");int slashes=0;
  foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='\"'){output.Append('\\',slashes*2+1);output.Append(c);}else{output.Append('\\',slashes);output.Append(c);}slashes=0;}output.Append('\\',slashes*2);return output.Append('"').ToString();
 }
}
