using System.Text;
namespace KurisuCompanion {
internal static class VoiceCommands {
 public static bool IsStop(string text){
  var clean=new StringBuilder();foreach(char c in text??"")if(!char.IsWhiteSpace(c)&&!char.IsPunctuation(c))clean.Append(char.ToLowerInvariant(c));
  string value=clean.ToString();if(value.Length>48)return false;
  foreach(string negation in new[]{"不要停","别停","不用停","继续说","继续讲","不是让你","不是叫你","没让你","如果我说","比如说","是什么意思","不要闭嘴","别闭嘴"})if(value.Contains(negation))return false;
  foreach(string phrase in new[]{"别说了","不要再说","不用再说","不要说了","不用说了","停止说话","停止回复","停止回答","停止朗读","暂停说话","暂停回复","停一下","停一停","安静一下","闭嘴","够了","打住","你停下"})if(value.Contains(phrase))return true;
  return value=="停止"||value=="停下"||value=="暂停";
 }
 public static BilingualSentence Acknowledgement(int index){
  string[] zh={"哼，知道了。我不说了，行了吧。","真是的，别突然打断我啊。我停了。","好啦，停就是了。……真会挑时候。"};
  string[] ja={"ふん、分かったわよ。もう言わない。それでいいんでしょ。","もう、急に話を遮らないでよ。やめるわよ。","はいはい、やめればいいんでしょ。……間が悪いんだから。"};
  index=(index%zh.Length+zh.Length)%zh.Length;
  return new BilingualSentence{Chinese=zh[index],Japanese=ja[index],Emotion="angry",Gesture="sigh",Complete=true};
 }
}
}
