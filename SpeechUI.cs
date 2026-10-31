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

sealed class SpeechState {
 public string Status, Code, Message, Details;
 public bool ModelInstalled, Installed, NeedsRestart, GameRunning, CanInstall, CanRestore;
 public int CommandCount;
 public static SpeechState Parse(string json) {
  if(json==null||json.Length>262144)throw new InvalidDataException("Ответ голосового модуля слишком велик.");
  var serializer=new JavaScriptSerializer{MaxJsonLength=262144,RecursionLimit=20};
  var data=serializer.DeserializeObject(json) as Dictionary<string,object>;
  if(data==null)throw new InvalidDataException("Голосовой модуль вернул неверный ответ.");
  var state=new SpeechState();
  state.Status=ReadText(data,"status",80);state.Code=ReadText(data,"code",120);state.Message=ReadText(data,"message",8000);
  state.ModelInstalled=ReadBool(data,"modelInstalled");state.Installed=ReadBool(data,"installed");state.NeedsRestart=ReadBool(data,"needsRestart");
  state.GameRunning=ReadBool(data,"gameRunning");state.CanInstall=ReadBool(data,"canInstall");state.CanRestore=ReadBool(data,"canRestore");
  object count;if(!data.TryGetValue("commandCount",out count)||!(count is int)||(int)count<0||(int)count>10000)throw new InvalidDataException("Неверное число команд в ответе.");
  state.CommandCount=(int)count;object details;state.Details=data.TryGetValue("details",out details)?serializer.Serialize(details):"";return state;
 }
 static string ReadText(Dictionary<string,object> data,string key,int maximum){object value;if(!data.TryGetValue(key,out value)||!(value is string)||((string)value).Length>maximum)throw new InvalidDataException("Неверное поле ответа: "+key);return (string)value;}
 static bool ReadBool(Dictionary<string,object> data,string key){object value;if(!data.TryGetValue(key,out value)||!(value is bool))throw new InvalidDataException("Неверное поле ответа: "+key);return (bool)value;}
}

