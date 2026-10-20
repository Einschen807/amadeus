using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace KurisuCompanion {
internal sealed class VoiceInputChecks : MonoBehaviour {
 bool failed;int count;
 void Check(bool ok,string name){count++;failed|=!ok;Debug.Log((ok?"PASS ":"FAIL ")+name);}
 IEnumerator Start(){
  yield return null;
  foreach(string phrase in new[]{"可以，你不要再说了","好了别说了","停一下","不用说了","停止回复","暂停","安静一下"})Check(VoiceCommands.IsStop(phrase),"stop command: "+phrase);
  foreach(string phrase in new[]{"不要停，继续说","别停下来","不用停止回复","你今天说了什么","如果我说别说了会怎样","闭嘴是什么意思","不要闭嘴"})Check(!VoiceCommands.IsStop(phrase),"not a stop command: "+phrase);
  var ack=VoiceCommands.Acknowledgement(0);Check(ack.Complete&&ack.Chinese.Contains("不说")&&!string.IsNullOrEmpty(ack.Japanese),"stop acknowledgement is a paired short reply");
  Check(ack.Emotion=="angry"&&EmoteCue.FromSentence(ack).ExpressionTimeline=="表情_怒る","interruption uses an angry expression");
  var detector=new UtteranceDetector();var silence=new float[320];var voiced=Enumerable.Repeat(.06f,320).ToArray();bool emitted=false;
  for(int i=0;i<100;i++)emitted|=detector.Feed(silence)!=null;Check(!emitted&&!detector.Speaking,"silence never submits a message");
  detector.Feed(voiced);for(int i=0;i<50;i++)emitted|=detector.Feed(silence)!=null;Check(!emitted,"brief click does not submit a message");
  for(int i=0;i<30;i++)detector.Feed(voiced);float[] result=null;for(int i=0;i<45;i++){var part=detector.Feed(silence);if(part!=null)result=part;}
  Check(result!=null&&result.Length>=9600&&result.Length<24000,"utterance ends after silence and retains spoken audio");
  detector.Reset();result=null;for(int i=0;i<180;i++){var part=detector.Feed(voiced,3.2f);if(part!=null)result=part;}Check(result!=null&&result.Length<=52000,"reply-time command recognition has bounded latency");
  detector.Reset();Check(!detector.Speaking,"cancel clears partial recording");
  var app=GetComponent<KurisuApp>();var root=GetComponent<UIDocument>().rootVisualElement;var input=app.input;
  var change=typeof(KurisuApp).GetMethod("ApplyVoiceMode",BindingFlags.NonPublic|BindingFlags.Instance);
  var textMode=typeof(KurisuApp).GetMethod("SetTextVisible",BindingFlags.NonPublic|BindingFlags.Instance);
  Check(!input.Visible,"text input is hidden on launch");
  textMode.Invoke(app,new object[]{true});yield return null;
  string original=input.value;input.value="保留尚未发送的草稿";
  change.Invoke(app,new object[]{true});yield return null;
  Check(input.Visible&&root.Q<Button>("microphone-toggle").resolvedStyle.display!=DisplayStyle.None,"voice mode does not hide text window");
  change.Invoke(app,new object[]{false});yield return null;
  Check(input.Visible&&input.value=="保留尚未发送的草稿","turning microphone off preserves text window and draft");input.value=original;
  Check(!root.Query<Button>().ToList().Any(b=>b.text=="打断回复"),"manual interruption button removed");
  Check(!GetComponent<VoiceInput>().Active,"UI test never starts microphone capture");
  Debug.Log("VOICE_INPUT_CHECKS_"+(failed?"FAILED":"OK")+" count="+count);
  Application.Quit(failed?1:0);
 }
}
}
