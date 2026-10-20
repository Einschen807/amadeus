using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
namespace KurisuCompanion {
[Serializable] internal sealed class VoiceSettings {
 public bool enabled=true,stopOwnedServerOnExit=true,autoDiscoverInstallation=true;
 public string installPath="D:/GPT-SoVITS-v2pro-20250604",endpoint="http://127.0.0.1:9880";
 public string gptModel="GPT_weights_v2Pro/kurisu_ja_v2pro_20260927-e15.ckpt",sovitsModel="SoVITS_weights_v2Pro/kurisu_ja_v2pro_20260927_e8_s8568.pth";
 public int requestTimeout=90,startupTimeout=180,maxSentenceChars=56;
}
internal sealed class SovitsService : MonoBehaviour {
 public VoiceSettings Settings;public bool Ready{get;private set;}public bool Starting{get;private set;}
 public Action<string> Problem,Progress; public Action<float,string> StartupProgress; public string LastError{get;private set;}
 public bool Warmed{get;private set;}
 public int WarmupCount{get;private set;}
 Process owned; UnityWebRequest probe; bool closing; string root,reference,prompt,prepared;
 string ConfigurationKey=>Settings.endpoint+"|"+Settings.installPath+"|"+Settings.gptModel+"|"+Settings.sovitsModel;
 public string ConfigPath=>Path.Combine(Application.persistentDataPath,"voice-settings.json");
 public string InstallationPath=>root??FindRoot();
 public void Configure() {
  Settings=new VoiceSettings();try{if(File.Exists(ConfigPath))Settings=JsonUtility.FromJson<VoiceSettings>(File.ReadAllText(ConfigPath))??Settings;}catch{}
  Save();reference=Path.Combine(Application.streamingAssetsPath,"Voice/reference.wav");
  string textPath=Path.Combine(Application.streamingAssetsPath,"Voice/reference.txt");prompt=File.Exists(textPath)?File.ReadAllText(textPath):"";
 }
 public void Save(){File.WriteAllText(ConfigPath,JsonUtility.ToJson(Settings,true));}
 string FindRoot(){
  if(IsRoot(Settings.installPath))return Settings.installPath;
  if(!Settings.autoDiscoverInstallation)return null;
  string env=Environment.GetEnvironmentVariable("GPT_SOVITS_HOME");if(IsRoot(env))return env;
  foreach(var drive in DriveInfo.GetDrives())try{if(drive.IsReady)foreach(var p in Directory.GetDirectories(drive.RootDirectory.FullName,"GPT-SoVITS*"))if(IsRoot(p))return p;}catch{}
  return null;
 }
 static bool IsRoot(string p)=>!string.IsNullOrWhiteSpace(p)&&File.Exists(Path.Combine(p,"api_v2.py"))&&File.Exists(Path.Combine(p,"runtime/python.exe"));
 public IEnumerator EnsureReady(){
  if(!Settings.enabled||closing)yield break;
  if(Starting){while(Starting&&!closing)yield return null;yield break;}
  if(!Uri.TryCreate(Settings.endpoint,UriKind.Absolute,out var endpointUri)||!endpointUri.IsLoopback||endpointUri.Scheme!="http"){Fail("语音服务地址必须是本机 HTTP 地址。");yield break;}
  if(!File.Exists(reference)||string.IsNullOrWhiteSpace(prompt)){Fail("缺少固定参考语音或台词，请恢复 StreamingAssets/Voice 中的参考文件。");yield break;}
  Starting=true;
  Progress?.Invoke("正在准备日语语音服务……");
  StartupProgress?.Invoke(.05f,"正在检查语音服务");
  try {
   bool available=false;
   yield return Probe(v=>available=v);
   StartupProgress?.Invoke(.2f,"正在查找语音模型");
   root=FindRoot();
   if(available){
    if(Ready&&Warmed&&prepared==ConfigurationKey){Progress?.Invoke("日语语音已就绪");yield break;}
    // A manually started API is reused, never adopted as an owned process.
    Ready=true;LastError=null;
    StartupProgress?.Invoke(.4f,"正在加载语音模型");
    if(root!=null){yield return LoadManualWeights();}
    if(Ready)yield return Warmup();
    yield break;
   }
   if(root==null){Fail("未发现可用的 GPT-SoVITS。请在设置中填写整合包目录；本次仍可文字聊天。");yield break;}
   if(!Uri.TryCreate(Settings.endpoint,UriKind.Absolute,out var uri)||!uri.IsLoopback||uri.Scheme!="http"){Fail("语音服务地址必须是本机 HTTP 地址。");yield break;}
   if(!File.Exists(Path.Combine(root,Settings.gptModel))||!File.Exists(Path.Combine(root,Settings.sovitsModel))){Fail("未找到配置的红莉栖语音模型，请检查语音设置中的模型路径。");yield break;}
   bool launched=false;
   StartupProgress?.Invoke(.38f,"正在启动语音服务");
   try{
    if(owned!=null&&!owned.HasExited)launched=true;
    else{
     owned?.Dispose();owned=null;
     var cfg=RpcJson.Obj("custom",RpcJson.Obj("device","cuda","is_half",true,"version","v2Pro","t2s_weights_path",Path.Combine(root,Settings.gptModel),"vits_weights_path",Path.Combine(root,Settings.sovitsModel),"bert_base_path",Path.Combine(root,"GPT_SoVITS/pretrained_models/chinese-roberta-wwm-ext-large"),"cnhuhbert_base_path",Path.Combine(root,"GPT_SoVITS/pretrained_models/chinese-hubert-base")));
     string config=Path.Combine(Application.persistentDataPath,"sovits-runtime.yaml");File.WriteAllText(config,RpcJson.Encode(cfg));
     var info=new ProcessStartInfo(Path.Combine(root,"runtime/python.exe"),"-u -s "+Quote(Path.Combine(Application.streamingAssetsPath,"Voice/start_api.py"))+" "+Quote(root)+" "+Quote(config)+" "+uri.Port){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true};
     info.EnvironmentVariables["PATH"]=Path.Combine(root,"runtime")+";"+Environment.GetEnvironmentVariable("PATH");info.EnvironmentVariables["PYTHONIOENCODING"]="utf-8";
     owned=Process.Start(info);owned.OutputDataReceived+=(s,e)=>{};owned.ErrorDataReceived+=(s,e)=>{};owned.BeginOutputReadLine();owned.BeginErrorReadLine();launched=true;
    }
   }catch(Exception e){Fail("GPT-SoVITS 启动失败："+e.Message);}
   if(!launched)yield break;
   float deadline=Time.realtimeSinceStartup+Mathf.Clamp(Settings.startupTimeout,15,600);
   while(!closing&&Time.realtimeSinceStartup<deadline){
    StartupProgress?.Invoke(Mathf.Lerp(.45f,.75f,1f-(deadline-Time.realtimeSinceStartup)/Mathf.Clamp(Settings.startupTimeout,15,600)),"正在等待语音服务响应");
    if(owned.HasExited){Fail("GPT-SoVITS 启动后退出，请检查整合包及模型配置。");yield break;}
    yield return Probe(v=>available=v);if(available){Ready=true;LastError=null;yield return Warmup();yield break;}
    yield return new WaitForSecondsRealtime(1);
   }
   if(!closing)Fail("GPT-SoVITS 启动超时，暂时仅显示文字。可在设置中重试连接。");
  } finally {Starting=false;}
 }
 static string Quote(string s)=>"\""+s.Replace("\"","")+"\"";
 IEnumerator Probe(Action<bool> result){
  using(var req=UnityWebRequest.Get(Settings.endpoint.TrimEnd('/')+"/openapi.json")){
   probe=req;req.timeout=3;yield return req.SendWebRequest();
   bool valid=false;if(req.result==UnityWebRequest.Result.Success)try{var paths=RpcJson.Get(RpcJson.Decode(req.downloadHandler.text),"paths");valid=RpcJson.Get(paths,"/tts")!=null&&RpcJson.Get(paths,"/set_gpt_weights")!=null;}catch{}
   result(valid);probe=null;
  }
 }
 IEnumerator LoadManualWeights(){
  int loaded=0;
  foreach(var pair in new[]{new[]{"/set_gpt_weights",Settings.gptModel},new[]{"/set_sovits_weights",Settings.sovitsModel}}){
   string path=Path.Combine(root,pair[1]);if(!File.Exists(path)){Fail("语音模型不存在："+path);yield break;}
   using(var req=UnityWebRequest.Get(Settings.endpoint.TrimEnd('/')+pair[0]+"?weights_path="+Uri.EscapeDataString(path))){probe=req;req.timeout=Settings.requestTimeout;yield return req.SendWebRequest();probe=null;if(req.result!=UnityWebRequest.Result.Success){Fail("已发现语音 API，但加载红莉栖模型失败。");yield break;}}
   StartupProgress?.Invoke(++loaded == 1 ? .58f : .75f,"正在加载语音模型");
  }
 }
 IEnumerator Warmup(){
  Warmed=false;WarmupCount++;Progress?.Invoke("正在预热日语语音，首次需要稍等……");StartupProgress?.Invoke(.82f,"正在预热日语语音");
  float began=Time.realtimeSinceStartup;string failure=null;
  using(var req=Request("おかえり。")){
   probe=req;req.timeout=Mathf.Clamp(Settings.requestTimeout,5,90);yield return req.SendWebRequest();probe=null;
   if(req.result!=UnityWebRequest.Result.Success)failure=req.error;
   else try{PcmAudio.Decode(req.downloadHandler.data);}catch(Exception e){failure=e.Message;}
  }
  // The warm-up response is discarded: no AudioClip, playback or file is created.
  if(failure==null){Warmed=true;prepared=ConfigurationKey;Progress?.Invoke("日语语音已预热，可以开始对话");StartupProgress?.Invoke(1f,"语音服务已就绪");}
  else {Progress?.Invoke("语音预热未完成，仍可尝试对话");UnityEngine.Debug.LogWarning("VOICE_WARMUP_FAILED "+failure);}
  UnityEngine.Debug.Log("VOICE_WARMUP seconds="+(Time.realtimeSinceStartup-began).ToString("F2")+" success="+Warmed);
 }
 public UnityWebRequest Request(string text){
  const string lang="ja"; // The bilingual LLM stream provides Japanese speech text directly.
  string body=RpcJson.Encode(RpcJson.Obj("text",text,"text_lang",lang,"ref_audio_path",reference,"prompt_text",prompt,"prompt_lang","ja","media_type","wav","streaming_mode",false,"batch_size",1,"parallel_infer",false,"text_split_method","cut5","speed_factor",1.0));
  var req=UnityWebRequest.Post(Settings.endpoint.TrimEnd('/')+"/tts",body,"application/json");req.timeout=Mathf.Clamp(Settings.requestTimeout,5,300);return req;
 }
 void Fail(string message){Ready=false;Warmed=false;prepared=null;LastError=message;Problem?.Invoke(message);StartupProgress?.Invoke(1f,"语音暂不可用，继续进入文字对话");}
 public void Invalidate(){Ready=false;Warmed=false;prepared=null;}
 public void Shutdown(){if(closing)return;closing=true;Ready=false;probe?.Abort();StopAllCoroutines();if(owned!=null){try{if(Settings.stopOwnedServerOnExit&&!owned.HasExited)owned.Kill();}catch{}owned.Dispose();owned=null;}}
 void Update(){if(Ready&&owned!=null){bool exited=false;try{exited=owned.HasExited;}catch{exited=true;}if(exited)Fail("GPT-SoVITS 已退出，当前回复将继续显示文字。可在设置中重新连接。");}}
 void OnApplicationQuit()=>Shutdown();
 void OnDestroy()=>Shutdown();
}
}