sealed class SpeechWindow : Form {
 readonly string home,game;readonly bool readOnly;
 readonly Label heading,stateText,modelText,pathText;
 readonly Button refresh,install,restore,verify,model,migrate,close,detailedGuide,extra,back,windowClose,copyReport;
 readonly TextBox diagnostics;readonly ListView commands;readonly TabControl tabs;
 readonly ProgressBar progress;readonly ContextMenuStrip additional;SpeechState state;bool busy,renderOnly;
 public string LastReport{get;private set;}
 const string Sample="{\"status\":\"ready\",\"code\":\"READY\",\"message\":\"Русские команды настроены для английского языка игры.\",\"modelInstalled\":true,\"installed\":true,\"needsRestart\":false,\"gameRunning\":false,\"canInstall\":true,\"canRestore\":true,\"commandCount\":37,\"details\":{\"preview\":true,\"systemChanges\":false}}";
 static readonly Color Ink=Color.FromArgb(244,245,245),Muted=Color.FromArgb(157,164,170),Accent=Color.FromArgb(227,38,46),Surface=Color.FromArgb(25,28,32),Line=Color.FromArgb(48,53,59);
 public SpeechWindow(string packageHome,string gamePath,bool preview) {
  home=Path.GetFullPath(packageHome);game=gamePath;readOnly=preview;
  Text="F1 25 · Голосовые команды";ClientSize=new Size(560,520);FormBorderStyle=FormBorderStyle.None;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;AutoScaleMode=AutoScaleMode.Dpi;
  BackColor=Surface;ForeColor=Ink;Font=new Font("Segoe UI",10.5f);LastReport="";KeyPreview=true;DoubleBuffered=true;
  using(var clip=Shape.Round(ClientRectangle,14))Region=new Region(clip);
  Paint+=(s,e)=>{using(var pen=new Pen(Line))using(var outline=Shape.Round(new Rectangle(0,0,Width-1,Height-1),14))e.Graphics.DrawPath(pen,outline);};
  back=ButtonAt(this,"Озвучка",16,12,128,34,false);((UiButton)back).Glyph=0xf12f;back.AccessibleName="Назад к озвучке";back.Click+=(s,e)=>Close();
  LabelAt(this,"Голосовые команды",163,14,330,28,12,FontStyle.Bold);
  windowClose=ButtonAt(this,"",514,12,30,30,false);((UiButton)windowClose).Glyph=0xf659;windowClose.AccessibleName="Закрыть голосовые команды";windowClose.Click+=(s,e)=>Close();
  tabs=new SpeechTabs{Bounds=new Rectangle(16,60,528,392),Font=Font};Controls.Add(tabs);
  var setup=new TabPage("Настройка"){BackColor=Surface,ForeColor=Ink};var guide=new TabPage("Все команды"){BackColor=Surface,ForeColor=Ink};var log=new TabPage("Диагностика"){BackColor=Surface,ForeColor=Ink};tabs.TabPages.AddRange(new[]{setup,guide,log});
  modelText=LabelAt(setup,"Русский компонент: ещё не проверен",16,16,488,22,10.5f,FontStyle.Regular);modelText.ForeColor=Muted;
  pathText=LabelAt(setup,"Папка: "+game,16,41,488,22,10.5f,FontStyle.Regular);pathText.AutoEllipsis=true;pathText.ForeColor=Muted;pathText.AccessibleDescription=game;
  heading=LabelAt(setup,"Проверяем состояние…",16,78,488,54,16,FontStyle.Bold);
  stateText=LabelAt(setup,"Установка сама не начинается. Проверка только читает настройки.",16,133,488,55,10.5f,FontStyle.Regular);stateText.AutoEllipsis=true;stateText.ForeColor=Muted;
  install=ButtonAt(setup,"Настроить русские команды",16,196,278,42,true);install.Click+=(s,e)=>ConfirmInstall();
  model=ButtonAt(setup,"Установить компонент Microsoft",16,196,278,42,true);model.Click+=(s,e)=>ConfirmModel();
  verify=ButtonAt(setup,"Проверить установку",16,196,278,42,true);verify.Click+=(s,e)=>Run("verify",null);
  refresh=ButtonAt(setup,"Проверить состояние",308,196,196,42,false);refresh.Click+=(s,e)=>Run("status",null);
  // Rare actions remain independently gated and are surfaced through the menu.
  restore=ButtonAt(setup,"Откатить голосовые команды",16,196,278,42,false);restore.Visible=false;restore.Click+=(s,e)=>ConfirmRestore();
  migrate=ButtonAt(setup,"Перенести прежнюю настройку…",16,196,278,42,false);migrate.Visible=false;migrate.Click+=(s,e)=>ConfirmMigration();
  setup.Controls.Add(new Panel{Bounds=new Rectangle(16,256,488,1),BackColor=Line});
  LabelAt(setup,"На трассе",16,274,488,24,11,FontStyle.Bold);
  LabelAt(setup,"Удерживайте T (русская Е) и скажите «Прогноз погоды».\nПроизносите фразу как в русском радиоменю.",16,300,488,49,10.5f,FontStyle.Regular).ForeColor=Muted;
  LabelAt(guide,"Произносите фразу из русского столбца.",16,12,488,24,10.5f,FontStyle.Regular).ForeColor=Muted;
  commands=new ListView{Bounds=new Rectangle(16,42,488,224),View=View.Details,FullRowSelect=true,GridLines=false,HideSelection=false,HeaderStyle=ColumnHeaderStyle.Nonclickable,BackColor=Surface,ForeColor=Ink,BorderStyle=BorderStyle.FixedSingle,Font=Font,ShowItemToolTips=true,OwnerDraw=true};commands.Columns.Add("Скажите по-русски",278);commands.Columns.Add("Игровая команда",188);guide.Controls.Add(commands);
  commands.DrawColumnHeader+=(s,e)=>{using(var b=new SolidBrush(Color.FromArgb(35,39,44)))e.Graphics.FillRectangle(b,e.Bounds);TextRenderer.DrawText(e.Graphics,e.Header.Text,Font,new Rectangle(e.Bounds.X+8,e.Bounds.Y,e.Bounds.Width-12,e.Bounds.Height),Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};
  commands.DrawItem+=(s,e)=>{};
  commands.DrawSubItem+=(s,e)=>{using(var b=new SolidBrush(e.Item.Selected?Color.FromArgb(45,50,56):Surface))e.Graphics.FillRectangle(b,e.Bounds);TextRenderer.DrawText(e.Graphics,e.SubItem.Text,Font,new Rectangle(e.Bounds.X+7,e.Bounds.Y,e.Bounds.Width-11,e.Bounds.Height),Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);};
  LabelAt(guide,"Доступность зависит от режима. Для первого теста\nиспользуйте «Прогноз погоды» на тренировке.",16,272,488,40,10.5f,FontStyle.Regular).ForeColor=Muted;
  detailedGuide=ButtonAt(guide,"Подробный справочник",16,314,278,34,false);detailedGuide.Click+=(s,e)=>OpenGuide();
  diagnostics=new TextBox{Bounds=new Rectangle(16,16,488,272),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Font=new Font("Consolas",10.5f),BackColor=Color.FromArgb(17,19,21),ForeColor=Ink,BorderStyle=BorderStyle.FixedSingle,AccessibleName="Полный диагностический отчёт"};log.Controls.Add(diagnostics);
  copyReport=ButtonAt(log,"Копировать отчёт",16,304,238,40,true);copyReport.Click+=(s,e)=>CopyReport();
  additional=new ContextMenuStrip{BackColor=Surface,ForeColor=Ink,Font=Font,ShowImageMargin=false,Renderer=new SpeechMenuRenderer()};
  extra=ButtonAt(this,"Дополнительно",24,480,190,30,false);((UiButton)extra).Glyph=0xf5d4;extra.Click+=(s,e)=>ShowAdditional();
  progress=new ProgressBar{Bounds=new Rectangle(24,453,512,3),Style=ProgressBarStyle.Marquee,Visible=false};Controls.Add(progress);
  LabelAt(this,readOnly?"Предпросмотр · данные демонстрационные":"Отдельная фоновая программа не требуется.",24,457,512,22,10.5f,FontStyle.Regular).ForeColor=Muted;
  close=ButtonAt(this,"Закрыть",414,480,122,30,false);close.Click+=(s,e)=>Close();CancelButton=close;
  LoadCommands();RefreshButtons();
  if(readOnly){Apply(SpeechState.Parse(Sample),Sample);heading.Text="Предпросмотр: команды настроены";}
  Shown+=(s,e)=>{if(!readOnly)Run("status",null);};
 }
 static Label LabelAt(Control owner,string text,int x,int y,int w,int h,float size,FontStyle style){var label=new Label{Text=text,Bounds=new Rectangle(x,y,w,h),Font=new Font("Segoe UI",size,style),BackColor=Color.Transparent};owner.Controls.Add(label);return label;}
 static Button ButtonAt(Control owner,string text,int x,int y,int w,int h,bool primary){var button=new UiButton{Text=text,Bounds=new Rectangle(x,y,w,h),Primary=primary,Font=new Font("Segoe UI",10.5f,FontStyle.Bold),AccessibleName=text};owner.Controls.Add(button);return button;}
 void ShowAdditional(){
  if(busy)return;additional.Items.Clear();
  AddAction("Проверить установку",verify.Enabled,()=>Run("verify",null));
  AddAction("Настроить повторно",install.Enabled,ConfirmInstall);
  additional.Items.Add(new ToolStripSeparator());
  AddAction("Вернуть прежние команды",restore.Enabled,ConfirmRestore);
  AddAction("Перенести прежнюю настройку…",migrate.Enabled,ConfirmMigration);
  additional.Show(extra,new Point(0,-additional.PreferredSize.Height));
 }
 void AddAction(string text,bool enabled,Action action){var item=new ToolStripMenuItem(text){Enabled=enabled};item.Click+=(s,e)=>action();additional.Items.Add(item);}
 internal string ReportToCopy(){return LastReport??"";}
 void CopyReport(){
  if(readOnly||busy||ReportToCopy().Length==0)return;
  try{Clipboard.SetText(ReportToCopy());copyReport.Text="Отчёт скопирован";}
  catch(Exception ex){stateText.Text="Копирование отчёта не завершено: "+ex.Message;tabs.SelectedIndex=2;}
 }
 void LoadCommands(){
  try{string file=Path.Combine(home,"speech","commands.ru.json");if(!File.Exists(file)){diagnostics.Text="Переоткройте исходный EXE: встроенный справочник команд не прошёл проверку.";return;}
   if(new FileInfo(file).Length>131072)throw new InvalidDataException("Справочник слишком велик.");
   var map=new JavaScriptSerializer{MaxJsonLength=131072}.Deserialize<Dictionary<string,string>>(File.ReadAllText(file,Encoding.UTF8));if(map==null||map.Count>500)throw new InvalidDataException("Неверный справочник.");
   foreach(var pair in map){if(pair.Key.Length>200||pair.Value==null||pair.Value.Length>200)throw new InvalidDataException("Неверная строка справочника.");var item=new ListViewItem(new[]{pair.Value,pair.Key}){ToolTipText=pair.Value+"\n"+pair.Key};commands.Items.Add(item);}
  }catch(Exception ex){diagnostics.Text="Справочник: "+ex.Message;}
 }
 void RefreshButtons(){
  bool active=!busy&&(!readOnly||renderOnly),known=state!=null&&!state.Status.Equals("error",StringComparison.OrdinalIgnoreCase)&&!state.Status.Equals("failed",StringComparison.OrdinalIgnoreCase);
  refresh.Enabled=active;verify.Enabled=active&&known&&state.Installed;
  install.Enabled=active&&known&&state.ModelInstalled&&state.CanInstall&&!state.GameRunning;
  install.Text=state!=null&&state.Code=="dictionary_update_available"?"Обновить фразы из меню":"Настроить русские команды";
  restore.Enabled=active&&known&&state.CanRestore&&!state.GameRunning;
  model.Enabled=active&&known&&!state.ModelInstalled&&!state.GameRunning;
  migrate.Enabled=active&&known&&state.ModelInstalled&&!state.GameRunning;
  bool missingModel=known&&!state.ModelInstalled,alreadyInstalled=known&&state.Installed&&state.Code!="dictionary_update_available";
  model.Visible=missingModel;verify.Visible=!missingModel&&alreadyInstalled;install.Visible=!missingModel&&!alreadyInstalled;
  restore.Visible=false;migrate.Visible=false;
  detailedGuide.Enabled=active&&File.Exists(Path.Combine(home,"speech","COMMANDS.html"));close.Enabled=!busy;back.Enabled=!busy;windowClose.Enabled=!busy;extra.Enabled=active;copyReport.Enabled=active&&ReportToCopy().Length>0;progress.Visible=busy;
 }
 void Apply(SpeechState result,string raw){
  state=result;LastReport=raw;diagnostics.Text=raw;
  copyReport.Text="Копировать отчёт";
  heading.Text=result.Installed?"Русские команды настроены":result.ModelInstalled?"Русский компонент установлен":"Нужен русский компонент Microsoft";
  if(result.Code=="external_route")heading.Text="Команды уже настроены ранее";
  if(result.Code=="dictionary_update_available")heading.Text="Доступны исправленные фразы из меню";
  if(result.GameRunning)heading.Text="Игра запущена — изменения отложены";
  if(result.Status.Equals("error",StringComparison.OrdinalIgnoreCase)||result.Status.Equals("failed",StringComparison.OrdinalIgnoreCase))heading.Text="Проверка требует внимания";
  stateText.Text=result.Message+(result.NeedsRestart?"\nПерезапустите игру после завершения настройки.":"");
  stateText.AccessibleDescription=stateText.Text;
  stateText.ForeColor=result.Status.Equals("error",StringComparison.OrdinalIgnoreCase)||result.Status.Equals("failed",StringComparison.OrdinalIgnoreCase)?Accent:Muted;
  modelText.Text="Русский компонент: "+(result.ModelInstalled?"установлен":"не найден")+"    ·    Команд: "+result.CommandCount;
  modelText.ForeColor=result.ModelInstalled?Color.FromArgb(84,206,105):Muted;
  RefreshButtons();
 }
 bool Confirm(string text,string title){if(readOnly)return false;using(var dialog=new SpeechConfirmWindow(title,text)){return dialog.ShowDialog(this)==DialogResult.Yes;}}
 void ConfirmInstall(){if(!install.Enabled)return;
  bool update=state!=null&&state.Code=="dictionary_update_available";
  string text=update?"Обновить фразы, чтобы они совпадали с русским радиоменю?\n\nИзменится только словарь игры; реестр и языковые привязки останутся прежними. Предыдущие фразы и исходный откат сохранятся. После обновления перезапустите игру.":"Настроить русские голосовые команды для English?\n\nБудет изменён файл команд игры и общие языковые привязки Microsoft Speech (409 / 809). Другие приложения, использующие эти привязки, тоже могут увидеть русский движок. Исходные значения сохранятся для отката.\n\nНаш русский текст и шрифты сохраняются. Язык в Steam / EA app оставьте English. После настройки перезапустите игру.\n\nПродолжить?";
  if(Confirm(text,update?"Обновление голосовых команд":"Настройка голосовых команд"))Run("install",null);
 }
 void ConfirmRestore(){if(!restore.Enabled)return;if(Confirm("Восстановить сохранённый файл голосовых команд и прежние языковые привязки?\n\nРусский перевод текста и шрифты останутся. При изменении настроек другой программой операция остановится, чтобы сохранить новые данные.","Откат голосовых команд"))Run("restore",null);}
 void ConfirmModel(){if(!model.Enabled)return;if(Confirm("Скачать и установить официальный русский компонент Microsoft Speech Recognition (примерно 18 МБ)?\n\nПотребуется интернет и подтверждение Windows. Это языковая модель, а не постоянно работающая сторонняя программа. Голосовые команды настраиваются отдельной кнопкой после установки.","Компонент Microsoft"))Run("model",null);}
 void ConfirmMigration(){
  if(!migrate.Enabled)return;using(var picker=new OpenFileDialog{Title="Выберите сохранённое состояние прежней настройки",Filter="Состояние JSON (*.json)|*.json",CheckFileExists=true,Multiselect=false,FileName="live_state.json"}){
   if(picker.ShowDialog(this)!=DialogResult.OK)return;
   if(Confirm("Перенести прежнюю настройку из выбранного файла?\n\n"+picker.FileName+"\n\nМодуль проверит текущие файлы и сохранённые значения. Игра должна быть закрыта. Русский текст и шрифты сохраняются.","Перенос прежней настройки"))Run("migrate",picker.FileName);
  }
 }
 void OpenGuide(){if(!detailedGuide.Enabled||readOnly)return;try{string file=Path.Combine(home,"speech","COMMANDS.html");if(!File.Exists(file)||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Переоткройте исходный EXE: встроенный подробный справочник не прошёл проверку.");Process.Start(new ProcessStartInfo(file){UseShellExecute=true});}catch(Exception ex){using(var dialog=new SpeechConfirmWindow("Справочник",ex.Message,true))dialog.ShowDialog(this);}}
 internal static bool IsMutation(string action){return action=="install"||action=="restore"||action=="migrate"||action=="model";}
 internal static string Quote(string value){
  if(value==null||value.IndexOf('\0')>=0||value.IndexOf('\r')>=0||value.IndexOf('\n')>=0)throw new ArgumentException("Неверный аргумент команды.");
  var output=new StringBuilder("\"");int slashes=0;foreach(char ch in value){if(ch=='\\'){slashes++;continue;}if(ch=='\"'){output.Append('\\',slashes*2+1);output.Append(ch);}else{output.Append('\\',slashes);output.Append(ch);}slashes=0;}output.Append('\\',slashes*2);return output.Append('"').ToString();
 }
 internal static string PowerShell64(){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.Is64BitOperatingSystem&&!Environment.Is64BitProcess?"Sysnative":"System32","WindowsPowerShell","v1.0","powershell.exe");}
 internal static string Arguments(string host,string action,string gamePath,string resultPath,string legacy){
  if(action!="status"&&action!="verify"&&!IsMutation(action))throw new ArgumentException("Неизвестная операция.");
  if(!Path.IsPathRooted(gamePath)||gamePath.IndexOf('"')>=0)throw new ArgumentException("Укажите полный путь к установленной F1 25.");
  string args="-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "+Quote(host)+" -Action "+action+" -GamePath "+Quote(Path.GetFullPath(gamePath))+" -ResultPath "+Quote(resultPath);
  if(action=="migrate"){if(String.IsNullOrWhiteSpace(legacy)||!Path.IsPathRooted(legacy))throw new ArgumentException("Выберите файл прежнего состояния.");args+=" -LegacyState "+Quote(Path.GetFullPath(legacy));}return args;
 }
 internal sealed class HostResponse{public SpeechState State;public string Raw,TemporaryDirectory;public int ExitCode;public bool Elevated;}
 internal static HostResponse ExecuteHost(string packageHome,string action,string gamePath,string legacy){
  string owned=null;
  try{
   string host=Path.Combine(packageHome,"speech","SpeechHost.ps1");if(!File.Exists(host))throw new FileNotFoundException("Переоткройте исходный EXE: встроенный модуль голосовых команд не прошёл проверку.");
   string parent=Path.GetFullPath(Path.GetTempPath());if((File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new IOException("Временная папка является ссылкой.");
   owned=Path.Combine(parent,"F1RU-Speech-"+Guid.NewGuid().ToString("N"));if(Directory.Exists(owned)||File.Exists(owned))throw new IOException("Временный путь уже занят.");Directory.CreateDirectory(owned);string result=Path.Combine(owned,"result.json");
   var start=new ProcessStartInfo(PowerShell64(),Arguments(host,action,gamePath,result,legacy)){UseShellExecute=IsMutation(action),CreateNoWindow=!IsMutation(action),WindowStyle=ProcessWindowStyle.Hidden};if(IsMutation(action))start.Verb="runas";
   int exit;using(var process=Process.Start(start)){if(process==null)throw new IOException("Windows не вернула процесс настройки.");process.WaitForExit();exit=process.ExitCode;}
   if(!File.Exists(result))throw new IOException("Модуль не записал результат. Код завершения: "+exit+".");
   if((File.GetAttributes(result)&FileAttributes.ReparsePoint)!=0||new FileInfo(result).Length>262144)throw new IOException("Неверный файл результата.");
   string raw=File.ReadAllText(result,Encoding.UTF8);SpeechState response=SpeechState.Parse(raw);
   if(exit!=0&&exit!=2&&exit!=1)throw new IOException("Неожиданный код завершения: "+exit+".\r\n"+response.Message);
   return new HostResponse{State=response,Raw=raw,ExitCode=exit,TemporaryDirectory=owned,Elevated=IsMutation(action)};
  }finally{if(owned!=null)Cleanup(owned);}
 }
 void Run(string action,string legacy){
  if(busy||readOnly)return;busy=true;RefreshButtons();heading.Text=action=="status"?"Проверяем состояние…":"Выполняем настройку…";stateText.Text=IsMutation(action)?"Подтвердите запрос Windows и дождитесь завершения. Игра автоматически не закрывается.":"Проверка читает состояние; установка сама не начинается.";
  ThreadPool.QueueUserWorkItem(delegate{
   string raw=null,error=null;SpeechState response=null;
   try{
    HostResponse result=ExecuteHost(home,action,game,legacy);raw=result.Raw;response=result.State;if(result.ExitCode==1)error=response.Message;
   }catch(Win32Exception ex){error=ex.NativeErrorCode==1223?"Запрос Windows отменён. Изменения не запускались.":ex.Message;}
   catch(Exception ex){error=ex.Message;}
   if(!IsDisposed)try{BeginInvoke((Action)delegate{busy=false;if(response!=null)Apply(response,raw);else state=null;if(error!=null){heading.Text="Операция не завершена";stateText.Text=error;LastReport=(raw??"")+"\r\n"+error;diagnostics.Text=LastReport;}RefreshButtons();});}catch(InvalidOperationException){}
  });
 }
 internal static void Cleanup(string directory){
  try{string root=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);string full=Path.GetFullPath(directory);string name=Path.GetFileName(full);const string prefix="F1RU-Speech-";Guid id;if(!String.Equals(Path.GetDirectoryName(full),root,StringComparison.OrdinalIgnoreCase)||!name.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring(prefix.Length),"N",out id)||!Directory.Exists(full)||(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)return;
   string result=Path.Combine(full,"result.json");if(File.Exists(result)&&(File.GetAttributes(result)&FileAttributes.ReparsePoint)==0)File.Delete(result);if(Directory.GetFileSystemEntries(full).Length==0)Directory.Delete(full);
  }catch(IOException){}catch(UnauthorizedAccessException){}
 }
 protected override bool ProcessDialogKey(Keys keyData){if(busy&&(keyData&Keys.KeyCode)==Keys.Escape)return true;return base.ProcessDialogKey(keyData);}
 protected override void OnFormClosing(FormClosingEventArgs e){if(busy){e.Cancel=true;return;}base.OnFormClosing(e);}
 protected override void Dispose(bool disposing){if(disposing&&additional!=null)additional.Dispose();base.Dispose(disposing);}
 public static int HostTest(string home,string output,string gamePath){
  var log=new StringBuilder();try{
   if(!Path.IsPathRooted(gamePath)||!Directory.Exists(gamePath))throw new ArgumentException("Для теста нужен полный путь к существующей игре.");
   string missing=Path.Combine(Path.GetTempPath(),"F1RU-MissingGame-"+Guid.NewGuid().ToString("N"));
   HostResponse absent=ExecuteHost(home,"status",missing,null);log.AppendLine("MISSING_GAME status_exit="+absent.ExitCode+" elevated="+absent.Elevated.ToString().ToLowerInvariant());log.AppendLine(absent.Raw);
   if(absent.ExitCode!=1||absent.State.Code=="host_error"||absent.Elevated||Directory.Exists(absent.TemporaryDirectory))throw new Exception("Missing-game status did not return a cleaned, non-elevated engine error.");
   HostResponse current=ExecuteHost(home,"status",gamePath,null);log.AppendLine("CURRENT_GAME status_exit="+current.ExitCode+" elevated="+current.Elevated.ToString().ToLowerInvariant());log.AppendLine(current.Raw);
   if((current.ExitCode!=0&&current.ExitCode!=2)||current.State.Code=="host_error"||current.Elevated||Directory.Exists(current.TemporaryDirectory)||current.State.CommandCount!=37)throw new Exception("Current-game status did not return the expected read-only engine result.");
   log.AppendLine("SPEECH_HOST_PASS calls=2 actions=status,status elevated=false temporary_folders=removed game_writes=false registry_writes=false");File.WriteAllText(output,log.ToString(),Encoding.UTF8);return 0;
  }catch(Exception ex){log.AppendLine(ex.ToString());File.WriteAllText(output,log.ToString(),Encoding.UTF8);return 1;}
 }
 internal static SpeechWindow CreatePreview(string home,string page){
  if(page==null)page="setup";
  if(page!="setup"&&page!="commands"&&page!="diagnostics")throw new ArgumentException("Неизвестное состояние предпросмотра.");
  var form=new SpeechWindow(home,@"C:\Games\F1 25",true);
  form.renderOnly=true;form.ShowInTaskbar=true;form.StartPosition=FormStartPosition.CenterScreen;
  form.heading.Text="Русские команды настроены";
  if(page=="commands")form.tabs.SelectedIndex=1;
  else if(page=="diagnostics")form.tabs.SelectedIndex=2;
  else{var sample=SpeechState.Parse(Sample);sample.Installed=false;sample.CanRestore=false;sample.Message="Русский текст и субтитры сохранятся.";form.Apply(sample,Sample);form.heading.Text="Можно настроить русские команды";}
  form.RefreshButtons();return form;
 }
 public static int SelfTest(string home,string output){
  int checks=0;Action<bool,string> check=(value,why)=>{if(!value)throw new Exception(why);checks++;};
  try{
   var parsed=SpeechState.Parse(Sample);check(parsed.Installed&&parsed.ModelInstalled&&parsed.CommandCount==37,"sample parse");
   foreach(string bad in new[]{"{}","[]",Sample.Replace("\"installed\":true","\"installed\":\"true\""),Sample.Replace("\"commandCount\":37","\"commandCount\":-1"),new string('x',262145)}){bool rejected=false;try{SpeechState.Parse(bad);}catch(Exception){rejected=true;}check(rejected,"malformed result rejected");}
   check(!IsMutation("status")&&!IsMutation("verify")&&IsMutation("install")&&IsMutation("restore")&&IsMutation("model")&&IsMutation("migrate"),"elevation allowlist");
   check(Quote("C:\\Games\\")=="\"C:\\Games\\\\\"","trailing slash quoting");check(Quote("a\"b")=="\"a\\\"b\"","embedded quote");
   check(Arguments(@"C:\Folder with spaces\SpeechHost.ps1","status",@"C:\Games\F1 25",@"C:\Temp\result.json",null).Contains("-Action status -GamePath \"C:\\Games\\F1 25\""),"path arguments");
   bool invalid=false;try{Arguments("host","unknown",@"C:\Games","out",null);}catch(ArgumentException){invalid=true;}check(invalid,"action injection rejected");
   invalid=false;try{Arguments("host","status","relative","out",null);}catch(ArgumentException){invalid=true;}check(invalid,"relative game path rejected");
   invalid=false;try{Arguments("host","migrate",@"C:\Games","out",null);}catch(ArgumentException){invalid=true;}check(invalid,"missing migration state rejected");
   using(var form=new SpeechWindow(home,@"C:\Games\F1 25",true)){
    check(!form.install.Enabled&&!form.restore.Enabled&&!form.model.Enabled&&!form.refresh.Enabled&&!form.migrate.Enabled&&!form.verify.Enabled&&!form.detailedGuide.Enabled,"preview is read only");
    check(form.detailedGuide.Text=="Подробный справочник","detailed guide entry");
    form.Run("install",null);check(!form.busy,"preview starts no process");check(form.tabs.TabPages.Count==3,"three pages");
    check(form.stateText.Text.Contains("английского"),"English routing description");check(form.commands.Columns.Count==2,"command reference columns");
    check(form.commands.Items.Count==37,"all command phrases retained");check(form.ReportToCopy()==Sample&&form.diagnostics.Text==Sample,"full diagnostic report retained for copying");
    foreach(TabPage tab in form.tabs.TabPages)foreach(Control control in tab.Controls)check(control.Left>=0&&control.Top>=0&&control.Right<=512&&control.Bottom<=354,"dialog child bounds");
   }
   using(var form=new SpeechWindow(home,@"C:\Games\F1 25",false)){
    check(form.state==null&&!form.install.Enabled&&!form.model.Enabled,"first action waits for status");
    form.Apply(parsed,Sample);check(form.install.Enabled&&form.restore.Enabled&&!form.model.Enabled,"installed action gating");
    parsed.Code="dictionary_update_available";form.Apply(parsed,Sample);check(form.install.Text=="Обновить фразы из меню"&&form.install.Enabled&&form.heading.Text.Contains("исправленные"),"dictionary update action");parsed.Code="READY";
    parsed.GameRunning=true;form.Apply(parsed,Sample);check(!form.install.Enabled&&!form.restore.Enabled&&!form.model.Enabled&&!form.migrate.Enabled,"running game frozen");
    parsed.GameRunning=false;parsed.ModelInstalled=false;parsed.Installed=false;parsed.CanInstall=false;parsed.CanRestore=false;form.Apply(parsed,Sample);check(form.model.Enabled&&!form.install.Enabled&&!form.restore.Enabled&&!form.migrate.Enabled,"missing model gating");
    parsed.Status="error";form.Apply(parsed,Sample);check(!form.model.Enabled&&!form.migrate.Enabled&&!form.install.Enabled&&!form.restore.Enabled,"error disables configuration actions");parsed.Status="ready";form.Apply(parsed,Sample);
    form.busy=true;form.RefreshButtons();check(!form.refresh.Enabled&&!form.model.Enabled&&!form.close.Enabled,"busy gating");var closing=new FormClosingEventArgs(CloseReason.UserClosing,false);form.OnFormClosing(closing);check(closing.Cancel,"busy close blocked");form.busy=false;
   }
   File.WriteAllText(output,"SPEECH_UI_PASS checks="+checks+" host_calls=0 game_writes=false registry_writes=false",Encoding.UTF8);return 0;
  }catch(Exception ex){File.WriteAllText(output,ex.ToString(),Encoding.UTF8);return 1;}
 }
}

sealed class SpeechTabs : TabControl {
 static readonly Color Surface=Color.FromArgb(25,28,32),Ink=Color.FromArgb(244,245,245),Muted=Color.FromArgb(157,164,170),Accent=Color.FromArgb(227,38,46);
 public SpeechTabs(){DrawMode=TabDrawMode.OwnerDrawFixed;SizeMode=TabSizeMode.Fixed;ItemSize=new Size(154,34);Padding=new Point(12,8);}
 protected override void OnDrawItem(DrawItemEventArgs e){
  bool selected=e.Index==SelectedIndex;Rectangle r=GetTabRect(e.Index);
  using(var brush=new SolidBrush(Surface))e.Graphics.FillRectangle(brush,r);
  TextRenderer.DrawText(e.Graphics,TabPages[e.Index].Text,Font,r,selected?Ink:Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
  if(selected)using(var pen=new Pen(Accent,2))e.Graphics.DrawLine(pen,r.Left+10,r.Bottom-2,r.Right-10,r.Bottom-2);
  if(Focused&&selected)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(r.Left+6,r.Top+4,r.Width-12,r.Height-8),Ink,Surface);
 }
 protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg==0x000F&&IsHandleCreated)using(var g=Graphics.FromHwnd(Handle))using(var pen=new Pen(Surface,3)){g.DrawRectangle(pen,1,1,Width-3,Height-3);if(TabPages.Count>0)g.DrawRectangle(pen,DisplayRectangle.Left-2,DisplayRectangle.Top-2,DisplayRectangle.Width+3,DisplayRectangle.Height+3);}}
}

sealed class SpeechMenuRenderer : ToolStripProfessionalRenderer {
 protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){using(var b=new SolidBrush(Color.FromArgb(25,28,32)))e.Graphics.FillRectangle(b,e.AffectedBounds);}
 protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){if(e.Item.Selected)using(var b=new SolidBrush(Color.FromArgb(45,50,56)))e.Graphics.FillRectangle(b,new Rectangle(Point.Empty,e.Item.Size));}
 protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=e.Item.Enabled?Color.FromArgb(244,245,245):Color.FromArgb(125,132,139);base.OnRenderItemText(e);}
}

