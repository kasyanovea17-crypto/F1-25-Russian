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
  if(state.Status=="error"&&(state.Installed||state.CanInstall||state.CanRestore))throw new InvalidDataException("Ошибка аудиомодуля не может разрешать установку.");
  return state;
 }
 static string Text(Dictionary<string,object> data,string key,int max,bool optional=false){object value;if(!data.TryGetValue(key,out value)||value==null){if(optional)return "";throw new InvalidDataException("Нет поля: "+key);}if(!(value is string)||((string)value).Length>max)throw new InvalidDataException("Неверное поле: "+key);return (string)value;}
 static bool Boolean(Dictionary<string,object> data,string key){object value;if(!data.TryGetValue(key,out value)||!(value is bool))throw new InvalidDataException("Неверный флаг: "+key);return (bool)value;}
}

sealed class MaxVoiceWindow : Form {
 readonly string home,game;
 readonly bool readOnly;
 static readonly Color WindowColor=Color.FromArgb(25,28,32),TextColor=Color.FromArgb(244,245,245),MutedColor=Color.FromArgb(157,164,170),RuleColor=Color.FromArgb(57,62,68),WarningColor=Color.FromArgb(216,184,110),SuccessColor=Color.FromArgb(84,206,105);
 readonly Label heading,description,stateHeading,stateMarker;
 readonly TextBox details;
 readonly Button refresh,install,verify,restore,close,back,topClose,diagnostics,copyReport;
 readonly Panel body,diagnosticBody;
 readonly ProgressBar progress;
 bool busy,diagnosticsOpen,renderOnly;
 MaxVoiceState state;
 public string LastReport { get { return details.Text; } }
 public MaxVoiceWindow(string packageHome,string gamePath):this(packageHome,gamePath,false){}
 internal static MaxVoiceWindow CreatePreview(string packageHome,string example){
  if(example!="supported"&&example!="installed"&&example!="unsupported"&&example!="diagnostics")throw new ArgumentException("Неизвестное состояние предпросмотра.");
  var window=new MaxVoiceWindow(Path.GetFullPath(packageHome),Path.GetFullPath(packageHome),true);window.renderOnly=true;
  bool installedExample=example=="installed",supported=example!="unsupported";
  var data=new Dictionary<string,object>{{"schema",1},{"status",installedExample?"installed":supported?"ready":"unavailable"},{"code",installedExample?"installed":supported?"ready":"unsupported_bank"},{"message",installedExample?"Предыдущий аудиобанк сохранён. Доступны проверка файлов и возврат озвучки.":supported?"Прежняя озвучка сохранится для восстановления.":"Этот аудиобанк не поддерживается тестовым пакетом. Исходная озвучка не изменена."},{"backup",null},{"installed",installedExample},{"canInstall",supported&&!installedExample},{"canRestore",installedExample},{"gameRunning",false},{"testOnly",true},{"maxSlots",192},{"previewOnly",true}};
  string raw=new JavaScriptSerializer().Serialize(data);window.Apply(MaxVoiceState.Parse(raw),raw);if(example=="diagnostics")window.ToggleDiagnostics();return window;
 }
 MaxVoiceWindow(string packageHome,string gamePath,bool testOnly){
  if(!Path.IsPathRooted(packageHome)||!Path.IsPathRooted(gamePath))throw new ArgumentException("Нужны полные пути к лаунчеру и игре.");
  home=Path.GetFullPath(packageHome);game=Path.GetFullPath(gamePath);readOnly=testOnly;
  Text="Голос Макса — тестовая озвучка";ClientSize=new Size(560,500);FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("Segoe UI",10.5f);BackColor=WindowColor;ForeColor=TextColor;KeyPreview=true;DoubleBuffered=true;
  back=MakeButton(this,"Озвучка",22,16,125,false);back.AccessibleName="Вернуться к озвучке";back.Click+=(s,e)=>Close();
  topClose=MakeButton(this,"",508,16,30,false);((UiButton)topClose).Glyph=0xf659;topClose.AccessibleName="Закрыть озвучку Макса";topClose.Click+=(s,e)=>Close();
  heading=MakeLabel(this,"Голос Макса",28,68,390,36,18,FontStyle.Bold,TextColor);
  var badge=MakeLabel(this,"Тест",230,75,48,22,9.5f,FontStyle.Regular,WarningColor);badge.BackColor=Color.FromArgb(52,44,25);badge.TextAlign=ContentAlignment.MiddleCenter;
  body=new Panel{Bounds=new Rectangle(0,112,560,316),BackColor=WindowColor,AutoScroll=true,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom};Controls.Add(body);
  MakeLabel(body,"192 русские реплики",28,0,500,26,12,FontStyle.Bold,TextColor);
  MakeLabel(body,"Синтетическая озвучка для F1 25 1.26 · English",28,31,500,23,10.5f,FontStyle.Regular,MutedColor);
  MakeLabel(body,"18 записей ещё требуют проверки.\nПрерывания реплик в игре не проверены.",28,66,500,44,10.5f,FontStyle.Regular,TextColor);
  MakeLabel(body,"Есть ненормативная лексика.",28,112,500,23,10.5f,FontStyle.Regular,MutedColor);
  Rule(body,28,146,500);
  stateMarker=MakeLabel(body,"",28,165,8,8,10.5f,FontStyle.Regular,MutedColor);stateMarker.BackColor=MutedColor;
  stateHeading=MakeLabel(body,"Состояние не проверено",48,155,480,29,11,FontStyle.Bold,TextColor);
  description=MakeLabel(body,"Нажмите «Проверить состояние».\nУстановка сама не начинается.",28,190,500,52,10.5f,FontStyle.Regular,MutedColor);description.AutoEllipsis=true;
  install=MakeButton(body,"Установить для теста",28,250,243,true);verify=MakeButton(body,"Проверить файлы",28,250,243,true);
  refresh=MakeButton(body,"Проверить состояние",286,250,242,false);
  progress=new ProgressBar{Bounds=new Rectangle(28,303,500,4),Style=ProgressBarStyle.Marquee,Visible=false};body.Controls.Add(progress);
  diagnosticBody=new Panel{Bounds=new Rectangle(28,326,500,224),BackColor=WindowColor,Visible=false};body.Controls.Add(diagnosticBody);
  Rule(diagnosticBody,0,0,500);
  details=new TextBox{Bounds=new Rectangle(0,13,500,165),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,BorderStyle=BorderStyle.FixedSingle,Font=new Font("Consolas",10),BackColor=Color.FromArgb(17,19,21),ForeColor=TextColor,AccessibleName="Полный отчёт аудиомодуля"};diagnosticBody.Controls.Add(details);
  copyReport=MakeButton(diagnosticBody,"Копировать отчёт",326,188,174,false);copyReport.Click+=(s,e)=>CopyReport();
  Rule(this,28,434,500);
  diagnostics=MakeButton(this,"Диагностика",28,450,130,false);diagnostics.Click+=(s,e)=>ToggleDiagnostics();
  restore=MakeButton(this,"Вернуть прежнюю озвучку",164,450,250,false);
  close=MakeButton(this,"Закрыть",430,450,98,false);CancelButton=close;
  refresh.Click+=(s,e)=>Run("status");verify.Click+=(s,e)=>Run("verify");close.Click+=(s,e)=>Close();
  install.Click+=(s,e)=>{if(install.Enabled&&Confirm("Установить тестовую русскую озвучку Макса?\n\n192 реплики, включая ненормативную лексику. Предыдущий аудиобанк будет сохранён. Перевод текста и другие гонщики сохраняются.\n\nЗакройте игру. Для записи файлов потребуется подтверждение Windows.","Тестовая озвучка"))Run("install");};
  restore.Click+=(s,e)=>{if(restore.Enabled&&Confirm("Восстановить аудиобанк, который был до установки этого тестового пакета?\n\nРусский текст, шрифты и голосовые команды останутся без изменений.","Восстановление озвучки"))Run("restore");};
  close.TabIndex=0;refresh.TabIndex=1;install.TabIndex=2;verify.TabIndex=2;diagnostics.TabIndex=3;restore.TabIndex=4;
  Shown+=(s,e)=>{close.Focus();if(!readOnly)Run("status");};RefreshButtons();
 }
 Button MakeButton(Control parent,string text,int x,int y,int width,bool primary){var b=new UiButton{Text=text,Bounds=new Rectangle(x,y,width,34),Primary=primary,Font=new Font("Segoe UI",10.5f),AccessibleName=text};if(primary)b.Height=42;parent.Controls.Add(b);return b;}
 static Label MakeLabel(Control parent,string text,int x,int y,int width,int height,float size,FontStyle style,Color color){var l=new Label{Text=text,Bounds=new Rectangle(x,y,width,height),Font=new Font("Segoe UI",size,style),ForeColor=color,BackColor=Color.Transparent};parent.Controls.Add(l);return l;}
 static void Rule(Control parent,int x,int y,int width){parent.Controls.Add(new Panel{Bounds=new Rectangle(x,y,width,1),BackColor=RuleColor});}
 void ToggleDiagnostics(){diagnosticsOpen=!diagnosticsOpen;diagnosticBody.Visible=diagnosticsOpen;body.AutoScrollMinSize=diagnosticsOpen?new Size(0,diagnosticBody.Bottom+12):Size.Empty;diagnostics.Text=diagnosticsOpen?"Скрыть отчёт":"Диагностика";if(diagnosticsOpen)body.ScrollControlIntoView(diagnosticBody);else body.AutoScrollPosition=Point.Empty;}
 void CopyReport(){try{if(details.Text.Length>0)Clipboard.SetText(details.Text);}catch(System.Runtime.InteropServices.ExternalException){description.Text="Буфер обмена занят. Повторите копирование отчёта.";description.AccessibleDescription=description.Text;}}
 bool Confirm(string text,string title){
  if(readOnly&&!renderOnly)return false;
  using(var dialog=new Form{Text=title,ClientSize=new Size(520,350),FormBorderStyle=FormBorderStyle.None,MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false,StartPosition=FormStartPosition.CenterParent,BackColor=WindowColor,ForeColor=TextColor,Font=new Font("Segoe UI",10.5f),AutoScaleMode=AutoScaleMode.Dpi,AutoScaleDimensions=new SizeF(96,96)}){
   using(var clip=Shape.Round(dialog.ClientRectangle,16))dialog.Region=new Region(clip);
   dialog.Paint+=(s,e)=>{using(var outline=Shape.Round(new Rectangle(0,0,dialog.ClientSize.Width-1,dialog.ClientSize.Height-1),16))using(var pen=new Pen(RuleColor))e.Graphics.DrawPath(pen,outline);};
   MakeLabel(dialog,title,28,24,418,34,17,FontStyle.Bold,TextColor);
   var dismiss=MakeButton(dialog,"",462,18,30,false);((UiButton)dismiss).Glyph=0xf659;dismiss.AccessibleName="Отмена";dismiss.DialogResult=DialogResult.Cancel;
   var message=new TextBox{Bounds=new Rectangle(28,76,464,197),Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=WindowColor,ForeColor=TextColor,Font=new Font("Segoe UI",10.5f),Text=text,ScrollBars=ScrollBars.Vertical};dialog.Controls.Add(message);
   var cancel=MakeButton(dialog,"Отмена",28,291,145,false);cancel.DialogResult=DialogResult.Cancel;
   var accept=MakeButton(dialog,title=="Восстановление озвучки"?"Вернуть озвучку":"Установить для теста",190,291,302,true);accept.DialogResult=DialogResult.OK;
   dialog.AcceptButton=cancel;dialog.CancelButton=cancel;dialog.Shown+=(s,e)=>{cancel.Focus();message.SelectionLength=0;};
   return dialog.ShowDialog(this)==DialogResult.OK&&!readOnly;
  }
 }
 internal static bool MayInstall(MaxVoiceState value){return value!=null&&value.CanInstall&&!value.GameRunning;}
 internal static bool MayRestore(MaxVoiceState value){return value!=null&&value.CanRestore&&!value.GameRunning;}
 void RefreshButtons(){bool active=!busy&&(!readOnly||renderOnly),known=state!=null;refresh.Enabled=active;install.Enabled=active&&MayInstall(state);install.Visible=!known||!state.Installed;verify.Enabled=active&&known&&state.Installed;verify.Visible=known&&state.Installed;restore.Enabled=active&&MayRestore(state);close.Enabled=!busy;back.Enabled=!busy;topClose.Enabled=!busy;diagnostics.Enabled=!busy;copyReport.Enabled=details.Text.Length>0;progress.Visible=busy;}
 void Apply(MaxVoiceState value,string raw){state=value;stateHeading.Text=value.GameRunning?"Закройте F1 25":value.Status=="error"?"Проверка не завершена":value.Installed?"Озвучка Макса установлена":value.Code=="restored"||value.Code=="already_restored"?"Предыдущая озвучка восстановлена":value.Code=="unknown_audio_bank"?"Нужна проверка совместимости":value.Code=="unfinished_transaction"?"Нужно восстановление озвучки":value.CanInstall?"Аудиобанк совместим":"Установка сейчас недоступна";stateMarker.BackColor=value.GameRunning?WarningColor:value.Installed||value.CanInstall?SuccessColor:WarningColor;description.Text=value.Message;if(value.GameRunning)description.Text+="\nИгра запущена: изменение файлов отложено.";description.AccessibleDescription=description.Text;details.Text=raw;RefreshButtons();}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var p=Shape.Round(new Rectangle(0,0,ClientSize.Width-1,ClientSize.Height-1),16))using(var pen=new Pen(RuleColor))e.Graphics.DrawPath(pen,p);}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(ClientSize.Width>32&&ClientSize.Height>32){var previous=Region;using(var p=Shape.Round(ClientRectangle,16))Region=new Region(p);if(previous!=null)previous.Dispose();}}
 protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Escape){e.Handled=true;e.SuppressKeyPress=true;if(!busy)Close();return;}base.OnKeyDown(e);}
 internal static bool IsMutation(string action){return action=="install"||action=="restore";}
 internal static string Quote(string value){
  if(value==null||value.IndexOf('\0')>=0||value.IndexOf('\r')>=0||value.IndexOf('\n')>=0)throw new ArgumentException("Неверный аргумент.");
  var b=new StringBuilder("\"");int slashes=0;foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='\"'){b.Append('\\',slashes*2+1);b.Append(c);}else{b.Append('\\',slashes);b.Append(c);}slashes=0;}b.Append('\\',slashes*2);return b.Append('"').ToString();
 }
 static string PowerShell64(){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.Is64BitOperatingSystem&&!Environment.Is64BitProcess?"Sysnative":"System32","WindowsPowerShell","v1.0","powershell.exe");}
 internal static string Arguments(string host,string action,string gamePath,string result){if(action!="status"&&action!="verify"&&!IsMutation(action))throw new ArgumentException("Неизвестная операция.");if(!Path.IsPathRooted(gamePath)||gamePath.IndexOf('"')>=0)throw new ArgumentException("Нужен полный путь к игре.");return "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "+Quote(host)+" -Action "+action+" -GamePath "+Quote(Path.GetFullPath(gamePath))+" -ResultPath "+Quote(result);}
 sealed class RuntimeLease:IDisposable {
  readonly List<FileStream> streams=new List<FileStream>();
  static readonly string[,] Pins=new string[,] {
   {"audio/MaxVoice.ps1","ef566595a63291fca2e9d3b6160d684df48d79befbebf708692b512ce3bac1d8"},
   {"audio/MaxVoiceHost.ps1","d09ee6ada15155b1876393b0759185d408935b11e5c7b0c60dc6db56bf6b9708"},
   {"audio/payload/max-ru-test.json","24353455483c192129a8cc6b9b6238d481da901d5237b9d16ca6c9d6b789e77b"},
   {"audio/payload/max-ru-test.delta","e26fb80e9ee24c335f2bdb1197d49c1104519670f56a619e7fe06e39446bca16"}
  };
  public RuntimeLease(string packageHome,string action){try{
   if(action!="status"&&action!="verify"&&!IsMutation(action))throw new ArgumentException("Неизвестная операция озвучки.");
   // Only install consumes replacement bytes. Status/verify/restore can inspect
   // and recover the owned backup even when unused payload files are missing.
   int count=action=="install"?Pins.GetLength(0):2;
   string root=Path.GetFullPath(packageHome).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   for(int n=0;n<count;n++){
    string path=Path.GetFullPath(Path.Combine(root,Pins[n,0].Replace('/',Path.DirectorySeparatorChar))),cursor=path;
    if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("Путь аудиомодуля вышел из папки лаунчера.");
    while(!String.IsNullOrEmpty(cursor)){if((File.Exists(cursor)||Directory.Exists(cursor))&&(File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)throw new IOException("Аудиомодуль расположен по ссылке.");cursor=Path.GetDirectoryName(cursor);}
    var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);streams.Add(stream);
    if(stream.Length<=0||stream.Length>67108864)throw new IOException("Размер файла аудиомодуля не прошёл проверку.");
    string hash;using(var h=System.Security.Cryptography.SHA256.Create())hash=BitConverter.ToString(h.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    if(hash!=Pins[n,1])throw new IOException("Файл аудиомодуля изменён. Переоткройте исходный EXE лаунчера: "+Path.GetFileName(path));stream.Position=0;
   }
  }catch{Dispose();throw;}}
  public void Dispose(){foreach(var stream in streams)stream.Dispose();streams.Clear();}
 }
 internal static MaxVoiceState ValidateCompletion(string raw,int exit){
  var value=MaxVoiceState.Parse(raw);
  int expected=value.Status=="error"?1:value.Status=="not_ready"||value.Status=="recovery_required"?2:value.Status=="ready"||value.Status=="installed"||value.Status=="installed_external"||value.Status=="restored"?0:-1;
  if(exit!=expected||expected<0)throw new InvalidDataException("Ответ аудиомодуля не соответствует коду завершения: "+exit);
  return value;
 }
 static string Execute(string packageHome,string action,string gamePath){
  string owned=null,raw=null;RuntimeLease lease=null;int exit=-1;
  string operation=SupportDiagnostics.Begin("max_voice",action,gamePath);
  try{
   SupportDiagnostics.Stage(operation,"runtime-validation","Проверка файлов модуля перед запуском.");
   lease=new RuntimeLease(packageHome,action);
   string host=Path.Combine(packageHome,"audio","MaxVoiceHost.ps1");if(!File.Exists(host)||(File.GetAttributes(host)&FileAttributes.ReparsePoint)!=0)throw new IOException("Отсутствует аудиомодуль. Запустите полный EXE лаунчера заново.");
   SupportDiagnostics.Stage(operation,"temporary-result","Подготовка отдельного файла результата.");
   string temp=Path.GetFullPath(Path.GetTempPath());if((File.GetAttributes(temp)&FileAttributes.ReparsePoint)!=0)throw new IOException("Временная папка является ссылкой.");
   owned=Path.Combine(temp,"F1RU-MaxVoice-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(owned);string result=Path.Combine(owned,"result.json");
   var start=new ProcessStartInfo(PowerShell64(),Arguments(host,action,gamePath,result)){UseShellExecute=IsMutation(action),CreateNoWindow=!IsMutation(action),WindowStyle=ProcessWindowStyle.Hidden};if(IsMutation(action))start.Verb="runas";
   SupportDiagnostics.Stage(operation,"host-launch",IsMutation(action)?"Запуск с подтверждением Windows.":"Запуск проверки без повышения прав.");
   using(var process=Process.Start(start)){if(process==null)throw new IOException("Windows не вернула процесс настройки.");SupportDiagnostics.Stage(operation,"host-wait","Модуль запущен; ожидается завершение.");process.WaitForExit();exit=process.ExitCode;}
   SupportDiagnostics.Stage(operation,"result-validation","Код завершения модуля: "+exit);
   if(!File.Exists(result)||(File.GetAttributes(result)&FileAttributes.ReparsePoint)!=0||new FileInfo(result).Length>262144)throw new IOException("Аудиомодуль не записал проверяемый результат. Код: "+exit);
   raw=File.ReadAllText(result,Encoding.UTF8);ValidateCompletion(raw,exit);SupportDiagnostics.End(operation,exit,raw,null);return raw;
  }catch(Exception ex){SupportDiagnostics.End(operation,exit,raw,ex);throw;}
  finally{if(lease!=null)lease.Dispose();if(owned!=null)Cleanup(owned);}
 }
 internal static void Cleanup(string directory){
  try{string parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);string path=Path.GetFullPath(directory);string name=Path.GetFileName(path);const string prefix="F1RU-MaxVoice-";Guid id;if(!String.Equals(Path.GetDirectoryName(path),parent,StringComparison.OrdinalIgnoreCase)||!name.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring(prefix.Length),"N",out id)||!Directory.Exists(path)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return;string result=Path.Combine(path,"result.json");if(File.Exists(result)&&(File.GetAttributes(result)&FileAttributes.ReparsePoint)==0)File.Delete(result);if(Directory.GetFileSystemEntries(path).Length==0)Directory.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}
 }
 void Run(string action){if(busy||readOnly)return;busy=true;RefreshButtons();description.Text=IsMutation(action)?"Подтвердите запрос Windows и дождитесь завершения. Игра автоматически не закрывается.":"Проверяем аудиофайлы без установки…";description.AccessibleDescription=description.Text;ThreadPool.QueueUserWorkItem(delegate{string raw=null,error=null;MaxVoiceState value=null;try{raw=Execute(home,action,game);value=MaxVoiceState.Parse(raw);}catch(Win32Exception ex){error=ex.NativeErrorCode==1223?"Запрос Windows отменён; установка не началась.":ex.Message;}catch(Exception ex){error=ex.Message;}if(!IsDisposed)try{BeginInvoke((Action)delegate{busy=false;if(value!=null)Apply(value,raw);else{state=null;stateHeading.Text="Проверка не завершена";stateMarker.BackColor=WarningColor;description.Text=error;description.AccessibleDescription=error;details.Text=error;RefreshButtons();}});}catch(InvalidOperationException){}});}
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
   test(MayInstall(value)&&!MayRestore(value),"ready bank install guard");
   var running=MaxVoiceState.Parse(ready.Replace("\"gameRunning\":false","\"gameRunning\":true"));test(!MayInstall(running)&&!MayRestore(running),"running game blocks both mutations");
   var unsupported=MaxVoiceState.Parse(ready.Replace("\"canInstall\":true","\"canInstall\":false"));test(!MayInstall(unsupported),"unsupported bank blocks installation");test(!MayInstall(null)&&!MayRestore(null),"unknown state fails closed");
   using(var w=new MaxVoiceWindow(Path.GetFullPath(home),Path.GetFullPath(home),true)){
    w.Apply(value,ready);test(!w.install.Enabled&&!w.restore.Enabled&&!w.refresh.Enabled,"self-test does not start host");test(w.close.Enabled,"test can close");test(w.details.Text==ready&&w.LastReport==ready,"full diagnostics preserved");test(w.description.Text=="test","message preserved");
    test(w.BackColor==WindowColor&&w.ForeColor==TextColor,"dark palette");test(w.Font.SizeInPoints==10.5f,"readable base typography");test(w.FormBorderStyle==FormBorderStyle.None,"modal surface");test(w.heading.Text=="Голос Макса","reference heading");
    test(!w.diagnosticsOpen,"diagnostics initially collapsed");w.ToggleDiagnostics();test(w.diagnosticsOpen&&w.body.AutoScrollMinSize.Height>0,"diagnostics expands into scrollable body");w.ToggleDiagnostics();test(!w.diagnosticsOpen&&w.body.AutoScrollMinSize==Size.Empty,"diagnostics collapses");
    test(w.CancelButton==w.close,"escape closes to parent");test(!w.Confirm("test","test"),"self-test confirmation never opens or authorizes host");
    w.busy=true;w.RefreshButtons();test(!w.close.Enabled&&!w.topClose.Enabled&&!w.back.Enabled&&!w.diagnostics.Enabled,"busy surface cannot close or change views");var closing=new FormClosingEventArgs(CloseReason.UserClosing,false);w.OnFormClosing(closing);test(closing.Cancel,"busy close veto");
    w.busy=false;w.Apply(running,ready);test(w.description.Text.Contains("Игра запущена")&&!w.install.Enabled&&!w.restore.Enabled,"running message and mutation gates");
    test(w.stateHeading.Text=="Закройте F1 25","running game has a clear title");
    string errorRaw=ready.Replace("\"status\":\"ready\"","\"status\":\"error\"").Replace("\"canInstall\":true","\"canInstall\":false");w.Apply(MaxVoiceState.Parse(errorRaw),errorRaw);test(w.stateHeading.Text=="Проверка не завершена"&&!w.install.Enabled,"process/permission errors differ from unsupported audio");
   }
   using(var preview=CreatePreview(home,"supported")){test(preview.readOnly&&preview.renderOnly&&preview.install.Enabled,"preview is visually ready but host-isolated");string before=preview.LastReport;preview.Run("status");test(preview.LastReport==before&&!preview.busy,"preview status never invokes host");}
   using(var preview=CreatePreview(home,"unsupported")){test(!preview.install.Enabled&&!preview.restore.Enabled,"unsupported preview fails closed");}
   using(var preview=CreatePreview(home,"diagnostics")){test(preview.diagnosticsOpen&&preview.LastReport.Contains("previewOnly"),"diagnostics preview is explicit synthetic report");}
   test(File.Exists(Path.Combine(home,"audio","MaxVoice.ps1")),"engine included");test(File.Exists(Path.Combine(home,"audio","MaxVoiceHost.ps1")),"host included");test(File.Exists(Path.Combine(home,"audio","payload","max-ru-test.json")),"manifest included");test(File.Exists(Path.Combine(home,"audio","payload","max-ru-test.delta")),"literal patch included");
   test(ValidateCompletion(ready,0).CanInstall,"successful ready result and exit agree");
   rejected=false;try{ValidateCompletion(ready,1);}catch(InvalidDataException){rejected=true;}test(rejected,"nonzero exit cannot report success");
   rejected=false;try{MaxVoiceState.Parse(ready.Replace("\"status\":\"ready\"","\"status\":\"error\""));}catch(InvalidDataException){rejected=true;}test(rejected,"error cannot grant mutation");
   using(var lease=new RuntimeLease(home,"install")){
    test(File.ReadAllBytes(Path.Combine(home,"audio","MaxVoiceHost.ps1")).Length>0,"lease permits child reading host");
    foreach(string file in new[]{"MaxVoice.ps1","MaxVoiceHost.ps1","payload/max-ru-test.json","payload/max-ru-test.delta"}){
     rejected=false;try{using(var stream=new FileStream(Path.Combine(home,"audio",file),FileMode.Open,FileAccess.Write,FileShare.ReadWrite)){} }catch(IOException){rejected=true;}test(rejected,"lease denies write to "+file);
    }
   }
   string isolated=Path.Combine(Path.GetTempPath(),"F1RU-MaxLeaseTest-"+Guid.NewGuid().ToString("N"));
   string audio=Path.Combine(isolated,"audio"),core=Path.Combine(audio,"MaxVoice.ps1");Directory.CreateDirectory(audio);File.WriteAllText(core,"tampered fixture source");
   try{rejected=false;try{using(var lease=new RuntimeLease(isolated,"restore")){} }catch(IOException){rejected=true;}test(rejected,"tampered core rejected before elevation");}
   finally{File.Delete(core);Directory.Delete(audio);Directory.Delete(isolated);}
   string coreOnly=isolated+"-core-only";Directory.CreateDirectory(Path.Combine(coreOnly,"audio"));
   foreach(string file in new[]{"MaxVoice.ps1","MaxVoiceHost.ps1"})File.Copy(Path.Combine(home,"audio",file),Path.Combine(coreOnly,"audio",file));
   try{
    foreach(string action in new[]{"status","verify","restore"})using(var lease=new RuntimeLease(coreOnly,action)){test(true,"core-only package supports "+action);}
    rejected=false;try{using(var lease=new RuntimeLease(coreOnly,"install")){} }catch(IOException){rejected=true;}test(rejected,"install still requires complete pinned payload");
    using(var lease=new RuntimeLease(coreOnly,"restore")){rejected=false;try{using(var stream=new FileStream(Path.Combine(coreOnly,"audio","MaxVoiceHost.ps1"),FileMode.Open,FileAccess.Write,FileShare.ReadWrite)){} }catch(IOException){rejected=true;}test(rejected,"core-only restore protects host while running");}
   }finally{foreach(string file in new[]{"MaxVoice.ps1","MaxVoiceHost.ps1"})File.Delete(Path.Combine(coreOnly,"audio",file));Directory.Delete(Path.Combine(coreOnly,"audio"));Directory.Delete(coreOnly);}
   report.Append("MAX_VOICE_UI_PASS checks=").Append(checks).Append(" host_calls=0 game_writes=false registry_writes=false\n");File.WriteAllText(resultPath,report.ToString(),Encoding.UTF8);return 0;
  }catch(Exception ex){File.WriteAllText(resultPath,"MAX_VOICE_UI_FAIL "+ex+"\n",Encoding.UTF8);return 1;}
 }
}
