using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
internal static class UpdaterIntegrationTests {
 static void Need(bool ok,string text){if(!ok)throw new InvalidDataException(text);}
 static int Wrapper(string exe,string report){using(var process=Process.Start(new ProcessStartInfo(exe,"--bundle-self-test "+BundleRules.Quote(report)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){Need(process!=null,"No wrapper process");process.WaitForExit();return process.ExitCode;}}
 static byte[] ModifiedLanguage(byte[] original){byte[] b=(byte[])original.Clone();int p=8;while(Encoding.ASCII.GetString(b,p,4)!="LNGB"){string tag=Encoding.ASCII.GetString(b,p,4);p+=8+(tag=="SIDA"?4:0)+checked((int)BundleRules.BE(b,p+4));}for(int i=p+8;i<b.Length;i++)if(b[i]>=(byte)'a'&&b[i]<=(byte)'z'){b[i]=b[i]==(byte)'a'?(byte)'b':(byte)'a';return b;}throw new InvalidDataException("No ASCII fixture byte");}
 static void Save(string path,object value){File.WriteAllText(path,BundleRules.Json.Serialize(value),new UTF8Encoding(false));}
 [STAThread]static int Main(string[] args){
  string exe=Path.GetFullPath(args[0]),root=Path.GetFullPath(args[1]),result=Path.Combine(root,"UPDATER_INTEGRATION.json");try{
   Directory.CreateDirectory(root);string first=Path.Combine(root,"updater-wrapper-before.json");Need(Wrapper(exe,first)==0,"Initial wrapper check failed");var firstInfo=BundleRules.ReadObject(first);string cache=BundleRules.Text(firstInfo,"cache");BundleManifest bundle=Bootstrap.Manifest();Bootstrap.ValidateDirectory(cache,bundle);
   string manifestPath=Path.Combine(cache,"manifest.json"),languagePath=Path.Combine(cache,"payload","language.lng");var original=Updates.Read(manifestPath);byte[] source=File.ReadAllBytes(languagePath),modified=ModifiedLanguage(source);string originalHash=Updates.Hash(languagePath),hash=BundleRules.Hash(modified),version="99.0.0";
   Need(new Version(Updates.Field(original,"text_version"))<new Version(version),"Expected baseline earlier than test version");
   var channel=new Dictionary<string,object>{{"schema",1},{"version",version},{"min_launcher","0.21"},{"dat_original",Updates.Field(original,"dat_original")},{"font_modified",Updates.Field(original,"font_modified")},{"file",version+"/language.lng"},{"sha256",hash},{"bytes",modified.Length},{"records",56134},{"notes","Local integration fixture"}};
   Func<string,int,byte[]> fetch=delegate(string url,int maximum){byte[] data;if(url==TextUpdate.Channel+"stable.json")data=Encoding.UTF8.GetBytes(BundleRules.Json.Serialize(channel));else if(url==TextUpdate.Channel+version+"/language.lng")data=modified;else throw new InvalidDataException("Unexpected local fixture URL");Need(data.Length<=maximum,"Fixture response exceeds request cap");return data;};
   TextUpdate update=Updates.Check(cache,fetch);Need(update.Available,"No fixture update available");string applied=Updates.Apply(cache,update,fetch,"",false,delegate(string h,string g){throw new Exception("Game engine must not be called");});Need(Updates.Hash(languagePath)==hash,"Actual Updater did not install fixture");Bootstrap.ValidateDirectory(cache,bundle);
   string after=Path.Combine(root,"updater-wrapper-after.json");Need(Wrapper(exe,after)==0,"Wrapper rejected valid actual updater state");Need(Updates.Hash(languagePath)==hash,"Wrapper silently overwrote updated language");
   string transaction=Directory.GetDirectories(Path.Combine(cache,".updates")).OrderByDescending(x=>Directory.GetCreationTimeUtc(x)).First();Need(Updates.Hash(Path.Combine(transaction,"new.lng"))==hash,"Unexpected transaction selected");
   Save(Path.Combine(cache,"update-pending.json"),new Dictionary<string,object>{{"transaction",Path.GetFileName(transaction)},{"game",""},{"install",false},{"new_hash",hash}});Bootstrap.ValidateDirectory(cache,bundle);
   string pending=Path.Combine(root,"updater-wrapper-pending.json");Need(Wrapper(exe,pending)==0,"Wrapper rejected pending actual updater transaction");
   string restored=Updates.Recover(cache,delegate(string h,string g){throw new Exception("Game engine must not be called");});Need(Updates.Hash(languagePath)==originalHash&&!File.Exists(Path.Combine(cache,"update-pending.json")),"Actual updater recovery failed");Bootstrap.ValidateDirectory(cache,bundle);
   string recovery=Path.Combine(root,"updater-wrapper-recovered.json");Need(Wrapper(exe,recovery)==0,"Wrapper rejected recovered actual updater state");
   Save(result,new{status="PASS",actualUpdaterApply=true,wrapperRelaunchAfterUpdate=true,updatedLanguagePreserved=true,pendingRecoveryAccepted=true,actualUpdaterRecover=true,wrapperRelaunchAfterRecovery=true,sourceLanguageRestored=true,cache=cache,originalHash=originalHash,updatedHash=hash,applyOutput=applied,recoveryOutput=restored,networkUsed=false,gameModified=false,registryModified=false});
   Console.WriteLine("UPDATER_INTEGRATION_PASS actual_apply=true relaunch_preserves_update=true pending_recovery=true actual_recover=true game_modified=false");return 0;
  }catch(Exception ex){Save(result,new{status="FAIL",error=ex.ToString(),gameModified=false,registryModified=false});Console.Error.WriteLine(ex);return 1;}
 }
}
