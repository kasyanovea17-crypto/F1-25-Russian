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
   if(windows!=null){string native=NativeKind(windows.NativeErrorCode);if(native!="other")return native;}
   if(current is IOException){string native=NativeKind(current.HResult&0xffff);if(native!="other")return native;}
  }
  return "other";
 }
 internal static string NativeKind(int code) {
  if(code==225||code==226)return "protection";
  if(code==1223)return "cancelled";
  if(code==740)return "elevation";
  if(code==577)return "signature";
  if(code==5)return "access";
  if(code==1260)return "policy";
  if(code==32||code==33)return "locked";
  return "other";
 }
 internal static string Message(Exception error) {
  string kind=Kind(error),hint="";
  if(kind=="access")hint="Windows отказала в доступе к файлу или запуску программы. Закройте другие копии лаунчера и проверьте «Журнал защиты» Windows или вашего антивируса. Если защищена папка игры (например, Program Files), повторите установку с правами администратора. Отключать защиту не требуется.";
  else if(kind=="locked")hint="Файл занят другой программой. Закройте другие окна лаунчера и игру, затем повторите действие.";
  else if(kind=="policy")hint="Запуск программы запрещён политикой Windows. Сведения о причине доступны в журнале защиты или у администратора компьютера.";
  else if(kind=="protection")hint="Windows сообщила об обнаружении угрозы и заблокировала файл. Операция остановлена. Откройте «Безопасность Windows → Журнал защиты»: сохраните название обнаружения и путь к файлу для проверки разработчиком. Отключать защиту или добавлять исключение не требуется.";
  else if(kind=="cancelled")hint="Запрос подтверждения Windows отменён. Действие с правами администратора не запущено.";
  else if(kind=="elevation")hint="Для этого действия Windows требует повышение прав. Повторите действие через кнопку установки в лаунчере и подтвердите запрос Windows.";
  else if(kind=="signature")hint="Windows отклонила цифровую подпись запускаемого файла. Операция остановлена; нужна проверка целостности комплекта и сведений в журнале защиты.";
  string detail=Detail(error==null?null:error.Message);
  return hint.Length==0?detail:hint+"\n\nПодробности: "+detail;
 }
}
