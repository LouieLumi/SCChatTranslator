using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

// Observes OS keyboard events; does not inject into or inspect the game process.
// Native chat visibility is NOT exposed by the chat service: each F12 requires the user to confirm visibility.
sealed class GameHotkeys:IDisposable {
 public bool Enabled;public int ChatState; // 0 unknown, 1 visible, 2 hidden; never persisted across launches.
 readonly Action<IntPtr> openComposer;readonly Action<int> changed;readonly Action<IntPtr> confirmVisibility;readonly HookProc callback;
 int lastGamePid;IntPtr hook;bool f12Down,returnDown,suppressReturn;
 delegate IntPtr HookProc(int code,IntPtr wParam,IntPtr lParam);
 [StructLayout(LayoutKind.Sequential)]struct KeyData {public uint key,scan,flags,time;public UIntPtr extra;}
 [DllImport("user32.dll")]static extern IntPtr SetWindowsHookEx(int id,HookProc proc,IntPtr module,uint thread);
 [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr hook);
 [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr w,IntPtr l);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 public GameHotkeys(Action<IntPtr> open,Action<int> stateChanged,Action<IntPtr> confirm){
  openComposer=open;changed=stateChanged;confirmVisibility=confirm;callback=Keyboard;
  hook=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
  if(hook==IntPtr.Zero)throw new InvalidOperationException("无法注册游戏快捷键");
 }
 public void Calibrate(int state){
  ChatState=state;lastGamePid=0;
  try{foreach(var p in Process.GetProcessesByName("StarCitizen")){using(p){lastGamePid=p.Id;break;}}}catch{}
  if(lastGamePid==0)ChatState=0;
 }
 bool IsGame(IntPtr h){try{uint pid;GetWindowThreadProcessId(h,out pid);using(var p=Process.GetProcessById((int)pid)){
  if(!p.ProcessName.Equals("StarCitizen",StringComparison.OrdinalIgnoreCase))return false;
  if(lastGamePid!=(int)pid){lastGamePid=(int)pid;ChatState=0;changed(0);}return true;
 }}catch{return false;}}
 public static bool ShouldIntercept(bool enabled,int state,bool gameForeground,bool modified){return enabled&&state==2&&gameForeground&&!modified;}
 IntPtr Keyboard(int code,IntPtr w,IntPtr l){
  if(code<0)return CallNextHookEx(hook,code,w,l);
  var key=(KeyData)Marshal.PtrToStructure(l,typeof(KeyData));if(key.key!=13&&key.key!=123)return CallNextHookEx(hook,code,w,l);
  bool down=w.ToInt32()==0x100||w.ToInt32()==0x104,up=w.ToInt32()==0x101||w.ToInt32()==0x105;
  if((key.flags&0x10)!=0)return CallNextHookEx(hook,code,w,l); // Ignore injected events.
  if(key.key==13&&up){returnDown=false;if(suppressReturn){suppressReturn=false;return new IntPtr(1);}}
  if(key.key==123&&up)f12Down=false;
  if(!down)return CallNextHookEx(hook,code,w,l);
  if(key.key==13&&suppressReturn)return new IntPtr(1);
  IntPtr foreground=GetForegroundWindow();bool game=Enabled&&IsGame(foreground);
  bool modified=(GetAsyncKeyState(0x10)&0x8000)!=0||(GetAsyncKeyState(0x11)&0x8000)!=0||(GetAsyncKeyState(0x12)&0x8000)!=0;
  if(key.key==123){
   if(!f12Down&&game&&!modified){ChatState=0;confirmVisibility(foreground);}f12Down=true;
   return CallNextHookEx(hook,code,w,l); // F12 always continues to the game.
  }
  bool first=!returnDown;returnDown=true;
  if(first&&ShouldIntercept(Enabled,ChatState,game,modified)){suppressReturn=true;openComposer(foreground);return new IntPtr(1);}
  return CallNextHookEx(hook,code,w,l);
 }
 public void Dispose(){if(hook!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}}
}
