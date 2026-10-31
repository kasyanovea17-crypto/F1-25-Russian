using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;


static class Theme {
 public static readonly Color Background=Color.FromArgb(17,19,21),Surface=Color.FromArgb(25,28,32),Ink=Color.FromArgb(244,245,245),Muted=Color.FromArgb(157,164,170),Line=Color.FromArgb(48,53,59),Accent=Color.FromArgb(227,38,46);
}
static class Shape {
 public static GraphicsPath Round(Rectangle r,int radius){var p=new GraphicsPath();int d=Math.Max(2,Math.Min(radius*2,Math.Min(r.Width,r.Height)));p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
}
static class Glyphs {
 static readonly PrivateFontCollection fonts=new PrivateFontCollection();
 public static void Load(string home){if(fonts.Families.Length==0)fonts.AddFontFile(Path.Combine(home,"assets","bootstrap-icons.ttf"));}
 public static void Draw(Graphics g,int glyph,float size,Rectangle box,Color color){if(glyph==-1)glyph=0xf611;if(fonts.Families.Length==0)return;using(var f=new Font(fonts.Families[0],size,FontStyle.Regular,GraphicsUnit.Pixel))using(var b=new SolidBrush(color))using(var fmt=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(char.ConvertFromUtf32(glyph),f,b,box,fmt);}
}
sealed class Card : Panel {
 public int Radius=12;
 public Card(){DoubleBuffered=true;BackColor=Theme.Surface;}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(Width>24&&Height>24){var old=Region;using(var p=Shape.Round(ClientRectangle,Radius))Region=new Region(p);if(old!=null)old.Dispose();}}
 protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var p=Shape.Round(new Rectangle(0,0,Width-1,Height-1),Radius))using(var pen=new Pen(Theme.Line))e.Graphics.DrawPath(pen,p);}
}
sealed class ModalShade : Panel {
 public Bitmap Backdrop;
 public ModalShade(){DoubleBuffered=true;BackColor=Theme.Background;}
 protected override void OnPaintBackground(PaintEventArgs e){if(Backdrop!=null)e.Graphics.DrawImage(Backdrop,ClientRectangle);else base.OnPaintBackground(e);using(var b=new SolidBrush(Color.FromArgb(175,0,0,0)))e.Graphics.FillRectangle(b,ClientRectangle);}
 protected override void Dispose(bool disposing){if(disposing&&Backdrop!=null){Backdrop.Dispose();Backdrop=null;}base.Dispose(disposing);}
}
sealed class DarkMenuRenderer : ToolStripProfessionalRenderer {
 protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){e.Graphics.Clear(Theme.Surface);}
 protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){using(var b=new SolidBrush(e.Item.Selected?Color.FromArgb(45,49,55):Theme.Surface))e.Graphics.FillRectangle(b,new Rectangle(Point.Empty,e.Item.Size));}
 protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=e.Item.Enabled?Theme.Ink:Theme.Muted;base.OnRenderItemText(e);}
 protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){using(var p=new Pen(Theme.Line))e.Graphics.DrawRectangle(p,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);}
}
sealed class UiButton : Button {
 public int Glyph;public bool Selected,Navigation,Primary;public bool Contact=false;public Color Brand=Color.Black;bool hover;
 public UiButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;UseVisualStyleBackColor=false;BackColor=Theme.Surface;ForeColor=Theme.Ink;}
 protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
 protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
 protected override void OnPaint(PaintEventArgs e){
  var g=e.Graphics;var saved=g.Save();
  if(Parent!=null){
   if(Parent is Launcher||Parent.BackgroundImage!=null||Parent.BackColor.A<255){
    g.SetClip(ClientRectangle,CombineMode.Intersect);g.TranslateTransform(-Left,-Top);
    InvokePaintBackground(Parent,new PaintEventArgs(g,new Rectangle(Left,Top,Width,Height)));
   }else using(var background=new SolidBrush(Parent.BackColor))g.FillRectangle(background,ClientRectangle);
  }
  g.Restore(saved);g.SmoothingMode=SmoothingMode.AntiAlias;
  Color bg=!Enabled?Color.FromArgb(38,41,45):Primary?(hover?Color.White:Color.FromArgb(242,243,243)):Navigation?(hover||Selected?Color.FromArgb(42,46,51):Color.Transparent):(hover||Selected?Color.FromArgb(47,52,58):Color.FromArgb(34,38,43));
  var bounds=new Rectangle(0,0,Math.Max(1,Width-1),Math.Max(1,Height-1));
  using(var p=Shape.Round(bounds,6)){using(var b=new SolidBrush(bg))g.FillPath(b,p);if(!Navigation&&!Primary)using(var pen=new Pen(Theme.Line))g.DrawPath(pen,p);if(Primary&&Enabled){var savedClip=g.Save();g.SetClip(p);using(var edge=new SolidBrush(Theme.Accent))g.FillRectangle(edge,Width-4,0,4,Height);g.Restore(savedClip);}}
  Color fg=!Enabled?Color.FromArgb(116,122,129):Primary?Color.FromArgb(19,21,23):Theme.Ink;
  if(Glyph!=0)Glyphs.Draw(g,Glyph,Math.Max(16,Font.SizeInPoints*1.65f),Text.Length==0?ClientRectangle:new Rectangle(12,0,25,Height),fg);
  TextRenderer.DrawText(g,Text,Font,new Rectangle(Glyph!=0?43:12,0,Width-(Glyph!=0?53:24),Height),fg,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|(Navigation?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter));
  if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle(4,4,Width-9,Height-9),Primary?Color.Black:Theme.Ink,bg);
 }
}

