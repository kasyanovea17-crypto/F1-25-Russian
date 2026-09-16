using System;
using System.IO;
using System.Net;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// Text-only channel: remote data never supplies scripts, executable paths, game
// paths, patch offsets, protected hashes or font files.
sealed class TextUpdate {
 public const string Repository="https://github.com/kasyanovea17-crypto/F1-25-Russian";
 public const string Channel="https://raw.githubusercontent.com/kasyanovea17-crypto/F1-25-Russian/main/updates/";
 public const string LauncherVersion="0.22";
 public string Version,Hash,Notes,Url; public int Bytes; public bool Available;
}
static class Updates {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=262144};
 public static Dictionary<string,object> Read(string file){return Parse(File.ReadAllText(file,Encoding.UTF8));}
 static Dictionary<string,object> Parse(string s){return Json.Deserialize<Dictionary<string,object>>(s);}
 public static string Field(Dictionary<string,object> d,string k){return Convert.ToString(d[k]);}
 public static string Hash(string p){using(var a=SHA256.Create())using(var s=File.OpenRead(p))return BitConverter.ToString(a.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
 static void Write(string p,Dictionary<string,object> d){File.WriteAllText(p,Json.Serialize(d),new UTF8Encoding(true));}
 static Version V(string s){Version v;if(!Version.TryParse(s,out v)||v.Major<0)throw new InvalidDataException("Неверный номер версии.");return v;}
 static void Require(bool ok,string error){if(!ok)throw new InvalidDataException(error);}
 public static string LocalVersion(string home){try{var m=Read(Path.Combine(home,"manifest.json"));return m.ContainsKey("text_version")?Field(m,"text_version"):"0.15";}catch{return "—";}}
 public static byte[] Download(string url,int maximum){
  var u=new Uri(url);Require(u.Scheme=="https"&&u.Host=="raw.githubusercontent.com"&&u.AbsoluteUri.StartsWith(TextUpdate.Channel,StringComparison.Ordinal),"Источник обновления не совпадает с репозиторием.");
  ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
  var r=(HttpWebRequest)WebRequest.Create(u);r.UserAgent="F1RU-Launcher/0.22";r.Timeout=30000;r.ReadWriteTimeout=30000;r.AllowAutoRedirect=false;
  using(var response=(HttpWebResponse)r.GetResponse()){
   Require(response.StatusCode==HttpStatusCode.OK,"GitHub не вернул файл обновления.");Require(response.ContentLength<=maximum,"Размер загрузки превышает предел.");
   using(var s=response.GetResponseStream())using(var output=new MemoryStream()){
    var clock=System.Diagnostics.Stopwatch.StartNew();var buffer=new byte[65536];int n;while((n=s.Read(buffer,0,buffer.Length))>0){Require(clock.Elapsed.TotalSeconds<120,"Время загрузки истекло.");Require(output.Length+n<=maximum,"Размер загрузки превышает предел.");output.Write(buffer,0,n);}return output.ToArray();
   }
  }
 }
 public static TextUpdate Check(string home,Func<string,int,byte[]> fetch){
  Require(!File.Exists(Path.Combine(home,"update-pending.json")),"Предыдущее обновление было прервано. Нажмите «Восстановить обновление».");
  var m=Read(Path.Combine(home,"manifest.json"));var d=Parse(new UTF8Encoding(false,true).GetString(fetch(TextUpdate.Channel+"stable.json",65536)));
  Require(Convert.ToInt32(d["schema"])==1,"Формат обновления требует другой версии лаунчера.");
  string version=Field(d,"version");V(version);
  Require(V(Field(d,"min_launcher"))<=V(TextUpdate.LauncherVersion),"Для этого пакета требуется новая версия лаунчера.");
  Require(Field(d,"dat_original")==Field(m,"dat_original")&&Field(d,"font_modified")==Field(m,"font_modified"),"Пакет предназначен для другой версии игры или шрифта.");
  string hash=Field(d,"sha256");Require(System.Text.RegularExpressions.Regex.IsMatch(hash,"^[a-f0-9]{64}$"),"Неверная контрольная сумма в описании.");
  int length=Convert.ToInt32(d["bytes"]);Require(length>=1024&&length<=33554432,"Неверный размер пакета.");
  Require(Convert.ToInt32(d["records"])==56134,"Неверное количество строк в пакете.");
  string relative=Field(d,"file");Require(relative==version+"/language.lng"&&System.Text.RegularExpressions.Regex.IsMatch(version,"^[0-9]+\\.[0-9]+(?:\\.[0-9]+){0,2}$"),"Неверный путь пакета.");
  var current=V(LocalVersion(home));Require(V(version)>=current,"Канал предлагает устаревший пакет.");
  if(V(version)==current)Require(hash==Field(m,"lng"),"Содержимое версии изменилось. Автору нужно выпустить новый номер пакета.");
  return new TextUpdate{Version=version,Hash=hash,Bytes=length,Url=TextUpdate.Channel+relative,Notes=d.ContainsKey("notes")?Field(d,"notes"):"Исправления русского текста.",Available=V(version)>current};
 }
 static uint BE(byte[] b,int p){Require(p>=0&&p+4<=b.Length,"Повреждён заголовок LNG.");return ((uint)b[p]<<24)|((uint)b[p+1]<<16)|((uint)b[p+2]<<8)|b[p+3];}
 static Dictionary<string,int[]> Sections(byte[] b){
  Require(b.Length>=8&&Encoding.ASCII.GetString(b,0,4)=="LNGT"&&BE(b,4)==b.Length,"Повреждён файл перевода.");
  var result=new Dictionary<string,int[]>();int p=8;
  foreach(string tag in new[]{"HSHS","HSHT","SIDA","SIDB","LNGB"}){
   Require(p+8<=b.Length&&Encoding.ASCII.GetString(b,p,4)==tag,"Повреждена структура LNG.");int size=checked((int)BE(b,p+4)),extra=tag=="SIDA"?4:0;
   Require((long)p+8+extra+size<=b.Length,"Секция LNG выходит за пределы файла.");result[tag]=new[]{p+8+extra,size};
   if(tag=="SIDA")Require(BE(b,p+8)==56134&&size==56134*8,"Неверное количество записей LNG.");p+=8+extra+size;
  }
  Require(p==b.Length,"Лишние данные в LNG.");return result;
 }
 public static void ValidateLanguage(string original,string candidate){
  var old=File.ReadAllBytes(original);var next=File.ReadAllBytes(candidate);var a=Sections(old);var b=Sections(next);
  foreach(string tag in new[]{"HSHS","HSHT","SIDB"}){Require(a[tag][1]==b[tag][1],"Изменена таблица ключей.");for(int i=0;i<a[tag][1];i++)Require(old[a[tag][0]+i]==next[b[tag][0]+i],"Изменены ключи перевода.");}
  new UTF8Encoding(false,true).GetString(next,b["LNGB"][0],b["LNGB"][1]);
  for(int i=0;i<56134;i++){
   Require(BE(old,a["SIDA"][0]+i*8)==BE(next,b["SIDA"][0]+i*8),"Изменён порядок ключей.");
   int offset=checked((int)BE(next,b["SIDA"][0]+i*8+4));Require(offset<b["LNGB"][1],"Строка выходит за пределы LNG.");
   Require(Array.IndexOf(next,(byte)0,b["LNGB"][0]+offset,b["LNGB"][1]-offset)>=0,"Строка LNG не завершена.");
  }
 }
 static void Put(string from,string to){
  string tmp=to+".tmp-"+Guid.NewGuid().ToString("N");File.Copy(from,tmp);try{if(File.Exists(to))File.Replace(tmp,to,null);else File.Move(tmp,to);}finally{if(File.Exists(tmp))File.Delete(tmp);}
 }
 static string Payload(string h){return Path.Combine(h,"payload","language.lng");}
 static ArrayList Previous(Dictionary<string,object> m,string hash){var list=new ArrayList();foreach(var x in (IEnumerable)m["previous_lng"])list.Add(x);if(!list.Contains(hash))list.Add(hash);return list;}
 static void RealDirectories(string home){
  foreach(string target in new[]{home,Path.Combine(home,"payload"),Path.Combine(home,".updates")}){
   var d=new DirectoryInfo(target);while(d!=null){if(d.Exists)Require((d.Attributes&FileAttributes.ReparsePoint)==0,"Папка обновления содержит ссылку.");d=d.Parent;}
  }
  foreach(string target in new[]{Path.Combine(home,"manifest.json"),Payload(home)})Require((File.GetAttributes(target)&FileAttributes.ReparsePoint)==0,"Файл пакета является ссылкой.");
 }
 public static string Apply(string home,TextUpdate update,Func<string,int,byte[]> fetch,string game,bool install,Func<string,string,string> engine){
  RealDirectories(home);using(var gate=new FileStream(Path.Combine(home,"update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
   Require(!File.Exists(Path.Combine(home,"update-pending.json")),"Сначала восстановите прерванное обновление.");
   // Recheck against current disk state; never trust a stale UI selection.
   var fresh=Check(home,fetch);Require(fresh.Version==update.Version&&fresh.Hash==update.Hash&&fresh.Available,"Сведения об обновлении изменились. Проверьте ещё раз.");
   var m=Read(Path.Combine(home,"manifest.json"));Require(Hash(Payload(home))==Field(m,"lng"),"Локальный перевод изменён вручную. Файл сохранён без изменений.");
   string dir=Path.Combine(home,".updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
   File.WriteAllBytes(Path.Combine(dir,"new.lng"),fetch(fresh.Url,33554432));
   Require(new FileInfo(Path.Combine(dir,"new.lng")).Length==fresh.Bytes&&Hash(Path.Combine(dir,"new.lng"))==fresh.Hash,"Контрольная сумма или размер загрузки не совпадает.");
   ValidateLanguage(Payload(home),Path.Combine(dir,"new.lng"));
   File.Copy(Payload(home),Path.Combine(dir,"old.lng"));File.Copy(Path.Combine(home,"manifest.json"),Path.Combine(dir,"old.json"));
   m["previous_lng"]=Previous(m,Field(m,"lng"));m["lng"]=fresh.Hash;m["text_version"]=fresh.Version;((Dictionary<string,object>)m["payload"])["language.lng"]=fresh.Hash;
   Write(Path.Combine(dir,"new.json"),m);
   string pending=Path.Combine(home,"update-pending.json");Write(pending,new Dictionary<string,object>{{"transaction",Path.GetFileName(dir)},{"game",game},{"install",install},{"new_hash",fresh.Hash}});
   try{
    Put(Path.Combine(dir,"new.lng"),Payload(home));Put(Path.Combine(dir,"new.json"),Path.Combine(home,"manifest.json"));
    string output=install?engine(home,game):"Пакет обновлён в лаунчере. Для игры нажмите «Установить перевод».";
    File.Delete(pending);return "UPDATE PASS; пакет "+fresh.Version+". "+output;
   }catch{
    // Engine.ps1 rolls a failed game operation back to its own snapshots.
    // Keep the journal if restoring the local package itself fails.
    Put(Path.Combine(dir,"old.lng"),Payload(home));Put(Path.Combine(dir,"old.json"),Path.Combine(home,"manifest.json"));throw;
   }
  }
 }
 public static string Recover(string home,Func<string,string,string> engine){
  RealDirectories(home);using(var gate=new FileStream(Path.Combine(home,"update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
   string p=Path.Combine(home,"update-pending.json");var journal=Read(p);string id=Field(journal,"transaction");Require(System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-f0-9]{32}$"),"Повреждён журнал обновления.");
   string dir=Path.Combine(home,".updates",id);Require((File.GetAttributes(dir)&FileAttributes.ReparsePoint)==0,"Транзакция является ссылкой.");
   var old=Read(Path.Combine(dir,"old.json"));Require(Hash(Path.Combine(dir,"old.lng"))==Field(old,"lng"),"Копия предыдущего текста повреждена.");
   old["previous_lng"]=Previous(old,Field(journal,"new_hash"));Write(Path.Combine(dir,"recovery.json"),old);
   Put(Path.Combine(dir,"old.lng"),Payload(home));Put(Path.Combine(dir,"recovery.json"),Path.Combine(home,"manifest.json"));
   // If an installed game had been updated before a crash, explicitly reinstall
   // the previous text. Factory backups remain untouched by Engine.ps1.
   string game=Field(journal,"game");if(Convert.ToBoolean(journal["install"]))engine(home,game);
   File.Delete(p);return "RECOVERY PASS; предыдущий пакет восстановлен.";
  }
 }
}
