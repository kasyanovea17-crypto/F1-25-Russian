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

sealed class EngineerVoiceState {
 public string Selection,Status,Code,Message,Backup;public bool CanApply,CanRestore,GameRunning;
 static string Text(Dictionary<string,object>d,string k,int max,bool optional=false){object o;if(!d.TryGetValue(k,out o)||o==null){if(optional)return "";throw new InvalidDataException("Нет поля аудиомодуля: "+k);}if(!(o is string)||((string)o).Length>max)throw new InvalidDataException("Неверное поле аудиомодуля: "+k);return (string)o;}
 static bool Flag(Dictionary<string,object>d,string k){object o;if(!d.TryGetValue(k,out o)||!(o is bool))throw new InvalidDataException("Неверный флаг аудиомодуля: "+k);return (bool)o;}
 public static EngineerVoiceState Parse(string raw){
  if(raw==null||raw.Length>262144)throw new InvalidDataException("Ответ аудиомодуля слишком велик.");var d=new JavaScriptSerializer{MaxJsonLength=262144,RecursionLimit=16}.DeserializeObject(raw)as Dictionary<string,object>;
  if(d==null||!d.ContainsKey("schema")||!(d["schema"]is int)||(int)d["schema"]!=1||!d.ContainsKey("engineerSlots")||!(d["engineerSlots"]is int)||(int)d["engineerSlots"]!=1596||!Flag(d,"testOnly"))throw new InvalidDataException("Неожиданный пакет озвучки инженера.");
  var s=new EngineerVoiceState{Selection=Text(d,"selection",20),Status=Text(d,"status",80),Code=Text(d,"code",120),Message=Text(d,"message",8000),Backup=Text(d,"backup",32768,true),CanApply=Flag(d,"canApply"),CanRestore=Flag(d,"canRestore"),GameRunning=Flag(d,"gameRunning")};
  if(s.Selection!="english"&&s.Selection!="russian"&&s.Selection!="mixed"&&s.Selection!="unknown")throw new InvalidDataException("Неизвестный язык инженера.");if(s.CanApply&&(s.Selection=="unknown"||(s.Status!="ready"&&s.Status!="restored")))throw new InvalidDataException("Непроверенное состояние не разрешает изменение озвучки.");return s;
 }
}

