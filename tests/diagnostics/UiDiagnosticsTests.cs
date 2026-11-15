using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

// Reflection exercises the actual compiled launcher. Every filesystem input is
// an owned fixture, and forms are never shown. Rendering is DrawToBitmap only.
internal static class UiDiagnosticsTests {
 static readonly BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static readonly StringBuilder Log=new StringBuilder();
 static int checks,failed;
 static void Check(bool pass,string label){checks++;Log.AppendLine((pass?"PASS ":"FAIL ")+label);if(!pass)failed++;}
 static object Field(object target,string name){var f=target.GetType().GetField(name,Any);if(f==null)throw new InvalidDataException("Missing field "+name);return f.GetValue(target);}
 static void Set(object target,string name,object value){var f=target.GetType().GetField(name,Any);if(f==null)throw new InvalidDataException("Missing field "+name);f.SetValue(target,value);}
 static object Call(object target,string method,params object[] args){Type type=target as Type??target.GetType();var m=type.GetMethod(method,Any);if(m==null)throw new InvalidDataException("Missing method "+method);try{return m.Invoke(target is Type?null:target,args);}catch(TargetInvocationException ex){throw ex.InnerException??ex;}}
 static string Hash(string path){using(var h=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
 static Dictionary<string,string> Files(string root){var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);if(Directory.Exists(root))foreach(string file in Directory.GetFiles(root,"*",SearchOption.AllDirectories))result.Add(file.Substring(root.Length),Hash(file));return result;}
 static bool Equal(Dictionary<string,string> a,Dictionary<string,string> b){if(a.Count!=b.Count)return false;foreach(var pair in a){string value;if(!b.TryGetValue(pair.Key,out value)||value!=pair.Value)return false;}return true;}
 static void Render(Control page,string path){
  // A hidden form makes child Visible false, which would produce an empty card.
  // Temporarily detach the card (not a top-level window), render, then restore.
  Control parent=page.Parent;int index=parent.Controls.GetChildIndex(page);parent.Controls.Remove(page);
  try{page.Visible=true;page.CreateControl();using(var bitmap=new Bitmap(page.Width,page.Height)){page.DrawToBitmap(bitmap,page.ClientRectangle);bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}Check(new FileInfo(path).Length>5000,"rendered content "+Path.GetFileName(path));}
  finally{parent.Controls.Add(page);parent.Controls.SetChildIndex(page,index);}
 }
 static void ButtonsInside(Control parent){var items=new List<Button>();foreach(Control child in parent.Controls)if(child is Button)items.Add((Button)child);foreach(var child in items)Check(parent.ClientRectangle.Contains(child.Bounds),parent.Name+" button inside: "+child.Text);for(int i=0;i<items.Count;i++)for(int j=i+1;j<items.Count;j++){Rectangle overlap=Rectangle.Intersect(items[i].Bounds,items[j].Bounds);Check(overlap.Width<=0||overlap.Height<=0,parent.Name+" buttons separated "+i+"/"+j);}}
 static string ReadZip(ZipArchive archive,string name){var e=archive.GetEntry(name);if(e==null)throw new InvalidDataException("Missing ZIP member: "+name);using(var reader=new StreamReader(e.Open(),Encoding.UTF8))return reader.ReadToEnd();}
 [STAThread] static int Main(string[] args){
  Console.OutputEncoding=new UTF8Encoding(false);
  if(args.Length!=3){Console.Error.WriteLine("Usage: UiDiagnosticsTests.exe LAUNCHER_EXE NEW_WORK_DIRECTORY REPORT.txt");return 2;}
  string assemblyPath=Path.GetFullPath(args[0]),work=Path.GetFullPath(args[1]),report=Path.GetFullPath(args[2]);
  try{
   if(Directory.Exists(work)||File.Exists(work))throw new IOException("Preserve previous fixtures; choose a new work directory.");Directory.CreateDirectory(work);
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   var assembly=Assembly.LoadFrom(assemblyPath);var launcher=assembly.GetType("Launcher",true);var core=assembly.GetType("SupportDiagnostics",false);
   Check(core!=null,"compiled local report core is present");Check(launcher.GetField("saveReport",Any)!=null,"journal report button is compiled");Check(launcher.GetField("errorReport",Any)!=null,"error report button is compiled");
   if(core==null||failed>0)throw new InvalidDataException("Diagnostic feature absent from baseline launcher.");
   string store=Path.Combine(work,"journal");Call(core,"InitializeForTests",Path.GetDirectoryName(assemblyPath),store);var initial=Files(store);
   var constructor=launcher.GetConstructor(Any,null,new[]{typeof(bool)},null);Check(constructor!=null,"preview constructor exists");
   using(var form=(Form)constructor.Invoke(new object[]{true})){
    var save=(Button)Field(form,"saveReport");var error=(Button)Field(form,"errorReport");var pages=(Dictionary<string,Panel>)Field(form,"pages");
    Check(save.Text=="Сохранить отчёт"&&error.Text=="Сохранить отчёт","report labels are discoverable in Russian");Check(save.Parent==pages["log"],"export is in journal");Check(error.Parent==pages["error"],"export is in failure dialog");
    Call(form,"SaveSupportReport");Check(Equal(initial,Files(store)),"preview export does not open dialog or journal host data");
    Set(form,"testing",false);Set(form,"busy",true);Call(form,"RefreshControls");Check(!save.Enabled&&!error.Enabled,"operation blocks both export controls");Call(form,"SaveSupportReport");Check(Equal(initial,Files(store)),"busy export is inert");
    Set(form,"busy",false);Set(form,"reportBusy",true);Call(form,"RefreshControls");Check(!save.Enabled&&!error.Enabled,"duplicate report generation is blocked");Call(form,"SaveSupportReport");Check(Equal(initial,Files(store)),"report-in-progress export is inert");
    Set(form,"testing",true);Set(form,"reportBusy",false);Call(form,"RefreshControls");Check(save.Enabled&&error.Enabled,"report controls recover when idle");
    Set(form,"lastLog","ОШИБКА: Доступ к папке игры отклонён.\r\nКомпонент: озвучка инженера\r\nЭтап: проверка комплекта\r\nКод Windows: 5");
    Set(form,"overlayOpen",true);Call(form,"ShowPage","log");ButtonsInside(pages["log"]);Render(pages["log"],Path.Combine(work,"JOURNAL.png"));
    ((Label)Field(form,"dialogStatus")).Text="Доступ к папке игры отклонён.\nТочный код и этап операции сохранены в отчёте.";Call(form,"ShowPage","error");ButtonsInside(pages["error"]);Render(pages["error"],Path.Combine(work,"ERROR.png"));
    Check(!form.Visible,"native launcher window never shown");
   }
   Check(Equal(initial,Files(store)),"all preview UI paths leave persisted journal unchanged");
   string game=Path.Combine(work,"steamapps","common","F1 25");Directory.CreateDirectory(game);File.WriteAllText(Path.Combine(game,"F1_25.exe"),"Fixture marker, not an executable.");File.WriteAllText(Path.Combine(game,"game.dat"),"Fixture marker, not game data.");File.WriteAllText(Path.Combine(work,"steamapps","appmanifest_3059520.acf"),"\"AppState\" { \"appid\" \"3059520\" \"buildid\" \"25251167\" \"installdir\" \"F1 25\" \"LastOwner\" \"76561199999999999\" \"UserConfig\" { \"language\" \"english\" } }");var gameBefore=Files(game);
   string op=(string)Call(core,"Begin","engineer","install",game);Call(core,"Stage",op,"fixture.validate","Проверяем тестовый комплект.");Call(core,"End",op,5,"Access denied at C:\\Users\\DiagnosticPrivateUser\\Downloads\\Launcher.exe; operator@example.invalid",new Win32Exception(5,"Test access denied"));
   string destination=Path.Combine(work,"LOCAL_REPORT.zip");string result=(string)Call(core,"Export",destination,Path.GetDirectoryName(assemblyPath),game,"Local test only");Check(File.Exists(destination),"local ZIP export succeeds");Check(Path.GetFullPath(result)==destination,"export returns selected path");
   string all="";using(var archive=ZipFile.OpenRead(destination)){Check(archive.Entries.Count==3,"ZIP contains only three diagnostic documents");all=ReadZip(archive,"SUMMARY.txt")+ReadZip(archive,"REPORT.json")+ReadZip(archive,"EVENTS.json");Check(all.Contains(op),"operation identifier is exported");Check(all.Contains("fixture.validate"),"failure stage is exported");Check(all.Contains("Win32Exception"),"native exception type is exported");Check(!all.Contains("DiagnosticPrivateUser"),"foreign Windows username is removed");Check(!all.Contains("operator@example.invalid"),"email is removed");Check(!all.Contains("76561199999999999"),"Steam owner identifier is excluded");}
   string exportHash=Hash(destination);bool refused=false;try{Call(core,"Export",destination,Path.GetDirectoryName(assemblyPath),game,"repeat");}catch(IOException){refused=true;}Check(refused,"existing report overwrite is rejected");Check(Hash(destination)==exportHash,"existing report is preserved exactly");
   string missing=Path.Combine(work,"MISSING_GAME_REPORT.zip");Call(core,"Export",missing,Path.GetDirectoryName(assemblyPath),Path.Combine(work,"absent-game"),"missing fixture");Check(File.Exists(missing),"missing game does not prevent support report");Check(Equal(gameBefore,Files(game)),"game fixture files and inventory remain unchanged");
  }catch(Exception ex){failed++;Log.AppendLine("ERROR "+ex);}
  string summary="DIAGNOSTIC_UI_"+(failed==0?"PASS":"FAIL")+" checks="+checks+" failures="+failed+" game_writes=false registry_writes=false native_window_shown=false";Log.AppendLine(summary);Directory.CreateDirectory(Path.GetDirectoryName(report));File.WriteAllText(report,Log.ToString(),new UTF8Encoding(false));Console.WriteLine(summary);Console.WriteLine(report);return failed==0?0:1;
 }
}
