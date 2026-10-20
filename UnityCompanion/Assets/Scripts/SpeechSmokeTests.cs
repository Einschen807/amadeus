using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace KurisuCompanion {
internal sealed class SpeechSmokeTests : MonoBehaviour {
 readonly List<string> results=new List<string>(); bool failed;
 void Check(bool value,string message){results.Add((value?"PASS ":"FAIL ")+message);if(!value)failed=true;Debug.Log(results[results.Count-1]);}
 static void Push(SpeechPipeline pipeline,string text){new SentenceBuffer().Push(text,s=>pipeline.PushBilingual(new BilingualSentence{Chinese=s,Japanese=s,Complete=true}),true);}
 IEnumerator Start(){
  var paired=new List<BilingualSentence>();var decoder=new BilingualBuffer(paired.Add);
  decoder.Push("{\"ja\":\"おかえり。\"");Check(paired.Count==1&&!paired[0].Complete,"Japanese emitted before Chinese caption");
  decoder.Push(",\"zh\":\"欢迎回来。\"}\n");decoder.Finish();Check(decoder.Error==null&&paired[0].Complete&&decoder.Chinese=="欢迎回来。","Chinese caption paired without protocol leakage");
  var escaped=new BilingualBuffer(s=>{});string escapedRecord=RpcJson.Encode(RpcJson.Obj("ja","引用は\"{}\"です。","zh","引用是\"{}\"。"));foreach(char c in escapedRecord)escaped.Push(c.ToString());escaped.Finish();Check(escaped.Error==null&&escaped.Chinese=="引用是\"{}\"。","JSON escapes survive token-by-token delivery");
  var incomplete=new List<BilingualSentence>();var broken=new BilingualBuffer(incomplete.Add);broken.Push("{\"ja\":\"こんにちは。\"");broken.Finish();Check(broken.Error!=null&&incomplete[0].Invalid,"truncated pair invalidates pending audio");
  var wrappedPairs=new List<BilingualSentence>();var wrapped=new BilingualBuffer(wrappedPairs.Add);
  wrapped.Push("{\"sentences\":[{\"ja\":\"おかえり。\"");Check(wrappedPairs.Count==1&&!wrappedPairs[0].Complete,"JSON envelope still emits Japanese before caption");
  wrapped.Push(",\"zh\":\"欢迎回来。\"},{\"ja\":\"元気？\",\"zh\":\"还好吗？\"}]}");wrapped.Finish();Check(wrapped.Error==null&&wrapped.Count==2&&wrappedPairs.Count==2,"JSON envelope yields each sentence once");
  var performed=new List<BilingualSentence>();var performanceDecoder=new BilingualBuffer(performed.Add);
  performanceDecoder.Push("{\"sentences\":[{\"ja\":\"ばか。\",\"zh\":\"笨蛋。\",\"emotion\":\"teasing\",\"gesture\":\"nod\"},{\"ja\":\"分かった。\",\"zh\":\"我知道了。\",\"emotion\":\"neutral\",\"gesture\":\"none\"}]}");performanceDecoder.Finish();
  Check(performanceDecoder.Error==null&&performed.Count==2&&performed[0].Emotion=="teasing"&&performed[0].Gesture=="nod","per-sentence LLM performance tags survive streaming JSON");
  var cue=EmoteCue.FromSentence(performed[0]);Check(cue.ExpressionTimeline=="表情_ニヤニヤ"&&cue.GestureTimeline=="うん","emotion and gesture select authored Kurisu timelines");
  cue=EmoteCue.FromSentence(new BilingualSentence{Chinese="这里有一个文件。",Emotion="arbitrary timeline",Gesture="delete"});Check(cue.Emotion=="neutral"&&cue.Gesture=="none","unknown LLM labels fall back to safe neutral animation");
  var aliases=new BilingualBuffer(s=>{});aliases.Push("```json\n[{\"japanese\":\"こんにちは。\",\"chinese\":\"你好。\"}]\n```");aliases.Finish();Check(aliases.Count==1&&aliases.Chinese=="你好。","array code fence and language aliases accepted");
  var plain=new BilingualBuffer(s=>{});plain.Push("嗯，我在听。");plain.Finish();Check(plain.RecoverPlainChinese("嗯，我在听。")&&plain.TextOnlyFallback&&plain.Chinese=="嗯，我在听。","plain Chinese survives unexpected response format");
  Check(!new BilingualBuffer(s=>{}).RecoverPlainChinese("{\"ja\":\"こんにちは\""),"malformed protocol is never shown as dialogue");
  var constrained=RpcJson.Decode(BilingualBuffer.ConstrainRequest("{\"messages\":[{\"role\":\"assistant\",\"content\":\"旧中文回复\"}],\"stream\":true}"));var constrainedMessages=(List<object>)RpcJson.Get(constrained,"messages");
  Check(RpcJson.Str(RpcJson.Get(constrained,"response_format"),"type")=="json_object"&&RpcJson.Str(constrainedMessages[constrainedMessages.Count-1],"content")==BilingualBuffer.Prompt,"JSON mode and final protocol rule override historical formatting");
  var sentences=new List<string>();var segment=new SentenceBuffer();segment.Push("值是3.14，Dr. Smith说",sentences.Add);Check(sentences.Count==0,"decimal and abbreviation retained");segment.Push("你好！下一句……\n结尾",sentences.Add);segment.Push("",sentences.Add,true);Check(sentences.Count==3,"split punctuation ellipsis and final tail");
  sentences.Clear();new SentenceBuffer().Push("链接https://a.example/x?q=1!ok。下一句。",sentences.Add,true);Check(sentences.Count==2&&sentences[0].Contains("?q=1!ok"),"URL query not split");
  sentences.Clear();new SentenceBuffer().Push(new string('中',140),sentences.Add,true);Check(sentences.All(s=>s.Length<=56)&&string.Concat(sentences).Length==140,"long text bounded without loss");
  var env=new MouthEnvelope();for(int i=0;i<4;i++)env.Step(.2f);Check(env.Open,"RMS opens mouth");for(int i=0;i<30;i++)env.Step(0);Check(!env.Open,"release closes mouth");env.Step(1);env.Reset();Check(!env.Open,"reset always closes");
  env.Reset();int transitions=0,openSamples=0;bool previous=false;int longestOpen=0,currentOpen=0;
  for(int i=0;i<125;i++){bool state=env.Step(.15f);if(state!=previous)transitions++;previous=state;if(state){openSamples++;currentOpen++;longestOpen=Math.Max(longestOpen,currentOpen);}else currentOpen=0;}
  Check(transitions>=20&&openSamples>50&&openSamples<110&&longestOpen<=8,"sustained speech repeatedly opens and closes within a sentence");
  for(int i=0;i<6;i++)env.Step(0);Check(!env.Open,"silence closes mouth within 240ms");
  bool silent=true;for(int i=0;i<30;i++)silent &= !env.Step(0);Check(silent,"cadence never animates silent audio");
  env.Reset();Check(!env.Open&&env.Step(.15f),"interruption resets cadence and next speech opens immediately");
  string toolReply=null,toolError=null;
  yield return AgentStream.Read("http://127.0.0.1:18990/tools","mock","{}",()=>false,null,s=>toolReply=s,s=>toolError=s);
  var choices=RpcJson.Get(RpcJson.Decode(toolReply??"{}"),"choices") as List<object>;
  var calls=choices==null?null:RpcJson.Get(RpcJson.Get(choices[0],"message"),"tool_calls") as List<object>;
  Check(toolError==null&&calls!=null&&calls.Count==1&&RpcJson.Str(RpcJson.Get(calls[0],"function"),"arguments")=="{\"url\":\"https://example.com\"}","streamed tool arguments assembled");
  var backend=GetComponent<SovitsService>();backend.Settings.endpoint="http://127.0.0.1:18990";backend.Settings.enabled=true;yield return backend.EnsureReady();Check(backend.Ready,"reuse reachable API");
  Check(backend.Warmed&&backend.WarmupCount==1&&GetComponent<AudioSource>().clip==null,"startup warmup completes silently");yield return backend.EnsureReady();Check(backend.WarmupCount==1,"ready service is not warmed or reloaded each turn");
  var pipeline=GetComponent<SpeechPipeline>();
  int finishedTurns=0;var originalFinished=pipeline.Finished;pipeline.Finished=()=>{finishedTurns++;originalFinished?.Invoke();};
  var displayed=new List<string>();var display=pipeline.Display;bool mouthOpened=false;int closuresDuringAudio=0;bool previousMouth=false;int warnings=0;float first=0,end=0,firstAudio=0;
  pipeline.Display=s=>{display(s);if(first==0)first=Time.realtimeSinceStartup;if(displayed.Count==0||displayed[displayed.Count-1]!=s)displayed.Add(s);};
  var mouth=pipeline.Mouth;pipeline.Mouth=o=>{if(o)mouthOpened=true;if(previousMouth&&!o&&GetComponent<AudioSource>().isPlaying)closuresDuringAudio++;previousMouth=o;mouth(o);};pipeline.Warning=s=>warnings++;
  pipeline.PlaybackStarted=s=>{if(firstAudio==0)firstAudio=Time.realtimeSinceStartup;};
  float firstSynthesis=0;pipeline.SynthesisStarted=s=>{if(firstSynthesis==0)firstSynthesis=Time.realtimeSinceStartup;};
  pipeline.Begin("mock");string error=null;bool llmDone=false;
  var stream=new BilingualBuffer(pipeline.PushBilingual);
  StartCoroutine(AgentStream.Read("http://127.0.0.1:18990/chat","mock","{}",()=>false,stream.Push,s=>{stream.Finish();error=stream.Error;end=Time.realtimeSinceStartup;llmDone=true;pipeline.Complete();},s=>{error=s;llmDone=true;pipeline.Complete();}));
  float deadline=Time.realtimeSinceStartup+25;while((!llmDone||pipeline.Busy)&&Time.realtimeSinceStartup<deadline)yield return null;
  yield return null;
  Check(error==null&&llmDone&&!pipeline.Busy,"stream and audio consumers finish");Check(first>0&&first<end,"first sentence presented before stream completion");Check(firstAudio>0&&firstAudio<end,"audio starts before LLM completion");Check(string.Join("",displayed)=="第一句。第二句！第三句？","ordered text including failed TTS sentence");Check(warnings==1,"single TTS failure skips only one sentence");Check(mouthOpened,"actual audio drives mouth");Check(closuresDuringAudio>=4,"mouth closes repeatedly while audio is still playing");Check(GetComponent<AudioSource>().clip==null,"played audio clip released");
  Check(finishedTurns==1&&GetComponent<EmotePortraitRenderer>().LastEmotion=="neutral","completed reply resets portrait once after playback");
  Check(GetComponent<UnityEngine.UIElements.UIDocument>().rootVisualElement.Q<UnityEngine.UIElements.Label>("sentence-dialogue").text=="","completed reply clears current dialogue without removing history");
  Check(firstSynthesis>0&&firstSynthesis<first,"Japanese synthesis starts before Chinese arrives");
  pipeline.Begin("mock");Push(pipeline,"取消测试。");pipeline.Complete();yield return new WaitForSecondsRealtime(.15f);pipeline.Cancel();yield return new WaitForSecondsRealtime(.6f);Check(!pipeline.Busy&&GetComponent<AudioSource>().clip==null,"cancel releases pending request and audio");
  pipeline.Begin("mock");Push(pipeline,"播放中断测试。");pipeline.Complete();deadline=Time.realtimeSinceStartup+10;
  while(!GetComponent<AudioSource>().isPlaying&&Time.realtimeSinceStartup<deadline)yield return null;
  Check(GetComponent<AudioSource>().isPlaying,"playback active before interruption");pipeline.Cancel();
  Check(!GetComponent<AudioSource>().isPlaying&&GetComponent<AudioSource>().clip==null&&!pipeline.Busy,"interrupt active playback releases clip and queues");
  pipeline.Begin("mock");Push(pipeline,"新的回复。");pipeline.Complete();deadline=Time.realtimeSinceStartup+10;
  while(pipeline.Busy&&Time.realtimeSinceStartup<deadline)yield return null;
  Check(!pipeline.Busy&&displayed[displayed.Count-1]=="新的回复。","new reply works after interruption");
  backend.Settings.requestTimeout=5;pipeline.Begin("mock");Push(pipeline,"超时测试。后句继续。");pipeline.Complete();deadline=Time.realtimeSinceStartup+15;
  while(pipeline.Busy&&Time.realtimeSinceStartup<deadline)yield return null;
  Check(!pipeline.Busy&&displayed[displayed.Count-1]=="后句继续。","TTS timeout preserves text and continues queue");
  yield return backend.EnsureReady();
  Check(FindFirstObjectByType<AudioListener>()!=null,"audio listener available");
  backend.Settings.endpoint="http://127.0.0.1:18991";
  pipeline.Begin("mock");Push(pipeline,"连接中断。后句保留。");pipeline.Complete();deadline=Time.realtimeSinceStartup+15;
  while(pipeline.Busy&&Time.realtimeSinceStartup<deadline)yield return null;
  Check(!pipeline.Busy&&!backend.Ready,"API exit falls back to text and drains queue");
  backend.Settings.autoDiscoverInstallation=false;backend.Settings.installPath="Z:/missing-test-sovits";yield return backend.EnsureReady();
  Check(!backend.Ready&&backend.LastError.Contains("未发现可用"),"missing installation reported without blocking chat");
  Check(GetComponent<UnityEngine.UIElements.UIDocument>().rootVisualElement.Q<UnityEngine.UIElements.Label>("sentence-dialogue").parent is not UnityEngine.UIElements.ScrollView,"dialogue is not scrollable");
  File.WriteAllLines(Path.Combine(Application.persistentDataPath,"speech-checks.txt"),results);Debug.Log("KURISU_SPEECH_CHECKS_"+(failed?"FAILED":"OK"));Application.Quit(failed?1:0);
 }
}
internal sealed class SpeechLiveCheck : MonoBehaviour {
 IEnumerator Start(){
  var service=GetComponent<SovitsService>();yield return service.EnsureReady();
  if(!service.Ready){Debug.LogError("VOICE_LIVE_START_FAILED "+service.LastError);Application.Quit(1);yield break;}
  var pipeline=GetComponent<SpeechPipeline>();string key=KeyVault.Read();
  if(string.IsNullOrWhiteSpace(key)){Debug.LogError("VOICE_LIVE_NO_KEY");Application.Quit(1);yield break;}
  int played=0,warnings=0;pipeline.PlaybackStarted=s=>played++;pipeline.Warning=s=>{warnings++;Debug.LogError(s);};
  string fullPersona=RoleGuard.LoadPersona();
  // Deliberately synthetic history: never load private saved conversations for automated API tests.
  var history=new List<ChatTurn>{new ChatTurn("user","晚上好。"),new ChatTurn("assistant","晚上好，实验刚告一段落。"),new ChatTurn("user","简单聊聊就好。"),new ChatTurn("assistant","好，轻松聊两句。")};
  bool all=true;int index=0;
  foreach(string question in new[]{"你好，红莉栖。","今天有点累。","简单说一下记忆是怎么形成的。"}){
   index++;int before=played;string error=null,answer=null;var pairs=new List<BilingualSentence>();float began=Time.realtimeSinceStartup;pipeline.Begin(key);
   yield return AgentClient.Send(key,fullPersona,"",history,question,null,null,s=>{},s=>answer=s,s=>error=s,null,()=>false,s=>{pairs.Add(s);pipeline.PushBilingual(s);});
   pipeline.Complete();float deadline=Time.realtimeSinceStartup+150;while(pipeline.Busy&&Time.realtimeSinceStartup<deadline)yield return null;
   bool ok=error==null&&!pipeline.Busy&&played>before&&pairs.Count>0&&pairs.All(s=>s.Complete&&!string.IsNullOrWhiteSpace(s.Japanese)&&!string.IsNullOrWhiteSpace(s.Chinese))&&answer!=null&&!answer.Contains("\"ja\"");
   all &= ok;Debug.Log("VOICE_HISTORY_TURN "+index+" success="+ok+" pairs="+pairs.Count+" elapsed="+(Time.realtimeSinceStartup-began).ToString("F2")+" error="+error);
   if(!ok){pipeline.Cancel();break;}
   history.Add(new ChatTurn("user",question));history.Add(new ChatTurn("assistant",answer));
  }
  bool success=all&&service.Ready&&service.Warmed&&warnings==0;
  Debug.Log("VOICE_LIVE_"+(success?"OK":"FAILED")+" fullPersona=true syntheticHistory=true turns="+index);Application.Quit(success?0:1);
 }
}
}