sealed partial class Launcher : Form {
 readonly string home=AppDomain.CurrentDomain.BaseDirectory;
 readonly Color muted=Theme.Muted,accent=Theme.Accent,green=Color.FromArgb(84,206,105);
 readonly ToolTip contactTips=new ToolTip();readonly Dictionary<string,Panel> pages=new Dictionary<string,Panel>();
 const string TelegramUrl="https://t.me/Arete_Eudaimonia_Ataraxia",SteamUrl="https://steamcommunity.com/profiles/76561198993070335/",MailUrl="mailto:sobesednik617@gmail.com",GuideUrl="https://steamcommunity.com/sharedfiles/filedetails/?id=3800786076";
 TextBox path,logBox;CheckBox ready,installUpdate;Button prepare,install,restore,browse,help,details,closeButton,minimize,checkUpdate,applyUpdate,recoverUpdate,downloadRelease;
 Label statusTitle,statusText,phase,packageVersion,updateMessage;ProgressBar progress;bool busy,prepared;Image backdrop;TextUpdate candidate;GameTextProfile detectedProfile;
 ComboBox translation;Label translationHint;string translationPreferencePath;
 ComboBox voice;Label voiceStatus,voiceSummary;Button voiceSteam,voiceRefresh;bool testing;string voicePreferencePath;
 string lastLog="Операции ещё не выполнялись. Игра автоматически не запускается.";string currentPage="install";
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ReleaseCapture();
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);

 ModalShade shade;Panel header,footer;Button settingsButton,voiceButton,moreButton,folderButton,installCancel,restoreCancel,journal;ComboBox settingsVoice;Label dialogStatus,operationTitle,successText,settingsPathError;ContextMenuStrip moreMenu;Image headerMark;bool installed,changingVoice,overlayOpen,previewPending,loadingFolder,folderAmbiguous;string folderPreferencePath;Control returnFocus;
 Launcher(bool testMode){
  testing=testMode;string prefs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Karsvein","F1RU");translationPreferencePath=Path.Combine(prefs,"text-variant.txt");voicePreferencePath=Path.Combine(prefs,"voice-language.txt");folderPreferencePath=Path.Combine(prefs,"game-folder.txt");
  Glyphs.Load(home);Text="F1 25 · На русском · 0.32 · новый интерфейс";AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(960,640);FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterScreen;BackColor=Theme.Background;ForeColor=Theme.Ink;Font=new Font("Segoe UI",10.5f);DoubleBuffered=true;KeyPreview=true;
  string hero=Path.Combine(home,"assets","redesign","hero-branded.png"),mark=Path.Combine(home,"assets","redesign","header-source.jpg");if(File.Exists(hero))using(var image=Image.FromFile(hero))backdrop=new Bitmap(image);if(File.Exists(mark))using(var image=Image.FromFile(mark))headerMark=new Bitmap(image);
  header=new Panel{Bounds=new Rectangle(0,0,960,48),BackColor=Theme.Background};Controls.Add(header);header.Paint+=(a,e)=>{var saved=e.Graphics.Save();e.Graphics.ScaleTransform(header.Width/960f,header.Height/48f);if(headerMark!=null)e.Graphics.DrawImage(headerMark,new Rectangle(34,16,118,18),54,26,188,29,GraphicsUnit.Pixel);using(var pen=new Pen(accent))e.Graphics.DrawLine(pen,171,16,171,34);e.Graphics.Restore(saved);};header.MouseDown+=DragWindow;
  voiceButton=ButtonAt(header,"Озвучка",520,7,124,34,false,0xf611);((UiButton)voiceButton).Navigation=true;voiceButton.Click+=(a,e)=>ShowPage("voice");
  settingsButton=ButtonAt(header,"Параметры",650,7,140,34,false,0xf3e5);((UiButton)settingsButton).Navigation=true;settingsButton.Click+=(a,e)=>ShowPage("settings");
  moreButton=ButtonAt(header,"Ещё",797,7,70,34,false);((UiButton)moreButton).Navigation=true;moreButton.Click+=(a,e)=>moreMenu.Show(moreButton,new Point(0,moreButton.Height));
  minimize=ButtonAt(header,"",875,8,34,32,false,0xf2ea);((UiButton)minimize).Navigation=true;minimize.AccessibleName="Свернуть";minimize.Click+=(a,e)=>WindowState=FormWindowState.Minimized;
  closeButton=ButtonAt(header,"",917,8,34,32,false,0xf659);((UiButton)closeButton).Navigation=true;closeButton.AccessibleName="Закрыть";closeButton.Click+=(a,e)=>Close();
  moreMenu=new ContextMenuStrip{BackColor=Theme.Surface,ForeColor=Theme.Ink,Renderer=new DarkMenuRenderer(),ShowImageMargin=false,Font=Font,Padding=new Padding(6)};
  foreach(var entry in new[]{new[]{"Обновления","updates"},new[]{"Вернуть оригинал","restore"},new[]{"Инструкция","help"},new[]{"Журнал операций","log"},new[]{"О проекте и контакты","about"}}){string target=entry[1];var item=new ToolStripMenuItem(entry[0]){Padding=new Padding(12,7,12,7)};item.Click+=(a,e)=>ShowPage(target);moreMenu.Items.Add(item);}
  help=ButtonAt(this,"Инструкция",32,494,144,38,false,0xf444);((UiButton)help).Navigation=true;help.Click+=(a,e)=>ShowPage("help");
  journal=ButtonAt(this,"Журнал",190,494,120,38,false,0xf194);((UiButton)journal).Navigation=true;journal.Click+=(a,e)=>ShowPage("log");
  footer=new Panel{Bounds=new Rectangle(0,546,960,94),BackColor=Theme.Background};Controls.Add(footer);footer.Paint+=(a,e)=>{using(var pen=new Pen(Theme.Line))e.Graphics.DrawLine(pen,0,0,footer.Width,0);};
  phase=LabelAt(footer,"",34,31,10,10,10.5f,FontStyle.Regular,green);phase.BackColor=accent;using(var circle=Shape.Round(new Rectangle(0,0,10,10),5))phase.Region=new Region(circle);
  statusTitle=LabelAt(footer,"Выберите папку игры",60,25,355,25,12,FontStyle.Bold,Theme.Ink);statusTitle.AutoEllipsis=true;
  statusText=LabelAt(footer,"Русский текст · Steam / EA app",60,53,355,24,10.5f,FontStyle.Regular,muted);statusText.AutoEllipsis=true;
  folderButton=ButtonAt(footer,"Папка игры",450,25,190,39,false,0xf3d8);((UiButton)folderButton).Navigation=true;folderButton.Click+=(a,e)=>ShowPage("settings");
  prepare=ButtonAt(footer,"Проверить игру",676,19,260,49,true);prepare.Font=new Font("Segoe UI",13,FontStyle.Bold);prepare.Click+=(a,e)=>PrimaryAction();
  shade=new ModalShade{Bounds=new Rectangle(0,0,960,640),Visible=false,TabStop=false};Controls.Add(shade);
  pages["install"]=new Panel();BuildInstall();LoadGameFolder();BuildVoice();BuildUpdates();BuildRestore();BuildHelp();BuildLog();BuildAbout();BuildResultPages();
  RefreshTranslation();RefreshVoice();RefreshVersions();ShowPage("install");RefreshControls();
  Activated+=(a,e)=>{if(!busy)RefreshVoice();};
  Shown+=(a,e)=>{if(path.Text.Length==0||folderAmbiguous)ShowPage("settings");else prepare.Focus();};
 }
 void LoadGameFolder(){
  loadingFolder=true;
  try{
   if(testing){path.Text=@"C:\F1RU-UI-Test";return;}
   string saved="";
   try{if(File.Exists(folderPreferencePath)&&new FileInfo(folderPreferencePath).Length<=32768)saved=File.ReadAllText(folderPreferencePath).Trim();}catch(IOException){}catch(UnauthorizedAccessException){}
   var found=GameLocation.Find(saved,LaunchContext.Directory(home));path.Text=found.Path;
   folderAmbiguous=found.Ambiguous&&found.Source!=GameLocation.SavedSource;
   lastLog="Автоматический поиск папки F1 25\r\n"+found.Diagnostics;if(logBox!=null)logBox.Text=lastLog;
   if(path.Text.Length==0)SetStatus("Выберите папку игры","Автопоиск не нашёл установленную F1 25",muted);
   else if(folderAmbiguous)SetStatus("Найдено несколько установок","Выберите нужную папку в параметрах",accent);
   else{
    SetStatus(found.Source==GameLocation.SavedSource?"Папка игры выбрана":"Игра найдена",found.Source+" · проверьте совместимость",muted);
    if(found.Source!=GameLocation.SavedSource)try{SaveFolder();}catch(IOException ex){lastLog+="\r\nПуть не сохранён: "+ex.Message;}catch(UnauthorizedAccessException ex){lastLog+="\r\nПуть не сохранён: "+ex.Message;}
   }
  }catch(Exception ex){path.Text="";lastLog="Автопоиск папки: "+ex.Message;SetStatus("Выберите папку игры","Укажите папку установленной F1 25",muted);}
  finally{loadingFolder=false;if(logBox!=null)logBox.Text=lastLog;}
 }
 void DragWindow(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left&&!busy){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}}
 Panel Page(string id,string title,int height=430,int width=560){var p=new Card{Bounds=new Rectangle((960-width)/2,(640-height)/2,width,height),Name=id,Visible=false};shade.Controls.Add(p);pages[id]=p;LabelAt(p,title,24,21,width-80,32,17,FontStyle.Bold,Theme.Ink);var close=ButtonAt(p,"",width-50,18,30,30,false,0xf659);((UiButton)close).Navigation=true;close.AccessibleName="Закрыть: "+title;close.Click+=(a,e)=>ShowPage("install");return p;}
 void CaptureShade(){if(overlayOpen)return;if(shade.Backdrop!=null){shade.Backdrop.Dispose();shade.Backdrop=null;}var snapshot=new Bitmap(ClientSize.Width,ClientSize.Height);DrawToBitmap(snapshot,ClientRectangle);shade.Backdrop=snapshot;}
 void ShowPage(string id){
  if(!pages.ContainsKey(id)||busy&&id!="progress")return;
  bool main=id=="install";if(!main&&!overlayOpen){var focused=FocusedChild(this);if(focused!=null)returnFocus=focused;CaptureShade();}
  currentPage=id;foreach(var item in pages)if(item.Key!="install")item.Value.Visible=item.Key==id;overlayOpen=!main;shade.Visible=!main;RefreshControls();
  if(!main){
   shade.BringToFront();pages[id].BringToFront();if(id=="log")logBox.Text=lastLog;
   Control focus=id=="settings"?(Control)path:id=="confirm"?(Control)installCancel:id=="restore"?restoreCancel:id=="updates"?(downloadRelease.Visible?(Control)downloadRelease:checkUpdate):null;
   if(focus!=null&&focus.CanSelect)focus.Select();else foreach(Control child in pages[id].Controls)if(child.Enabled&&child.CanSelect){child.Select();break;}
  }else if(returnFocus!=null&&returnFocus.CanFocus)returnFocus.Focus();else prepare.Focus();
 }
 static Control FocusedChild(Control root){if(root.Focused)return root;foreach(Control child in root.Controls)if(child.ContainsFocus||child.Focused){var found=FocusedChild(child);if(found!=null)return found;}return null;}
 void PrimaryAction(){if(busy)return;returnFocus=prepare;if(path.Text.Trim().Length==0){ShowPage("settings");return;}if(PendingUpdate){ShowPage("updates");return;}if(installed){ShowPage("updates");return;}if(!prepared){if(!testing)Run("prepare");return;}ShowPage("confirm");}
 ComboBox ComboAt(Control parent,int x,int y,int width,string name){var c=new ComboBox{Bounds=new Rectangle(x,y,width,32),DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(34,38,43),ForeColor=Theme.Ink,Font=Font,AccessibleName=name,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=25};parent.Controls.Add(c);c.DrawItem+=(a,e)=>{if(e.Index<0)return;using(var b=new SolidBrush((e.State&DrawItemState.Selected)!=0?Color.FromArgb(51,56,63):c.BackColor))e.Graphics.FillRectangle(b,e.Bounds);TextRenderer.DrawText(e.Graphics,c.Items[e.Index].ToString(),c.Font,new Rectangle(e.Bounds.X+8,e.Bounds.Y,e.Bounds.Width-14,e.Bounds.Height),Theme.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};return c;}
 void BrowseFolder(){if(busy||testing)return;using(var d=new FolderBrowserDialog()){d.Description="Выберите папку установленной F1 25 (с game.dat и F1_25.exe)";if(Directory.Exists(path.Text))d.SelectedPath=path.Text;if(d.ShowDialog(this)==DialogResult.OK)path.Text=d.SelectedPath;}}
 void BuildInstall(){var p=Page("settings","Параметры",500);
  LabelAt(p,"Папка F1 25",24,75,500,24,10.5f,FontStyle.Regular,muted);path=new TextBox{Bounds=new Rectangle(24,104,460,28),BorderStyle=BorderStyle.FixedSingle,Font=Font,BackColor=Color.FromArgb(34,38,43),ForeColor=Theme.Ink,AccessibleName="Папка игры F1 25"};p.Controls.Add(path);
  path.TextChanged+=(a,e)=>{installed=false;InvalidatePreparation();ready.Checked=false;if(settingsPathError!=null){settingsPathError.Text="";settingsPathError.Visible=false;}RefreshTranslation();RefreshControls();RefreshVoice();if(!loadingFolder)SetStatus(path.Text.Length==0?"Выберите папку игры":"Нужна проверка файлов",path.Text.Length==0?"Укажите папку установленной F1 25":"Путь изменён · проверьте совместимость",muted);contactTips.SetToolTip(path,path.Text);if(folderButton!=null)contactTips.SetToolTip(folderButton,path.Text);};
  browse=ButtonAt(p,"",492,100,44,36,false,0xf3d8);browse.AccessibleName="Выбрать папку игры";browse.Click+=(a,e)=>BrowseFolder();
  settingsPathError=LabelAt(p,"",24,137,512,22,10.5f,FontStyle.Regular,accent);settingsPathError.AutoEllipsis=true;settingsPathError.Visible=false;settingsPathError.AccessibleName="Ошибка выбора папки игры";
  LabelAt(p,"Вариант перевода",24,161,500,24,10.5f,FontStyle.Regular,muted);translation=ComboAt(p,24,191,512,"Вариант перевода");translation.Items.AddRange(new object[]{"Русский перевод","Русский + имена на английском"});translation.SelectedIndex=!testing&&TranslationVariants.Load(translationPreferencePath)==TranslationVariants.OriginalNames?1:0;
  translationHint=LabelAt(p,"",24,235,512,47,10.5f,FontStyle.Regular,muted);translation.SelectedIndexChanged+=(a,e)=>{installed=false;InvalidatePreparation();ready.Checked=false;try{if(!testing)TranslationVariants.Save(translationPreferencePath,SelectedTranslation);SetStatus("Вариант перевода выбран","Повторите проверку выбранной папки",accent);}catch(Exception ex){SetStatus("Выбор не сохранён",ex.Message,accent);}RefreshTranslation();RefreshControls();};
  LabelAt(p,"Язык озвучки в игре",24,302,500,24,10.5f,FontStyle.Regular,muted);settingsVoice=ComboAt(p,24,332,512,"Язык озвучки в параметрах");settingsVoice.Items.AddRange(new object[]{"Английская / English","Японская / Japanese"});settingsVoice.SelectedIndex=0;settingsVoice.SelectedIndexChanged+=(a,e)=>{if(!changingVoice&&voice!=null){changingVoice=true;voice.SelectedIndex=settingsVoice.SelectedIndex;changingVoice=false;}};
  LabelAt(p,"Русский текст не меняет язык голосов.\nШрифт входит в этот EXE.",24,378,512,46,10.5f,FontStyle.Regular,muted);
  var done=ButtonAt(p,"Готово",326,443,210,38,true);done.Click+=(a,e)=>CompleteSettings();
  var cancel=ButtonAt(p,"На главный экран",24,443,193,38,false);cancel.Click+=(a,e)=>ShowPage("install");
  p=Page("confirm","Подготовка к установке",350);LabelAt(p,"Закройте игру и дождитесь загрузки выбранного\nязыка в Steam или EA app.",24,77,512,52,11,FontStyle.Regular,muted);
  voiceSummary=LabelAt(p,"",24,146,512,25,11,FontStyle.Regular,Theme.Ink);ready=new CheckBox{Bounds=new Rectangle(24,190,512,42),Text="Язык выбран, игра закрыта",BackColor=Theme.Surface,ForeColor=Theme.Ink,Font=Font,AccessibleName="Подтверждение подготовки"};p.Controls.Add(ready);ready.CheckedChanged+=(a,e)=>RefreshControls();
  installCancel=ButtonAt(p,"Отмена",24,288,172,38,false);installCancel.Click+=(a,e)=>ShowPage("install");install=ButtonAt(p,"Установить перевод",220,288,316,38,true);install.Click+=(a,e)=>{if(prepared&&ready.Checked&&!busy&&!testing)Run("install");};
 }
 void CompleteSettings(){
  if(busy)return;
  try{
   string directory=GameTextProfiles.CanonicalPath(path.Text);
   if(!GameLocation.IsGameDirectory(directory))throw new IOException("Выберите папку с F1_25.exe и game.dat.");
   if(!testing)SaveFolder();settingsPathError.Visible=false;ShowPage("install");
  }catch(Exception ex){
   settingsPathError.Text=ex.Message;settingsPathError.Visible=true;contactTips.SetToolTip(settingsPathError,ex.Message);
   lastLog="Проверка папки игры: "+ex.Message;if(logBox!=null)logBox.Text=lastLog;SetStatus("Проверьте папку игры",ex.Message,accent);path.Focus();
  }
 }
 void SaveFolder(){Directory.CreateDirectory(Path.GetDirectoryName(folderPreferencePath));string temp=folderPreferencePath+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,GameTextProfiles.CanonicalPath(path.Text),Encoding.UTF8);if(File.Exists(folderPreferencePath))File.Replace(temp,folderPreferencePath,null);else File.Move(temp,folderPreferencePath);}finally{if(File.Exists(temp))File.Delete(temp);}}

 string SelectedTranslation { get { return translation!=null&&translation.SelectedIndex==1?TranslationVariants.OriginalNames:TranslationVariants.Russian; } }
 void InvalidatePreparation(){installed=false;prepared=false;candidate=null;detectedProfile=null;if(installUpdate!=null)installUpdate.Checked=false;RefreshVersions();}
 bool CanUpdateText { get { return prepared&&detectedProfile!=null&&detectedProfile.Stable&&detectedProfile.Matches(path.Text,SelectedTranslation)&&SelectedTranslation==TranslationVariants.Russian; } }
 void RefreshTranslation(){
  bool original=SelectedTranslation==TranslationVariants.OriginalNames;
  if(translationHint!=null)translationHint.Text=original?"Пилоты, команды и трассы — как в оригинале.\nОстальной текст и субтитры — на русском.":"Обычная русская версия текста.\nШрифты и выбор озвучки не меняются.";
  if(updateMessage!=null){
   if(detectedProfile==null)updateMessage.Text="Сначала проверьте совместимость игры\nна вкладке «Установка». Версия и канал\nтекста определяются для выбранной папки.";
   else if(!detectedProfile.Stable)updateMessage.Text="F1 25 "+detectedProfile.GameVersion+" · текст "+detectedProfile.TextVersion+".\nЭтот перевод обновляется только новым\nEXE лаунчера через Releases на GitHub.\nКанал текста 1.26 к этой версии не применяется.";
   else updateMessage.Text=original?"Вариант с оригинальными именами входит в\nновый EXE лаунчера. Обновляйте его через\nReleases на GitHub. Обычный канал текста\nне заменяет этот вариант.":"Подтверждена F1 25 "+detectedProfile.GameVersion+".\nНажмите «Проверить обновления».\nОбновляется обычный русский перевод.";
  }
  RefreshVersions();
 }

 string SelectedVoice { get { return voice!=null&&voice.SelectedIndex==1?"japanese":"english"; } }
 static string VoiceTitle(string language){return language=="english"?"Английская / English":language=="japanese"?"Японская / Japanese":"Не определён";}
 static string ParseSteamVoice(string text){
  // Only UserConfig is the selected launch language. MountedConfig can lag it.
  var section=Regex.Match(text,"\"UserConfig\"\\s*\\{([^{}]*)\\}",RegexOptions.IgnoreCase);
  if(!section.Success)return "";
  var lang=Regex.Match(section.Groups[1].Value,"\"language\"\\s*\"([a-z]+)\"",RegexOptions.IgnoreCase);
  return lang.Success?lang.Groups[1].Value.ToLowerInvariant():"";
 }
 static string LoadVoice(string file){try{string value=File.ReadAllText(file).Trim();return value=="english"||value=="japanese"?value:"";}catch(IOException){return "";}catch(UnauthorizedAccessException){return "";}}
 static void SaveVoice(string file,string value){
  if(value!="english"&&value!="japanese")throw new ArgumentException("Unsupported voice language");
  Directory.CreateDirectory(Path.GetDirectoryName(file));string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temp,value,Encoding.UTF8);if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
 static string DetectClient(string game){
  try{var dir=Directory.GetParent(game.Trim().TrimEnd('\\'));if(dir!=null&&dir.Parent!=null&&File.Exists(Path.Combine(dir.Parent.FullName,"appmanifest_3059520.acf")))return "Steam";
   if(Directory.Exists(Path.Combine(game,"_Installer")))return "EA app";
  }catch(ArgumentException){}catch(IOException){}catch(UnauthorizedAccessException){}
  return "Steam / EA app";
 }
 string ClientName { get { return DetectClient(path.Text.Trim()); } }
 string SteamVoice(){
  if(testing)return "";
  try{var dir=Directory.GetParent(path.Text.Trim().TrimEnd('\\'));if(dir==null||dir.Parent==null)return "";string f=Path.Combine(dir.Parent.FullName,"appmanifest_3059520.acf");if(!File.Exists(f)||new FileInfo(f).Length>1048576)return "";return ParseSteamVoice(File.ReadAllText(f));}catch(Exception){return "";}
 }

 void BuildVoice(){var p=Page("voice","Озвучка",490);LabelAt(p,"Язык голосов",24,75,512,24,10.5f,FontStyle.Regular,muted);voice=ComboAt(p,24,106,512,"Язык озвучки");voice.Items.AddRange(new object[]{"Английская / English","Японская / Japanese"});string saved=testing?"":LoadVoice(voicePreferencePath);if(saved.Length==0)saved=SteamVoice();voice.SelectedIndex=saved=="japanese"?1:0;settingsVoice.SelectedIndex=voice.SelectedIndex;
  voiceStatus=LabelAt(p,"",24,155,512,70,10.5f,FontStyle.Regular,muted);voiceSteam=ButtonAt(p,"Выбрать язык в клиенте",24,231,306,38,false);voiceSteam.Click+=(a,e)=>{if(testing)return;if(Process.GetProcessesByName("F1_25").Length>0){SetStatus("Сначала закройте игру","После выхода откройте свойства игры в клиенте.",accent);return;}if(ClientName!="Steam"){ShowPage("help");return;}try{Process.Start(new ProcessStartInfo("steam://gameproperties/3059520"){UseShellExecute=true});SetStatus("Примените язык в Steam","Общие → Язык → "+(SelectedVoice=="english"?"English":"Japanese"),accent);}catch(Exception ex){lastLog=ex.Message;ShowPage("help");}};
  voiceRefresh=ButtonAt(p,"Проверить",346,231,190,38,false);voiceRefresh.Click+=(a,e)=>RefreshVoice();
  p.Controls.Add(new Panel{Bounds=new Rectangle(24,292,512,1),BackColor=Theme.Line});speechButton=ButtonAt(p,"Голосовые команды",24,314,512,42,false,0xf490);speechButton.Click+=(a,e)=>ShowVoiceChild(false);
  var maxVoice=ButtonAt(p,"Русский голос Макса · тест",24,368,512,42,false,0xf611);maxVoice.Click+=(a,e)=>ShowVoiceChild(true);LabelAt(p,"Русские команды и тест Макса работают на базе English.\nЯпонский также поддерживается, но не обязателен.",24,429,512,45,10.5f,FontStyle.Regular,muted);
  voice.SelectedIndexChanged+=(a,e)=>{installed=false;InvalidatePreparation();ready.Checked=false;RefreshTranslation();if(!changingVoice){changingVoice=true;settingsVoice.SelectedIndex=voice.SelectedIndex;changingVoice=false;}try{if(!testing)SaveVoice(voicePreferencePath,SelectedVoice);SetStatus("Выбор озвучки сохранён","Примените язык в клиенте и проверьте файлы",accent);}catch(Exception ex){SetStatus("Выбор не сохранён",ex.Message,accent);}RefreshVoice();RefreshControls();};RefreshVoice();
 }
 void ShowVoiceChild(bool max){if(busy)return;pages["voice"].Visible=false;try{if(max){using(var window=testing?MaxVoiceWindow.CreatePreview(home,"supported"):new MaxVoiceWindow(home,path.Text.Trim())){window.ShowDialog(this);if(window.LastReport.Length>0){lastLog=window.LastReport;logBox.Text=lastLog;}}}else OpenSpeech();}finally{ShowPage("voice");}}

 void RefreshVoice(){
  if(voiceStatus==null)return;
  string client=ClientName,actual=SteamVoice();voiceSummary.Text="Озвучка: "+VoiceTitle(SelectedVoice);
  voiceSteam.Text=client=="Steam"?"Открыть выбор языка в Steam":"Как выбрать язык в EA app / Steam";
  if(client!="Steam")voiceStatus.Text=client+": язык не проверяется автоматически.\nВыберите English или Japanese в свойствах\nигры клиента и дождитесь загрузки файлов.";
  else voiceStatus.Text=actual==SelectedVoice?"В Steam выбран нужный язык.\nДождитесь загрузки файлов; затем проверьте\nрадио и комментарии в настройках игры.":actual.Length==0?"Язык Steam пока не определён. Откройте\nсвойства игры и выберите нужный язык.":"В Steam: "+VoiceTitle(actual)+".\nДля смены выберите "+(SelectedVoice=="english"?"English":"Japanese")+" в свойствах игры.";
 }


 void BuildUpdates(){var p=Page("updates","Обновления",450);packageVersion=LabelAt(p,"",24,72,512,25,11,FontStyle.Bold,Theme.Ink);updateMessage=LabelAt(p,"",24,114,512,111,10.5f,FontStyle.Regular,muted);
  checkUpdate=ButtonAt(p,"Проверить обновления",24,237,512,40,true,0xf130);checkUpdate.Click+=(a,e)=>{if(!testing)CheckUpdates();};downloadRelease=ButtonAt(p,"Новый EXE · GitHub Releases",24,237,512,40,true,0xf1c6);downloadRelease.Click+=(a,e)=>OpenContact(TextUpdate.Repository+"/releases");
  installUpdate=new CheckBox{Bounds=new Rectangle(24,293,512,43),Text="Также установить обновление в игру (игра закрыта)",BackColor=Theme.Surface,ForeColor=Theme.Ink,Font=Font};p.Controls.Add(installUpdate);
  applyUpdate=ButtonAt(p,"Обновить перевод",24,352,248,40,false);applyUpdate.Click+=(a,e)=>{if(!testing)ApplyUpdate();};recoverUpdate=ButtonAt(p,"Восстановить пакет",288,352,248,40,false);recoverUpdate.Click+=(a,e)=>{if(!testing)RecoverUpdate();};
 }
 void BuildRestore(){var p=Page("restore","Вернуть оригинал",340);LabelAt(p,"Будет восстановлен исходный game.dat.\nРусский текст и дополнительный шрифт удалятся.\n\nСохранения и резервная копия останутся.\nШтатные шрифты не меняются. Закройте игру.",24,79,512,149,11,FontStyle.Regular,muted);restoreCancel=ButtonAt(p,"Отмена",24,277,166,40,false);restoreCancel.Click+=(a,e)=>ShowPage("install");restore=ButtonAt(p,"Восстановить оригинал",206,277,330,40,true);restore.Click+=(a,e)=>{if(!testing)Run("restore");};}
 void BuildHelp(){var p=Page("help","Инструкция",500,640);var steam=ButtonAt(p,"Steam",24,72,140,34,false);var ea=ButtonAt(p,"EA app",176,72,140,34,false);var text=LabelAt(p,"",24,125,592,276,11,FontStyle.Regular,muted);Action<bool> show=steamClient=>{((UiButton)steam).Selected=steamClient;((UiButton)ea).Selected=!steamClient;text.Text="1. Выберите язык игры в клиенте:\n   "+(steamClient?"Steam → F1 25 → Свойства → Общие → Язык.":"EA app → F1 25 → Управление → Свойства → Язык.")+"\n2. Выберите English либо Japanese. Дождитесь загрузки.\n3. Закройте игру. Укажите её папку в «Параметрах».\n4. Проверьте совместимость и установите перевод.\n5. Запускайте игру через свой клиент.\n\nШрифт входит в этот EXE.\nЯпонский также поддерживается, но не обязателен.\n\nВ аудионастройках игры проверьте язык радио и комментариев.";steam.Invalidate();ea.Invalidate();};steam.Click+=(a,e)=>show(true);ea.Click+=(a,e)=>show(false);show(true);var guide=ButtonAt(p,"Руководство в Steam",24,436,592,40,false,0xf194);guide.Click+=(a,e)=>OpenContact(GuideUrl);}
 void BuildLog(){var p=Page("log","Журнал операций",460,720);logBox=new TextBox{Bounds=new Rectangle(24,77,672,292),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,BorderStyle=BorderStyle.FixedSingle,BackColor=Color.FromArgb(17,19,21),ForeColor=Theme.Ink,Font=new Font("Consolas",10.5f),Text=lastLog,AccessibleName="Полный журнал операций"};p.Controls.Add(logBox);details=ButtonAt(p,"Копировать журнал",24,395,672,40,false);details.Click+=(a,e)=>{if(testing)return;try{Clipboard.SetText(lastLog);}catch(Exception ex){lastLog=ex.Message;logBox.Text=lastLog;}};}
 void BuildAbout(){var p=Page("about","О проекте",450);LabelAt(p,"F1 25 — русский текст и субтитры\nАвтор и разработчик: Karsvein\nЛаунчер 0.32 · новый интерфейс\n\nТочные профили: 1.18, 1.20, 1.24, 1.26.\nШрифт входит в этот EXE.\nШтатные шрифты, EXE и античит не меняются.\nЗамечания к переводу: EDITORIAL_NOTES.json.",24,79,512,218,11,FontStyle.Regular,muted);var git=ButtonAt(p,"GitHub · исходный код и релизы",24,304,512,40,false,0xf3ed);git.Click+=(a,e)=>OpenContact(TextUpdate.Repository);
  ContactAt(p,"Telegram",24,366,0xf5b3,TelegramUrl);ContactAt(p,"Steam",200,366,0xf6c1,SteamUrl);ContactAt(p,"Почта",376,366,0xf32c,MailUrl);
 }
 void ContactAt(Control parent,string title,int x,int y,int glyph,string url){var b=ButtonAt(parent,title,x,y,160,40,false,glyph);contactTips.SetToolTip(b,url);b.Click+=(a,e)=>OpenContact(url);}
 void BuildResultPages(){var p=Page("error","Операция не завершена",340);dialogStatus=LabelAt(p,"",24,81,512,155,11,FontStyle.Regular,muted);var log=ButtonAt(p,"Открыть журнал",24,276,240,40,false,0xf444);log.Click+=(a,e)=>ShowPage("log");var ok=ButtonAt(p,"Хорошо",280,276,256,40,true);ok.Click+=(a,e)=>ShowPage("install");p=Page("success","Готово",300,520);successText=LabelAt(p,"",24,81,472,120,11,FontStyle.Regular,muted);var done=ButtonAt(p,"На главный экран",24,237,472,40,true);done.Click+=(a,e)=>ShowPage("install");p=Page("progress","Выполняется операция",270,520);operationTitle=LabelAt(p,"",24,81,472,30,13,FontStyle.Bold,Theme.Ink);LabelAt(p,"Не закрывайте лаунчер. Исходные файлы сохраняются.\nИгра автоматически не запускается.",24,126,472,60,11,FontStyle.Regular,muted);progress=new ProgressBar{Bounds=new Rectangle(24,216,472,4),Style=ProgressBarStyle.Marquee,MarqueeAnimationSpeed=30,Visible=false};p.Controls.Add(progress);foreach(Control c in p.Controls)if(c is UiButton){c.Enabled=false;}}
 bool Confirm(string title,string message){if(testing)return false;Panel previous=pages.ContainsKey(currentPage)?pages[currentPage]:null;if(previous!=null)previous.Visible=false;try{using(var dialog=new Form{Text=title,ClientSize=new Size(520,270),FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.CenterParent,BackColor=Theme.Surface,ForeColor=Theme.Ink,Font=Font,KeyPreview=true,AutoScaleMode=AutoScaleMode.Dpi}){LabelAt(dialog,title,24,24,472,34,17,FontStyle.Bold,Theme.Ink);LabelAt(dialog,message,24,85,472,95,11,FontStyle.Regular,muted);var no=ButtonAt(dialog,"Отмена",24,207,220,40,false);no.DialogResult=DialogResult.Cancel;var yes=ButtonAt(dialog,"Продолжить",260,207,236,40,true);yes.DialogResult=DialogResult.OK;dialog.CancelButton=no;dialog.AcceptButton=no;dialog.Shown+=(a,e)=>no.Focus();dialog.KeyDown+=(a,e)=>{if(e.KeyCode==Keys.Escape){dialog.DialogResult=DialogResult.Cancel;dialog.Close();}};return dialog.ShowDialog(this)==DialogResult.OK;}}finally{if(previous!=null)previous.Visible=true;}}

 bool IsContact(string s){return s==TelegramUrl||s==SteamUrl||s==MailUrl||s==GuideUrl||s==TextUpdate.Repository||s==TextUpdate.Repository+"/releases";}
 void OpenContact(string s){if(testing||!IsContact(s))return;try{Process.Start(new ProcessStartInfo(s){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,"Адрес: "+s+"\n\n"+ex.Message,"Открыть ссылку");}}
 Label LabelAt(Control parent,string text,int x,int y,int w,int h,float size,FontStyle style,Color color){var l=new Label{Text=text,Bounds=new Rectangle(x,y,w,h),Font=new Font("Segoe UI",size,style),ForeColor=color,BackColor=Color.Transparent,UseMnemonic=false};parent.Controls.Add(l);return l;}
 Button ButtonAt(Control parent,string text,int x,int y,int w,int h,bool primary,int glyph=0){var b=new UiButton{Primary=primary,Glyph=glyph,Text=text,Bounds=new Rectangle(x,y,w,h),Font=new Font("Segoe UI",10.5f,FontStyle.Regular),AccessibleName=text};parent.Controls.Add(b);return b;}
 void RefreshVersions(){if(packageVersion!=null)packageVersion.Text=detectedProfile==null?"Игра: версия ещё не проверена":"F1 25 "+detectedProfile.GameVersion+" · текст "+detectedProfile.TextVersion;}

 bool PendingUpdate {get{return (testing&&previewPending)||File.Exists(Path.Combine(home,"update-pending.json"));}}
 void RefreshControls(){if(prepare==null||install==null||restore==null)return;bool pending=PendingUpdate;bool mainAvailable=!busy&&!overlayOpen;header.Enabled=mainAvailable;footer.Enabled=mainAvailable;if(journal!=null)journal.Enabled=mainAvailable;prepare.Enabled=!busy;prepare.Text=pending?"Восстановить пакет":installed?"Перевод установлен":prepared?"Установить перевод":path.Text.Trim().Length==0?"Выбрать папку игры":"Проверить игру";install.Enabled=!busy&&!pending&&prepared&&ready.Checked;restore.Enabled=!busy&&!pending;browse.Enabled=!busy;path.Enabled=!busy;ready.Enabled=!busy;help.Enabled=mainAvailable;details.Enabled=!busy;checkUpdate.Enabled=!busy&&!pending&&CanUpdateText;bool fullArchive=SelectedTranslation==TranslationVariants.OriginalNames||(detectedProfile!=null&&!detectedProfile.Stable);checkUpdate.Visible=!fullArchive;downloadRelease.Visible=fullArchive;downloadRelease.Enabled=!busy;applyUpdate.Enabled=!busy&&!pending&&CanUpdateText&&candidate!=null&&candidate.Available;recoverUpdate.Enabled=!busy&&pending;installUpdate.Enabled=!busy&&!pending&&CanUpdateText;translation.Enabled=!busy&&!pending;voice.Enabled=!busy;settingsVoice.Enabled=!busy;if(voiceSteam!=null)voiceSteam.Enabled=!busy;if(voiceRefresh!=null)voiceRefresh.Enabled=!busy;if(speechButton!=null)speechButton.Enabled=!busy;progress.Visible=busy;closeButton.Enabled=!busy;minimize.Enabled=!busy;settingsButton.Enabled=!busy;voiceButton.Enabled=!busy;moreButton.Enabled=!busy;folderButton.Enabled=!busy;AcceptButton=currentPage=="confirm"?installCancel:currentPage=="restore"?restoreCancel:currentPage=="install"?prepare:null;}
 void SetStatus(string title,string text,Color color){if(statusTitle==null)return;statusTitle.Text=title;statusText.Text=text.Length>120?text.Substring(0,117)+"…":text;phase.BackColor=color;contactTips.SetToolTip(statusText,text);if(dialogStatus!=null)dialogStatus.Text=text;if(operationTitle!=null)operationTitle.Text=title;if(successText!=null)successText.Text=title+"\n\n"+text;if(busy&&pages.ContainsKey("progress"))ShowPage("progress");}

 void ShowLog(){ShowPage("log");}
 void StartJob(string title,Func<string> job,Action done){if(busy||testing)return;string jobPage=currentPage;busy=true;SetStatus(title,"Дождитесь завершения операции.",accent);RefreshControls();ThreadPool.QueueUserWorkItem(delegate{string result;bool ok=true;try{result=job();}catch(Exception ex){ok=false;result=ex.Message;}if(!IsDisposed)BeginInvoke((Action)delegate{busy=false;lastLog=result;logBox.Text=result;SetStatus(ok?"Готово":"Операция не завершена",result,ok?green:Color.FromArgb(171,63,28));RefreshVersions();if(jobPage=="updates")updateMessage.Text=result;if(ok&&done!=null)done();RefreshControls();ShowPage(ok?jobPage:"error");});});}
 void CheckUpdates(){if(!CanUpdateText)return;string game=path.Text.Trim();candidate=null;RefreshControls();StartJob("Проверяем GitHub…",delegate{candidate=Updates.Check(home,Updates.Download,game);return candidate.Available?"Доступен перевод "+candidate.Version+". "+candidate.Notes:"У вас актуальный перевод "+candidate.Version+".";},delegate{if(!CanUpdateText){candidate=null;RefreshTranslation();return;}updateMessage.Text=lastLog;installUpdate.Checked=File.Exists(Path.Combine(path.Text.Trim(),"localisation","2025_russian","language_jap.lng"));});}
 static string EngineInstall(string h,string game){var si=new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(h,"Engine.ps1")+"\" -Action install -GamePath \""+game.TrimEnd('\\')+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};using(var p=Process.Start(si)){var err=p.StandardError.ReadToEndAsync();var result=p.StandardOutput.ReadToEnd();p.WaitForExit();result+=err.Result;if(p.ExitCode!=0)throw new IOException(result);return result;}}
 void ApplyUpdate(){if(!CanUpdateText||candidate==null||!candidate.Available||busy)return;bool apply=installUpdate.Checked;string game=path.Text.Trim();if(game.IndexOf('"')>=0||game.Length==0){SetStatus("Проверьте папку","Укажите полный путь к F1 25.",accent);return;}if(!Confirm("Обновить перевод?","Скачать перевод "+candidate.Version+" из GitHub?\n"+(apply?"Пакет будет также установлен в выбранную папку игры. Закройте игру.":"Обновится пакет в лаунчере. Игра останется без изменений.")))return;
  StartJob("Обновляем перевод…",delegate{return Updates.Apply(home,candidate,Updates.Download,game,apply,EngineInstall);},delegate{InvalidatePreparation();updateMessage.Text="Перевод обновлён до "+Updates.LocalVersion(home)+". Перед установкой снова проверьте совместимость.";});
 }
 void RecoverUpdate(){if(busy)return;if(!Confirm("Восстановить обновление?","Будет возвращён предыдущий пакет перевода.\nЕсли обновлялась игра, вернётся и прежний текст. Закройте игру."))return;StartJob("Восстанавливаем пакет…",delegate{return Updates.Recover(home,EngineInstall);},delegate{InvalidatePreparation();updateMessage.Text="Предыдущий пакет восстановлен.";});}

 protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Theme.Background);if(backdrop!=null){e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;e.Graphics.DrawImage(backdrop,ClientRectangle);}using(var pen=new Pen(Theme.Line))e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(ClientSize.Width>32&&ClientSize.Height>32){var previous=Region;using(var clip=Shape.Round(ClientRectangle,20))Region=new Region(clip);if(previous!=null)previous.Dispose();}}
 protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Escape){e.Handled=true;e.SuppressKeyPress=true;if(!busy){if(moreMenu.Visible)moreMenu.Close();else if(currentPage!="install")ShowPage("install");}return;}base.OnKeyDown(e);}

 protected override void OnFormClosing(FormClosingEventArgs e){if(busy){e.Cancel=true;return;}base.OnFormClosing(e);}
 protected override void Dispose(bool disposing){if(disposing){contactTips.Dispose();if(moreMenu!=null)moreMenu.Dispose();if(headerMark!=null)headerMark.Dispose();if(backdrop!=null)backdrop.Dispose();}base.Dispose(disposing);}
  void ApplyResult(string action,int code,string output,string expectedGame=null,string expectedVariant=null){
  busy=false;
  try{if((expectedGame!=null&&!String.Equals(GameTextProfiles.CanonicalPath(expectedGame),GameTextProfiles.CanonicalPath(path.Text),StringComparison.OrdinalIgnoreCase))||(expectedVariant!=null&&expectedVariant!=SelectedTranslation)){InvalidatePreparation();RefreshTranslation();RefreshControls();SetStatus("Выбор изменён","Повторите проверку выбранной папки и варианта.",accent);return;}}
  catch(Exception ex){code=1;output+="\r\nОШИБКА: "+ex.Message;}
  if(code==0&&(action=="prepare"||action=="install")){
   try{detectedProfile=GameTextProfiles.Parse(output,path.Text,SelectedTranslation);}
   catch(Exception ex){code=1;output+="\r\nОШИБКА: "+ex.Message;}
  }
  lastLog=output+"\r\nКод завершения: "+code;
  if(code==0){
   if(action=="prepare"){candidate=null;prepared=true;RefreshTranslation();SetStatus("Файлы совместимы","F1 25 "+detectedProfile.GameVersion+" · перевод "+detectedProfile.TextVersion+". "+(ready.Checked?"Можно устанавливать.":"Подтвердите подготовку галочкой."),green);}
   else if(action=="install"){installed=true;prepared=true;RefreshTranslation();SetStatus("Перевод установлен","F1 25 "+detectedProfile.GameVersion+" · перевод "+detectedProfile.TextVersion+". Запускайте игру через свой клиент.",green);}
   else{installed=false;InvalidatePreparation();RefreshTranslation();SetStatus("Оригинал восстановлен","Перевод удалён. Язык можно сменить в клиенте игры.",green);}
  }else{installed=false;InvalidatePreparation();RefreshTranslation();string reason=output.Trim();int at=reason.LastIndexOf("ОШИБКА:");if(at>=0)reason=reason.Substring(at+7).Trim();if(reason.Length>150)reason=reason.Substring(0,147)+"…";SetStatus("Операция не завершена",reason.Length==0?"Подробности — в журнале операции.":reason,Color.FromArgb(172,73,12));}
  RefreshVersions();RefreshControls();if(!testing)ShowPage(code!=0?"error":action=="prepare"?"confirm":"success");
 }

 void Run(string action){
  if(testing)return;
  if(action=="install"&&(!prepared||detectedProfile==null||!detectedProfile.Matches(path.Text,SelectedTranslation)))return;
  if(action=="prepare")InvalidatePreparation();
  if(action!="restore"){string actual=SteamVoice();if(actual.Length>0&&actual!=SelectedVoice){prepared=false;RefreshControls();ShowPage("voice");SetStatus("Язык Steam отличается","Примените выбранную озвучку в Steam или измените выбор здесь.",accent);return;}}
  string variant=SelectedTranslation;string game=path.Text.Trim();if(game.Length==0||game.IndexOf('"')>=0){SetStatus("Проверьте папку игры","Укажите полный путь к установленной F1 25.",accent);return;}
  try{game=GameTextProfiles.CanonicalPath(game);}catch(Exception ex){InvalidatePreparation();RefreshControls();SetStatus("Проверьте папку игры",ex.Message,accent);return;}
  if(!File.Exists(Path.Combine(home,"Engine.ps1"))||!File.Exists(Path.Combine(home,"manifest.json"))){SetStatus("Переоткройте EXE лаунчера","Встроенные файлы пакета не прошли проверку; запустите исходный EXE заново.",accent);return;}
  busy=true;RefreshControls();SetStatus(action=="prepare"?"Проверяем файлы…":action=="install"?"Устанавливаем перевод…":"Восстанавливаем оригинал…","Дождитесь завершения. Идёт проверка файлов.",accent);
  ThreadPool.QueueUserWorkItem(delegate{
   int code=-1;string output;
   try{var si=new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(home,"Engine.ps1")+"\" -Action "+action+" -Translation "+variant+" -GamePath \""+game.TrimEnd('\\')+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};using(var p=Process.Start(si)){var err=p.StandardError.ReadToEndAsync();output=p.StandardOutput.ReadToEnd();p.WaitForExit();output+=err.Result;code=p.ExitCode;}}
   catch(Exception ex){output=ex.Message;}
   if(!IsDisposed)BeginInvoke((Action)delegate{ApplyResult(action,code,output,game,variant);});
  });
 }

 const string CurrentProfileLine="PROFILE game=1.26 records=56134 text=0.15.5 channel=stable";
 static bool ContainsText(Control parent,string text){if(parent.Text.Contains(text))return true;foreach(Control child in parent.Controls)if(ContainsText(child,text))return true;return false;}
 int checks=0;void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
 void SelfTest(){
  Check(testing&&!prepared&&detectedProfile==null&&!installed&&!ready.Checked,"startup folder never grants installation readiness");
  Check(statusTitle.Right+16<=folderButton.Left,"footer title and folder have separate columns");
  Check(statusText.Right+16<=folderButton.Left&&!statusText.Bounds.IntersectsWith(folderButton.Bounds),"footer status cannot paint over folder");
  Check(folderButton.Right+16<=prepare.Left,"folder and main action have separate columns");
  ShowPage("voice");Check(!header.Enabled&&!footer.Enabled&&!help.Enabled&&!journal.Enabled,"modal blocks background keyboard targets");
  OnKeyDown(new KeyEventArgs(Keys.Escape));Check(currentPage=="install"&&header.Enabled&&footer.Enabled&&help.Enabled&&journal.Enabled,"Esc restores main keyboard targets");
  Check(IsContact(TelegramUrl)&&IsContact(SteamUrl)&&IsContact(MailUrl)&&IsContact(GuideUrl)&&IsContact(TextUpdate.Repository)&&!IsContact("https://example.com"),"contact destinations");
  Check(Text.Contains("0.32")&&TextUpdate.LauncherVersion=="0.32","launcher version");
  Check(ContainsText(this,"Японский также поддерживается, но не обязателен.")&&!ContainsText(this,"Японский нужен для шрифта"),"optional Japanese base");
  Check(ContainsText(this,"Шрифт входит в этот EXE.")&&ContainsText(this,"Будет восстановлен исходный game.dat."),"native package information");
  Check(ClientSize==new Size(960,640),"window size");foreach(Control c in Controls)Check(c.Left>=0&&c.Top>=0&&c.Right<=Width&&c.Bottom<=Height,"inside frame: "+c.Text);
  foreach(string page in new[]{"install","voice","updates","restore","help","log","about"}){ShowPage(page);Check(currentPage==page&&(page=="install"?!overlayOpen:pages[page].Parent==shade),"navigation "+page);}ShowPage("install");
  Check(ContainsText(this,"Русский голос Макса · тест"),"optional Max voice entry");Check(voice.Items.Count==2&&SelectedVoice=="english","English default and Japanese option");Check(speechButton!=null&&speechButton.Text=="Голосовые команды","speech entry on voice page");
  Check(ParseSteamVoice("\"UserConfig\" { \"language\" \"english\" } \"MountedConfig\" { \"language\" \"japanese\" }")=="english","selected language beats mounted language");
  Check(ParseSteamVoice("\"MountedConfig\" { \"language\" \"japanese\" }")=="","no guessing from mounted language");
  Check(ParseSteamVoice("\"UserConfig\" { \"language\" \"japanese\" }")=="japanese","Japanese detected");
  Check(ParseSteamVoice("broken")=="","malformed config");
  string clientTest=Path.Combine(home,"evidence","client-detection");Directory.CreateDirectory(clientTest);
  string eaPath=Path.Combine(clientTest,"ea");Directory.CreateDirectory(Path.Combine(eaPath,"_Installer"));
  string steamPath=Path.Combine(clientTest,"steamapps","common","F1 25");Directory.CreateDirectory(Path.Combine(steamPath,"_Installer"));
  File.WriteAllText(Path.Combine(clientTest,"steamapps","appmanifest_3059520.acf"),"test");
  Check(DetectClient(eaPath)=="EA app","EA client detection");Check(DetectClient(steamPath)=="Steam","Steam manifest takes precedence");
  Check(DetectClient(Path.Combine(clientTest,"missing"))=="Steam / EA app","unknown client not guessed");
  string savedPath=path.Text;path.Text=eaPath;RefreshVoice();Check(voiceStatus.Text.Contains("EA app")&&voiceSteam.Text.Contains("EA app"),"EA language instructions");path.Text=savedPath;
  string testPrefs=Path.Combine(Path.GetTempPath(),"f1ru-voice-test-"+Guid.NewGuid().ToString("N"),"voice.txt");
  try{SaveVoice(testPrefs,"japanese");Check(LoadVoice(testPrefs)=="japanese","Japanese preference roundtrip");SaveVoice(testPrefs,"english");Check(LoadVoice(testPrefs)=="english","atomic preference replacement");File.WriteAllText(testPrefs,"invalid");Check(LoadVoice(testPrefs)=="","invalid preference");bool rejected=false;try{SaveVoice(testPrefs,"other");}catch(ArgumentException){rejected=true;}Check(rejected,"invalid write rejected");}finally{if(File.Exists(testPrefs))File.Delete(testPrefs);Directory.Delete(Path.GetDirectoryName(testPrefs));}
  ready.Checked=true;ApplyResult("prepare",0,CurrentProfileLine);voice.SelectedIndex=1;Check(SelectedVoice=="japanese"&&!prepared&&!ready.Checked&&!install.Enabled,"voice change invalidates readiness");Check(voiceSummary.Text.Contains("Японская"),"Japanese summary");voice.SelectedIndex=0;Check(voiceSummary.Text.Contains("Английская"),"English summary");
  Check(translation.Items.Count==2&&SelectedTranslation==TranslationVariants.Russian,"translation default");
  ready.Checked=true;ApplyResult("prepare",0,CurrentProfileLine);translation.SelectedIndex=1;
  Check(SelectedTranslation==TranslationVariants.OriginalNames&&!prepared&&!ready.Checked&&!install.Enabled,"variant change invalidates readiness");
  Check(SelectedVoice=="english"&&translationHint.Text.Contains("Пилоты"),"variant independent from voice");
  Check(!checkUpdate.Enabled&&!applyUpdate.Enabled&&!installUpdate.Enabled,"standard updater does not replace original names");
  Check(translation.Bottom<translationHint.Top&&translationHint.Bottom<settingsVoice.Top,"translation controls do not overlap");
  string prefs=Path.Combine(home,"evidence","variant-pref-test.txt");
  TranslationVariants.Save(prefs,TranslationVariants.OriginalNames);Check(TranslationVariants.Load(prefs)==TranslationVariants.OriginalNames,"variant preference roundtrip");
  TranslationVariants.Save(prefs,TranslationVariants.Russian);Check(TranslationVariants.Load(prefs)==TranslationVariants.Russian,"variant preference overwrite");
  File.WriteAllText(prefs,"unknown");Check(TranslationVariants.Load(prefs)==TranslationVariants.Russian,"invalid variant falls back");
  bool invalid=false;try{TranslationVariants.Save(prefs,"../bad");}catch(ArgumentException){invalid=true;}Check(invalid,"invalid variant rejected");File.Delete(prefs);
  busy=true;RefreshControls();Check(!translation.Enabled,"variant frozen while busy");busy=false;
  translation.SelectedIndex=0;Check(!checkUpdate.Enabled&&SelectedVoice=="english","standard update awaits recheck");
  Check(!install.Enabled,"initial gate");ready.Checked=true;Check(!install.Enabled,"checkbox alone");ApplyResult("prepare",0,CurrentProfileLine);Check(install.Enabled,"prepared");path.Text+="_test";Check(!prepared&&!install.Enabled,"path invalidation");busy=true;RefreshControls();Check(!checkUpdate.Enabled&&!applyUpdate.Enabled&&!prepare.Enabled&&!restore.Enabled&&!path.Enabled&&!voice.Enabled&&!voiceSteam.Enabled&&!voiceRefresh.Enabled,"busy");var closing=new FormClosingEventArgs(CloseReason.UserClosing,false);OnFormClosing(closing);Check(closing.Cancel,"busy close");
  ApplyResult("install",1,"ОШИБКА: Тест");Check(!prepared&&!install.Enabled,"failed install");ApplyResult("prepare",0,CurrentProfileLine);ApplyResult("install",0,CurrentProfileLine);Check(statusTitle.Text=="Перевод установлен","installed");ApplyResult("restore",0,"PASS");Check(!install.Enabled&&statusTitle.Text=="Оригинал восстановлен","restored");
  Check(detectedProfile==null&&!checkUpdate.Enabled&&!installUpdate.Enabled,"unknown profile updater gate");
  ready.Checked=true;ApplyResult("prepare",0,"PROFILE game=1.18 records=56439 text=0.15.5.118 channel=pinned");
  Check(prepared&&install.Enabled&&detectedProfile.GameVersion=="1.18","1.18 preparation");
  Check(packageVersion.Text.Contains("1.18")&&packageVersion.Text.Contains("0.15.5.118"),"1.18 exact displayed version");
  Check(!checkUpdate.Enabled&&!applyUpdate.Enabled&&!installUpdate.Enabled&&updateMessage.Text.Contains("EXE"),"1.18 pinned UI update gate");Check(downloadRelease.Enabled&&IsContact(TextUpdate.Repository+"/releases"),"legacy full archive link available");
  ApplyResult("prepare",0,"PROFILE game=1.24 records=55991 text=0.15.5.124 channel=pinned");
  Check(prepared&&install.Enabled&&packageVersion.Text.Contains("0.15.5.124")&&!checkUpdate.Enabled,"1.24 preparation and pinning");
  ApplyResult("prepare",0,"PROFILE game=1.20 records=55991 text=0.15.6.120 channel=pinned");
  Check(prepared&&install.Enabled&&detectedProfile.GameVersion=="1.20"&&packageVersion.Text.Contains("0.15.6.120")&&!checkUpdate.Enabled,"1.20 preparation and pinning");
  ApplyResult("prepare",0,CurrentProfileLine);candidate=new TextUpdate{Available=true};RefreshControls();
  Check(checkUpdate.Enabled&&applyUpdate.Enabled&&installUpdate.Enabled,"current profile stable UI regression");
  path.Text+="_different";Check(detectedProfile==null&&candidate==null&&!prepared&&!applyUpdate.Enabled,"path clears profile and candidate");
  ApplyResult("prepare",0,CurrentProfileLine);candidate=new TextUpdate{Available=true};translation.SelectedIndex=1;
  Check(detectedProfile==null&&candidate==null&&!prepared&&!install.Enabled,"variant clears profile and candidate");
  ApplyResult("prepare",0,"PROFILE game=1.26 records=56134 text=0.15.5.1 channel=stable");
  Check(packageVersion.Text.Contains("0.15.5.1")&&!checkUpdate.Enabled,"original-names exact displayed text version");
  translation.SelectedIndex=0;ApplyResult("prepare",0,"PREPARE PASS");Check(!prepared&&detectedProfile==null,"missing structured profile rejected");
  ApplyResult("prepare",0,"PROFILE game=1.18 records=56134 text=0.15.5.118 channel=pinned");Check(!prepared&&detectedProfile==null,"mismatched profile count rejected");
  ApplyResult("prepare",0,CurrentProfileLine+"\r\n"+CurrentProfileLine);Check(!prepared,"duplicate profile rejected");
  ApplyResult("prepare",0,CurrentProfileLine);ApplyResult("install",1,CurrentProfileLine+"\r\nОШИБКА: fixture");Check(detectedProfile==null&&!prepared,"failed operation cannot retain profile");
  ApplyResult("prepare",0,CurrentProfileLine,path.Text+"_old",SelectedTranslation);Check(detectedProfile==null&&!prepared,"stale path result rejected");
  string validPath=path.Text;path.Text=@"C:\bad|path";ApplyResult("prepare",1,"Invalid fixture path",path.Text,SelectedTranslation);Check(detectedProfile==null&&!prepared&&statusTitle.Text=="Операция не завершена","invalid path result does not crash UI");path.Text=validPath;

  ApplyResult("prepare",0,CurrentProfileLine);ready.Checked=true;candidate=new TextUpdate{Available=true};previewPending=true;RefreshControls();Check(!checkUpdate.Enabled&&!applyUpdate.Enabled&&!installUpdate.Enabled&&!install.Enabled&&!restore.Enabled&&recoverUpdate.Enabled&&prepare.Text=="Восстановить пакет","interrupted update blocks writes and exposes recovery");previewPending=false;RefreshControls();ShowPage("confirm");Check(AcceptButton==installCancel,"install confirmation defaults to cancel");ShowPage("restore");Check(AcceptButton==restoreCancel,"restore confirmation defaults to cancel");
  Check(BackColor==Theme.Background&&ForeColor==Theme.Ink,"dark main palette");Check(Font.SizeInPoints==10.5f,"readable minimum base text");Check(backdrop!=null&&headerMark!=null,"verified reference artwork present");
  ShowPage("settings");Check(overlayOpen&&currentPage=="settings","settings overlay");OnKeyDown(new KeyEventArgs(Keys.Escape));Check(!overlayOpen&&currentPage=="install","Esc returns home");
  foreach(string id in new[]{"settings","confirm","voice","updates","restore","help","log","about","error","success","progress"}){ShowPage(id);Check(currentPage==id,"designed view "+id);Panel panel=pages[id];Check(panel.Left>=0&&panel.Top>=0&&panel.Right<=960&&panel.Bottom<=640,"modal bounds "+id);foreach(Control control in panel.Controls)Check(control.Left>=0&&control.Top>=0&&control.Right<=panel.Width&&control.Bottom<=panel.Height,"modal content bounds "+id+" / "+control.Text);}
  ShowPage("install");Check(!Confirm("test","test"),"test mode cannot approve mutation");bool prior=busy;Run("prepare");Check(busy==prior,"preview cannot execute backend");

 }

 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 internal void DesignState(string state){
  path.Text=@"C:\F1RU-UI-Test";
  if(state=="settings-error"){path.Text=@"C:\bad|path";ShowPage("settings");CompleteSettings();return;}
  if(state=="interrupted"){previewPending=true;ShowPage("updates");updateMessage.Text="Предыдущее обновление было прервано. Верните прежний пакет, прежде чем продолжать установку.";return;}
  if(state=="original-updates"){translation.SelectedIndex=1;ApplyResult("prepare",0,"PROFILE game=1.26 records=56134 text=0.15.5.1 channel=stable");ShowPage("updates");return;}
  if(state=="first-launch"){path.Text="";ShowPage("settings");return;}
  if(state=="ready"||state=="confirm"||state=="installed"||state=="legacy120"||state=="updates"||state=="error"||state=="installing"){ready.Checked=true;ApplyResult("prepare",0,state=="legacy120"?"PROFILE game=1.20 records=55991 text=0.15.6.120 channel=pinned":CurrentProfileLine);}
  if(state=="installed"){ApplyResult("install",0,CurrentProfileLine);ShowPage("success");}
  else if(state=="error"){ApplyResult("install",1,"ОШИБКА: Структура игровых ресурсов изменилась. Нужен новый профиль совместимости, файлы сохранены.");ShowPage("error");}
  else if(state=="installing"){busy=true;RefreshControls();SetStatus("Устанавливаем перевод…","Дождитесь завершения проверки и записи файлов.",accent);}
  else if(state=="legacy120"||state=="updates")ShowPage("updates");
  else if(state=="original-names"){translation.SelectedIndex=1;ShowPage("settings");}
  else if(state=="confirm"){ready.Checked=false;ShowPage("confirm");}
  else if(pages.ContainsKey(state))ShowPage(state);else ShowPage("install");
 }
 [STAThread]static int Main(string[] args){SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  if(args.Length==2&&args[0]=="--launch-context-self-test"){
   try{string directory=LaunchContext.Directory(AppDomain.CurrentDomain.BaseDirectory);File.WriteAllText(args[1],new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{status="PASS",singleExe=Environment.GetEnvironmentVariable("F1RU_SINGLE_EXE")=="1",launcherDirectory=directory,packageDirectory=AppDomain.CurrentDomain.BaseDirectory,gameWrites=0,registryWrites=0,gameLaunched=false}),Encoding.UTF8);return 0;}
   catch(Exception ex){File.WriteAllText(args[1],"LAUNCH_CONTEXT_FAIL "+ex.Message,Encoding.UTF8);return 1;}
  }
  if(args.Length==2&&args[0]=="--max-voice-self-test")return MaxVoiceWindow.SelfTest(AppDomain.CurrentDomain.BaseDirectory,args[1]);
  if(args.Length==2&&args[0]=="--speech-self-test")return SpeechWindow.SelfTest(AppDomain.CurrentDomain.BaseDirectory,args[1]);
  if(args.Length==3&&args[0]=="--speech-host-test")return SpeechWindow.HostTest(AppDomain.CurrentDomain.BaseDirectory,args[1],args[2]);
  if(args.Length==2&&args[0]=="--max-design-preview"){Glyphs.Load(AppDomain.CurrentDomain.BaseDirectory);using(var w=MaxVoiceWindow.CreatePreview(AppDomain.CurrentDomain.BaseDirectory,args[1]))Application.Run(w);return 0;}
  if(args.Length==2&&args[0]=="--speech-design-preview"){Glyphs.Load(AppDomain.CurrentDomain.BaseDirectory);using(var w=SpeechWindow.CreatePreview(AppDomain.CurrentDomain.BaseDirectory,args[1]))Application.Run(w);return 0;}
  bool first;using(var mutex=new Mutex(true,"Local\\Karsvein-F1RU-021",out first)){if(!first&&args.Length==0){MessageBox.Show("Лаунчер уже открыт.","F1 25");return 1;}
   using(var f=new Launcher(args.Length>0)){
    if(args.Length==2&&args[0]=="--self-test"){try{f.SelfTest();File.WriteAllText(args[1],"UI_TEST_PASS checks="+f.checks+" real_game_writes=false",Encoding.UTF8);return 0;}catch(Exception ex){File.WriteAllText(args[1],ex.ToString());return 1;}}
    if(args.Length==2&&args[0]=="--design-preview")f.Shown+=(a,e)=>f.DesignState(args[1]);else if(args.Length>0)return 2;
    Application.Run(f);return 0;
   }
  }
 }
}