sealed class EngineerVoiceWindow:Form {
 readonly string home,game;readonly bool readOnly;bool preview,busy,diagnosticsOpen;EngineerVoiceState state;
 readonly ComboBox choice;readonly Label stateTitle,description;readonly Button apply,refresh,verify,restore,close,back,topClose,diagnostics;readonly TextBox report;readonly Panel body;readonly ProgressBar progress;
 static readonly Color Bg=Color.FromArgb(25,28,32),Ink=Color.FromArgb(244,245,245),Muted=Color.FromArgb(157,164,170),Rule=Color.FromArgb(57,62,68);
 public string LastReport {get{return report.Text;}}
 public EngineerVoiceWindow(string packageHome,string gamePath):this(packageHome,gamePath,false){}
 EngineerVoiceWindow(string packageHome,string gamePath,bool ro){
  if(!Path.IsPathRooted(packageHome)||!Path.IsPathRooted(gamePath))throw new ArgumentException("Нужны полные пути к игре и лаунчеру.");home=Path.GetFullPath(packageHome);game=Path.GetFullPath(gamePath);readOnly=ro;
  Text="Голос инженера";ClientSize=new Size(590,570);FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);Font=new Font("Segoe UI",10.5f);BackColor=Bg;ForeColor=Ink;KeyPreview=true;DoubleBuffered=true;
  back=ButtonAt(this,"Озвучка",24,16,120,false);back.Click+=(s,e)=>Close();topClose=ButtonAt(this,"",538,16,30,false);((UiButton)topClose).Glyph=0xf659;topClose.AccessibleName="Закрыть озвучку инженера";topClose.Click+=(s,e)=>Close();
  LabelAt(this,"Голос инженера",28,64,525,36,18,FontStyle.Bold,Ink);
  body=new Panel{Bounds=new Rectangle(0,112,590,372),BackColor=Bg,AutoScroll=true};Controls.Add(body);
  LabelAt(body,"Выберите озвучку",28,0,520,25,11,FontStyle.Bold,Ink);
  choice=new ComboBox{Bounds=new Rectangle(28,34,534,30),DropDownStyle=ComboBoxStyle.DropDownList,Font=new Font("Segoe UI",10.5f),AccessibleName="Озвучка инженера"};choice.Items.AddRange(new object[]{"Оригинальный инженер — English","Русский инженер — тест · 1596 фрагментов"});choice.SelectedIndex=0;body.Controls.Add(choice);choice.SelectedIndexChanged+=(s,e)=>RefreshButtons();
  LabelAt(body,"Русский вариант — синтетическая тестовая озвучка.\nПять, семь, девять и составные сводки проверьте в гонке.",28,78,534,48,10.5f,FontStyle.Regular,Ink);
  LabelAt(body,"Для игры выберите язык English в Steam или EA app.\nГолос Макса, русский текст и другие звуки не меняются.",28,139,534,48,10.5f,FontStyle.Regular,Muted);
  body.Controls.Add(new Panel{Bounds=new Rectangle(28,204,534,1),BackColor=Rule});
  stateTitle=LabelAt(body,"Состояние не проверено",28,220,534,25,11,FontStyle.Bold,Ink);description=LabelAt(body,"Нажмите «Проверить состояние». Выбор сам не применяется.",28,257,534,60,10.5f,FontStyle.Regular,Muted);description.AutoEllipsis=true;
  apply=ButtonAt(body,"Применить выбор",28,322,258,true);refresh=ButtonAt(body,"Проверить состояние",302,322,260,false);verify=ButtonAt(body,"Проверить файлы",302,322,260,false);verify.Visible=false;
  progress=new ProgressBar{Bounds=new Rectangle(28,369,534,3),Style=ProgressBarStyle.Marquee,Visible=false};body.Controls.Add(progress);
  report=new TextBox{Bounds=new Rectangle(28,390,534,180),Multiline=true,ReadOnly=true,WordWrap=false,ScrollBars=ScrollBars.Both,Font=new Font("Consolas",9),BackColor=Color.FromArgb(17,19,21),ForeColor=Ink,Visible=false,AccessibleName="Полный отчёт озвучки инженера"};body.Controls.Add(report);
  Controls.Add(new Panel{Bounds=new Rectangle(28,494,534,1),BackColor=Rule});diagnostics=ButtonAt(this,"Диагностика",28,515,115,false);restore=ButtonAt(this,"Вернуть предыдущую настройку",151,515,303,false);close=ButtonAt(this,"Закрыть",466,515,96,false);CancelButton=close;
  close.Click+=(s,e)=>Close();refresh.Click+=(s,e)=>Run("status");verify.Click+=(s,e)=>Run("verify");diagnostics.Click+=(s,e)=>ToggleDiagnostics();
  apply.Click+=(s,e)=>{if(!MayApply(state)||choice.SelectedIndex<0)return;string mode=choice.SelectedIndex==1?"russian":"english";string text=mode=="russian"?"Установить тестовую русскую озвучку инженера?\n\n1596 фрагментов. Произношение коротких чисел и игровые ветви ещё требуют проверки.\n\nОба текущих аудиобанка сохранятся. Другие звуки, русский текст и голос Макса не изменятся. Закройте игру.":"Вернуть оригинальные английские записи инженера?\n\nИзменятся только выбранные 1596 фрагментов. Другие звуки, моды и русский текст сохраняются.\n\nТекущие аудиобанки сохранятся для возврата. Закройте игру.";if(Confirm(text,"Выбор озвучки"))Run(mode);};
  restore.Click+=(s,e)=>{if(MayRestore(state)&&Confirm("Вернуть точный состав аудиобанков, сохранённый перед последним изменением в этом модуле?\n\nЭто предыдущая настройка, она может быть русской. Для оригинального инженера выберите English в списке.\n\nЗакройте игру.","Предыдущая настройка"))Run("restore");};
  Shown+=(s,e)=>{close.Focus();if(!readOnly)Run("status");};RefreshButtons();
 }
 static Label LabelAt(Control p,string text,int x,int y,int w,int h,float size,FontStyle style,Color color){var l=new Label{Text=text,Bounds=new Rectangle(x,y,w,h),Font=new Font("Segoe UI",size,style),ForeColor=color,BackColor=Color.Transparent};p.Controls.Add(l);return l;}
 static Button ButtonAt(Control p,string text,int x,int y,int width,bool primary){var b=new UiButton{Text=text,Bounds=new Rectangle(x,y,width,primary?42:34),Primary=primary,Font=new Font("Segoe UI",10.5f),AccessibleName=text};p.Controls.Add(b);return b;}
 internal static bool Mutation(string a){return a=="russian"||a=="english"||a=="restore";}
 internal static bool MayApply(EngineerVoiceState s){return s!=null&&s.CanApply&&!s.GameRunning;}
 internal static bool MayRestore(EngineerVoiceState s){return s!=null&&s.CanRestore&&!s.GameRunning;}
 void RefreshButtons(){
  bool active=!busy&&(!readOnly||preview),same=state!=null&&MayApply(state)&&state.Selection==(choice.SelectedIndex==1?"russian":"english");
  choice.Enabled=active;apply.Enabled=active&&MayApply(state)&&!same;
  apply.Text=state!=null&&state.GameRunning?"Закройте игру":same?"Уже установлено":"Применить выбор";apply.AccessibleName=apply.Text;
  refresh.Enabled=active;verify.Enabled=active;restore.Enabled=active&&MayRestore(state);close.Enabled=!busy;back.Enabled=!busy;topClose.Enabled=!busy;diagnostics.Enabled=!busy;progress.Visible=busy;
  if(!busy&&state!=null){description.Text=state.GameRunning?"Закройте F1 25 и нажмите «Проверить состояние».\nОзвучку меняем только при закрытой игре.":same?"Выбранная озвучка уже установлена. Повторное применение не требуется. Для смены голоса выберите другой вариант.":state.Message;description.AccessibleDescription=description.Text;}
 }
 void Apply(EngineerVoiceState s,string raw){state=s;choice.SelectedIndex=s.Selection=="russian"?1:0;stateTitle.Text=s.Selection=="russian"?"Русский инженер установлен":s.Selection=="english"?"Оригинальный инженер установлен":s.Selection=="mixed"?"Смешанная озвучка":"Состояние не определено";description.Text=s.Message+(s.GameRunning?"\nИгра запущена: изменение файлов отложено.":"");description.AccessibleDescription=description.Text;report.Text=raw;RefreshButtons();}
 void ToggleDiagnostics(){diagnosticsOpen=!diagnosticsOpen;report.Visible=diagnosticsOpen;body.AutoScrollMinSize=diagnosticsOpen?new Size(0,report.Bottom+16):Size.Empty;diagnostics.Text=diagnosticsOpen?"Скрыть отчёт":"Диагностика";if(diagnosticsOpen)body.ScrollControlIntoView(report);else body.AutoScrollPosition=Point.Empty;}
 bool Confirm(string text,string title){if(readOnly)return false;return MessageBox.Show(this,text,title,MessageBoxButtons.OKCancel,MessageBoxIcon.Information,MessageBoxDefaultButton.Button2)==DialogResult.OK;}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var p=Shape.Round(new Rectangle(0,0,ClientSize.Width-1,ClientSize.Height-1),16))using(var pen=new Pen(Rule))e.Graphics.DrawPath(pen,p);}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(ClientSize.Width>32&&ClientSize.Height>32){var previous=Region;using(var p=Shape.Round(ClientRectangle,16))Region=new Region(p);if(previous!=null)previous.Dispose();}}
 protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Escape){e.Handled=true;e.SuppressKeyPress=true;if(!busy)Close();return;}base.OnKeyDown(e);}
 protected override void OnFormClosing(FormClosingEventArgs e){if(busy){e.Cancel=true;return;}base.OnFormClosing(e);}
 internal static EngineerVoiceWindow CreatePreview(string home,string example){
  if(example!="supported"&&example!="installed"&&example!="english"&&example!="unsupported"&&example!="diagnostics")throw new ArgumentException("Неизвестное состояние предпросмотра.");var w=new EngineerVoiceWindow(Path.GetFullPath(home),Path.GetFullPath(home),true);w.preview=true;bool russian=example=="installed",supported=example!="unsupported";var d=new Dictionary<string,object>{{"schema",1},{"engineerSlots",1596},{"testOnly",true},{"status",supported?"ready":"error"},{"code","preview"},{"selection",russian?"russian":supported?"english":"unknown"},{"message",supported?"Оба аудиобанка проверены. Другие звуки и русский текст сохраняются.":"Выбранная версия аудиобанков отличается. Файлы сохранены без изменений."},{"backup",null},{"canApply",supported},{"canRestore",russian},{"gameRunning",false}};string raw=new JavaScriptSerializer().Serialize(d);w.Apply(EngineerVoiceState.Parse(raw),raw);if(example=="diagnostics")w.ToggleDiagnostics();return w;
 }
 internal static string Arguments(string host,string action,string game,string result){if(action!="status"&&action!="verify"&&!Mutation(action))throw new ArgumentException("Неизвестная операция озвучки.");if(!Path.IsPathRooted(game)||game.IndexOf('"')>=0)throw new ArgumentException("Нужен полный путь к игре.");return "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "+MaxVoiceWindow.Quote(host)+" -Action "+action+" -GamePath "+MaxVoiceWindow.Quote(Path.GetFullPath(game))+" -ResultPath "+MaxVoiceWindow.Quote(result);}

 sealed class RuntimeLease:IDisposable {
  readonly List<FileStream> streams=new List<FileStream>();
  static readonly string[,] Pins=new string[,] {
   {"audio/engineer/EngineerNefs.cs","df90ac59993611d2143225f914d2a8674df22a64b695ab18e9f1321908b4ee73"},
   {"audio/engineer/EngineerVoice.ps1","4a424cc092ff26c893dc2d24a0a8eedad90ea3ec2dc48bb21be379c43515bda1"},
   {"audio/engineer/EngineerVoiceHost.ps1","64b277072eadfae124050e4d8be21d3ca74b1cf1a90751bb62597c138bfb3b03"},
   {"audio/engineer/payload/engineer-voices.json","542e5747041528f06fe42a06e025b502ceb3369747c357bc7dfccc0151b65e83"},
   {"audio/engineer/payload/engineer-voices.bin","63258512143ae981dc9a8cea2f689ddee41593013ea18586edfafc58c322258b"}
  };
  public RuntimeLease(string home){try{
   for(int n=0;n<Pins.GetLength(0);n++){
    string path=Path.GetFullPath(Path.Combine(home,Pins[n,0].Replace('/',Path.DirectorySeparatorChar))),cursor=path;
    if(!path.StartsWith(Path.GetFullPath(home).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Путь аудиомодуля вышел из папки лаунчера.");
    while(!String.IsNullOrEmpty(cursor)){if((File.Exists(cursor)||Directory.Exists(cursor))&&(File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)throw new IOException("Аудиомодуль расположен по ссылке.");cursor=Path.GetDirectoryName(cursor);}
    var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);streams.Add(stream);
    if(stream.Length<=0||stream.Length>67108864)throw new IOException("Размер файла аудиомодуля не прошёл проверку.");
    string hash;using(var h=System.Security.Cryptography.SHA256.Create())hash=BitConverter.ToString(h.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    if(hash!=Pins[n,1])throw new IOException("Файл аудиомодуля изменён. Переоткройте исходный EXE лаунчера: "+Path.GetFileName(path));stream.Position=0;
   }
  }catch{Dispose();throw;}}
  public void Dispose(){foreach(var s in streams)s.Dispose();streams.Clear();}
 }

 static string Execute(string home,string action,string game){
  string owned=null;RuntimeLease lease=null;try{lease=new RuntimeLease(home);string host=Path.Combine(home,"audio","engineer","EngineerVoiceHost.ps1");if(!File.Exists(host)||(File.GetAttributes(host)&FileAttributes.ReparsePoint)!=0)throw new IOException("Модуль озвучки отсутствует. Переоткройте исходный EXE лаунчера.");string temp=Path.GetFullPath(Path.GetTempPath());if((File.GetAttributes(temp)&FileAttributes.ReparsePoint)!=0)throw new IOException("Временная папка является ссылкой.");owned=Path.Combine(temp,"F1RU-EngineerVoice-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(owned);string result=Path.Combine(owned,"result.json"),ps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.Is64BitOperatingSystem&&!Environment.Is64BitProcess?"Sysnative":"System32","WindowsPowerShell","v1.0","powershell.exe");var start=new ProcessStartInfo(ps,Arguments(host,action,game,result)){UseShellExecute=Mutation(action),CreateNoWindow=!Mutation(action),WindowStyle=ProcessWindowStyle.Hidden};if(Mutation(action))start.Verb="runas";int code;using(var process=Process.Start(start)){if(process==null)throw new IOException("Windows не вернула процесс настройки.");process.WaitForExit();code=process.ExitCode;}if(!File.Exists(result)||(File.GetAttributes(result)&FileAttributes.ReparsePoint)!=0||new FileInfo(result).Length>262144)throw new IOException("Аудиомодуль не записал проверяемый результат. Код: "+code);string raw=File.ReadAllText(result,Encoding.UTF8);EngineerVoiceState.Parse(raw);if(code!=0&&code!=1&&code!=2)throw new IOException("Неожиданный код аудиомодуля: "+code);return raw;}finally{if(lease!=null)lease.Dispose();if(owned!=null)Cleanup(owned);}
 }
 internal static void Cleanup(string path){try{string parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),full=Path.GetFullPath(path),name=Path.GetFileName(full);const string prefix="F1RU-EngineerVoice-";Guid id;if(!String.Equals(Path.GetDirectoryName(full),parent,StringComparison.OrdinalIgnoreCase)||!name.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring(prefix.Length),"N",out id)||!Directory.Exists(full)||(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)return;string result=Path.Combine(full,"result.json");if(File.Exists(result)&&(File.GetAttributes(result)&FileAttributes.ReparsePoint)==0)File.Delete(result);if(Directory.GetFileSystemEntries(full).Length==0)Directory.Delete(full);}catch(IOException){}catch(UnauthorizedAccessException){}}
 void Run(string action){if(busy||readOnly)return;busy=true;RefreshButtons();description.Text=Mutation(action)?"Подтвердите запрос Windows и дождитесь завершения. Игра автоматически не закрывается.":"Проверяем аудиобанки без изменения файлов…";ThreadPool.QueueUserWorkItem(delegate{string raw=null,error=null;EngineerVoiceState value=null;try{raw=Execute(home,action,game);value=EngineerVoiceState.Parse(raw);}catch(Win32Exception ex){error=ex.NativeErrorCode==1223?"Запрос Windows отменён; файлы не изменены.":ex.Message;}catch(Exception ex){error=ex.Message;}if(!IsDisposed)try{BeginInvoke((Action)delegate{busy=false;if(value!=null)Apply(value,raw);else{state=null;stateTitle.Text="Проверка не завершена";description.Text=error;report.Text=error;RefreshButtons();}});}catch(InvalidOperationException){}});}
 public static int SelfTest(string home,string result){int checks=0;try{Action<bool,string>test=(ok,label)=>{if(!ok)throw new Exception(label);checks++;};test(!Mutation("status")&&!Mutation("verify"),"read-only actions");test(Mutation("russian")&&Mutation("english")&&Mutation("restore"),"explicit mutation actions");var ready="{\"schema\":1,\"engineerSlots\":1596,\"testOnly\":true,\"status\":\"ready\",\"code\":\"verified\",\"selection\":\"english\",\"message\":\"test\",\"backup\":null,\"canApply\":true,\"canRestore\":false,\"gameRunning\":false}";var s=EngineerVoiceState.Parse(ready);test(MayApply(s)&&!MayRestore(s),"English state");test(!MayApply(null)&&!MayRestore(null),"unknown fails closed");var running=EngineerVoiceState.Parse(ready.Replace("\"gameRunning\":false","\"gameRunning\":true"));test(!MayApply(running)&&!MayRestore(running),"game running gates");bool bad=false;try{EngineerVoiceState.Parse(ready.Replace("1596","1595"));}catch(InvalidDataException){bad=true;}test(bad,"wrong package count rejected");bad=false;try{EngineerVoiceState.Parse(ready.Replace("english","other"));}catch(InvalidDataException){bad=true;}test(bad,"unknown choice rejected");bad=false;try{EngineerVoiceState.Parse(ready.Replace("\"status\":\"ready\"","\"status\":\"error\""));}catch(InvalidDataException){bad=true;}test(bad,"error cannot grant mutation");bad=false;try{Arguments("host","execute",Path.GetFullPath(home),"result");}catch(ArgumentException){bad=true;}test(bad,"unknown action rejected");test(Arguments("a b","status",Path.GetFullPath(home),"x").Contains("-Action status"),"host arguments");
  using(var w=new EngineerVoiceWindow(Path.GetFullPath(home),Path.GetFullPath(home),true)){w.Apply(s,ready);test(w.choice.Items.Count==2&&w.choice.SelectedIndex==0,"two languages");test(!w.apply.Enabled&&!w.restore.Enabled&&!w.refresh.Enabled,"self-test host isolation");test(w.LastReport==ready,"report retained");test(!w.Confirm("test","test"),"self-test never grants mutation");test(w.close.Enabled,"can close");test(w.CancelButton==w.close,"Escape target");w.ToggleDiagnostics();test(w.diagnosticsOpen&&w.report.Parent==w.body&&w.body.AutoScrollMinSize.Height>=w.report.Bottom,"diagnostics scroll");w.ToggleDiagnostics();test(!w.diagnosticsOpen&&w.body.AutoScrollMinSize==Size.Empty,"diagnostics collapse");w.busy=true;w.RefreshButtons();test(!w.close.Enabled&&!w.back.Enabled&&!w.topClose.Enabled&&!w.choice.Enabled,"busy gates");var e=new FormClosingEventArgs(CloseReason.UserClosing,false);w.OnFormClosing(e);test(e.Cancel,"busy close veto");test(w.BackColor==Bg&&w.Font.SizeInPoints==10.5f,"theme and typography");}
  using(var w=CreatePreview(home,"supported")){test(w.readOnly&&w.preview&&!w.apply.Enabled,"matching choice initially no-op");w.choice.SelectedIndex=1;test(w.apply.Enabled,"Russian selectable in preview");string old=w.LastReport;w.Run("russian");test(w.LastReport==old&&!w.busy,"preview never invokes backend");}
  using(var w=CreatePreview(home,"installed")){test(w.choice.SelectedIndex==1&&w.restore.Enabled,"Russian actual state");test(!w.apply.Enabled&&w.apply.Text=="Уже установлено","installed Russian explains no-op");test(w.description.Text.Contains("Повторное применение не требуется"),"installed no-op explanation");w.choice.SelectedIndex=0;test(w.apply.Enabled,"original selectable over existing Russian");test(w.apply.Text=="Применить выбор","different selection actionable label");w.choice.SelectedIndex=1;test(!w.apply.Enabled&&w.apply.Text=="Уже установлено","returning to installed selection remains no-op");w.Apply(running,ready);test(!w.apply.Enabled&&w.apply.Text=="Закройте игру","real game explains gate");w.choice.SelectedIndex=1;test(!w.apply.Enabled&&w.description.Text.Contains("Проверить состояние"),"real game blocks changed selection too");}
  using(var w=CreatePreview(home,"unsupported")){test(!w.apply.Enabled&&!w.restore.Enabled,"unsupported fails closed");}
  foreach(string f in new[]{"EngineerNefs.cs","EngineerVoice.ps1","EngineerVoiceHost.ps1","payload/engineer-voices.json","payload/engineer-voices.bin"})test(File.Exists(Path.Combine(home,"audio","engineer",f)),"runtime file "+f);
  using(var lease=new RuntimeLease(home)){
   test(File.ReadAllBytes(Path.Combine(home,"audio","engineer","EngineerVoiceHost.ps1")).Length>0,"runtime lease permits child reads");
   bool denied=false;try{using(var f=new FileStream(Path.Combine(home,"audio","engineer","EngineerVoiceHost.ps1"),FileMode.Open,FileAccess.Write,FileShare.ReadWrite)){} }catch(IOException){denied=true;}test(denied,"runtime lease denies concurrent script writes");
  }
  string isolated=Path.Combine(Path.GetTempPath(),"F1RU-EngineerLeaseTest-"+Guid.NewGuid().ToString("N"));
  try{string sub=Path.Combine(isolated,"audio","engineer");Directory.CreateDirectory(sub);string core=Path.Combine(sub,"EngineerNefs.cs");File.WriteAllText(core,"tampered fixture source");bool denied=false;try{using(var lease=new RuntimeLease(isolated)){} }catch(IOException){denied=true;}test(denied,"runtime lease rejects tampered core before host execution");File.Delete(core);Directory.Delete(sub);Directory.Delete(Path.GetDirectoryName(sub));Directory.Delete(isolated);}finally{}
  File.WriteAllText(result,"ENGINEER_VOICE_UI_PASS checks="+checks+" host_calls=0 game_writes=false registry_writes=false\n",Encoding.UTF8);return 0;
 }catch(Exception ex){File.WriteAllText(result,"ENGINEER_VOICE_UI_FAIL "+ex+"\n",Encoding.UTF8);return 1;}}
}
