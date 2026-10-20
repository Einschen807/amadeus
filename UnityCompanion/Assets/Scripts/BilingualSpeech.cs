using System;
using System.Collections.Generic;
using System.Text;
namespace KurisuCompanion {
internal sealed class BilingualSentence {
 public string Japanese, Chinese;
 public string Emotion, Gesture;
 public bool Complete, Invalid;
}
internal sealed class BilingualBuffer {
 public const string Prompt = "\n[应用输出协议]本轮回复输出一个合法 JSON 对象，示例：{\"sentences\":[{\"ja\":\"おかえり。\",\"zh\":\"欢迎回来。\",\"emotion\":\"happy\",\"gesture\":\"nod\"}]}。sentences 数组中每项对应一句话，先写 ja 日语再写 zh 中文，两个字符串均不能为空且意思一致。每句还必须有 emotion 和 gesture：emotion 只能选 neutral、happy、teasing、annoyed、angry、sad、worried、surprised、shy、serious、sleepy；gesture 只能选 none、nod、shake、sigh、think、look_away、laugh、surprise、blush。按红莉栖说出该句时的语气和内容选，事实说明与工具报告通常用 neutral/none，不要为了动画改写话语。中文用于界面和历史，日语只供语音；人格要求用中文交流是指 zh 字段，不代表省略 ja。历史里的普通中文是已提取的字幕，不是本轮输出格式范例。不要输出代码围栏或对象外文字。首句直接回应用户，中文约10至20字，日语尽量不超过24字；后续每句中文不超过40字、日语不超过56字。保持简洁，不为填格式多说话。代码、路径或网址在中文中保留准确内容，日语自然概括其读法。工具调用仍走原有工具协议，不把工具参数当作回复。[/应用输出协议]";
 sealed class Frame { public readonly StringBuilder Text=new StringBuilder("{"); public BilingualSentence Pending; }
 readonly List<Frame> frames=new List<Frame>();
 readonly StringBuilder chinese=new StringBuilder();
 readonly Action<BilingualSentence> emit;
 bool quoted,escaped;
 public string Error { get; private set; }
 public bool TextOnlyFallback { get; private set; }
 public string Chinese=>chinese.ToString();
 public int Count { get; private set; }
 // 被角色一致性检查拦下、不会播放的句子数。
 public int Dropped { get; private set; }
 public BilingualBuffer(Action<BilingualSentence> onSentence){emit=onSentence;}
 public static string ConstrainRequest(string body){
  var request=(Dictionary<string,object>)RpcJson.Decode(body);
  request["response_format"]=RpcJson.Obj("type","json_object");
  ((List<object>)request["messages"]).Add(RpcJson.Obj("role","system","content",Prompt));
  return RpcJson.Encode(request);
 }
 static string Field(object value,string primary,string alias,string shortAlias){
  string result=RpcJson.Str(value,primary);if(string.IsNullOrWhiteSpace(result))result=RpcJson.Str(value,alias);if(string.IsNullOrWhiteSpace(result))result=RpcJson.Str(value,shortAlias);return result.Trim();
 }
 public void Push(string delta){
  if(Error!=null||delta==null)return;
  foreach(char c in delta){
   foreach(var frame in frames){frame.Text.Append(c);if(frame.Text.Length>65536){Fail("双语回复过长");return;}}
   if(quoted){
    if(escaped)escaped=false;
    else if(c=='\\')escaped=true;
    else if(c=='"'){quoted=false;if(frames.Count>0)PublishJapanese(frames[frames.Count-1]);}
   }else if(c=='"'&&frames.Count>0)quoted=true;
   else if(c=='{'){if(frames.Count>=16){Fail("双语回复嵌套过深");return;}frames.Add(new Frame());}
   else if(c=='}'&&frames.Count>0){var frame=frames[frames.Count-1];FinishRecord(frame);frames.RemoveAt(frames.Count-1);}
   if(Error!=null)return;
  }
 }
 void PublishJapanese(Frame frame){
  if(frame.Pending!=null)return;
  string ja;
  try{ja=Field(RpcJson.Decode(frame.Text.ToString()+"}"),"ja","japanese","jp");}catch{return;}
  if(ja.Length==0)return;
  frame.Pending=new BilingualSentence{Japanese=ja};emit?.Invoke(frame.Pending);
 }
 void FinishRecord(Frame frame){
  object parsed;try{parsed=RpcJson.Decode(frame.Text.ToString());}catch{Fail("双语回复格式无效");return;}
  string ja=Field(parsed,"ja","japanese","jp"),zh=Field(parsed,"zh","chinese","cn");
  if(ja.Length==0&&zh.Length==0)return;
  if(zh.Length==0){if(frame.Pending!=null)frame.Pending.Invalid=true;return;}
  if(frame.Pending==null){frame.Pending=new BilingualSentence{Japanese=ja};emit?.Invoke(frame.Pending);}
  if(frame.Pending.Japanese!=ja){frame.Pending.Invalid=true;return;}
  // 逐句检查：跳出角色的句子在播放前作废，其余句子照常输出。
  if(RoleGuard.IsOutOfCharacter(zh)){frame.Pending.Invalid=true;Dropped++;Metrics.Count("persona.ooc_sentence_dropped");return;}
  frame.Pending.Chinese=zh;
  frame.Pending.Emotion=Field(parsed,"emotion","mood","tone");
  frame.Pending.Gesture=Field(parsed,"gesture","action","motion");
  frame.Pending.Complete=true;
  TextOnlyFallback |= ja.Length==0;
  if(chinese.Length>0)chinese.Append('\n');chinese.Append(zh);Count++;
 }
 void Fail(string error){Error=error;foreach(var frame in frames)if(frame.Pending!=null&&!frame.Pending.Complete)frame.Pending.Invalid=true;}
 public void Finish(){if(frames.Count!=0)Fail("双语回复未完整结束");}
 public bool RecoverPlainChinese(string raw){
  if(Count>0)return true;
  string text=(raw??"").Trim();
  if(text.Length==0||text.StartsWith("{")||text.StartsWith("[")||text.StartsWith("\u0060\u0060\u0060"))return false;
  bool hasChinese=false;foreach(char c in text){if(c>='\u3040'&&c<='\u30ff')return false;if(c>='\u4e00'&&c<='\u9fff')hasChinese=true;}
  if(!hasChinese)return false;
  TextOnlyFallback=true;Error=null;
  new SentenceBuffer().Push(text,s=>{emit?.Invoke(new BilingualSentence{Chinese=s,Emotion=EmoteCue.Infer(s),Gesture="none",Complete=true});if(chinese.Length>0)chinese.Append('\n');chinese.Append(s);Count++;},true);
  return true;
 }
}
}

