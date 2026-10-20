using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace KurisuCompanion {
internal sealed class DetachedInputWindow : IDisposable {
 [Serializable] internal sealed class Reply {public string op,value,attachment;public string[] paths;public bool visible;public int x,y,width,height,ownerBottom,ownerLeft,ownerWidth;public long owner,handle;}
 [Serializable] sealed class Command {public string op,value;public bool enabled;}
 readonly ConcurrentQueue<string> messages=new ConcurrentQueue<string>(); Process process;string draft="",attachment="";bool? enabled;
 public Action Send,Clear;public Action<string[]> Drop;public Action<string> Problem;
 public bool Ready{get;private set;} public bool Visible{get;private set;} public Reply Probe{get;private set;}
 public int ProcessId=>process==null?0:process.Id;
 delegate bool EnumProc(IntPtr hwnd,IntPtr state);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr state);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder name,int length);
 public string value {get=>draft;set{draft=value??"";Write("text",draft);}}
 public DetachedInputWindow(){
  if(Application.platform!=RuntimePlatform.WindowsPlayer)return;
  try{
   IntPtr owner=IntPtr.Zero;uint pid=(uint)Process.GetCurrentProcess().Id;
   EnumWindows((h,p)=>{GetWindowThreadProcessId(h,out uint id);var name=new StringBuilder(128);GetClassName(h,name,name.Capacity);if(id==pid&&name.ToString()=="UnityWndClass"){owner=h;return false;}return true;},IntPtr.Zero);
   if(owner==IntPtr.Zero)throw new InvalidOperationException("找不到主窗口");
   var path=Path.Combine(Application.streamingAssetsPath,"DesktopInput","DetachedInput.exe");
   process=new Process{StartInfo=new ProcessStartInfo(path,owner.ToInt64().ToString()){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,StandardInputEncoding=new UTF8Encoding(false)}};
   process.OutputDataReceived+=(s,e)=>{if(e.Data!=null)messages.Enqueue(e.Data);};process.ErrorDataReceived+=(s,e)=>{};
   process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
  }catch(Exception e){messages.Enqueue(JsonUtility.ToJson(new Reply{op="error",value="输入窗口启动失败："+e.Message}));}
 }
 void Write(string op,string text=null,bool state=false){
  try{if(process!=null&&!process.HasExited){process.StandardInput.WriteLine(JsonUtility.ToJson(new Command{op=op,value=text,enabled=state}));process.StandardInput.Flush();}}
  catch(Exception e){messages.Enqueue(JsonUtility.ToJson(new Reply{op="error",value="输入窗口通信失败："+e.Message}));}
 }
 public void Tick(){
  while(messages.TryDequeue(out var line)){
   Reply reply;try{reply=JsonUtility.FromJson<Reply>(line);}catch{continue;}
   switch(reply.op){
    case "ready":Ready=true;break;
    case "change":draft=reply.value??"";break;
    case "send":draft=reply.value??"";Send?.Invoke();break;
    case "clear":Clear?.Invoke();break;
    case "drop":Drop?.Invoke(reply.paths);break;
    case "probe":Probe=reply;break;
    case "error":UnityEngine.Debug.LogWarning(reply.value);Problem?.Invoke(reply.value);break;
   }
  }
 }
 public void SetEnabled(bool state){if(enabled==state)return;enabled=state;Write("enabled",state:state);}
 public void SetVisible(bool state){if(Visible==state)return;Visible=state;Write("visible",state:state);}
 public void SetAttachment(string name){name=name??"";if(attachment==name)return;attachment=name;Write("attachment",name);}
 public void Focus(){Write("focus");}
 public void Blur(){}
 public void RequestProbe(){Probe=null;Write("probe");}
 public void Capture(string path){Write("capture",path);}
 public void Dispose(){
  if(process==null)return;
  try{Write("quit");if(!process.WaitForExit(1000))process.Kill();}catch{}finally{process.Dispose();process=null;}
 }
}
}
