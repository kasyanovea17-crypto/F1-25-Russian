using System;
using System.IO;
using System.Text;

// Selection is independent of the English/Japanese voice preference.
static class TranslationVariants {
 public const string Russian="russian", OriginalNames="original_names";
 public static bool Valid(string id){return id==Russian||id==OriginalNames;}
 public static string Load(string file){
  try{string value=File.ReadAllText(file,Encoding.UTF8).Trim();return Valid(value)?value:Russian;}
  catch(IOException){return Russian;}catch(UnauthorizedAccessException){return Russian;}
 }
 public static void Save(string file,string id){
  if(!Valid(id))throw new ArgumentException("Unknown translation variant");
  Directory.CreateDirectory(Path.GetDirectoryName(file));string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temp,id,new UTF8Encoding(false));if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}
  finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
