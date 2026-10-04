using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

// A profile line is emitted only after Engine's resource verification succeeds.
// UI state is bound to the exact selected directory and translation variant.
sealed class GameTextProfile {
 public string GameVersion, TextVersion, Channel, GamePath, Variant;
 public int Records;
 public bool Stable { get { return GameVersion=="1.26"&&Records==56134&&Channel=="stable"; } }
 public bool Matches(string game,string variant){try{return String.Equals(GamePath,GameTextProfiles.CanonicalPath(game),StringComparison.OrdinalIgnoreCase)&&Variant==variant;}catch{return false;}}
}
static class GameTextProfiles {
 static void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
 public static string CanonicalPath(string game){if(String.IsNullOrWhiteSpace(game))throw new InvalidDataException("Укажите папку игры.");return Path.GetFullPath(game.Trim()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);}
 public static GameTextProfile Parse(string output,string game,string variant){
  Require(TranslationVariants.Valid(variant),"Неизвестный вариант перевода.");
  var all=Regex.Matches(output??"",@"^PROFILE[^\r\n]*",RegexOptions.Multiline);
  Require(all.Count==1,"Профиль игры не подтверждён. Повторите проверку совместимости.");
  var match=Regex.Match(all[0].Value,@"^PROFILE game=(1\.18|1\.24|1\.26) records=([0-9]+) text=([0-9]+(?:\.[0-9]+){1,3}) channel=(pinned|stable)$");
  Require(match.Success,"Ответ проверки профиля игры повреждён.");
  string version=match.Groups[1].Value, channel=match.Groups[4].Value;
  int records=Int32.Parse(match.Groups[2].Value);
  Require(records==(version=="1.18"?56439:version=="1.24"?55991:56134),"Число строк не соответствует версии игры.");
  Require(channel==(version=="1.26"?"stable":"pinned"),"Канал текста не соответствует версии игры.");
  return new GameTextProfile{GameVersion=version,Records=records,TextVersion=match.Groups[3].Value,Channel=channel,GamePath=CanonicalPath(game),Variant=variant};
 }
 // This independent API check deliberately accepts only bundled executable
 // identities. It never executes the game, writes a lock, or downloads data.
 // Engine's signed-header fallback remains available for normal installation.
 public static void RequireStableTarget(string home,string game){
  string directory=CanonicalPath(game), executable=Path.Combine(directory,"F1_25.exe");
  Require(File.Exists(executable),"Сначала проверьте совместимость выбранной папки игры.");
  string hash=Updates.Hash(executable);
  var manifest=Updates.Read(Path.Combine(home,"manifest.json"));
  object raw;Require(manifest.TryGetValue("native_profiles",out raw)&&raw is IEnumerable,"В пакете отсутствуют профили игры.");
  Dictionary<string,object> selected=null;int matches=0;
  foreach(object item in (IEnumerable)raw){
   var profile=item as Dictionary<string,object>;if(profile==null)continue;
   object value;if(profile.TryGetValue("exe_sha256",out value)&&String.Equals(Convert.ToString(value),hash,StringComparison.OrdinalIgnoreCase)){selected=profile;matches++;}
  }
  Require(matches==1,"Эта сборка игры не подтверждена для обновления текста. Используйте полный архив лаунчера; обычная установка проверяется отдельно.");
  object field;string set=selected.TryGetValue("language_set",out field)?Convert.ToString(field):"";
  string version=selected.TryGetValue("game_version",out field)?Convert.ToString(field):"1.26";
  Require(String.IsNullOrEmpty(set)&&version=="1.26","Для F1 25 "+version+" текст обновляется только полным архивом лаунчера. Канал 1.26 не применяется.");
 }
}
