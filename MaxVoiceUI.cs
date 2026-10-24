using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

sealed class MaxVoiceState {
 public string Status, Code, Message, Backup;
 public bool Installed, CanInstall, CanRestore, GameRunning;
 public static MaxVoiceState Parse(string json) {
  if(json==null||json.Length>262144)throw new InvalidDataException("Ответ аудиомодуля слишком велик.");
  var data=new JavaScriptSerializer{MaxJsonLength=262144,RecursionLimit=12}.DeserializeObject(json) as Dictionary<string,object>;
  if(data==null||!data.ContainsKey("schema")||!(data["schema"] is int)||(int)data["schema"]!=1)throw new InvalidDataException("Неверный ответ аудиомодуля.");
  var state=new MaxVoiceState{Status=Text(data,"status",80),Code=Text(data,"code",120),Message=Text(data,"message",8000),Backup=Text(data,"backup",32768,true),Installed=Boolean(data,"installed"),CanInstall=Boolean(data,"canInstall"),CanRestore=Boolean(data,"canRestore"),GameRunning=Boolean(data,"gameRunning")};
  if(!data.ContainsKey("maxSlots")||!(data["maxSlots"] is int)||(int)data["maxSlots"]!=192||!Boolean(data,"testOnly"))throw new InvalidDataException("Неожиданный тип аудиопакета.");
  return state;
 }
 static string Text(Dictionary<string,object> data,string key,int max,bool optional=false){object value;if(!data.TryGetValue(key,out value)||value==null){if(optional)return "";throw new InvalidDataException("Нет поля: "+key);}if(!(value is string)||((string)value).Length>max)throw new InvalidDataException("Неверное поле: "+key);return (string)value;}
 static bool Boolean(Dictionary<string,object> data,string key){object value;if(!data.TryGetValue(key,out value)||!(value is bool))throw new InvalidDataException("Неверный флаг: "+key);return (bool)value;}
}

