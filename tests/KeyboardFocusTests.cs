// Windows desktop integration test. Uses a local dummy game window and a fake
// send spool; never starts the backend or sends messages to a public channel.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

static class KeyboardFocusTests {
 static ChatWindow chat;static Window game;static Application app;static TextBox input;static string home;
 static DispatcherTimer timer;static Queue<Action> steps=new Queue<Action>();static Dictionary<string,object> results=new Dictionary<string,object>();static int gameKeys;static IntPtr originalForeground;
 [StructLayout(LayoutKind.Explicit,Size=40)]struct Input {
  [FieldOffset(0)]public uint type;[FieldOffset(8)]public ushort key;[FieldOffset(12)]public uint flags;
 }
 [DllImport("user32.dll")]static extern uint SendInput(uint count,Input[] input,int size);
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll")]static extern IntPtr LoadKeyboardLayout(string id,uint flags);
 [DllImport("user32.dll")]static extern IntPtr ActivateKeyboardLayout(IntPtr layout,uint flags);
 static IntPtr oldLayout;
 static IntPtr ChatHandle {get{return new WindowInteropHelper(chat).Handle;}}
 static IntPtr GameHandle {get{return new WindowInteropHelper(game).Handle;}}
 static object Field(string name){return typeof(ChatWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(chat);}
 static void Call(string name,params object[] args){typeof(ChatWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(chat,args);}
 static void Check(string name,bool pass){results[name]=pass;if(!pass)throw new Exception(name);}
 static bool Visible {get{return ((StackPanel)Field("composerPanel")).Visibility==Visibility.Visible;}}
 static void Key(int vk,bool up){
  var fg=ComposerKeyboard.GetForegroundWindow();if(fg!=ChatHandle&&fg!=GameHandle)throw new Exception("User switched applications; stopped keyboard test");
  if(SendInput(1,new[]{new Input{type=1,key=(ushort)vk,flags=up?2u:0u}},40)!=1)throw new Exception("SendInput failed");
 }
 static void Press(int vk){Key(vk,false);Key(vk,true);}
 static void Open(){SetForegroundWindow(GameHandle);Call("OpenComposerFromGame",GameHandle);}
 static void Add(Action action){steps.Enqueue(action);}
 static void Result(string id,bool ok){File.WriteAllText(Path.Combine(home,"data","send-result.json"),new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"id",id},{"ok",ok},{"message",ok?"sent":"test failure"}}));Call("UpdateSendResult");}
 static void Finish(Exception error){
  timer.Stop();results["result"]=error==null?"ok":"failed";if(error!=null)results["error"]=error.ToString();
  try{Key(13,true);Key(27,true);Key(0x11,true);Key(0x10,true);}catch{}
  Call("CancelFocusReturn");Call("ExitApplication");game.Close();if(oldLayout!=IntPtr.Zero)ActivateKeyboardLayout(oldLayout,0);if(originalForeground!=IntPtr.Zero)SetForegroundWindow(originalForeground);
  File.WriteAllText(Path.Combine(home,"keyboard-focus-results.json"),new JavaScriptSerializer().Serialize(results));app.Shutdown(error==null?0:1);
 }
 [STAThread]static void Main(){
  home=Path.Combine(Path.GetTempPath(),"SCChatKeyboardTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(home);Environment.SetEnvironmentVariable("SCCHAT_HOME",home);
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"keyboard-test-home.txt"),home);
  originalForeground=ComposerKeyboard.GetForegroundWindow();oldLayout=ActivateKeyboardLayout(LoadKeyboardLayout("00000409",0),0);
  app=new Application();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  game=new Window{Title="SC Chat keyboard test target",Width=300,Height=120,Left=20,Top=20,Content=new TextBox()};game.PreviewKeyDown+=delegate{gameKeys++;};game.Show();
  chat=new ChatWindow(false,true);chat.Show();input=(TextBox)Field("composer");InputMethod.SetIsInputMethodEnabled(input,false);
  Add(delegate{Open();Check("opens_with_native_keyboard_focus",Visible&&input.IsKeyboardFocused&&ComposerKeyboard.GetForegroundWindow()==ChatHandle);Check("foreground_guard_active",((ComposerKeyboard)Field("composerKeyboard")).Active);Press(0x57);Press(0x41);Press(0x53);Press(0x44);});
  Add(delegate{results["typed_text"]=input.Text;results["game_keys_while_typing"]=gameKeys;results["focus_during_typing"]=input.IsKeyboardFocused;Check("typing_wasd_stays_in_composer",input.Text.Equals("wasd",StringComparison.OrdinalIgnoreCase)&&gameKeys==0);Press(9);Press(112);Press(123);});
  Add(delegate{Check("tab_and_function_keys_keep_focus",input.IsKeyboardFocused&&gameKeys==0&&Field("visibilityPrompt")==null);Key(0x11,false);Press(0x41);Key(0x11,true);Press(0x58);Press(0x25);Key(0x10,false);Press(0x41);Key(0x10,true);});
  Add(delegate{Check("editing_and_modifiers_work",input.Text=="Ax"&&gameKeys==0);input.Text="你好，飞行员。";Call("SubmitSendRequest",input.Text);Check("submit_hides_before_any_result",!Visible&&((string)Field("pendingSend")).Length>0);});
  Add(delegate{results["returning_focus"]=Field("returningFocus");results["saved_game"]=((IntPtr)Field("gameFocus")).ToInt64();results["expected_game"]=GameHandle.ToInt64();results["actual_foreground"]=ComposerKeyboard.GetForegroundWindow().ToInt64();var held=new List<int>();for(int k=8;k<255;k++)if((GetAsyncKeyState(k)&0x8000)!=0)held.Add(k);results["held_keys"]=held;Check("submit_returns_game_before_result",ComposerKeyboard.GetForegroundWindow()==GameHandle);Check("foreground_guard_released_after_submit",!((ComposerKeyboard)Field("composerKeyboard")).Active);string id=(string)Field("pendingSend");Result(id,false);Check("failure_does_not_reopen_or_steal_focus",!Visible&&ComposerKeyboard.GetForegroundWindow()==GameHandle);Check("failure_preserves_draft",input.Text=="你好，飞行员。");File.Delete(Path.Combine(home,"data","send-request.json"));Open();input.Clear();Key(13,false);});
  Add(delegate{Check("empty_enter_hides",!Visible);Check("held_enter_not_returned_to_game",ComposerKeyboard.GetForegroundWindow()==ChatHandle&&gameKeys==0);Key(13,false);});
  Add(delegate{Check("held_enter_does_not_reopen",!Visible&&gameKeys==0);Key(13,true);});
  Add(delegate{Check("enter_key_up_returns_game",ComposerKeyboard.GetForegroundWindow()==GameHandle);Press(0x57);});
  Add(delegate{Check("game_keys_restored",gameKeys==1);Open();input.Text="草稿";Press(27);});
  Add(delegate{Check("escape_returns_game_and_keeps_draft",!Visible&&input.Text=="草稿"&&ComposerKeyboard.GetForegroundWindow()==GameHandle);Open();Call("SetComposerVisible",false);SetForegroundWindow(GameHandle);});
  Add(delegate{Check("switch_away_releases_guard",!((ComposerKeyboard)Field("composerKeyboard")).Active&&!Visible);Open();Call("SubmitSendRequest",input.Text);});
  Add(delegate{string id=(string)Field("pendingSend");Result(id,true);Check("success_stays_hidden_and_clears",!Visible&&input.Text.Length==0&&ComposerKeyboard.GetForegroundWindow()==GameHandle);Check("no_live_backend",Field("backend")==null);});
  timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(350)};timer.Tick+=delegate{try{if(steps.Count==0){Finish(null);return;}steps.Dequeue()();}catch(Exception e){Finish(e);}};timer.Start();app.Run();
 }
}
