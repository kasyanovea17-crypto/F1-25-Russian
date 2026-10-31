using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

// Geometry regression, not a screenshot or native-DPI equivalence assertion.
internal static class FooterAudit {
 static readonly BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static readonly StringBuilder Log=new StringBuilder();
 static int checks,failed,states,intersections;
 static Control Field(Form f,string name){var info=f.GetType().GetField(name,Any);if(info==null)throw new InvalidDataException("Missing footer field: "+name);return (Control)info.GetValue(f);}
 static string Hash(string file){using(var hash=SHA256.Create())using(var input=File.OpenRead(file))return BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
 static void Check(bool pass,string label){checks++;if(!pass){failed++;Log.AppendLine("FAIL "+label);}}
 [STAThread] static int Main(string[] args){
  Console.OutputEncoding=new UTF8Encoding(false);
  if(args.Length!=2){Console.Error.WriteLine("Usage: FooterAudit.exe ASSEMBLY_EXE REPORT.txt");return 2;}
  string input=Path.GetFullPath(args[0]),report=Path.GetFullPath(args[1]);
  try{
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   Log.AppendLine("FOOTER CONTROL-BOUNDS AUDIT / no Show, screenshot, capture, game or host invocation");
   Log.AppendLine("Assembly: "+input);Log.AppendLine("SHA256: "+Hash(input));
   Log.AppendLine("Scale 100/125/150% below is proportional WinForms geometry simulation, not OS DPI certification.");
   var assembly=Assembly.LoadFrom(input);var type=assembly.GetType("Launcher",true);
   var constructor=type.GetConstructor(Any,null,new[]{typeof(bool)},null);
   if(constructor==null)throw new InvalidDataException("Missing Launcher(bool)");
   foreach(float scale in new[]{1f,1.25f,1.5f})foreach(string state in new[]{"main","ready","error","installed","interrupted"}){
    using(var form=(Form)constructor.Invoke(new object[]{true})){
     var open=type.GetField("overlayOpen",Any);if(open==null)throw new InvalidDataException("Missing overlay state");
     open.SetValue(form,true);var design=type.GetMethod("DesignState",Any);if(design==null)throw new InvalidDataException("Missing isolated DesignState");
     design.Invoke(form,new object[]{state});
     if(scale!=1f)form.Scale(new SizeF(scale,scale));
     Control footer=Field(form,"footer"),title=Field(form,"statusTitle"),text=Field(form,"statusText"),folder=Field(form,"folderButton"),primary=Field(form,"prepare"),marker=Field(form,"phase");
     string label=state+" @"+(scale*100).ToString("0")+"%";states++;
     Log.AppendLine("STATE "+label+" client="+form.ClientSize+" footer="+footer.Bounds);
     var controls=new[]{title,text,folder,primary,marker};
     foreach(var c in controls){Check(c.Parent==footer,label+" / same footer parent "+c.Text);Check(c.Left>=0&&c.Top>=0&&c.Right<=footer.ClientSize.Width&&c.Bottom<=footer.ClientSize.Height,label+" / control inside footer "+c.Text);}
     for(int i=0;i<controls.Length;i++)for(int j=i+1;j<controls.Length;j++){
      Rectangle overlap=Rectangle.Intersect(controls[i].Bounds,controls[j].Bounds);
      if(overlap.Width>0&&overlap.Height>0){intersections++;Log.AppendLine("INTERSECTION "+label+" "+controls[i].Text+" "+controls[i].Bounds+" VS "+controls[j].Text+" "+controls[j].Bounds+" => "+overlap);}
      Check(overlap.Width<=0||overlap.Height<=0,label+" / nonoverlapping full control bounds "+i+"/"+j);
     }
     int gap=Math.Max(1,(int)Math.Round(16*scale));
     Check(title.Right+gap<=folder.Left,label+" / title-to-folder spacing");
     Check(text.Right+gap<=folder.Left,label+" / status-to-folder spacing");
     Check(folder.Right+gap<=primary.Left,label+" / folder-to-primary spacing");
     Check(primary.Width>=(int)(220*scale),label+" / primary width");
     Check(folder.Width>=(int)(160*scale),label+" / folder target width");
     Check(!form.Visible,label+" / native form never shown");
    }
   }
  }catch(Exception ex){failed++;Log.AppendLine("ERROR "+(ex is TargetInvocationException&&ex.InnerException!=null?ex.InnerException:ex));}
  int result=failed==0?0:1;
  string summary="FOOTER_AUDIT_"+(failed==0?"PASS":"FAIL")+" checks="+checks+" states="+states+" control_intersections="+intersections+" failures="+failed+" capture=false game_writes=false";
  Log.AppendLine(summary);Log.AppendLine("Exit: "+result);
  Directory.CreateDirectory(Path.GetDirectoryName(report));File.WriteAllText(report,Log.ToString(),new UTF8Encoding(false));
  Console.WriteLine(summary);Console.WriteLine(report);return result;
 }
}