sealed class SpeechConfirmWindow : Form {
 public SpeechConfirmWindow(string title,string text,bool information=false){
  Text=title;ClientSize=new Size(500,384);FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterParent;AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("Segoe UI",10.5f);BackColor=Color.FromArgb(25,28,32);ForeColor=Color.FromArgb(244,245,245);KeyPreview=true;
  using(var clip=Shape.Round(ClientRectangle,14))Region=new Region(clip);
  Paint+=(s,e)=>{using(var pen=new Pen(Color.FromArgb(48,53,59)))using(var outline=Shape.Round(new Rectangle(0,0,Width-1,Height-1),14))e.Graphics.DrawPath(pen,outline);};
  Controls.Add(new Label{Text=title,Bounds=new Rectangle(24,20,452,52),Font=new Font("Segoe UI",16,FontStyle.Bold),ForeColor=ForeColor,BackColor=Color.Transparent});
  Controls.Add(new TextBox{Text=text,Bounds=new Rectangle(24,84,452,224),Font=Font,Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,ScrollBars=ScrollBars.Vertical,WordWrap=true,BackColor=BackColor,ForeColor=ForeColor,AccessibleName="Описание подтверждаемого действия"});
  var cancel=new UiButton{Text=information?"Закрыть":"Отмена",Bounds=new Rectangle(information?360:236,336,116,36),Font=Font,DialogResult=DialogResult.No,AccessibleName=information?"Закрыть сообщение":"Отменить настройку"};
  string action=title.StartsWith("Обновление",StringComparison.Ordinal)?"Обновить":title.StartsWith("Компонент",StringComparison.Ordinal)?"Установить":title.StartsWith("Откат",StringComparison.Ordinal)?"Восстановить":title.StartsWith("Перенос",StringComparison.Ordinal)?"Перенести":"Настроить";
  var accept=new UiButton{Text=action,Bounds=new Rectangle(360,336,116,36),Font=new Font("Segoe UI",10.5f,FontStyle.Bold),Primary=true,DialogResult=DialogResult.Yes,AccessibleName=action};
  Controls.Add(cancel);if(!information)Controls.Add(accept);AcceptButton=cancel;CancelButton=cancel;Shown+=(s,e)=>cancel.Focus();
 }
}

sealed partial class Launcher {
 Button speechButton;
 void OpenSpeech(){if(busy)return;using(var dialog=new SpeechWindow(home,path.Text.Trim(),testing)){dialog.ShowDialog(this);if(dialog.LastReport.Length>0){lastLog=dialog.LastReport;if(logBox!=null)logBox.Text=lastLog;}}}
}
