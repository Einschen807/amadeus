using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KurisuCompanion {
// Twenty-millisecond mono frames; keep a short pre-roll so word beginnings are not clipped.
internal sealed class UtteranceDetector {
 readonly Queue<float[]> preRoll=new Queue<float[]>();
 readonly List<float> samples=new List<float>();
 int onset,voiced;float silence,noise=.002f;
 public bool Speaking{get;private set;}
 public void Reset(){preRoll.Clear();samples.Clear();onset=voiced=0;silence=0;Speaking=false;}
 public float[] Feed(float[] frame,float maxSeconds=20){
  double sum=0;foreach(float value in frame)sum+=value*value;
  float rms=(float)Math.Sqrt(sum/frame.Length),threshold=Mathf.Clamp(noise*3,.009f,.03f);
  if(!Speaking){
   if(rms<.008f)noise=Mathf.Lerp(noise,rms,.03f);
   preRoll.Enqueue((float[])frame.Clone());while(preRoll.Count>10)preRoll.Dequeue();
   onset=rms>=threshold?onset+1:0;
   if(onset>=3){Speaking=true;voiced=onset;foreach(var prior in preRoll)samples.AddRange(prior);preRoll.Clear();}
   return null;
  }
  samples.AddRange(frame);
  if(rms>=threshold*.65f){voiced++;silence=0;}else silence+=.02f;
  if(silence<.8f&&samples.Count<(int)(16000*maxSeconds))return null;
  float[] result=null;
  if(voiced>=9){int keep=samples.Count-Math.Max(0,(int)((silence-.2f)*16000));result=samples.GetRange(0,Math.Max(1,keep)).ToArray();}
  Reset();return result;
 }
}

internal sealed class VoiceInput : MonoBehaviour {
 public Action<bool> StateChanged;
 public Action<string> Status,Problem;
 public Action<string,bool> Recognized;
 public Func<bool> Suppressed,Replying;
 public Func<string> InstallationPath;
 public bool Active{get;private set;}
 public bool Ready{get;private set;}
 public bool Recognizing{get;private set;}
 public bool IsUserSpeaking=>detector.Speaking||Recognizing;
 readonly ConcurrentQueue<string> messages=new ConcurrentQueue<string>();
 readonly UtteranceDetector detector=new UtteranceDetector();
 Process worker;AudioClip recording;float[] block;readonly float[] mono=new float[320];
 int cursor,frameSize,generation,requestGeneration,workerEpoch;float loadStarted,requestStarted,micStarted,resumeAfter;
 bool closing,paused,requestDuringReply,pendingDuringReply,segmentDuringReply;float[] pendingAudio;string lastStatus;
 void Say(string value){if(lastStatus==value)return;lastStatus=value;Status?.Invoke(value);}
 public void SetActive(bool value){
  generation++;pendingAudio=null;Active=value;StopCapture();StateChanged?.Invoke(value);
  if(!value){Say("麦克风已关闭");return;}
  try{
   if(Microphone.devices.Length==0){Fail("未发现麦克风，请连接设备并检查 Windows 麦克风权限。");return;}
   StartWorker();
  }catch(Exception e){Fail("无法启用麦克风："+e.Message);}
 }
 void StartWorker(){
  if(worker!=null&&!worker.HasExited){Say(Ready?"准备收音……":"正在加载中文识别模型……");return;}
  string root=InstallationPath?.Invoke();
  if(string.IsNullOrEmpty(root)||!File.Exists(Path.Combine(root,"runtime/python.exe"))||!File.Exists(Path.Combine(root,"tools/asr/models/speech_paraformer-large_asr_nat-zh-cn-16k-common-vocab8404-pytorch/model.pt"))){Fail("未找到本地中文识别模型，请检查 GPT-SoVITS 整合包目录。");return;}
  worker?.Dispose();Ready=false;Recognizing=false;while(messages.TryDequeue(out _)){}
  var info=new ProcessStartInfo(Path.Combine(root,"runtime/python.exe"),"-u -s "+Quote(Path.Combine(Application.streamingAssetsPath,"Voice/asr_worker.py"))+" "+Quote(root)){
   WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
   RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,StandardInputEncoding=new UTF8Encoding(false)
  };
  info.EnvironmentVariables["PYTHONIOENCODING"]="utf-8";
  int epoch=++workerEpoch;
  worker=Process.Start(info);worker.OutputDataReceived+=(s,e)=>{if(e.Data!=null&&epoch==workerEpoch)messages.Enqueue(e.Data);};worker.ErrorDataReceived+=(s,e)=>{};
  worker.BeginOutputReadLine();worker.BeginErrorReadLine();loadStarted=Time.realtimeSinceStartup;Say("正在加载中文识别模型……");
 }
 static string Quote(string s)=>"\""+s.Replace("\"","")+"\"";
 void StartCapture(){
  if(recording!=null)return;
  try{
   recording=Microphone.Start(null,true,2,16000);
   if(recording==null){Fail("麦克风启动失败，请检查设备及 Windows 麦克风权限。");return;}
   frameSize=Math.Max(1,recording.frequency/50);block=new float[frameSize*recording.channels];cursor=0;micStarted=Time.realtimeSinceStartup;detector.Reset();Say("请说话 · 停顿后自动发送");
  }catch(Exception e){Fail("录音启动失败："+e.Message);}
 }
 public void DiscardPending(){generation++;pendingAudio=null;detector.Reset();segmentDuringReply=false;}
 void StopCapture(){if(recording!=null){Microphone.End(null);Destroy(recording);recording=null;}block=null;detector.Reset();segmentDuringReply=false;}
 void Update(){
  while(messages.TryDequeue(out var line))Handle(line);
  if(worker!=null&&worker.HasExited){Ready=false;Recognizing=false;worker.Dispose();worker=null;if(Active)Fail("语音识别服务已退出，请重新开启麦克风。");}
  if(!Active||closing)return;
  if(!Ready){if(Time.realtimeSinceStartup-loadStarted>120){KillWorker();Fail("中文识别模型加载超时，请重试。");}return;}
  if(Recognizing&&Time.realtimeSinceStartup-requestStarted>30){KillWorker();Fail("语音识别超时，请重试。");return;}
  bool blocked=Suppressed?.Invoke()??false;
  if(blocked){StopCapture();pendingAudio=null;paused=true;resumeAfter=Time.realtimeSinceStartup+.45f;Say("停止回复中 · 稍后恢复收音");return;}
  if(paused&&Time.realtimeSinceStartup<resumeAfter)return;paused=false;
  if(!Recognizing&&pendingAudio!=null){var pending=pendingAudio;pendingAudio=null;Submit(pending,pendingDuringReply);}
  StartCapture();if(recording==null)return;
  int position=Microphone.GetPosition(null);
  if(position<0||(!Microphone.IsRecording(null)&&Time.realtimeSinceStartup-micStarted>1)){Fail("麦克风已断开，请检查设备后重新开启。");return;}
  if(position==0&&Time.realtimeSinceStartup-micStarted>3&&cursor==0){Fail("没有收到麦克风数据，请检查 Windows 麦克风权限。");return;}
  int available=(position-cursor+recording.samples)%recording.samples;
  while(available>=frameSize&&recording!=null){
   if(!recording.GetData(block,cursor)){Fail("读取麦克风数据失败。");return;}
   int channels=recording.channels;
   for(int i=0;i<320;i++){int sample=Math.Min(frameSize-1,i*frameSize/320);float value=0;for(int c=0;c<channels;c++)value+=block[sample*channels+c];mono[i]=value/channels;}
   cursor=(cursor+frameSize)%recording.samples;available-=frameSize;
   bool replying=Replying?.Invoke()??false;segmentDuringReply|=replying;
   var utterance=detector.Feed(mono,replying?3.2f:20f);
   Say(replying?"回复中 · 可说“别说了”打断":Recognizing?"正在识别中文……":detector.Speaking?"正在收音 · 停顿后自动发送":"请说话 · 停顿后自动发送");
   if(utterance!=null){bool during=segmentDuringReply;segmentDuringReply=false;if(Recognizing){pendingAudio=utterance;pendingDuringReply=during;}else Submit(utterance,during);}
   else if(!detector.Speaking)segmentDuringReply=replying;
  }
 }
 void Submit(float[] audio,bool duringReply){
  byte[] pcm=new byte[audio.Length*2];for(int i=0;i<audio.Length;i++){short value=(short)Mathf.RoundToInt(Mathf.Clamp(audio[i],-1,1)*32767);pcm[i*2]=(byte)value;pcm[i*2+1]=(byte)(value>>8);}
  Recognizing=true;requestGeneration=generation;requestDuringReply=duringReply;requestStarted=Time.realtimeSinceStartup;Say("正在识别中文……");
  string line=RpcJson.Encode(RpcJson.Obj("id",generation,"pcm",Convert.ToBase64String(pcm)));var target=worker;int epoch=workerEpoch;
  Task.Run(()=>{try{target.StandardInput.WriteLine(line);target.StandardInput.Flush();}catch{if(epoch==workerEpoch)messages.Enqueue("{\"event\":\"fatal\",\"message\":\"识别服务连接中断\"}");}});
 }
 void Handle(string line){
  object packet;try{packet=RpcJson.Decode(line);}catch{return;}
  string kind=RpcJson.Str(packet,"event");
  if(kind=="ready"){Ready=true;return;}
  if(kind=="fatal"){KillWorker();if(Active)Fail("中文识别模型加载失败："+RpcJson.Str(packet,"message"));return;}
  if(kind!="result"&&kind!="error")return;
  Recognizing=false;
  if(!Active||requestGeneration!=generation||Convert.ToInt32(RpcJson.Get(packet,"id"))!=generation)return;
  if(kind=="error"){Say("本句未能识别，请再说一次");resumeAfter=Time.realtimeSinceStartup+1;paused=true;return;}
  string text=RpcJson.Str(packet,"text").Trim();
  if(text.Length==0){Say("没有识别到清晰语音，请再说一次");resumeAfter=Time.realtimeSinceStartup+1;paused=true;return;}
  Say("已识别："+text);Recognized?.Invoke(text,requestDuringReply);
 }
 void Fail(string message){Active=false;generation++;StopCapture();StateChanged?.Invoke(false);Say("麦克风已关闭");Problem?.Invoke(message);}
 void KillWorker(){workerEpoch++;Ready=false;Recognizing=false;if(worker==null)return;try{if(!worker.HasExited)worker.Kill();}catch{}worker.Dispose();worker=null;}
 void Shutdown(){if(closing)return;closing=true;Active=false;generation++;StopCapture();KillWorker();while(messages.TryDequeue(out _)){}}
 void OnApplicationQuit()=>Shutdown();void OnDestroy()=>Shutdown();
}
}
