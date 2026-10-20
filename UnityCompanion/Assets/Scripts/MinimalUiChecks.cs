using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace KurisuCompanion {
internal sealed class MinimalUiChecks : MonoBehaviour {
 bool failed; int count;
 [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h,int command);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
 void Check(bool ok,string title){count++;failed|=!ok;Debug.Log((ok?"PASS ":"FAIL ")+title);}
 bool Separated(DetachedInputWindow.Reply p)=>p.y>=p.ownerBottom||p.x>=p.ownerLeft+p.ownerWidth||p.x+p.width<=p.ownerLeft;
 IEnumerator Probe(DetachedInputWindow input){input.RequestProbe();float until=Time.realtimeSinceStartup+4;while(input.Probe==null&&Time.realtimeSinceStartup<until)yield return null;}
 IEnumerator ProbeBubble(DesktopSpeechBubble bubble){bubble.RequestProbe();float until=Time.realtimeSinceStartup+4;while(bubble.Probe==null&&Time.realtimeSinceStartup<until)yield return null;}
 IEnumerator Start(){
  var app=GetComponent<KurisuApp>();var root=GetComponent<UIDocument>().rootVisualElement;var input=app.input;
  float until=Time.realtimeSinceStartup+8;while(!input.Ready&&Time.realtimeSinceStartup<until)yield return null;
  Check(input.Ready,"native input process is ready");
  var folder=Path.Combine(Application.persistentDataPath,"detached-input-checks");Directory.CreateDirectory(folder);
  var flags=BindingFlags.NonPublic|BindingFlags.Instance;var mode=typeof(KurisuApp).GetMethod("ApplyVoiceMode",flags);
  var textMode=typeof(KurisuApp).GetMethod("SetTextVisible",flags);
  var dialogue=root.Q<Label>("sentence-dialogue");var bubble=root.Q<SpeechBubble>("speech-bubble");
  Check(dialogue.text==""&&bubble!=null&&!bubble.Visible,"speech bubble starts hidden despite saved conversation history");
  var desktop=(DesktopSpeechBubble)typeof(KurisuApp).GetField("desktopBubble",flags).GetValue(app);
  until=Time.realtimeSinceStartup+8;while(!desktop.Ready&&Time.realtimeSinceStartup<until)yield return null;
  Check(desktop.Ready,"separate desktop speech bubble is ready");
  var chrome=GetComponent<PhoneWindowChrome>();until=Time.realtimeSinceStartup+4;
  while(!chrome.Hidden&&Time.realtimeSinceStartup<until)yield return null;
  Check(chrome.Hidden,"native title bar is hidden");
  Check(chrome.Locked&&Screen.width==PhoneWindowChrome.ClientWidth&&Screen.height==PhoneWindowChrome.ClientHeight,
   "phone opens at the captured fixed client size");
  yield return Probe(input);
  if(input.Probe==null){Check(false,"input IPC probe returned");Application.Quit(1);yield break;}
  Check(!input.Visible&&!input.Probe.visible,"native text input starts hidden");
  IntPtr owner=new IntPtr(input.Probe.owner);ShowWindow(owner,4);yield return new WaitForSeconds(.4f);yield return Probe(input);
  Check(input.Probe.handle!=0&&input.Probe.handle!=input.Probe.owner,"input has its own native top-level window");
  var emote=(EmotePortraitRenderer)typeof(KurisuApp).GetField("emoteRenderer",flags).GetValue(app);
  until=Time.realtimeSinceStartup+8;while(!emote.Ready&&Time.realtimeSinceStartup<until)yield return null;
  Check(emote.Ready,"dynamic portrait is ready before startup screenshot");
  yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,"startup.png"));
  yield return new WaitForSeconds(.3f);
  textMode.Invoke(app,new object[]{true});yield return new WaitForSeconds(.2f);yield return Probe(input);
  Check(input.Probe.visible&&Separated(input.Probe),"input is visible beside or below the fixed phone without overlap");
  Check(Screen.width==PhoneWindowChrome.ClientWidth&&Screen.height==PhoneWindowChrome.ClientHeight,"showing input does not resize phone");
  Check(root.Q("text-composer")==null&&root.Q("input-dock")==null,"main window contains no input or reserved dock");
  Check(Mathf.Abs(root.Q("portrait-stage").worldBound.height-root.worldBound.height)<2,"portrait stage fills main window to bottom");
  var portrait=root.Q("portrait-viewport").Q<Image>();
  Check(Mathf.Abs(portrait.resolvedStyle.width/root.Q("portrait-stage").resolvedStyle.width-1.65f*PortraitPose.ConfirmedDefault.zoom)<.02f,"portrait is enlarged for upper-body framing");
  Check(root.resolvedStyle.backgroundColor==Color.black,"background remains pure black");
  var circuit=root.Q<FlowingCircuitBackground>("flowing-circuit-background");
  Check(circuit!=null&&circuit.TrailCount==11&&circuit.HorizontalTrailCount==6&&circuit.VerticalTrailCount==5&&circuit.CellSize>=60,"eleven straight light trails use horizontal and vertical grid paths");
  var pipeline=GetComponent<SpeechPipeline>();
  pipeline.Display?.Invoke("嗯，我在听。有什么想说的就说吧。");
  yield return ProbeBubble(desktop);
  Check(desktop.Probe!=null&&desktop.Probe.visible&&!bubble.Visible&&desktop.Probe.text.Length>0,
   "dialogue appears in a separate bubble outside the phone");
  Check(desktop.Probe!=null&&((desktop.Probe.side=="left"&&desktop.Probe.x+desktop.Probe.width<=desktop.Probe.ownerLeft)||
   (desktop.Probe.side=="right"&&desktop.Probe.x>=desktop.Probe.ownerRight)),"bubble points toward phone from its outer side");
  Check(desktop.Probe!=null&&desktop.Probe.centerTransparency==40&&desktop.Probe.edgeTransparency==0,
   "bubble fades from opaque edges to 40 percent transparent center");
  desktop.Capture(Path.Combine(folder,"bubble.png"));yield return new WaitForSecondsRealtime(.3f);
  pipeline.Finished?.Invoke();yield return new WaitForSecondsRealtime(2.7f);
  Check(desktop.Visible,"last sentence stays visible during reading time");
  yield return new WaitForSecondsRealtime(.5f);
  yield return ProbeBubble(desktop);
  Check(!desktop.Visible&&desktop.Probe!=null&&!desktop.Probe.visible,
   "external speech bubble disappears three seconds after the final sentence");
  desktop.Show("窗口位置测试");
  SetWindowPos(owner,IntPtr.Zero,GetSystemMetrics(0)-PhoneWindowChrome.ClientWidth-50,40,0,0,0x15);
  yield return new WaitForSecondsRealtime(.5f);yield return ProbeBubble(desktop);
  Check(desktop.Probe!=null&&desktop.Probe.side=="left"&&desktop.Probe.x+desktop.Probe.width<=desktop.Probe.ownerLeft,
   "bubble moves to left when phone is on right");
  SetWindowPos(owner,IntPtr.Zero,50,40,0,0,0x15);
  yield return new WaitForSecondsRealtime(.5f);yield return ProbeBubble(desktop);
  Check(desktop.Probe!=null&&desktop.Probe.side=="right"&&desktop.Probe.x>=desktop.Probe.ownerRight,
   "bubble moves to right when phone is on left");
  desktop.Hide();
  input.value="中文草稿\r\n第二行";yield return Probe(input);
  Check(input.Probe.value==input.value,"native multiline Unicode draft round trips");
  float stageHeight=root.Q("portrait-stage").worldBound.height;
  var textButton=root.Q("text-toggle").worldBound;var micButton=root.Q("microphone-toggle").worldBound;
  float chinTop=root.worldBound.yMax-98;
  Check(textButton.yMin>=chinTop+8&&micButton.yMin>=chinTop+8&&
   textButton.yMax<=root.worldBound.yMax-11&&micButton.yMax<=root.worldBound.yMax-11&&
   textButton.xMax<=micButton.xMin&&micButton.xMax<=root.worldBound.xMax-12,
   "T and microphone buttons sit side by side inside the bottom shell");
  mode.Invoke(app,new object[]{true});yield return new WaitForSeconds(.2f);yield return Probe(input);
  Check(input.Probe.visible,"turning microphone on leaves text window visible");
  textMode.Invoke(app,new object[]{false});yield return new WaitForSeconds(.2f);yield return Probe(input);
  Check(!input.Probe.visible&&(bool)typeof(KurisuApp).GetField("voiceMode",flags).GetValue(app),"T hides text without changing voice mode");
  Check(Mathf.Abs(root.Q("portrait-stage").worldBound.height-stageHeight)<2,"hiding input does not change portrait viewport");
  mode.Invoke(app,new object[]{false});yield return new WaitForSeconds(.2f);yield return Probe(input);
  Check(!input.Probe.visible,"turning microphone off leaves text hidden");
  textMode.Invoke(app,new object[]{true});yield return new WaitForSeconds(.2f);yield return Probe(input);
  Check(input.Probe.visible&&input.Probe.value=="中文草稿\r\n第二行","T restores native window and draft");
  string sample=Path.Combine(folder,"独立窗口附件.txt");File.WriteAllText(sample,"附件测试内容",new System.Text.UTF8Encoding(false));
  mode.Invoke(app,new object[]{true});textMode.Invoke(app,new object[]{false});
  app.AcceptDroppedFiles(new[]{sample});yield return new WaitForSeconds(.3f);yield return Probe(input);
  Check(input.Probe.visible&&(bool)typeof(KurisuApp).GetField("voiceMode",flags).GetValue(app),"file drop reveals text without disabling microphone");
  mode.Invoke(app,new object[]{false});
  Check(input.Probe.attachment=="独立窗口附件.txt"&&input.Probe.value==input.value,"file drop updates attachment in separate window without replacing draft");
  Check(Separated(input.Probe),"attachment input still does not overlap portrait window");
  ShowWindow(owner,6);yield return new WaitForSeconds(.3f);yield return Probe(input);Check(!input.Probe.visible,"minimizing main window hides input");
  ShowWindow(owner,9);yield return new WaitForSeconds(.4f);
  SetWindowPos(owner,IntPtr.Zero,100,80,620,650,0x14);yield return new WaitForSeconds(1f);yield return Probe(input);
  Check(chrome.Locked&&Screen.width==PhoneWindowChrome.ClientWidth&&Screen.height==PhoneWindowChrome.ClientHeight,
   "external resize attempt cannot change phone dimensions");
  Check(input.Probe.width==input.Probe.ownerWidth&&Separated(input.Probe),"input follows the fixed-size phone after movement");
  yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,"main.png"));
  input.Capture(Path.Combine(folder,"input.png"));yield return new WaitForSeconds(.4f);
  Check(File.Exists(Path.Combine(folder,"input.png")),"native input window captured");
  int child=input.ProcessId;input.Dispose();bool stopped;try{using(var p=System.Diagnostics.Process.GetProcessById(child))stopped=p.HasExited;}catch{stopped=true;}
  Check(stopped,"input helper exits and releases process");
  Debug.Log("DETACHED_INPUT_CHECKS_"+(failed?"FAILED":"OK")+" count="+count+" screenshots="+folder);
  Application.Quit(failed?1:0);
 }
}
}
