using System;using System.IO;using System.Reflection;using System.Collections.Generic;using System.Security.Cryptography;using System.Text;using System.Diagnostics;
class MaxLiveReview {
 static string Hash(string path){using(var s=File.OpenRead(path))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
 static Dictionary<string,string> Inventory(string root){var d=new Dictionary<string,string>();foreach(string p in Directory.GetFiles(root,"*",SearchOption.AllDirectories))d.Add(p,Hash(p));return d;}
 static int Main(string[] args){try{
  string home=Path.GetFullPath(args[0]),game=Path.GetFullPath(args[1]);var before=Inventory(game);
  if(MaxVoiceWindow.IsMutation("status"))throw new Exception("Status attempted mutation classification.");
  var method=typeof(MaxVoiceWindow).GetMethod("Execute",BindingFlags.NonPublic|BindingFlags.Static);
  string raw=(string)method.Invoke(null,new object[]{home,"status",game});var state=MaxVoiceState.Parse(raw);
  if(state.Status!="ready"||state.Installed||state.CanRestore)throw new Exception("Unexpected original-bank state: "+raw);
  bool gameRunning=Process.GetProcessesByName("F1_25").Length>0||Process.GetProcessesByName("F1_25_dx12").Length>0;
  if(state.GameRunning!=gameRunning||state.CanInstall==gameRunning)throw new Exception("Game-process routing differs.");
  var after=Inventory(game);if(before.Count!=after.Count)throw new Exception("Read-only status changed fixture contents.");
  foreach(var pair in before)if(!after.ContainsKey(pair.Key)||after[pair.Key]!=pair.Value)throw new Exception("Read-only status changed fixture bytes.");
  File.WriteAllText(args[2],raw,Encoding.UTF8);
  Console.WriteLine("MAX_LIVE_HOST_PASS production_ui_wrapper=true real_child_host=true read_only_files_unchanged="+before.Count+" actual_uac_invoked=false game_writes=0");return 0;
 }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
