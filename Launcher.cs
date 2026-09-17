using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Shape {
 public static GraphicsPath Round(Rectangle r,int radius){var p=new GraphicsPath();int d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
}
static class Glyphs {
 static PrivateFontCollection fonts=new PrivateFontCollection();
 public static void Load(string home){fonts.AddFontFile(Path.Combine(home,"assets","bootstrap-icons.ttf"));}
 public static void Draw(Graphics g,int glyph,float size,Rectangle box,Color color){using(var f=new Font(fonts.Families[0],size,FontStyle.Regular,GraphicsUnit.Pixel))using(var b=new SolidBrush(color))using(var fmt=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(char.ConvertFromUtf32(glyph),f,b,box,fmt);}
}
sealed class Card : Panel {
 public int Radius=20;
 public Card(){DoubleBuffered=true;BackColor=Color.White;}
 protected override void OnPaintBackground(PaintEventArgs e){var g=e.Graphics;var s=g.Save();if(Parent!=null){g.TranslateTransform(-Left,-Top);InvokePaintBackground(Parent,new PaintEventArgs(g,Parent.ClientRectangle));}g.Restore(s);g.SmoothingMode=SmoothingMode.AntiAlias;using(var p=Shape.Round(new Rectangle(0,0,Width-1,Height-1),Radius))using(var b=new SolidBrush(BackColor)){g.FillPath(b,p);if(BackColor==Color.White)using(var pen=new Pen(Color.FromArgb(229,229,225),1))g.DrawPath(pen,p);}}
}
sealed class UiButton : Button {
 public int Glyph;public bool Selected,Navigation,Contact,Primary;public Color Brand=Color.Black;bool hover;
 public UiButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;Cursor=Cursors.Hand;}
 protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
 protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
 protected override void OnPaint(PaintEventArgs e){
  var g=e.Graphics;var saved=g.Save();g.TranslateTransform(-Left,-Top);InvokePaintBackground(Parent,new PaintEventArgs(g,Parent.ClientRectangle));g.Restore(saved);g.SmoothingMode=SmoothingMode.AntiAlias;
  if(Contact){
   // Rounded raised keycap; the central marks come from Bootstrap Icons.
   for(int i=7;i>=1;i--)using(var shadow=Shape.Round(new Rectangle(7-i/2,8-i/2,Width-15+i,Height-15+i),15))using(var brush=new SolidBrush(Color.FromArgb(5,0,0,0)))g.FillPath(brush,shadow);
   var r=new Rectangle(5,3,Width-13,Height-13);using(var p=Shape.Round(r,14))using(var fill=new LinearGradientBrush(r,Color.White,hover?Color.FromArgb(250,239,206):Color.FromArgb(229,231,230),60f)){g.FillPath(fill,p);using(var pen=new Pen(Brand,2.5f))g.DrawPath(pen,p);}
   using(var inset=Shape.Round(new Rectangle(9,7,Width-21,Height-21),11))using(var pen=new Pen(Color.White,1))g.DrawPath(pen,inset);
   Glyphs.Draw(g,Glyph,29,new Rectangle(5,4,Width-13,Height-13),Brand);
  }else{
   Color bg=!Enabled?Color.FromArgb(223,223,217):Navigation?(Selected?Color.Black:hover?Color.FromArgb(241,232,204):Color.Transparent):Primary?(hover?Color.FromArgb(240,175,8):Color.FromArgb(251,189,20)):(hover?Color.FromArgb(43,43,43):Color.Black);
   using(var p=Shape.Round(new Rectangle(0,0,Width-1,Height-1),12))using(var b=new SolidBrush(bg))g.FillPath(b,p);
   Color fg=!Enabled?Color.FromArgb(100,100,100):Navigation?(Selected?Color.White:Color.FromArgb(45,45,45)):Primary?Color.Black:Color.White;
   if(Glyph!=0)Glyphs.Draw(g,Glyph,Navigation?18:17,Text.Length==0?new Rectangle(0,0,Width,Height):new Rectangle(Navigation?14:10,0,28,Height),fg);
   TextRenderer.DrawText(g,Text,Font,new Rectangle(Glyph!=0?47:8,0,Width-(Glyph!=0?54:16),Height),fg,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|(Navigation?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter));
  }
  if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle(4,4,Width-9,Height-9));
 }
}
sealed class Launcher : Form {
 readonly string home=AppDomain.CurrentDomain.BaseDirectory;
 readonly Color muted=Color.FromArgb(98,101,101),accent=Color.FromArgb(251,189,20),green=Color.FromArgb(32,114,61);
 readonly ToolTip contactTips=new ToolTip();readonly Dictionary<string,Panel> pages=new Dictionary<string,Panel>();readonly Dictionary<string,UiButton> nav=new Dictionary<string,UiButton>();
 const string TelegramUrl="https://t.me/Arete_Eudaimonia_Ataraxia",SteamUrl="https://steamcommunity.com/profiles/76561198993070335/",MailUrl="mailto:sobesednik617@gmail.com",GuideUrl="https://steamcommunity.com/sharedfiles/filedetails/?id=3800786076";
 TextBox path,logBox;CheckBox ready,installUpdate;Button prepare,install,restore,browse,help,details,closeButton,minimize,checkUpdate,applyUpdate,recoverUpdate;
 Label statusTitle,statusText,phase,packageVersion,updateMessage;ProgressBar progress;bool busy,prepared;Image backdrop;TextUpdate candidate;
 string lastLog="Операции ещё не выполнялись. Игра автоматически не запускается.";string currentPage="install";
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ReleaseCapture();
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
 Launcher(){
  Glyphs.Load(home);Text="F1 25 · Русский текст · 0.23";ClientSize=new Size(960,680);FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.None;BackColor=Color.Black;ForeColor=Color.Black;Font=new Font("Segoe UI",9.5f);DoubleBuffered=true;
  if(File.Exists(Path.Combine(home,"background.png")))using(var image=Image.FromFile(Path.Combine(home,"background.png")))backdrop=new Bitmap(image);
  using(var clip=Shape.Round(ClientRectangle,36))Region=new Region(clip);
  MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}};
  LabelAt(this,"F1",42,42,125,48,30,FontStyle.Bold,Color.Black);
  LabelAt(this,"РУССКИЙ ТЕКСТ",42,94,148,23,8,FontStyle.Bold,muted);
  LabelAt(this,"ПАДДОК  /  KARSVEIN",220,39,390,22,9,FontStyle.Regular,muted);
  LabelAt(this,"F1 25 на русском",220,69,408,43,23,FontStyle.Bold,Color.Black);
  LabelAt(this,"Папка игры",220,116,390,22,9,FontStyle.Regular,muted);
  var location=new Card{Bounds=new Rectangle(220,141,400,40),BackColor=Color.FromArgb(237,238,234),Radius=12};Controls.Add(location);
  path=new TextBox{Bounds=new Rectangle(12,12,332,22),BorderStyle=BorderStyle.None,Font=new Font("Segoe UI",9),BackColor=location.BackColor,Text=@"C:\Program Files (x86)\Steam\steamapps\common\F1 25",AccessibleName="Папка игры F1 25"};location.Controls.Add(path);
  path.TextChanged+=(s,e)=>{prepared=false;RefreshControls();SetStatus("Путь изменён","Проверьте выбранную папку перед установкой.",muted);};
  browse=ButtonAt(location,"",351,5,39,30,false,0xf3d8);browse.AccessibleName="Выбрать папку игры";contactTips.SetToolTip(browse,"Выбрать папку F1 25");browse.Click+=(s,e)=>{using(var d=new FolderBrowserDialog()){d.Description="Папка F1 25";if(Directory.Exists(path.Text))d.SelectedPath=path.Text;if(d.ShowDialog(this)==DialogResult.OK)path.Text=d.SelectedPath;}};
  Nav("install","Установка",0xf423,182);Nav("updates","Обновления",0xf130,230);Nav("restore","Вернуть оригинал",0xf117,278);
  LabelAt(this,"ПОМОЩЬ И ИНФОРМАЦИЯ",38,346,171,20,7,FontStyle.Bold,muted);
  Nav("help","Инструкция",0xf194,377);Nav("log","Журнал",0xf444,425);Nav("about","О проекте",0xf431,473);
  LabelAt(this,"Текст и субтитры.\nОригинальные голоса.",39,574,170,48,9,FontStyle.Regular,muted);
  LabelAt(this,"Связаться с автором",650,42,195,23,10,FontStyle.Bold,Color.Black);
  minimize=ButtonAt(this,"",873,29,28,28,false,0xf2ea);minimize.AccessibleName="Свернуть";minimize.Click+=(s,e)=>WindowState=FormWindowState.Minimized;
  closeButton=ButtonAt(this,"",908,29,28,28,false,0xf659);closeButton.AccessibleName="Закрыть";closeButton.Click+=(s,e)=>Close();
  ContactAt("Telegram",650,77,0xf5b3,Color.FromArgb(18,145,194),TelegramUrl);ContactAt("Steam",744,77,0xf6c1,Color.FromArgb(27,55,79),SteamUrl);ContactAt("Почта",838,77,0xf32c,Color.FromArgb(198,139,17),MailUrl);
  var status=new Card{Bounds=new Rectangle(650,186,270,133)};Controls.Add(status);
  phase=LabelAt(status,"",19,22,4,33,9,FontStyle.Regular,green);phase.BackColor=accent;
  statusTitle=LabelAt(status,"Начните с подготовки",33,20,219,30,11,FontStyle.Bold,Color.Black);
  statusText=LabelAt(status,"Выберите язык в Steam, затем проверьте файлы игры.",20,59,231,58,9,FontStyle.Regular,muted);
  progress=new ProgressBar{Bounds=new Rectangle(668,309,234,3),Style=ProgressBarStyle.Marquee,Visible=false};Controls.Add(progress);
  var tip=new Card{Bounds=new Rectangle(650,341,270,170),BackColor=Color.FromArgb(247,244,232)};Controls.Add(tip);
  LabelAt(tip,"ПЕРЕД СТАРТОМ",20,18,233,25,9,FontStyle.Bold,Color.Black);
  LabelAt(tip,"Японский язык — основа пакета.\n\nМеняются текст и субтитры.\nГолоса остаются оригинальными.\n\nЗапускайте игру через Steam.",20,52,234,104,9,FontStyle.Regular,muted);
  help=ButtonAt(this,"Руководство в Steam",650,533,270,38,false,0xf194);help.Click+=(s,e)=>OpenContact(GuideUrl);
  packageVersion=LabelAt(this,"",653,590,272,22,9,FontStyle.Regular,muted);
  LabelAt(this,"Лаунчер 0.23",653,612,266,22,9,FontStyle.Bold,Color.Black);
  LabelAt(this,"Разработано Karsvein",653,635,266,22,9,FontStyle.Regular,muted);
  BuildInstall();BuildUpdates();BuildRestore();BuildHelp();BuildLog();BuildAbout();ShowPage("install");RefreshVersions();RefreshControls();
  path.TabIndex=0;browse.TabIndex=1;ready.TabIndex=2;prepare.TabIndex=3;install.TabIndex=4;
  Shown+=(s,e)=>{path.SelectionStart=path.Text.Length;path.SelectionLength=0;nav["install"].Focus();};contactTips.SetToolTip(path,path.Text);
 }
 Panel Page(string id,string title){var p=new Panel{Bounds=new Rectangle(220,199,400,439),BackColor=Color.Transparent};Controls.Add(p);pages[id]=p;LabelAt(p,title,0,0,400,40,19,FontStyle.Bold,Color.Black);return p;}
 void Nav(string id,string title,int glyph,int y){var b=new UiButton{Navigation=true,Glyph=glyph,Text=title,Bounds=new Rectangle(23,y,178,42),Font=new Font("Segoe UI",9.5f,FontStyle.Bold),AccessibleName=title};Controls.Add(b);nav[id]=b;b.Click+=(s,e)=>ShowPage(id);}
 void ShowPage(string id){currentPage=id;foreach(var p in pages)p.Value.Visible=p.Key==id;foreach(var n in nav){n.Value.Selected=n.Key==id;n.Value.Invalidate();}if(id=="log"&&logBox!=null)logBox.Text=lastLog;RefreshControls();}
 void BuildInstall(){var p=Page("install","Установка перевода");
  var one=new Card{Bounds=new Rectangle(0,51,400,184)};p.Controls.Add(one);
  LabelAt(one,"01   Подготовьте игру",20,18,361,25,12,FontStyle.Bold,Color.Black);
  LabelAt(one,"В Steam выберите японский язык. Дождитесь\nзагрузки файлов и полностью закройте игру.",20,55,361,47,9.5f,FontStyle.Regular,muted);
  ready=new CheckBox{Bounds=new Rectangle(20,103,360,24),Text="Язык выбран, игра закрыта",BackColor=Color.White,AccessibleName="Подтверждение подготовки"};one.Controls.Add(ready);ready.CheckedChanged+=(s,e)=>RefreshControls();
  prepare=ButtonAt(one,"Проверить файлы",20,138,359,33,true);prepare.Click+=(s,e)=>Run("prepare");
  var two=new Card{Bounds=new Rectangle(0,249,400,171),BackColor=accent};p.Controls.Add(two);
  LabelAt(two,"02   Установите русский текст",20,19,360,26,12,FontStyle.Bold,Color.Black);
  LabelAt(two,"Исходные файлы сохранятся в резервной копии.\nСохранения игры останутся на месте.",20,60,360,46,9.5f,FontStyle.Regular,Color.FromArgb(68,54,15));
  install=ButtonAt(two,"Установить перевод",20,121,359,36,false);install.Click+=(s,e)=>{if(prepared&&ready.Checked&&!busy)Run("install");};
 }
 void BuildUpdates(){var p=Page("updates","Обновления текста");var c=new Card{Bounds=new Rectangle(0,51,400,369)};p.Controls.Add(c);
  LabelAt(c,"Новый перевод, тот же лаунчер",20,20,360,27,12,FontStyle.Bold,Color.Black);
  updateMessage=LabelAt(c,"Нажмите «Проверить обновления».\nЗагрузка начинается только по вашей команде.",20,59,360,86,9.5f,FontStyle.Regular,muted);
  checkUpdate=ButtonAt(c,"Проверить обновления",20,149,359,35,true,0xf130);checkUpdate.Click+=(s,e)=>CheckUpdates();
  installUpdate=new CheckBox{Bounds=new Rectangle(20,198,360,48),Text="После загрузки установить также в игру\n(игра должна быть закрыта)",BackColor=Color.White};c.Controls.Add(installUpdate);
  applyUpdate=ButtonAt(c,"Обновить перевод",20,256,359,35,false);applyUpdate.Click+=(s,e)=>ApplyUpdate();
  recoverUpdate=ButtonAt(c,"Восстановить обновление",20,306,359,35,false);recoverUpdate.Click+=(s,e)=>RecoverUpdate();
 }
 void BuildRestore(){var p=Page("restore","Вернуть оригинал");var c=new Card{Bounds=new Rectangle(0,51,400,304),BackColor=Color.Black};p.Controls.Add(c);
  LabelAt(c,"Игра без русификатора",20,23,360,32,14,FontStyle.Bold,Color.White);
  LabelAt(c,"Будут восстановлены исходные файлы игры\nи удалены файлы русского текста.\n\nСохранения и резервная копия сохранятся.\nПосле восстановления язык можно сменить\nв свойствах игры в Steam.",20,80,360,139,10,FontStyle.Regular,Color.FromArgb(215,215,211));
  restore=ButtonAt(c,"Восстановить оригинал",20,247,359,36,true);restore.Click+=(s,e)=>{if(MessageBox.Show(this,"Закройте игру перед восстановлением.\nВернуть исходные файлы и удалить перевод?","Вернуть оригинал",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes)Run("restore");};
 }
 void BuildHelp(){var p=Page("help","Как установить");var c=new Card{Bounds=new Rectangle(0,51,400,369)};p.Controls.Add(c);
  LabelAt(c,"1.  Откройте библиотеку Steam.\n2.  F1 25 → Свойства → Общие → Язык.\n3.  Выберите японский и дождитесь загрузки.\n4.  Закройте игру.\n5.  На вкладке «Установка» проверьте папку,\n     отметьте готовность и проверьте файлы.\n6.  Нажмите «Установить перевод».\n7.  Запускайте игру через Steam.\n\nДля свежих исправлений откройте «Обновления».\nДля удаления — «Вернуть оригинал».\n\nЯпонский нужен для шрифта с кириллицей.\nАнглийский вариант пока не проверен в игре.",20,24,361,319,10,FontStyle.Regular,Color.FromArgb(64,64,64));
 }
 void BuildLog(){var p=Page("log","Журнал операций");logBox=new TextBox{Bounds=new Rectangle(0,51,400,317),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,BackColor=Color.White,Font=new Font("Consolas",9),Text=lastLog};p.Controls.Add(logBox);details=ButtonAt(p,"Копировать журнал",0,383,400,37,false);details.Click+=(s,e)=>{try{Clipboard.SetText(lastLog);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Буфер обмена");}};}
 void BuildAbout(){var p=Page("about","О проекте");var c=new Card{Bounds=new Rectangle(0,51,400,369)};p.Controls.Add(c);
  LabelAt(c,"F1 25 — русский текст и субтитры\nАвтор и разработчик: Karsvein\n\nПеревод продолжает улучшаться.\nРедакторские замечания сохранены в\nEDITORIAL_NOTES.json.\n\nЛаунчер обновляет только текстовый пакет.\nГолоса, EXE игры и античит не меняются.\n\nИсходный код и история изменений — в GitHub.",20,22,360,275,10,FontStyle.Regular,Color.FromArgb(64,64,64));
  var git=ButtonAt(c,"Открыть GitHub",20,306,359,35,false);git.Click+=(s,e)=>OpenContact(TextUpdate.Repository);
 }
 void ContactAt(string title,int x,int y,int glyph,Color brand,string url){var b=new UiButton{Contact=true,Glyph=glyph,Brand=brand,Bounds=new Rectangle(x,y,76,70),AccessibleName=title,AccessibleDescription=url};Controls.Add(b);contactTips.SetToolTip(b,url);b.Click+=(s,e)=>OpenContact(url);var caption=LabelAt(this,title,x-2,y+69,77,23,8.5f,FontStyle.Regular,muted);caption.TextAlign=ContentAlignment.TopCenter;}
 bool IsContact(string s){return s==TelegramUrl||s==SteamUrl||s==MailUrl||s==GuideUrl||s==TextUpdate.Repository;}
 void OpenContact(string s){if(!IsContact(s))return;try{Process.Start(new ProcessStartInfo(s){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,"Адрес: "+s+"\n\n"+ex.Message,"Открыть ссылку");}}
 Label LabelAt(Control parent,string text,int x,int y,int w,int h,float size,FontStyle style,Color color){var l=new Label{Text=text,Bounds=new Rectangle(x,y,w,h),Font=new Font(size>=19?"Bahnschrift":"Segoe UI",size,style),ForeColor=color,BackColor=Color.Transparent,UseMnemonic=false};parent.Controls.Add(l);return l;}
 Button ButtonAt(Control parent,string text,int x,int y,int w,int h,bool primary,int glyph=0){var b=new UiButton{Primary=primary,Glyph=glyph,Text=text,Bounds=new Rectangle(x,y,w,h),Font=new Font("Segoe UI",9.5f,FontStyle.Bold),AccessibleName=text};parent.Controls.Add(b);return b;}
 void RefreshVersions(){if(packageVersion!=null)packageVersion.Text="Версия перевода: "+Updates.LocalVersion(home);}
 void RefreshControls(){if(prepare==null||install==null)return;bool pending=File.Exists(Path.Combine(home,"update-pending.json"));prepare.Enabled=!busy&&!pending;install.Enabled=!busy&&!pending&&prepared&&ready.Checked;restore.Enabled=!busy&&!pending;browse.Enabled=!busy;path.Enabled=!busy;ready.Enabled=!busy;help.Enabled=!busy;details.Enabled=!busy;checkUpdate.Enabled=!busy;applyUpdate.Enabled=!busy&&candidate!=null&&candidate.Available;recoverUpdate.Enabled=!busy&&File.Exists(Path.Combine(home,"update-pending.json"));installUpdate.Enabled=!busy;progress.Visible=busy;closeButton.Enabled=!busy;AcceptButton=currentPage=="install"?(install.Enabled?install:prepare):currentPage=="updates"?checkUpdate:null;}
 void SetStatus(string title,string text,Color color){if(statusTitle==null)return;statusTitle.Text=title;statusText.Text=text.Length>120?text.Substring(0,117)+"…":text;phase.BackColor=color;}
 void ShowLog(){ShowPage("log");}
 void StartJob(string title,Func<string> job,Action done){if(busy)return;busy=true;SetStatus(title,"Дождитесь завершения операции.",accent);RefreshControls();ThreadPool.QueueUserWorkItem(delegate{string result;bool ok=true;try{result=job();}catch(Exception ex){ok=false;result=ex.Message;}if(!IsDisposed)BeginInvoke((Action)delegate{busy=false;lastLog=result;logBox.Text=result;SetStatus(ok?"Готово":"Операция не завершена",result,ok?green:Color.FromArgb(171,63,28));RefreshVersions();if(currentPage=="updates")updateMessage.Text=result;if(ok&&done!=null)done();RefreshControls();});});}
 void CheckUpdates(){candidate=null;RefreshControls();StartJob("Проверяем GitHub…",delegate{candidate=Updates.Check(home,Updates.Download);return candidate.Available?"Доступен перевод "+candidate.Version+". "+candidate.Notes:"У вас актуальный перевод "+candidate.Version+".";},delegate{updateMessage.Text=lastLog;installUpdate.Checked=File.Exists(Path.Combine(path.Text.Trim(),"localisation","2025_russian","language_jap.lng"));});}
 static string EngineInstall(string h,string game){var si=new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(h,"Engine.ps1")+"\" -Action install -GamePath \""+game.TrimEnd('\\')+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};using(var p=Process.Start(si)){var err=p.StandardError.ReadToEndAsync();var result=p.StandardOutput.ReadToEnd();p.WaitForExit();result+=err.Result;if(p.ExitCode!=0)throw new IOException(result);return result;}}
 void ApplyUpdate(){if(candidate==null||!candidate.Available||busy)return;bool apply=installUpdate.Checked;string game=path.Text.Trim();if(game.IndexOf('"')>=0||game.Length==0){SetStatus("Проверьте папку","Укажите полный путь к F1 25.",accent);return;}if(MessageBox.Show(this,"Скачать перевод "+candidate.Version+" из GitHub?\n"+(apply?"Пакет будет также установлен в выбранную папку игры. Закройте игру.":"Обновится пакет в лаунчере. Игра останется без изменений."),"Обновить перевод",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
  StartJob("Обновляем перевод…",delegate{return Updates.Apply(home,candidate,Updates.Download,game,apply,EngineInstall);},delegate{candidate=null;prepared=false;updateMessage.Text="Перевод обновлён до "+Updates.LocalVersion(home)+".";});
 }
 void RecoverUpdate(){if(busy)return;if(MessageBox.Show(this,"Восстановить предыдущий пакет после прерванного обновления?\nЕсли обновлялась игра, предыдущий текст будет установлен обратно. Закройте игру.","Восстановление обновления",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;StartJob("Восстанавливаем пакет…",delegate{return Updates.Recover(home,EngineInstall);},delegate{prepared=false;candidate=null;updateMessage.Text="Предыдущий пакет восстановлен.";});}
 protected override void OnPaintBackground(PaintEventArgs e){
  var g=e.Graphics;g.Clear(Color.Black);var saved=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
  // The window's entire exterior is black; only the inset is white. No inset
  // stroke can expose a white sliver between the stroke and window region.
  using(var inner=Shape.Round(new Rectangle(5,5,Width-10,Height-10),31)){
   using(var b=new SolidBrush(Color.FromArgb(249,250,247)))g.FillPath(b,inner);g.SetClip(inner);
   if(backdrop!=null){g.InterpolationMode=InterpolationMode.HighQualityBicubic;float scale=Math.Max((float)Width/backdrop.Width,(float)Height/backdrop.Height);int w=(int)(backdrop.Width*scale),h=(int)(backdrop.Height*scale);using(var attrs=new System.Drawing.Imaging.ImageAttributes()){var m=new System.Drawing.Imaging.ColorMatrix();m.Matrix33=0.12f;attrs.SetColorMatrix(m);g.DrawImage(backdrop,new Rectangle((Width-w)/2,(Height-h)/2,w,h),0,0,backdrop.Width,backdrop.Height,GraphicsUnit.Pixel,attrs);}}
  }g.Restore(saved);
 }
 protected override void OnFormClosing(FormClosingEventArgs e){if(busy){e.Cancel=true;return;}base.OnFormClosing(e);}
 protected override void Dispose(bool disposing){if(disposing){contactTips.Dispose();if(backdrop!=null)backdrop.Dispose();}base.Dispose(disposing);}
  void ApplyResult(string action,int code,string output){
  busy=false;lastLog=output+"\r\nКод завершения: "+code;
  if(code==0){
   if(action=="prepare"){prepared=true;SetStatus("Файлы совместимы",ready.Checked?"Можно устанавливать перевод.":"Подтвердите подготовку галочкой.",green);}
   else if(action=="install"){prepared=true;SetStatus("Перевод установлен","Готово. Запускайте игру через Steam.",green);}
   else{prepared=false;SetStatus("Оригинал восстановлен","Перевод удалён. Язык можно сменить в Steam.",green);}
  }else{prepared=false;string reason=output.Trim();int at=reason.LastIndexOf("ОШИБКА:");if(at>=0)reason=reason.Substring(at+7).Trim();if(reason.Length>150)reason=reason.Substring(0,147)+"…";SetStatus("Операция не завершена",reason.Length==0?"Подробности — в журнале операции.":reason,Color.FromArgb(172,73,12));}
  RefreshControls();
 }
 void Run(string action){
  string game=path.Text.Trim();if(game.Length==0||game.IndexOf('"')>=0){SetStatus("Проверьте папку игры","Укажите полный путь к установленной F1 25.",accent);return;}
  if(!File.Exists(Path.Combine(home,"Engine.ps1"))||!File.Exists(Path.Combine(home,"manifest.json"))){SetStatus("Распакуйте весь архив","Рядом с Launcher.exe должны находиться Engine.ps1, manifest.json и папка payload.",accent);return;}
  busy=true;RefreshControls();SetStatus(action=="prepare"?"Проверяем файлы…":action=="install"?"Устанавливаем перевод…":"Восстанавливаем оригинал…","Дождитесь завершения. Идёт проверка файлов.",accent);
  ThreadPool.QueueUserWorkItem(delegate{
   int code=-1;string output;
   try{var si=new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(home,"Engine.ps1")+"\" -Action "+action+" -GamePath \""+game.TrimEnd('\\')+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};using(var p=Process.Start(si)){var err=p.StandardError.ReadToEndAsync();output=p.StandardOutput.ReadToEnd();p.WaitForExit();output+=err.Result;code=p.ExitCode;}}
   catch(Exception ex){output=ex.Message;}
   if(!IsDisposed)BeginInvoke((Action)delegate{ApplyResult(action,code,output);});
  });
 }

 int checks=0;void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
 void SelfTest(){
  Check(IsContact(TelegramUrl)&&IsContact(SteamUrl)&&IsContact(MailUrl)&&IsContact(GuideUrl)&&IsContact(TextUpdate.Repository)&&!IsContact("https://example.com"),"contact destinations");
  Check(ClientSize==new Size(960,680),"window size");foreach(Control c in Controls)Check(c.Left>=5&&c.Top>=5&&c.Right<=Width-5&&c.Bottom<=Height-5,"inside frame: "+c.Text);
  foreach(string page in new[]{"install","updates","restore","help","log","about"}){ShowPage(page);Check(nav[page].Selected&&currentPage==page,"navigation "+page);}ShowPage("install");
  Check(!install.Enabled,"initial gate");ready.Checked=true;Check(!install.Enabled,"checkbox alone");ApplyResult("prepare",0,"PASS");Check(install.Enabled,"prepared");path.Text+="_test";Check(!prepared&&!install.Enabled,"path invalidation");busy=true;RefreshControls();Check(!checkUpdate.Enabled&&!applyUpdate.Enabled&&!prepare.Enabled&&!restore.Enabled&&!path.Enabled,"busy");var closing=new FormClosingEventArgs(CloseReason.UserClosing,false);OnFormClosing(closing);Check(closing.Cancel,"busy close");
  ApplyResult("install",1,"ОШИБКА: Тест");Check(!prepared&&!install.Enabled,"failed install");ApplyResult("prepare",0,"PASS");ApplyResult("install",0,"PASS");Check(statusTitle.Text=="Перевод установлен","installed");ApplyResult("restore",0,"PASS");Check(!install.Enabled&&statusTitle.Text=="Оригинал восстановлен","restored");
  using(var bmp=new Bitmap(Width,Height)){DrawToBitmap(bmp,ClientRectangle);foreach(Point pt in new[]{new Point(60,0),new Point(60,1),new Point(0,60),new Point(1,60),new Point(Width-1,60),new Point(60,Height-1)})Check(bmp.GetPixel(pt.X,pt.Y).R<8,"black outer edge");}
 }
 [STAThread]static int Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);bool first;using(var mutex=new Mutex(true,"Local\\Karsvein-F1RU-021",out first)){
  if(!first&&args.Length==0){MessageBox.Show("Лаунчер уже открыт.","F1 25");return 1;}
  using(var f=new Launcher()){
   if(args.Length==2&&args[0]=="--self-test"){try{f.SelfTest();File.WriteAllText(args[1],"UI_TEST_PASS checks="+f.checks+" real_game_writes=false",Encoding.UTF8);return 0;}catch(Exception ex){File.WriteAllText(args[1],ex.ToString());return 1;}}
   if(args.Length>=2&&args[0]=="--preview"){f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-30000,-30000);f.Show();Application.DoEvents();if(args.Length==3){if(f.pages.ContainsKey(args[2]))f.ShowPage(args[2]);else{f.ready.Checked=true;f.ApplyResult("prepare",0,"PREPARE PASS");if(args[2]=="installed")f.ApplyResult("install",0,"INSTALL PASS");if(args[2]=="restored")f.ApplyResult("restore",0,"RESTORE PASS");if(args[2]=="error")f.ApplyResult("install",1,"ОШИБКА: Сначала закройте F1 25.");}}using(var b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,f.ClientRectangle);b.Save(args[1]);}return 0;}
   Application.Run(f);return 0;
  }
 }}
}
