using System;
using System.ComponentModel;
using System.IO;
using System.Text;

// Classify observed Windows errors; do not infer quarantine or change protection.
internal static class StartupDiagnostics {
 internal static string Detail(string value) {
  if(String.IsNullOrWhiteSpace(value))return "Дополнительных сведений нет.";
  var text=new StringBuilder();
  foreach(char c in value){if(text.Length>=600)break;if(c=='\r'||c=='\n'||c=='\t')text.Append(' ');else if(!Char.IsControl(c))text.Append(c);}
  return text.ToString().Trim();
 }
 internal static string Kind(Exception error) {
  Exception current=error;
  for(int depth=0;current!=null&&depth<8;depth++,current=current.InnerException){
   if(current is UnauthorizedAccessException)return "access";
   var windows=current as Win32Exception;
   if(windows!=null){if(windows.NativeErrorCode==5)return "access";if(windows.NativeErrorCode==1260)return "policy";if(windows.NativeErrorCode==32||windows.NativeErrorCode==33)return "locked";}
   if(current is IOException){int code=current.HResult&0xffff;if(code==5)return "access";if(code==32||code==33)return "locked";}
  }
  return "other";
 }
 internal static string Message(Exception error) {
  string kind=Kind(error),hint="";
  if(kind=="access")hint="Windows отказала в доступе к файлу или запуску программы. Закройте другие копии лаунчера и проверьте «Журнал защиты» Windows или вашего антивируса. Если защищена папка игры (например, Program Files), повторите установку с правами администратора. Отключать защиту не требуется.";
  else if(kind=="locked")hint="Файл занят другой программой. Закройте другие окна лаунчера и игру, затем повторите действие.";
  else if(kind=="policy")hint="Запуск программы запрещён политикой Windows. Сведения о причине доступны в журнале защиты или у администратора компьютера.";
  string detail=Detail(error==null?null:error.Message);
  return hint.Length==0?detail:hint+"\n\nПодробности: "+detail;
 }
}
