using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

internal static class DeepStateAudit {
 static readonly BindingFlags Any=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static readonly StringBuilder Log=new StringBuilder();
 static Type launcher;static ConstructorInfo constructor;static int checks,findings;
 static readonly MethodInfo GetState=typeof(Control).GetMethod("GetState",BindingFlags.Instance|BindingFlags.NonPublic);
 static readonly MethodInfo GetStyle=typeof(Control).GetMethod("GetStyle",BindingFlags.Instance|BindingFlags.NonPublic);
 static object Field(object o,string name){return o.GetType().GetField(name,Any).GetValue(o);}
 static void Set(object o,string name,object value){o.GetType().GetField(name,Any).SetValue(o,value);}
 static object Invoke(object o,string name,params object[] args){return o.GetType().GetMethod(name,Any).Invoke(o,args);}
 static object Property(object o,string name){return o.GetType().GetProperty(name,Any).GetValue(o,null);}
 static Form Form(bool testing){return (Form)constructor.Invoke(new object[]{testing});}
 static void Check(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
 static void Finding(string name,string details){findings++;Log.AppendLine("FINDING "+name+" / "+details);}
 static bool OwnVisible(Control c){return (bool)GetState.Invoke(c,new object[]{2});}
 static bool Selectable(Control c){return (bool)GetStyle.Invoke(c,new object[]{ControlStyles.Selectable});}
 static void PotentialTabControls(Control parent,Control shade,bool active,List<Control> found){
  foreach(Control child in parent.Controls){if(child==shade)continue;bool next=active&&child.Enabled&&OwnVisible(child);if(next&&child.TabStop&&Selectable(child))found.Add(child);PotentialTabControls(child,shade,next,found);}
 }
 static void Page(Form form,string id){Set(form,"overlayOpen",true);Invoke(form,"ShowPage",id);}
 static Panel Panel(Form form,string id){var pages=(IDictionary)Field(form,"pages");return (Panel)pages[id];}
 static string Hash(string file){using(var hash=SHA256.Create())using(var stream=File.OpenRead(file))return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
 static bool ContainsMessage(Control parent,string a,string b){foreach(Control c in parent.Controls){if(c is Label&&(c.Text.Contains(a)||c.Text.Contains(b)))return true;if(ContainsMessage(c,a,b))return true;}return false;}
 static Button Button(Control parent,string text){foreach(Control child in parent.Controls){if(child is Button&&child.Text==text)return (Button)child;var nested=Button(child,text);if(nested!=null)return nested;}return null;}
 static Label InlineError(Form form){var field=launcher.GetField("settingsPathError",Any);return field==null?null:(Label)field.GetValue(form);}
 static void SettingsCase(string name,string value,bool valid){
  using(var form=Form(true)){
   Page(form,"settings");((TextBox)Field(form,"path")).Text=value;Invoke(form,"CompleteSettings");
   var error=InlineError(form);string current=(string)Field(form,"currentPage");
   Check(valid?current=="install":current=="settings",name+" / completion route");
   Check(error!=null&&(valid?error.Text.Length==0:error.Text.Length>0),name+" / inline error state");
   Check(!(bool)Field(form,"prepared")&&Field(form,"detectedProfile")==null&&!(bool)Field(form,"installed")&&!((CheckBox)Field(form,"ready")).Checked&&!((Button)Field(form,"install")).Enabled,name+" / settings cannot grant install readiness");
   Check(!form.Visible,name+" / settings audit never shows windows");
   Log.AppendLine("SETTINGS_CASE "+name+" page="+current+" inline="+error.Text+" prepared=false installed=false ready=false");
  }
 }
 [STAThread] static int Main(string[] args){
  Console.OutputEncoding=new UTF8Encoding(false);if(args.Length!=2){Console.Error.WriteLine("Usage: DeepStateAudit.exe ASSEMBLY REPORT");return 2;}
  string report=Path.GetFullPath(args[1]);int exit=0;
  try{
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   string assemblyPath=Path.GetFullPath(args[0]);var assembly=Assembly.LoadFrom(assemblyPath);launcher=assembly.GetType("Launcher",true);constructor=launcher.GetConstructor(Any,null,new[]{typeof(bool)},null);
   Log.AppendLine("DEEP STATE AUDIT / hidden forms only; no Show, screenshot, game, host, real-preference write or registry write");
   Log.AppendLine("Assembly="+assemblyPath+" SHA256="+Hash(assemblyPath));
   Log.AppendLine("The fixture assembly changes only the user-preferences root expression; real constructor order and event handlers remain intact.");
   string voicePreference=null;
   using(var form=Form(false)){
    voicePreference=(string)Field(form,"voicePreferencePath");
    string selected=(string)Property(form,"SelectedVoice"),actual=(string)Invoke(form,"SteamVoice");
    Check(actual=="japanese","Japanese manifest fixture is used");Check(!form.Visible,"constructor remains hidden");
    Check(!(bool)Field(form,"prepared")&&Field(form,"detectedProfile")==null&&!(bool)Field(form,"installed"),"startup never grants preparation");
    Log.AppendLine("STARTUP selectedVoice="+selected+" SteamUserConfig="+actual+" title="+((Label)Field(form,"statusTitle")).Text);
    if(selected!=actual)Finding("P2_STARTUP_VOICE", "no saved voice preference; Japanese Steam manifest exists, but startup selects "+selected+"; BuildVoice preceded LoadGameFolder");
   }
   Check(voicePreference!=null&&!File.Exists(voicePreference),"voice preference fixture initially absent");
   File.WriteAllText(voicePreference,"english",new UTF8Encoding(false));string voiceHash=Hash(voicePreference);
   using(var form=Form(false)){
    Check((string)Property(form,"SelectedVoice")=="english","explicit English preference wins over Japanese Steam configuration");
    Check((string)Invoke(form,"SteamVoice")=="japanese","explicit preference test still reads Japanese fixture");
    Check(Hash(voicePreference)==voiceHash,"automatic voice fallback does not rewrite explicit preference");
    Check(!(bool)Field(form,"prepared")&&!((CheckBox)Field(form,"ready")).Checked,"voice initialization never prepares install");
    Log.AppendLine("VOICE_PRIORITY explicit=english actualSteam=japanese preference_unchanged=true");
   }
   foreach(string page in new[]{"settings","voice","updates","restore","help","log","about","error","success","confirm"})using(var form=Form(true)){
    Page(form,page);Check((string)Field(form,"currentPage")==page,"requested panel state");
    var exposed=new List<Control>();PotentialTabControls(form,(Control)Field(form,"shade"),true,exposed);
    Log.Append("MODAL "+page+" backgroundTabTargets="+exposed.Count+" / ");foreach(var c in exposed)Log.Append(c.AccessibleName+"["+c.Text+"]; ");Log.AppendLine();
    if(exposed.Count>0)Finding("P2_MODAL_TAB_"+page,"opaque shade leaves "+exposed.Count+" selectable enabled visible background controls in the root Tab chain");
    if(page=="confirm"||page=="restore")Check(form.AcceptButton==(IButtonControl)Field(form,page=="confirm"?"installCancel":"restoreCancel"),page+" / confirmation defaults to cancel");
    Invoke(form,"OnKeyDown",new KeyEventArgs(Keys.Escape));Check((string)Field(form,"currentPage")=="install"&&!(bool)Field(form,"overlayOpen"),page+" / Esc closes ordinary modal");
    var restored=new List<Control>();PotentialTabControls(form,(Control)Field(form,"shade"),true,restored);Check(restored.Count>0,page+" / main navigation restored after Esc");
    Check(!form.Visible,"modal audit never shows form");
   }
   using(var form=Form(true)){
    Page(form,"settings");((TextBox)Field(form,"path")).Text=@"C:\bad|path";
    var done=Button(Panel(form,"settings"),"Готово");Check(done!=null,"settings Done exists");
    typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(done,new object[]{EventArgs.Empty});
    string title=((Label)Field(form,"statusTitle")).Text,current=(string)Field(form,"currentPage");
    string reason=((Label)Field(form,"dialogStatus")).Text;
    var inline=InlineError(form);bool inlineVisible=inline!=null&&inline.Text.Length>0&&OwnVisible(inline)&&current=="settings";
    bool shown=current=="error"||inlineVisible||ContainsMessage(Panel(form,current),title,reason);
    Check(title=="Проверьте папку игры"||current=="error"||inlineVisible,"invalid path reached error handling");
    Log.AppendLine("SETTINGS_ERROR currentPage="+current+" backgroundTitle="+title+" visibleErrorFeedback="+shown+" error="+reason);
    if(!shown)Finding("P2_SETTINGS_ERROR_HIDDEN","Done rejects an invalid path, but SetStatus only changes the covered footer and hidden error panel; settings shows no error feedback");
   }
   if(launcher.GetMethod("CompleteSettings",Any)!=null){
    string fixture=Path.Combine(Path.GetDirectoryName(report),"settings-fixtures-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
    string missingMarkers=Path.Combine(fixture,"missing-markers");Directory.CreateDirectory(missingMarkers);
    string valid=Path.Combine(fixture,"valid-game");Directory.CreateDirectory(valid);File.WriteAllText(Path.Combine(valid,"F1_25.exe"),"Not a PE image, fixture marker only");File.WriteAllText(Path.Combine(valid,"game.dat"),"Not game resources, fixture marker only");
    SettingsCase("empty","",false);SettingsCase("invalid",@"C:\bad|path",false);SettingsCase("missing",Path.Combine(fixture,"missing"),false);SettingsCase("missing-markers",missingMarkers,false);SettingsCase("valid-markers",valid,true);
    using(var form=Form(true)){Page(form,"settings");var input=(TextBox)Field(form,"path");input.Text="";Invoke(form,"CompleteSettings");Check(InlineError(form).Text.Length>0,"error shown before editing");input.Text=valid;Check(InlineError(form).Text.Length==0,"editing path clears stale inline error");Set(form,"busy",true);string before=(string)Field(form,"currentPage");Invoke(form,"CompleteSettings");Check((string)Field(form,"currentPage")==before,"busy completion cannot switch page or save preferences");}
   }
   using(var form=Form(true)){
    Set(form,"busy",true);Page(form,"progress");Invoke(form,"RefreshControls");
    Check(!((Button)Field(form,"closeButton")).Enabled&&!((Button)Field(form,"prepare")).Enabled,"busy disables main closing and preparation");
    var closing=new FormClosingEventArgs(CloseReason.UserClosing,false);Invoke(form,"OnFormClosing",closing);Check(closing.Cancel,"busy close veto");
    Invoke(form,"OnKeyDown",new KeyEventArgs(Keys.Escape));Check((string)Field(form,"currentPage")=="progress","busy Esc does not close operation");
    Set(form,"busy",false);Invoke(form,"ApplyResult","prepare",0,"PROFILE game=1.26 records=56134 text=0.15.5 channel=stable",null,null);((CheckBox)Field(form,"ready")).Checked=true;
    var update=Activator.CreateInstance(assembly.GetType("TextUpdate",true));Set(update,"Available",true);Set(form,"candidate",update);Invoke(form,"RefreshControls");
    Check(((Button)Field(form,"checkUpdate")).Enabled&&((Button)Field(form,"applyUpdate")).Enabled,"stable prepared state makes update test meaningful");
    Set(form,"previewPending",true);Invoke(form,"RefreshControls");
    Check(!((Button)Field(form,"install")).Enabled&&!((Button)Field(form,"restore")).Enabled&&!((Button)Field(form,"applyUpdate")).Enabled&&((Button)Field(form,"recoverUpdate")).Enabled,"interrupted update gates mutations and exposes recovery");
   }
  }catch(Exception ex){exit=2;Log.AppendLine("ERROR "+(ex is TargetInvocationException&&ex.InnerException!=null?ex.InnerException:ex));}
  if(exit==0&&findings>0)exit=1;
  string summary="DEEP_STATE_AUDIT checks="+checks+" findings="+findings+" windows_shown=false game_writes=0 registry_writes=0 host_calls=0 exit="+exit;
  Log.AppendLine(summary);Directory.CreateDirectory(Path.GetDirectoryName(report));File.WriteAllText(report,Log.ToString(),new UTF8Encoding(false));Console.WriteLine(summary);Console.WriteLine(report);return exit;
 }
}
