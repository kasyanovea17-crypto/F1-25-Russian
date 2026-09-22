using System;
using System.IO;
using System.Text;
public static class AddonTests {
 static int n;
 static void Check(bool ok,string what){if(!ok)throw new Exception(what);n++;}
 static void Reject(Action action,string what){bool rejected=false;try{action();}catch(IOException){rejected=true;}Check(rejected,what);}
 public static int Main(string[] args){
  string root=Path.Combine(Path.GetTempPath(),"F1RU-addon-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try {
   string source=Path.GetFullPath(args[0]),expected=RussianAddon.Hash(source),target=Path.Combine(root,RussianAddon.FileName);
   string other=Path.Combine(root,"RaycerRay.PluginSdk.dll");File.WriteAllText(other,"original plugin fixture");string original=RussianAddon.Hash(other);
   Reject(()=>RussianAddon.InstallFiles(root,source,new string('0',64)),"corrupt source refused");
   Check(!File.Exists(target),"no write on bad hash");
   RussianAddon.InstallFiles(root,source,expected);Check(RussianAddon.Hash(target)==expected,"new install");
   Check(RussianAddon.InstallFiles(root,source,expected).Contains("уже установлен"),"idempotent install");
   File.WriteAllText(target,"external change");Reject(()=>RussianAddon.RemoveFiles(root),"external edit guard");
   File.Copy(source,target,true);RussianAddon.RemoveFiles(root);Check(!File.Exists(target),"fresh install removed");
   Check(RussianAddon.Hash(other)==original,"main plugin unchanged");
   File.WriteAllText(target,"previous Russian module");string before=RussianAddon.Hash(target);
   RussianAddon.InstallFiles(root,source,expected);Check(RussianAddon.Hash(target)==expected,"upgrade installed");
   RussianAddon.RemoveFiles(root);Check(RussianAddon.Hash(target)==before,"previous module restored exactly");
   Reject(()=>RussianAddon.RemoveFiles(root),"unmanaged module preserved");Check(RussianAddon.Hash(target)==before,"unmanaged bytes intact");
   File.WriteAllText(Path.Combine(root,"Karsvein-RussianUI.previous.dll"),"existing backup");
   Reject(()=>RussianAddon.InstallFiles(root,source,expected),"existing backup preserved");
   Check(RussianAddon.Hash(target)==before,"backup conflict no mutation");
   Console.WriteLine("ADDON_TEST_PASS checks="+n+" install=true rollback=true main_plugin_unchanged=true real_simhub_writes=false");return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
  finally{Directory.Delete(root,true);}
 }
}
