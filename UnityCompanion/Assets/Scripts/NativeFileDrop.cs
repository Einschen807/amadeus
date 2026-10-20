using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace KurisuCompanion {
// Native OLE callbacks only copy paths and update thread-safe state. Unity consumes them in Update.
// A manually allocated IUnknown/IDropTarget vtable avoids Mono COM callable-wrapper limitations.
internal sealed class NativeFileDrop : IDisposable {
 public volatile bool Hovering;
 public bool Ready { get; private set; }
 public string Error { get; private set; }
 readonly ConcurrentQueue<string[]> files=new ConcurrentQueue<string[]>();
 IntPtr window,previous,instance,table; bool ole,registered,disposed;
 Delegate[] methods; WindowProc windowProc; int refs=1;
 readonly uint message=RegisterWindowMessage("Amadeus.FileDrop.Lifecycle.v1");
 static readonly Guid Unknown=new Guid("00000000-0000-0000-C000-000000000046"),Target=new Guid("00000122-0000-0000-C000-000000000046");
 [StructLayout(LayoutKind.Sequential)] struct Point { public int x,y; }
 [StructLayout(LayoutKind.Sequential)] struct Format { public short format;public IntPtr target;public int aspect,index,medium; }
 [StructLayout(LayoutKind.Sequential)] struct Medium { public int type;public IntPtr data,release; }
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Query(IntPtr self,ref Guid id,out IntPtr result);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint Ref(IntPtr self);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Enter(IntPtr self,IntPtr data,uint keys,Point point,ref uint effect);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Over(IntPtr self,uint keys,Point point,ref uint effect);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Leave(IntPtr self);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetData(IntPtr self,ref Format format,out Medium medium);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CanGet(IntPtr self,ref Format format);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr WindowProc(IntPtr hwnd,uint msg,IntPtr wp,IntPtr lp);
 delegate bool EnumProc(IntPtr hwnd,IntPtr param);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr param);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder text,int count);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
 [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)] static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
 [DllImport("user32.dll")] static extern IntPtr CallWindowProc(IntPtr prev,IntPtr hwnd,uint msg,IntPtr wp,IntPtr lp);
 [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,uint msg,IntPtr wp,IntPtr lp);
 [DllImport("ole32.dll")] static extern int OleInitialize(IntPtr reserved);
 [DllImport("ole32.dll")] static extern void OleUninitialize();
 [DllImport("ole32.dll")] static extern int RegisterDragDrop(IntPtr hwnd,IntPtr target);
 [DllImport("ole32.dll")] static extern int RevokeDragDrop(IntPtr hwnd);
 [DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref Medium medium);
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern uint DragQueryFile(IntPtr drop,uint index,StringBuilder path,uint length);
 static Format FileFormat() {return new Format{format=15,aspect=1,index=-1,medium=1};}
 static T Method<T>(IntPtr obj,int slot) where T:class {
  return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),slot*IntPtr.Size),typeof(T)) as T;
 }
 public NativeFileDrop() {
  if(Application.platform!=RuntimePlatform.WindowsPlayer) return;
  try {
   uint pid=(uint)System.Diagnostics.Process.GetCurrentProcess().Id;
   EnumWindows((h,p)=>{GetWindowThreadProcessId(h,out uint owner);var name=new StringBuilder(128);GetClassName(h,name,name.Capacity);
    if(owner==pid&&name.ToString()=="UnityWndClass"){window=h;return false;}return true;},IntPtr.Zero);
   if(window==IntPtr.Zero)throw new InvalidOperationException("找不到程序窗口");
   methods=new Delegate[]{new Query(QueryInterface),new Ref(AddRef),new Ref(Release),new Enter(DragEnter),new Over(DragOver),new Leave(DragLeave),new Enter(Drop)};
   table=Marshal.AllocHGlobal(IntPtr.Size*methods.Length);instance=Marshal.AllocHGlobal(IntPtr.Size);
   for(int i=0;i<methods.Length;i++)Marshal.WriteIntPtr(table,i*IntPtr.Size,Marshal.GetFunctionPointerForDelegate(methods[i]));
   Marshal.WriteIntPtr(instance,table);
   windowProc=WndProc;previous=SetWindowLongPtr(window,-4,Marshal.GetFunctionPointerForDelegate(windowProc));
   if(previous==IntPtr.Zero)throw new InvalidOperationException("无法连接窗口拖放事件");
   // Execute OLE initialization on the window's owning thread, which pumps the native message loop.
   SendMessage(window,message,new IntPtr(1),IntPtr.Zero);
  } catch(Exception e){Error=e.Message;Dispose();}
 }
 int QueryInterface(IntPtr self,ref Guid id,out IntPtr result){result=IntPtr.Zero;if(id!=Unknown&&id!=Target)return unchecked((int)0x80004002);result=instance;AddRef(self);return 0;}
 uint AddRef(IntPtr self){return (uint)System.Threading.Interlocked.Increment(ref refs);}
 uint Release(IntPtr self){return (uint)System.Threading.Interlocked.Decrement(ref refs);}
 bool Supports(IntPtr data){try{var f=FileFormat();return Method<CanGet>(data,5)(data,ref f)==0;}catch{return false;}}
 int DragEnter(IntPtr self,IntPtr data,uint keys,Point point,ref uint effect){Hovering=Supports(data)&&(effect&1)!=0;effect=Hovering?1u:0u;return 0;}
 int DragOver(IntPtr self,uint keys,Point point,ref uint effect){effect=Hovering&&(effect&1)!=0?1u:0u;return 0;}
 int DragLeave(IntPtr self){Hovering=false;return 0;}
 int Drop(IntPtr self,IntPtr data,uint keys,Point point,ref uint effect){
  Hovering=false;uint allowed=effect;effect=0;Medium medium=default;
  try{
   if((allowed&1)==0)return 0;
   var f=FileFormat();if(Method<GetData>(data,3)(data,ref f,out medium)!=0)return 0;
   uint count=DragQueryFile(medium.data,uint.MaxValue,null,0);
   // The composer has one attachment slot; report multi-file drops instead of silently losing files.
   var paths=new string[Math.Min(count,2u)];
   for(uint i=0;i<paths.Length;i++){uint n=DragQueryFile(medium.data,i,null,0);var b=new StringBuilder((int)n+1);DragQueryFile(medium.data,i,b,(uint)b.Capacity);paths[i]=b.ToString();}
   if(paths.Length>0){files.Enqueue(paths);effect=1;}
  }catch{effect=0;}finally{if(medium.type!=0)ReleaseStgMedium(ref medium);}
  return 0;
 }
 IntPtr WndProc(IntPtr h,uint msg,IntPtr wp,IntPtr lp){
  if(msg==message){
   if(wp==new IntPtr(1)){
    int hr=OleInitialize(IntPtr.Zero);ole=hr>=0;
    if(ole){hr=RegisterDragDrop(window,instance);registered=hr>=0;Ready=registered;}
    if(hr<0)Error="文件拖放初始化失败（0x"+hr.ToString("X8")+"）";
   }else CleanupOle();
   return IntPtr.Zero;
  }
  if(msg==0x82)CleanupOle();
  return CallWindowProc(previous,h,msg,wp,lp);
 }
 void CleanupOle(){Hovering=false;Ready=false;if(registered){RevokeDragDrop(window);registered=false;}if(ole){OleUninitialize();ole=false;}}
 public bool TryTake(out string[] paths){return files.TryDequeue(out paths);}
 public void Dispose(){
  if(disposed)return;disposed=true;
  if(window!=IntPtr.Zero&&previous!=IntPtr.Zero&&IsWindow(window)){
   SendMessage(window,message,IntPtr.Zero,IntPtr.Zero);SetWindowLongPtr(window,-4,previous);
  }
  if(instance!=IntPtr.Zero)Marshal.FreeHGlobal(instance);if(table!=IntPtr.Zero)Marshal.FreeHGlobal(table);
  instance=table=IntPtr.Zero;Hovering=false;Ready=false;
 }
}
}
