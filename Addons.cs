using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// Only installs the separate Russian UI module. Never replaces RaycerRay itself.
public static class RussianAddon {
 public const string FileName="RaycerRayRussian.Plugin.dll";
 const string StateName="Karsvein-RussianUI-state.json";
 public static string Hash(string file){using(var s=File.OpenRead(file))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
 public class Receipt {public string Hash,PreviousHash;public bool HadPrevious;}
 static void Guard(string root){
  if(Process.GetProcessesByName("SimHubWPF").Length>0)throw new IOException("Полностью закройте SimHub, включая значок в области уведомлений.");
  if(!File.Exists(Path.Combine(root,"SimHubWPF.exe"))||!File.Exists(Path.Combine(root,"RaycerRay.PluginSdk.dll")))throw new IOException("Выберите папку SimHub с установленным RaycerRay. Это не папка игры.");
 }
 static string Backup(string root){return Path.Combine(root,"Karsvein-RussianUI.previous.dll");}
 public static string Install(string root,string source,string expected){Guard(root);return InstallFiles(root,source,expected);}
 // Core is public for filesystem transaction tests against isolated fixtures.
 public static string InstallFiles(string root,string source,string expected){
  if(Hash(source)!=expected)throw new IOException("Контрольная сумма модуля перевода не совпадает. Распакуйте пакет заново.");
  string target=Path.Combine(root,FileName),state=Path.Combine(root,StateName),backup=Backup(root);
  var json=new JavaScriptSerializer();
  if(File.Exists(state)){
   var prior=json.Deserialize<Receipt>(File.ReadAllText(state));
   if(prior.Hash==expected&&File.Exists(target)&&Hash(target)==expected)return "Русский модуль уже установлен. Включите его в SimHub → Add/remove features.";
   throw new IOException("Сначала удалите установленный через лаунчер модуль перевода.");
  }
  if(File.Exists(backup))throw new IOException("Найдена предыдущая резервная копия. Сохраните её отдельно перед установкой.");
  bool existed=File.Exists(target);var receipt=new Receipt{Hash=expected,HadPrevious=existed,PreviousHash=existed?Hash(target):null};
  string temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
  if(existed)File.Copy(target,backup,false);
  try {
   // Persist receipt before replacement so interrupted installs remain recoverable.
   File.WriteAllText(state,json.Serialize(receipt),Encoding.UTF8);
   File.Copy(source,temp,false);
   if(Hash(temp)!=expected)throw new IOException("Ошибка проверки скопированного модуля.");
   if(existed)File.Replace(temp,target,null);else File.Move(temp,target);
  }catch {
   if(existed&&File.Exists(backup))File.Copy(backup,target,true);
   else if(!existed&&File.Exists(target)&&Hash(target)==expected)File.Delete(target);
   if(File.Exists(state))File.Delete(state);
   if(File.Exists(backup))File.Delete(backup);
   throw;
  }finally{if(File.Exists(temp))File.Delete(temp);}
  return "Модуль установлен. Запустите SimHub → Add/remove features и включите «RaycerRay — русский интерфейс».";
 }
 public static string Remove(string root){Guard(root);return RemoveFiles(root);}
 public static string RemoveFiles(string root){
  string state=Path.Combine(root,StateName),target=Path.Combine(root,FileName),backup=Backup(root);
  if(!File.Exists(state))throw new IOException("Нет записи об установке через этот лаунчер. Чужие файлы не изменены.");
  var receipt=new JavaScriptSerializer().Deserialize<Receipt>(File.ReadAllText(state));
  if(File.Exists(target)&&Hash(target)!=receipt.Hash&&(!receipt.HadPrevious||Hash(target)!=receipt.PreviousHash))throw new IOException("Модуль изменён после установки. Автоматическое удаление остановлено.");
  if(receipt.HadPrevious){
   if(!File.Exists(backup)||Hash(backup)!=receipt.PreviousHash)throw new IOException("Резервная копия отсутствует или изменена.");
   File.Copy(backup,target,true);
  }else if(File.Exists(target))File.Delete(target);
  File.Delete(state);if(File.Exists(backup))File.Delete(backup);
  return "Изменения модуля перевода отменены. RaycerRay, оверлеи и настройки SimHub сохранены.";
 }
}

sealed partial class Launcher {
 TextBox simHubPath;Button addonInstall,addonRemove,addonGuide,addonBrowse;
 void BuildAddons(){
  var p=Page("addons","Дополнения");var c=new Card{Bounds=new Rectangle(0,51,400,369)};p.Controls.Add(c);
  LabelAt(c,"RaycerRay • русский интерфейс",18,16,366,25,12,FontStyle.Bold,Color.Black);
  LabelAt(c,"Скорость, позиции, шины и телеметрия поверх\nигры. Это не замена встроенного HUD.\nТребуются SimHub, RaycerRay и его оверлеи.",18,48,366,59,9.5f,FontStyle.Regular,muted);
  LabelAt(c,"Папка SimHub (не F1 25)",18,113,366,21,9,FontStyle.Bold,Color.Black);
  simHubPath=new TextBox{Bounds=new Rectangle(18,140,318,25),Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"SimHub"),AccessibleName="Папка SimHub"};c.Controls.Add(simHubPath);
  addonBrowse=ButtonAt(c,"…",342,136,40,31,false);addonBrowse.AccessibleName="Выбрать папку SimHub";
  addonBrowse.Click+=(s,e)=>{using(var d=new FolderBrowserDialog()){d.Description="Папка с SimHubWPF.exe";if(Directory.Exists(simHubPath.Text))d.SelectedPath=simHubPath.Text;if(d.ShowDialog(this)==DialogResult.OK)simHubPath.Text=d.SelectedPath;}};
  LabelAt(c,"Перевод интерфейса: тестовая версия.\nВсе экраны и тексты оверлеев ещё не проверены.",18,176,366,40,9,FontStyle.Regular,muted);
  addonInstall=ButtonAt(c,"Установить русский модуль",18,229,364,34,true);
  addonRemove=ButtonAt(c,"Отменить установку модуля",18,270,364,32,false);
  addonGuide=ButtonAt(c,"Назначение и пошаговая инструкция",18,311,364,34,false,0xf194);
  addonGuide.Click+=(s,e)=>{try{Process.Start(new ProcessStartInfo(Path.Combine(home,"addons","raycerray-ru","guide.html")){UseShellExecute=true});}catch(Exception ex){SetStatus("Инструкция не открыта",ex.Message,accent);}};
  addonInstall.Click+=(s,e)=>{
   string root=simHubPath.Text.Trim(),src=Path.Combine(home,"addons","raycerray-ru",RussianAddon.FileName);
   if(MessageBox.Show(this,"Установить отдельный модуль русского интерфейса в:\n"+root+"?\n\nПолностью закройте SimHub. Предыдущая версия модуля будет сохранена. Основной плагин и игра не меняются.","Дополнение SimHub",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
   StartJob("Устанавливаем модуль…",delegate{return RussianAddon.Install(root,src,AddonHash);},null);
  };
  addonRemove.Click+=(s,e)=>{string root=simHubPath.Text.Trim();if(MessageBox.Show(this,"Отменить установку русского модуля в:\n"+root+"?\nЗакройте SimHub. Предыдущая версия, если она была, будет восстановлена.","Отмена установки",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;StartJob("Восстанавливаем модуль…",delegate{return RussianAddon.Remove(root);},null);};
 }
 void RefreshAddons(){if(addonInstall==null)return;addonInstall.Enabled=addonRemove.Enabled=addonBrowse.Enabled=addonGuide.Enabled=simHubPath.Enabled=!busy;}
 const string AddonHash="17a894ae97f6d224133c41e03d5514eb6e2735b455822bd84e95b50158587a2a";
}
