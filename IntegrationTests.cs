using System;using System.IO;using System.Text;using System.Diagnostics;
class IntegrationTests {
 static string Engine(string home,string game,string action){
  var s=new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(home,"Engine.ps1")+"\" -Action "+action+" -GamePath \""+game+"\" -TestMode"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
  using(var p=Process.Start(s)){var err=p.StandardError.ReadToEndAsync();string result=p.StandardOutput.ReadToEnd();p.WaitForExit();result+=err.Result;if(p.ExitCode!=0)throw new Exception(result);return result;}
 }
 static int Main(string[] a){try{
  if(a[0]=="--live"){var u=Updates.Check(a[1],Updates.Download);var bytes=Updates.Download(u.Url,33554432);File.WriteAllBytes(a[2],bytes);if(Updates.Hash(a[2])!=u.Hash||bytes.Length!=u.Bytes)throw new Exception("remote hash");Updates.ValidateLanguage(Path.Combine(a[1],"payload","language.lng"),a[2]);Console.WriteLine("GITHUB_PASS version="+u.Version+" bytes="+u.Bytes+" sha256="+u.Hash+" current="+(!u.Available));return 0;}
  string h=Path.GetFullPath(a[0]),g=Path.Combine(h,"test-game");var m=Updates.Read(Path.Combine(h,"manifest.json"));Console.WriteLine(Engine(h,g,"prepare").Trim());Console.WriteLine(Engine(h,g,"install").Trim());
  Func<string,int,byte[]> fetch=(url,max)=>File.ReadAllBytes(Path.Combine(h,url.EndsWith("stable.json")?"channel.json":"next.lng"));
  var u2=Updates.Check(h,fetch);Console.WriteLine(Updates.Apply(h,u2,fetch,g,true,(root,game)=>Engine(root,game,"install")).Trim());
  foreach(string n in new[]{"language_jap.lng","language_eng.lng"})if(Updates.Hash(Path.Combine(g,"localisation","2025_russian",n))!=u2.Hash)throw new Exception("installed hash");
  Console.WriteLine(Engine(h,g,"restore").Trim());if(Updates.Hash(Path.Combine(g,"game.dat"))!=Updates.Field(m,"dat_original"))throw new Exception("restore dat");if(File.Exists(Path.Combine(g,"localisation","2025_russian","language_jap.lng")))throw new Exception("restore alias");
  Console.WriteLine("INTEGRATION_PASS original_install_update_restore=true real_game_writes=false");return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}
}
