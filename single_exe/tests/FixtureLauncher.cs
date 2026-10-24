using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
internal static class FixtureLauncher {
 [STAThread]static int Main(string[] args){
  if(args.Length>=2&&args[0]=="--self-test"){
   File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{status="PASS",args=args,home=AppDomain.CurrentDomain.BaseDirectory,wrapped=Environment.GetEnvironmentVariable("F1RU_SINGLE_EXE"),version=Environment.GetEnvironmentVariable("F1RU_BUNDLE_VERSION"),gameModified=false}),new UTF8Encoding(false));return 0;
  }
  if(args.Length==2&&args[0]=="--exit")return Int32.Parse(args[1]);return 0;
 }
}
