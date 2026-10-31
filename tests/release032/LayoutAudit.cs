using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

internal static class LayoutAudit {
 [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
 static readonly BindingFlags Any=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static readonly MethodInfo GetControlState=typeof(Control).GetMethod("GetState",BindingFlags.Instance|BindingFlags.NonPublic);
 static readonly StringBuilder Report=new StringBuilder();
 static int clips, overlaps, ellipses, controls, checks, errors, outside;
 static readonly Dictionary<Control,string> Names=new Dictionary<Control,string>();
 sealed class Item { public Control Control; public Rectangle Ink; public string Id; }
 [STAThread] static int Main(string[] args) {
  Console.OutputEncoding=new UTF8Encoding(false);string home=AppDomain.CurrentDomain.BaseDirectory;
  string output=args.Length>0?Path.GetFullPath(args[0]):Path.Combine(home,"layout.txt");
  try {
   SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
   string exe=Path.Combine(home,"Launcher.exe");
   Report.AppendLine("NATIVE LAYOUT AUDIT / hidden handles only / no Show, Application.Run, screenshot, capture, backend or game launch");
   Report.AppendLine("Assembly: "+exe); Report.AppendLine("SHA256: "+Hash(exe)); Report.AppendLine("Modified UTC: "+File.GetLastWriteTimeUtc(exe).ToString("O"));
   Assembly asm=Assembly.LoadFrom(exe);Type launcher=asm.GetType("Launcher",true);
   MethodInfo design=launcher.GetMethod("DesignState",Any); FieldInfo open=launcher.GetField("overlayOpen",Any);
   if(design==null||open==null)throw new InvalidOperationException("Compiled launcher is stale: new DesignState/overlayOpen absent.");
   ConstructorInfo ctor=launcher.GetConstructor(Any,null,new Type[]{typeof(bool)},null);
   if(ctor==null)throw new InvalidOperationException("Launcher(bool) constructor absent.");
   foreach(string state in new[]{"main","settings","confirm","voice","updates","legacy120","error","help","about","log","first-launch","ready","installed","installing","original-names","restore","success","original-updates","interrupted","settings-error"}) {
    using(Form form=(Form)ctor.Invoke(new object[]{true})) {
     // CaptureShade exits early: no DrawToBitmap or visual capture occurs.
     open.SetValue(form,true); design.Invoke(form,new object[]{state});
     Audit(form,"Launcher/"+state);
    }
   }
   Type speech=asm.GetType("SpeechWindow",true); MethodInfo speechPreview=speech.GetMethod("CreatePreview",Any);
   if(speechPreview==null)throw new InvalidOperationException("Compiled SpeechWindow is stale: CreatePreview absent.");
   foreach(string page in new[]{"setup","commands","diagnostics"})using(Form form=(Form)speechPreview.Invoke(null,new object[]{home,page}))Audit(form,"Speech/"+page);
   Type max=asm.GetType("MaxVoiceWindow",true);MethodInfo maxPreview=max.GetMethod("CreatePreview",Any);
   if(maxPreview==null)throw new InvalidOperationException("Compiled MaxVoiceWindow is stale: CreatePreview absent.");
   foreach(string page in new[]{"supported","installed","unsupported","diagnostics"})using(Form form=(Form)maxPreview.Invoke(null,new object[]{home,page}))Audit(form,"MaxVoice/"+page);
  } catch(Exception ex) { errors++;Report.AppendLine("ERROR "+(ex is TargetInvocationException&&ex.InnerException!=null?ex.InnerException:ex).ToString()); }
  Report.AppendLine();Report.AppendLine("SUMMARY states="+checks+" controls="+controls+" clipped="+clips+" ellipsis="+ellipses+" overlaps="+overlaps+" outside="+outside+" errors="+errors);
  Report.AppendLine("backend_invoked=false; game_launched=false; windows_shown=false; screenshots=false; native_handles_only=true");
  int result=errors>0?2:clips+overlaps+outside>0?1:0;Report.AppendLine("EXIT "+result);
  Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,Report.ToString(),new UTF8Encoding(false));Console.WriteLine("LAYOUT states="+checks+" clipped="+clips+" ellipsis="+ellipses+" overlaps="+overlaps+" outside="+outside+" errors="+errors+" EXIT "+result);Console.WriteLine(output);return result;
 }
 static string Hash(string path) { using(var h=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant(); }
 static void NameFields(Form form) {
  Names.Clear();for(Type type=form.GetType();type!=null&&type!=typeof(Form);type=type.BaseType)foreach(FieldInfo f in type.GetFields(Any))if(!f.IsStatic&&typeof(Control).IsAssignableFrom(f.FieldType)) {Control c=f.GetValue(form) as Control;if(c!=null&&!Names.ContainsKey(c))Names.Add(c,f.Name);}
 }
 static bool OwnVisible(Control c) {
  if(GetControlState==null)throw new InvalidOperationException("WinForms GetState reflection unavailable; local visibility cannot be audited while keeping forms hidden.");
  return (bool)GetControlState.Invoke(c,new object[]{2});
 }
 static bool Active(Control c,Control root) {
  if(c==root)return true;TabPage page=c as TabPage;if(page!=null&&page.Parent is TabControl&&((TabControl)page.Parent).SelectedTab!=page)return false;
  return OwnVisible(c)&&c.Parent!=null&&Active(c.Parent,root);
 }
 static string Id(Control c) { string n;return Names.TryGetValue(c,out n)?n:c.Name.Length>0?c.Name:c.GetType().Name+"["+(c.Parent==null?0:c.Parent.Controls.IndexOf(c))+"]"; }
 static string Text(Control c) { return c.Text.Replace("\r","").Replace("\n"," / "); }
 static string Rect(Rectangle r) { return "("+r.X+","+r.Y+","+r.Width+","+r.Height+")"; }
 static void Handles(Control c) { IntPtr handle=c.Handle;foreach(Control child in c.Controls)Handles(child);c.PerformLayout(); }
 static void Audit(Form form,string state) {
  NameFields(form);Handles(form);checks++;Report.AppendLine();Report.AppendLine("STATE "+state+" client="+form.ClientSize+" hidden="+!form.Visible);
  using(Graphics g=Graphics.FromHwnd(form.Handle))Report.AppendLine("DPI "+g.DpiX+"x"+g.DpiY);
  Walk(form,form,state);
 }
 static void Walk(Control parent,Control root,string state) {
  List<Item> items=new List<Item>();
  foreach(Control c in parent.Controls) {
   if(!Active(c,root))continue;controls++;
   Item item=Measure(c,state);if(item!=null)items.Add(item);
   if(c.Left<0||c.Top<0||c.Right>parent.ClientSize.Width+1||c.Bottom>parent.ClientSize.Height+1) {
    ScrollableControl sc=parent as ScrollableControl;
    if(sc==null||!sc.AutoScroll){outside++;Report.AppendLine("BOUNDS "+state+"/"+Id(c)+" "+Rect(c.Bounds)+" outside parent "+Id(parent)+" client="+parent.ClientSize);}
   }
   Walk(c,root,state);
  }
  for(int i=0;i<items.Count;i++)for(int j=i+1;j<items.Count;j++) {
   Item a=items[i],b=items[j];Rectangle both=Rectangle.Intersect(a.Ink,b.Ink);
   if(both.Width>1&&both.Height>1){overlaps++;Report.AppendLine("OVERLAP "+state+"/"+Id(parent)+" "+a.Id+" "+Rect(a.Ink)+" \""+Text(a.Control)+"\" vs "+b.Id+" "+Rect(b.Ink)+" \""+Text(b.Control)+"\" intersection="+Rect(both));}
  }
 }
 static Item Measure(Control c,string state) {
  if(c.Text.Length==0)return null;Label label=c as Label;Button button=c as Button;CheckBox check=c as CheckBox;
  if(label==null&&button==null&&check==null)return null;
  int width=c.ClientSize.Width-c.Padding.Horizontal,height=c.ClientSize.Height-c.Padding.Vertical;
  Rectangle local=new Rectangle(c.Padding.Left,c.Padding.Top,width,height);Size measured;bool ellipse=false;string kind;
  if(button!=null) {
   FieldInfo glyph=c.GetType().GetField("Glyph",Any);int icon=glyph==null?0:(int)glyph.GetValue(c);int offset=icon!=0?43:12;int sub=icon!=0?53:24;
   width=c.Width-sub;height=c.Height;local=new Rectangle(offset,0,width,height);
   measured=TextRenderer.MeasureText(c.Text,c.Font,new Size(int.MaxValue,int.MaxValue),TextFormatFlags.SingleLine);
   kind="BUTTON glyph="+icon;bool navigation=false;FieldInfo nav=c.GetType().GetField("Navigation",Any);if(nav!=null)navigation=(bool)nav.GetValue(c);
   if(!navigation)local.X+=Math.Max(0,(width-measured.Width)/2);local.Y+=Math.Max(0,(height-measured.Height)/2);
  } else if(check!=null) {
   width-=24;local.X+=24;local.Width=width;
   measured=TextRenderer.MeasureText(c.Text,c.Font,new Size(width,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl);
   kind="CHECKBOX";local.Y+=Math.Max(0,(height-measured.Height)/2);
  } else {
   ellipse=label.AutoEllipsis;kind="LABEL";
   measured=TextRenderer.MeasureText(c.Text,c.Font,new Size(Math.Max(1,width),int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl);
   ContentAlignment align=label.TextAlign;
   if(align==ContentAlignment.TopCenter||align==ContentAlignment.MiddleCenter||align==ContentAlignment.BottomCenter)local.X+=Math.Max(0,(width-measured.Width)/2);
   else if(align==ContentAlignment.TopRight||align==ContentAlignment.MiddleRight||align==ContentAlignment.BottomRight)local.X+=Math.Max(0,width-measured.Width);
   if(align==ContentAlignment.MiddleLeft||align==ContentAlignment.MiddleCenter||align==ContentAlignment.MiddleRight)local.Y+=Math.Max(0,(height-measured.Height)/2);
   else if(align==ContentAlignment.BottomLeft||align==ContentAlignment.BottomCenter||align==ContentAlignment.BottomRight)local.Y+=Math.Max(0,height-measured.Height);
  }
  bool clipped=measured.Width>width+1||measured.Height>height+1;
  if(clipped) {
   if(ellipse)ellipses++;else clips++;
   Report.AppendLine((ellipse?"ELLIPSIS ":"CLIPPED ")+state+"/"+Id(c)+" "+kind+" available="+width+"x"+height+" measured="+measured.Width+"x"+measured.Height+" font="+c.Font.Name+" "+c.Font.SizeInPoints+"pt text=\""+Text(c)+"\"");
  }
  local.Width=Math.Min(Math.Max(0,width),measured.Width);local.Height=Math.Min(Math.Max(0,height),measured.Height);local.Offset(c.Location);
  return new Item{Control=c,Ink=local,Id=Id(c)};
 }
}