sealed class MaxVoiceWindow : Form {
 readonly string home,game;
 readonly bool readOnly;
 readonly Label heading,description;
 readonly TextBox details;
 readonly Button refresh,install,verify,restore,close;
 readonly ProgressBar progress;
 bool busy;
 MaxVoiceState state;
 public MaxVoiceWindow(string packageHome,string gamePath):this(packageHome,gamePath,false){}
 MaxVoiceWindow(string packageHome,string gamePath,bool testOnly){
  if(!Path.IsPathRooted(packageHome)||!Path.IsPathRooted(gamePath))throw new ArgumentException("Нужны полные пути к лаунчеру и игре.");
  home=Path.GetFullPath(packageHome);game=Path.GetFullPath(gamePath);readOnly=testOnly;
  Text="Макс — тестовая русская озвучка";ClientSize=new Size(770,545);MinimumSize=new Size(786,584);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",9);BackColor=Color.FromArgb(247,248,245);
  heading=new Label{Bounds=new Rectangle(22,18,725,32),Text="Тестовая озвучка Макса",Font=new Font("Segoe UI",16,FontStyle.Bold)};Controls.Add(heading);
  var note=new Label{Bounds=new Rectangle(22,58,725,76),Text="192 синтетические русские реплики Макса. Это необязательный тестовый пакет.\n18 записей ещё требуют проверки произношения; игровые прерывания не проверены.\nЕсть ненормативная лексика. Исходная озвучка сохраняется для восстановления.",ForeColor=Color.FromArgb(80,83,86)};Controls.Add(note);
  description=new Label{Bounds=new Rectangle(22,137,725,64),Text="Нажмите «Проверить состояние». Установка сама не начинается.",AutoEllipsis=true};Controls.Add(description);
  refresh=MakeButton("Проверить состояние",22,208,177);install=MakeButton("Установить для теста",209,208,177);verify=MakeButton("Проверить файлы",396,208,165);restore=MakeButton("Вернуть предыдущую",571,208,177);
  progress=new ProgressBar{Bounds=new Rectangle(22,251,725,8),Style=ProgressBarStyle.Marquee,Visible=false};Controls.Add(progress);
  details=new TextBox{Bounds=new Rectangle(22,272,725,203),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Font=new Font("Consolas",8.5f),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom};Controls.Add(details);
  var bottom=new Label{Bounds=new Rectangle(22,488,550,40),Text="Закройте F1 25 перед установкой или восстановлением.\nРусский текст, шрифты, команды и остальные гонщики не меняются.",ForeColor=Color.FromArgb(80,83,86),Anchor=AnchorStyles.Left|AnchorStyles.Bottom};Controls.Add(bottom);
  close=MakeButton("Закрыть",606,489,141);close.Anchor=AnchorStyles.Right|AnchorStyles.Bottom;
  refresh.Click+=(s,e)=>Run("status");verify.Click+=(s,e)=>Run("verify");close.Click+=(s,e)=>Close();
  install.Click+=(s,e)=>{if(install.Enabled&&Confirm("Установить тестовую русскую озвучку Макса?\n\n192 реплики, включая ненормативную лексику. Предыдущий аудиобанк будет сохранён. Перевод текста и другие гонщики сохраняются.\n\nЗакройте игру. Для записи файлов потребуется подтверждение Windows.","Тестовая озвучка"))Run("install");};
  restore.Click+=(s,e)=>{if(restore.Enabled&&Confirm("Восстановить аудиобанк, который был до установки этого тестового пакета?\n\nРусский текст, шрифты и голосовые команды останутся без изменений.","Восстановление озвучки"))Run("restore");};
  Shown+=(s,e)=>{if(!readOnly)Run("status");};RefreshButtons();
 }
 Button MakeButton(string text,int x,int y,int width){var b=new Button{Text=text,Bounds=new Rectangle(x,y,width,34),FlatStyle=FlatStyle.Flat};Controls.Add(b);return b;}
 bool Confirm(string text,string title){return !readOnly&&MessageBox.Show(this,text,title,MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;}
 void RefreshButtons(){bool active=!busy&&!readOnly,known=state!=null;refresh.Enabled=active;install.Enabled=active&&known&&state.CanInstall&&!state.GameRunning;verify.Enabled=active&&known&&state.Installed;restore.Enabled=active&&known&&state.CanRestore&&!state.GameRunning;close.Enabled=!busy;progress.Visible=busy;}
 void Apply(MaxVoiceState value,string raw){state=value;heading.Text=value.Installed?"Озвучка Макса установлена":value.Code=="restored"||value.Code=="already_restored"?"Предыдущая озвучка восстановлена":"Тестовая озвучка Макса";description.Text=value.Message;if(value.GameRunning)description.Text+="\nИгра запущена: изменение файлов отложено.";details.Text=raw;RefreshButtons();}
 internal static bool IsMutation(string action){return action=="install"||action=="restore";}
 internal static string Quote(string value){
  if(value==null||value.IndexOf('\0')>=0||value.IndexOf('\r')>=0||value.IndexOf('\n')>=0)throw new ArgumentException("Неверный аргумент.");
  var b=new StringBuilder("\"");int slashes=0;foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='\"'){b.Append('\\',slashes*2+1);b.Append(c);}else{b.Append('\\',slashes);b.Append(c);}slashes=0;}b.Append('\\',slashes*2);return b.Append('"').ToString();
 }
 static string PowerShell64(){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.Is64BitOperatingSystem&&!Environment.Is64BitProcess?"Sysnative":"System32","WindowsPowerShell","v1.0","powershell.exe");}
 internal static string Arguments(string host,string action,string gamePath,string result){if(action!="status"&&action!="verify"&&!IsMutation(action))throw new ArgumentException("Неизвестная операция.");if(!Path.IsPathRooted(gamePath)||gamePath.IndexOf('"')>=0)throw new ArgumentException("Нужен полный путь к игре.");return "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "+Quote(host)+" -Action "+action+" -GamePath "+Quote(Path.GetFullPath(gamePath))+" -ResultPath "+Quote(result);}
 static string Execute(string packageHome,string action,string gamePath){
  string owned=null;
  try{
   string host=Path.Combine(packageHome,"audio","MaxVoiceHost.ps1");if(!File.Exists(host)||(File.GetAttributes(host)&FileAttributes.ReparsePoint)!=0)throw new IOException("Отсутствует аудиомодуль. Запустите полный EXE лаунчера заново.");
   string temp=Path.GetFullPath(Path.GetTempPath());if((File.GetAttributes(temp)&FileAttributes.ReparsePoint)!=0)throw new IOException("Временная папка является ссылкой.");
   owned=Path.Combine(temp,"F1RU-MaxVoice-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(owned);string result=Path.Combine(owned,"result.json");
   var start=new ProcessStartInfo(PowerShell64(),Arguments(host,action,gamePath,result)){UseShellExecute=IsMutation(action),CreateNoWindow=!IsMutation(action),WindowStyle=ProcessWindowStyle.Hidden};if(IsMutation(action))start.Verb="runas";
   int exit;using(var process=Process.Start(start)){if(process==null)throw new IOException("Windows не вернула процесс настройки.");process.WaitForExit();exit=process.ExitCode;}
   if(!File.Exists(result)||(File.GetAttributes(result)&FileAttributes.ReparsePoint)!=0||new FileInfo(result).Length>262144)throw new IOException("Аудиомодуль не записал проверяемый результат. Код: "+exit);
   string raw=File.ReadAllText(result,Encoding.UTF8);MaxVoiceState.Parse(raw);if(exit!=0&&exit!=1&&exit!=2)throw new IOException("Неожиданный код аудиомодуля: "+exit);return raw;
  }finally{if(owned!=null)Cleanup(owned);}
 }
 internal static void Cleanup(string directory){
  try{string parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);string path=Path.GetFullPath(directory);string name=Path.GetFileName(path);const string prefix="F1RU-MaxVoice-";Guid id;if(!String.Equals(Path.GetDirectoryName(path),parent,StringComparison.OrdinalIgnoreCase)||!name.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring(prefix.Length),"N",out id)||!Directory.Exists(path)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return;string result=Path.Combine(path,"result.json");if(File.Exists(result)&&(File.GetAttributes(result)&FileAttributes.ReparsePoint)==0)File.Delete(result);if(Directory.GetFileSystemEntries(path).Length==0)Directory.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}
 }
 void Run(string action){if(busy||readOnly)return;busy=true;RefreshButtons();description.Text=IsMutation(action)?"Подтвердите запрос Windows и дождитесь завершения. Игра автоматически не закрывается.":"Проверяем аудиофайлы без установки…";ThreadPool.QueueUserWorkItem(delegate{string raw=null,error=null;MaxVoiceState value=null;try{raw=Execute(home,action,game);value=MaxVoiceState.Parse(raw);}catch(Win32Exception ex){error=ex.NativeErrorCode==1223?"Запрос Windows отменён; установка не началась.":ex.Message;}catch(Exception ex){error=ex.Message;}if(!IsDisposed)try{BeginInvoke((Action)delegate{busy=false;if(value!=null)Apply(value,raw);else{state=null;description.Text=error;details.Text=error;RefreshButtons();}});}catch(InvalidOperationException){}});}
 protected override void OnFormClosing(FormClosingEventArgs e){if(busy){e.Cancel=true;return;}base.OnFormClosing(e);}
 public static int SelfTest(string home,string resultPath){
  int checks=0;var report=new StringBuilder();try{
   Action<bool,string> test=(ok,label)=>{if(!ok)throw new Exception(label);checks++;};
   test(!IsMutation("status")&&!IsMutation("verify"),"read-only actions");test(IsMutation("install")&&IsMutation("restore"),"mutation actions");
   test(Quote("a b")=="\"a b\"","space quoting");test(Quote("x\\")=="\"x\\\\\"","trailing slash quoting");
   bool rejected=false;try{Quote("a\nb");}catch(ArgumentException){rejected=true;}test(rejected,"newline rejected");
   rejected=false;try{Arguments("host","launch",Path.GetFullPath(home),"result");}catch(ArgumentException){rejected=true;}test(rejected,"unknown action rejected");
   string ready="{\"schema\":1,\"status\":\"ready\",\"code\":\"ready\",\"message\":\"test\",\"backup\":null,\"installed\":false,\"canInstall\":true,\"canRestore\":false,\"gameRunning\":false,\"testOnly\":true,\"maxSlots\":192}";
   var value=MaxVoiceState.Parse(ready);test(value.CanInstall&&!value.Installed,"ready state parse");
   rejected=false;try{MaxVoiceState.Parse(ready.Replace("192","193"));}catch(InvalidDataException){rejected=true;}test(rejected,"wrong pack rejected");
   using(var w=new MaxVoiceWindow(Path.GetFullPath(home),Path.GetFullPath(home),true)){w.Apply(value,ready);test(!w.install.Enabled&&!w.restore.Enabled&&!w.refresh.Enabled,"self-test does not start host");test(w.close.Enabled,"test can close");test(w.details.Text==ready,"diagnostics preserved");test(w.description.Text=="test","message preserved");}
   test(File.Exists(Path.Combine(home,"audio","MaxVoice.ps1")),"engine included");test(File.Exists(Path.Combine(home,"audio","MaxVoiceHost.ps1")),"host included");test(File.Exists(Path.Combine(home,"audio","payload","max-ru-test.json")),"manifest included");test(File.Exists(Path.Combine(home,"audio","payload","max-ru-test.delta")),"literal patch included");
   report.Append("MAX_VOICE_UI_PASS checks=").Append(checks).Append(" host_calls=0 game_writes=false registry_writes=false\n");File.WriteAllText(resultPath,report.ToString(),Encoding.UTF8);return 0;
  }catch(Exception ex){File.WriteAllText(resultPath,"MAX_VOICE_UI_FAIL "+ex+"\n",Encoding.UTF8);return 1;}
 }
}
