using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

class ChatWindow : Window {
 readonly string root=AppDomain.CurrentDomain.BaseDirectory;
 readonly string home=Environment.GetEnvironmentVariable("SCCHAT_HOME")??AppDomain.CurrentDomain.BaseDirectory;
 SettingsWindow settingsWindow;ChildJob childJob;GameHotkeys hotkeys;ChatVisibilityPrompt visibilityPrompt;readonly DispatcherTimer visibilityTimer=new DispatcherTimer();IntPtr confirmationGame;bool hotkeysEnabled;IntPtr gameFocus;
 readonly JavaScriptSerializer json=new JavaScriptSerializer();
 readonly DispatcherTimer timer=new DispatcherTimer();
 readonly DispatcherTimer scrollBarTimer=new DispatcherTimer();
 readonly Dictionary<string,ChatRow> rows=new Dictionary<string,ChatRow>();
 readonly List<Thumb> resizeGrips=new List<Thumb>();
 readonly bool smoke,demo;
 System.Windows.Forms.NotifyIcon tray;bool exiting;HwndSource source;
 Grid view;StackPanel controlsHover,composerPanel;TextBox composer;Button sendButton,sendLanguage;TextBlock sendStatus;readonly DispatcherTimer controlsTimer=new DispatcherTimer();bool showOriginal=true,typing;string sendMode="en",pendingSend="";
 Border backdrop;StackPanel messages;ScrollViewer scroll;Button lockButton,latest;TextBlock hint;Thumb move;
 System.Windows.Shapes.Path resizeMark;
 Process backend;
 DateTime lastWrite=DateTime.MinValue,launched;
 bool locked,closing,updating,internalScroll,following=true,smokeQueued,demoStarted;
 double blackAlpha=0.68;
 string groupId="",status="",engineStatus="",lastSignature="";
 int renderUpdates,insertedRows;
 IList groups=new ArrayList();
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h,int i);
 [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h,int i,IntPtr v);
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint flags);
 string DataPath(string n){return System.IO.Path.Combine(home,"data",n);}
 static string Str(Dictionary<string,object> d,string k){object v;return d.TryGetValue(k,out v)&&v!=null?Convert.ToString(v):"";}
 static bool Bool(Dictionary<string,object> d,string k){object v;return d.TryGetValue(k,out v)&&v is bool&&(bool)v;}
 static Brush BrushOf(string v){return (Brush)new BrushConverter().ConvertFromString(v);}
 static void TryDelete(string p){try{if(File.Exists(p))File.Delete(p);}catch(IOException){}}

 public ChatWindow(bool test,bool demonstration){
  smoke=test;demo=demonstration;Title="星际公民聊天翻译";Width=620;Height=540;MinWidth=320;MinHeight=200;
  WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;
  FontFamily=new FontFamily("Microsoft YaHei UI");Foreground=Brushes.White;ShowActivated=!test;ShowInTaskbar=false;Topmost=true;Focusable=true;
  WindowStartupLocation=WindowStartupLocation.Manual;Left=90;Top=100;
  Directory.CreateDirectory(System.IO.Path.Combine(home,"data"));json.MaxJsonLength=4*1024*1024;
  view=(Grid)XamlReader.Parse(File.ReadAllText(System.IO.Path.Combine(root,"Overlay.xaml"),Encoding.UTF8));Content=view;
  backdrop=(Border)view.FindName("Backdrop");messages=(StackPanel)view.FindName("Messages");scroll=(ScrollViewer)view.FindName("Scroll");
  lockButton=(Button)view.FindName("Lock");latest=(Button)view.FindName("Latest");hint=(TextBlock)view.FindName("EmptyHint");
  controlsHover=(StackPanel)view.FindName("ControlsHover");composerPanel=(StackPanel)view.FindName("ComposerPanel");composer=(TextBox)view.FindName("Composer");sendButton=(Button)view.FindName("Send");sendLanguage=(Button)view.FindName("SendLanguage");sendStatus=(TextBlock)view.FindName("SendStatus");
  controlsTimer.Interval=TimeSpan.FromSeconds(3);controlsTimer.Tick+=delegate{controlsTimer.Stop();if(!controlsHover.IsMouseOver)FadeControls(false);};
  controlsHover.MouseEnter+=delegate{controlsTimer.Stop();FadeControls(true);};controlsHover.MouseLeave+=delegate{controlsTimer.Stop();controlsTimer.Start();};
  sendButton.Click+=delegate{BeginSend();};
  composer.KeyDown+=delegate(object sender,KeyEventArgs e){
   if(e.Key==Key.Enter){e.Handled=true;if(!e.IsRepeat)BeginSend();}
   else if(e.Key==Key.Escape){e.Handled=true;SetComposerVisible(false);ReturnToGame();}
  };
  PreviewKeyDown+=delegate(object sender,KeyEventArgs e){
   if(e.Key==Key.Enter&&!e.IsRepeat&&Keyboard.Modifiers==ModifierKeys.None&&composerPanel.Visibility!=Visibility.Visible&&settingsWindow==null&&visibilityPrompt==null){e.Handled=true;OpenComposerFromGame(IntPtr.Zero);}
  };
  sendLanguage.Click+=delegate{ToggleSendLanguage();};
  composer.PreviewMouseLeftButtonDown+=delegate{typing=true;ApplyActivationStyle();Activate();composer.Focus();};
  Deactivated+=delegate{SetComposerVisible(false);};
  ((Button)view.FindName("Settings")).Click+=delegate{OpenSettings();};
  move=(Thumb)view.FindName("MoveGrip");resizeMark=(System.Windows.Shapes.Path)view.FindName("ResizeMark");
  ((Button)view.FindName("Close")).Click+=delegate{HideToTray();};lockButton.Click+=delegate{SetLocked(!locked);SaveSettings();};
  latest.Click+=delegate{following=true;ToLatest();};
  move.DragDelta+=delegate(object s,DragDeltaEventArgs e){if(!locked){Left+=e.HorizontalChange;Top+=e.VerticalChange;}};
  move.DragCompleted+=delegate{SaveSettings();};
  foreach(string side in new string[]{"L","R","T","B","TL","TR","BL","BR"}){
   Thumb grip=(Thumb)view.FindName("Resize"+side);resizeGrips.Add(grip);
   grip.DragDelta+=delegate(object sender,DragDeltaEventArgs e){ResizeFrom(Convert.ToString(((Thumb)sender).Tag),e.HorizontalChange,e.VerticalChange);};
   grip.DragCompleted+=delegate{SaveSettings();};
  }
  scrollBarTimer.Interval=TimeSpan.FromSeconds(3);scrollBarTimer.Tick+=delegate{scrollBarTimer.Stop();FadeScrollBar(false);};
  scroll.PreviewMouseWheel+=delegate(object s,MouseWheelEventArgs e){if(e.Delta==0)return;if(e.Delta>0)following=false;FadeScrollBar(true);scrollBarTimer.Stop();scrollBarTimer.Start();};
  scroll.ScrollChanged+=delegate(object s,ScrollChangedEventArgs e){
   if(!updating&&!internalScroll&&e.ExtentHeightChange==0&&e.ViewportHeightChange==0&&Math.Abs(e.VerticalChange)>0.01){following=scroll.ScrollableHeight-scroll.VerticalOffset<4;}
   latest.Visibility=following?Visibility.Collapsed:Visibility.Visible;
  };
  view.ContextMenu=new ContextMenu();view.ContextMenuOpening+=delegate{BuildMenu();};
  LoadSettings();sendLanguage.Content=sendMode=="zh"?"中":"英";SetLocked(locked);UpdateBackground();controlsTimer.Start();
  if(!demo)try{hotkeys=new GameHotkeys(h=>Dispatcher.BeginInvoke(new Action(delegate{OpenComposerFromGame(h);})),state=>{},h=>Dispatcher.BeginInvoke(new Action(delegate{QueueVisibilityPrompt(h);})));hotkeys.Enabled=hotkeysEnabled;}catch(Exception){engineStatus="快捷键不可用";}
  Icon=BitmapFrame.Create(new Uri(System.IO.Path.Combine(root,"app.ico")));InitTray();
  SourceInitialized+=delegate{source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);source.AddHook(WindowMessage);ApplyActivationStyle();};
  ContentRendered+=delegate{if(demo)StartDemo();else StartReader();};
  Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){
   if(!exiting&&!smoke){e.Cancel=true;HideToTray();return;}
   controlsTimer.Stop();scrollBarTimer.Stop();visibilityTimer.Stop();if(visibilityPrompt!=null)visibilityPrompt.Close();if(hotkeys!=null)hotkeys.Dispose();StopReader();SaveSettings();if(tray!=null){tray.Visible=false;tray.Dispose();tray=null;}
   if(source!=null)source.RemoveHook(WindowMessage);
  };
  StateChanged+=delegate{if(WindowState==WindowState.Minimized)HideToTray();};
  timer.Interval=TimeSpan.FromMilliseconds(600);timer.Tick+=delegate{UpdateReader();};
 }
 void QueueVisibilityPrompt(IntPtr game){
  confirmationGame=game;if(visibilityPrompt!=null){visibilityPrompt.Activate();return;}
  visibilityTimer.Stop();visibilityTimer.Interval=TimeSpan.FromMilliseconds(250);visibilityTimer.Tick-=VisibilityTick;visibilityTimer.Tick+=VisibilityTick;visibilityTimer.Start();
 }
 void VisibilityTick(object sender,EventArgs args){
  visibilityTimer.Stop();if(closing||hotkeys==null||!hotkeys.Enabled)return;
  var prompt=new ChatVisibilityPrompt();visibilityPrompt=prompt;
  try{prompt.ShowDialog();if(hotkeys!=null){hotkeys.Calibrate(prompt.Choice);}}
  finally{visibilityPrompt=null;if(!closing&&confirmationGame!=IntPtr.Zero)SetForegroundWindow(confirmationGame);confirmationGame=IntPtr.Zero;}
 }
 void OpenComposerFromGame(IntPtr h){
  gameFocus=h;typing=true;Show();WindowState=WindowState.Normal;Topmost=true;SetComposerVisible(true);ApplyActivationStyle();SetForegroundWindow(new WindowInteropHelper(this).Handle);Activate();composer.Focus();Keyboard.Focus(composer);composer.CaretIndex=composer.Text.Length;
 }
 void ToggleSendLanguage(){sendMode=sendMode=="zh"?"en":"zh";sendLanguage.Content=sendMode=="zh"?"中":"英";SaveSettings();if(composerPanel.Visibility==Visibility.Visible&&!composer.IsReadOnly){composer.Focus();Keyboard.Focus(composer);}}
 void SetComposerVisible(bool visible){
  composerPanel.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
  if(!visible){if(composer.IsKeyboardFocusWithin)Keyboard.ClearFocus();if(IsActive)Keyboard.Focus(this);typing=false;ApplyActivationStyle();}
  UpdateComposerLayout();
 }
 void UpdateComposerLayout(){
  bool stick=following;double offset=scroll.VerticalOffset;updating=true;
  double bottom=composerPanel.Visibility==Visibility.Visible?(sendStatus.Visibility==Visibility.Visible?83:62):14;
  scroll.Margin=new Thickness(17,39,9,bottom);latest.Margin=new Thickness(0,0,20,bottom+2);scroll.UpdateLayout();
  if(stick)ToLatest();else{internalScroll=true;scroll.ScrollToVerticalOffset(offset);internalScroll=false;}
  following=stick;updating=false;
 }
 void ShowSendStatus(string text){sendStatus.Text=text;sendStatus.Visibility=string.IsNullOrEmpty(text)?Visibility.Collapsed:Visibility.Visible;UpdateComposerLayout();}
 void ReturnToGame(){if(gameFocus==IntPtr.Zero)return;var h=gameFocus;gameFocus=IntPtr.Zero;typing=false;ApplyActivationStyle();SetForegroundWindow(h);}
 void FadeControls(bool visible){controlsHover.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(visible?1:0,TimeSpan.FromMilliseconds(visible?160:280)));}
 void FadeScrollBar(bool visible){
  scroll.ApplyTemplate();var bar=scroll.Template.FindName("PART_VerticalScrollBar",scroll) as ScrollBar;if(bar==null)return;
  bar.IsHitTestVisible=visible;bar.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(visible?1:0,TimeSpan.FromMilliseconds(visible?100:180)));
 }
 void SetShowOriginal(bool value){showOriginal=value;foreach(var row in rows.Values)row.ShowOriginal(value);if(following)Dispatcher.BeginInvoke(new Action(ToLatest),DispatcherPriority.Loaded);}
 void BeginSend(){
  if(pendingSend.Length>0)return;string text=composer.Text.Trim();
  if(text.Length==0){composer.Clear();ShowSendStatus("");SetComposerVisible(false);ReturnToGame();return;}
  if(demo){ShowSendStatus("演示模式");return;}
  if(groupId.Length==0||!status.StartsWith("已连接 · ")||(DateTime.UtcNow-lastWrite).TotalSeconds>60){ShowSendStatus("频道未连接");return;}
  try{
   string id=Guid.NewGuid().ToString("N");var request=new Dictionary<string,object>{{"id",id},{"group_id",groupId},{"text",text},{"language",sendMode},{"created_at",(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds}};
   string path=DataPath("send-request.json"),temp=path+".tmp";if(File.Exists(path)){ShowSendStatus("上一条尚未处理");return;}
   File.WriteAllText(temp,json.Serialize(request),new UTF8Encoding(false));File.Move(temp,path);pendingSend=id;sendButton.IsEnabled=false;composer.IsReadOnly=true;sendLanguage.IsEnabled=false;ShowSendStatus("发送中…");ReturnToGame();
  }catch(IOException){ShowSendStatus("提交失败");}
 }
 void UpdateSendResult(){
  if(pendingSend.Length==0)return;
  try{var result=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(DataPath("send-result.json"),Encoding.UTF8));if(Str(result,"id")!=pendingSend)return;
   sendStatus.ToolTip=null;
   if(Bool(result,"ok")){composer.Clear();ShowSendStatus("");SetComposerVisible(false);}
   else{SetComposerVisible(true);ShowSendStatus(Str(result,"message"));}
   pendingSend="";sendButton.IsEnabled=true;composer.IsReadOnly=false;sendLanguage.IsEnabled=true;
  }catch(IOException){}catch(ArgumentException){}
 }
 void UpdateBackground(){
  // A zero-alpha layered-window pixel is click-through in Win32, even if WPF hit testing is enabled.
  backdrop.Background=new SolidColorBrush(Color.FromArgb((byte)Math.Max(1,255*blackAlpha),0,0,0));
 }
 void InitTray(){
  tray=new System.Windows.Forms.NotifyIcon();tray.Text="星际公民聊天翻译";
  tray.Icon=new System.Drawing.Icon(System.IO.Path.Combine(root,"app.ico"));
  var menu=new System.Windows.Forms.ContextMenuStrip();
  menu.Items.Add("显示聊天窗口",null,delegate{Dispatcher.BeginInvoke(new Action(ShowOverlay));});
  menu.Items.Add("设置",null,delegate{Dispatcher.BeginInvoke(new Action(delegate{ShowOverlay();OpenSettings();}));});
  menu.Items.Add("锁定 / 解锁位置",null,delegate{Dispatcher.BeginInvoke(new Action(delegate{ShowOverlay();SetLocked(!locked);SaveSettings();}));});
  menu.Items.Add("隐藏到托盘",null,delegate{Dispatcher.BeginInvoke(new Action(HideToTray));});
  menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
  menu.Items.Add("退出",null,delegate{Dispatcher.BeginInvoke(new Action(ExitApplication));});
  tray.ContextMenuStrip=menu;tray.DoubleClick+=delegate{Dispatcher.BeginInvoke(new Action(ShowOverlay));};tray.Visible=true;
 }
 void HideToTray(){if(settingsWindow!=null)settingsWindow.Close();scrollBarTimer.Stop();FadeScrollBar(false);SetComposerVisible(false);SaveSettings();Hide();}
 void ShowOverlay(){Show();WindowState=WindowState.Normal;Topmost=true;if(!locked){ApplyActivationStyle();Activate();}}
 void ExitApplication(){exiting=true;Close();}
 IntPtr WindowMessage(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled){
  // Keep lock clicks functional without stealing game focus. Unlock restores normal activation.
  if(msg==0x0021){if(composerPanel!=null&&composerPanel.IsMouseOver){typing=true;ApplyActivationStyle();}handled=true;return new IntPtr(locked&&!typing?3:1);}
  return IntPtr.Zero;
 }
 void StartDemo(){
  if(demoStarted)return;demoStarted=true;launched=DateTime.Now;
  groups=new ArrayList{new Dictionary<string,object>{{"id","demo"},{"name","演示频道"}}};groupId="demo";status="演示模式 · 示例聊天";engineStatus="本机 · Argos 1.9";
  var items=new ArrayList();
  string[,] samples={{"Pilot 01","Anyone need a ride to Area18?","有人需要搭便船去 18 区吗？"},{"Pilot 02","Looking for a crew for my Carrack.","我的克拉克正在招募船员。"},{"Pilot 03","我在机库等你们，准备出发。",""},{"Pilot 04","Meet at the space station. Bring a tractor beam.","在空间站集合，记得带上牵引光束。"},{"Pilot 05","Quantum travel is ready. Let's go!","量子航行准备完毕，出发！"}};
  for(int i=0;i<samples.GetLength(0);i++)items.Add(new Dictionary<string,object>{{"id","demo-"+i},{"nickname",samples[i,0]},{"time","20:30:0"+i},{"display_original",samples[i,1]},{"translation",samples[i,2]},{"is_chinese",i==2},{"term_reference",i==1?"Carrack → 克拉克":""}});
  RenderMessages(items);hint.Visibility=Visibility.Collapsed;ToLatest();
  if(smoke)Dispatcher.BeginInvoke(new Action(delegate{FinishSmoke(items);}),DispatcherPriority.ApplicationIdle);
 }
 void ChooseChannel(string id){
  File.WriteAllText(DataPath("selection.json"),json.Serialize(new Dictionary<string,object>{{"id",id}}),Encoding.UTF8);
 }
 FrameworkElement LockIcon(bool isLocked){
  var path=new System.Windows.Shapes.Path{Data=Geometry.Parse(isLocked?"M 5,10 L 5,7 A 5,5 0 0 1 15,7 L 15,10 M 3,10 L 17,10 L 17,20 L 3,20 Z M 10,14 L 10,17":"M 5,10 L 5,7 A 5,5 0 0 1 15,7 M 3,10 L 17,10 L 17,20 L 3,20 Z M 10,14 L 10,17"),Stroke=isLocked?BrushOf("#99F1DE"):Brushes.White,StrokeThickness=1.5,Stretch=Stretch.Uniform,Width=16,Height=18};
  return path;
 }
 void OpenSettings(){
  if(settingsWindow!=null){settingsWindow.Activate();return;}
  double before=blackAlpha;bool originalBefore=showOriginal;bool keysBefore=hotkeysEnabled;int stateBefore=hotkeys==null?0:hotkeys.ChatState;
  var dialog=new SettingsWindow(root,home,()=>groups,()=>groupId,()=>status.Contains(engineStatus)?status:engineStatus+"\n"+status,ChooseChannel,blackAlpha,a=>{blackAlpha=a;UpdateBackground();},showOriginal,SetShowOriginal,hotkeysEnabled,hotkeys==null?0:hotkeys.ChatState,(enabled,state)=>{hotkeysEnabled=enabled;if(hotkeys!=null){hotkeys.Enabled=enabled;hotkeys.Calibrate(state);}});settingsWindow=dialog;dialog.Owner=this;dialog.Topmost=Topmost;
  try{dialog.ShowDialog();}finally{if(dialog.Applied)SaveSettings();else{blackAlpha=before;UpdateBackground();SetShowOriginal(originalBefore);hotkeysEnabled=keysBefore;if(hotkeys!=null){hotkeys.Enabled=keysBefore;hotkeys.ChatState=stateBefore;}}settingsWindow=null;}
 }
 void ApplyActivationStyle(){
  var h=new WindowInteropHelper(this).Handle;if(h==IntPtr.Zero)return;
  long style=GetWindowLongPtr(h,-20).ToInt64();const long NoActivate=0x08000000,Transparent=0x20;
  style&=~Transparent;SetWindowLongPtr(h,-20,new IntPtr(locked&&!typing?style|NoActivate:style&~NoActivate));
  SetWindowPos(h,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0010|0x0020);
 }
 void SetLocked(bool value){
  locked=value;Topmost=true;move.IsHitTestVisible=!value;move.Cursor=value?Cursors.Arrow:Cursors.SizeAll;
  foreach(var g in resizeGrips)g.IsHitTestVisible=!value;
  resizeMark.Visibility=value?Visibility.Collapsed:Visibility.Visible;
  lockButton.Content=LockIcon(value);
  System.Windows.Automation.AutomationProperties.SetName(lockButton,value?"解锁":"锁定");
  lockButton.ToolTip=value?"已锁定并置顶；点击后可移动和调整尺寸":"锁定当前位置并置顶到游戏上方";
  lockButton.Foreground=value?BrushOf("#99F1DE"):Brushes.White;
  ApplyActivationStyle();
  if(!value&&IsVisible)Activate();
 }
 void ResizeFrom(string side,double x,double y){
  if(locked)return;
  if(side.Contains("L")){double right=Left+Width;Width=Math.Max(MinWidth,Width-x);Left=right-Width;}
  if(side.Contains("R"))Width=Math.Max(MinWidth,Width+x);
  if(side.Contains("T")){double bottom=Top+Height;Height=Math.Max(MinHeight,Height-y);Top=bottom-Height;}
  if(side.Contains("B"))Height=Math.Max(MinHeight,Height+y);
  if(following)Dispatcher.BeginInvoke(new Action(ToLatest),DispatcherPriority.Loaded);
 }
 void LoadSettings(){
  try{
   var d=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(DataPath("ui-settings.json"),Encoding.UTF8));
   Width=Math.Max(MinWidth,Math.Min(3000,Convert.ToDouble(d["width"])));Height=Math.Max(MinHeight,Math.Min(2200,Convert.ToDouble(d["height"])));
   Left=Convert.ToDouble(d["left"]);Top=Convert.ToDouble(d["top"]);locked=Convert.ToBoolean(d["locked"]);
   if(d.ContainsKey("game_hotkeys"))hotkeysEnabled=Convert.ToBoolean(d["game_hotkeys"]);
   if(d.ContainsKey("show_original"))showOriginal=Convert.ToBoolean(d["show_original"]);
   if(d.ContainsKey("send_language"))sendMode=Convert.ToString(d["send_language"])=="zh"?"zh":"en";
   if(d.ContainsKey("black_alpha"))blackAlpha=Math.Max(0,Math.Min(1,Convert.ToDouble(d["black_alpha"])));
   if(Left+100<SystemParameters.VirtualScreenLeft||Left>SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-100)Left=SystemParameters.WorkArea.Left+60;
   if(Top+50<SystemParameters.VirtualScreenTop||Top>SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-50)Top=SystemParameters.WorkArea.Top+60;
  }catch(Exception){}
 }
 void SaveSettings(){
  if(smoke||demo)return;
  try{File.WriteAllText(DataPath("ui-settings.json"),json.Serialize(new Dictionary<string,object>{{"left",Left},{"top",Top},{"width",Width},{"height",Height},{"locked",locked},{"black_alpha",blackAlpha},{"show_original",showOriginal},{"send_language",sendMode},{"game_hotkeys",hotkeysEnabled}}),Encoding.UTF8);}catch(IOException){}
 }
 void BuildMenu(){
  var menu=view.ContextMenu;menu.Items.Clear();
  var toggle=new MenuItem{Header=locked?"解锁位置":"锁定并置顶"};toggle.Click+=delegate{SetLocked(!locked);SaveSettings();};menu.Items.Add(toggle);
  var opacity=new MenuItem{Header="背景深浅"};
  foreach(int percent in new int[]{35,50,68,80,90}){
   int p=percent;var option=new MenuItem{Header=p+"%",IsCheckable=true,IsChecked=Math.Abs(blackAlpha-p/100.0)<.01};
   option.Click+=delegate{blackAlpha=p/100.0;UpdateBackground();SaveSettings();};opacity.Items.Add(option);
  }menu.Items.Add(opacity);
  var settings=new MenuItem{Header="设置"};settings.Click+=delegate{OpenSettings();};menu.Items.Add(settings);
  var newest=new MenuItem{Header="显示最新消息"};newest.Click+=delegate{following=true;ToLatest();};menu.Items.Add(newest);
  var exit=new MenuItem{Header="退出程序"};exit.Click+=delegate{ExitApplication();};menu.Items.Add(exit);
 }
 void StartReader(){
  if(backend!=null)return;
  SettingsStore.Ensure(home);
  TryDelete(DataPath("stop"));TryDelete(DataPath("pause"));TryDelete(DataPath("state.json"));launched=DateTime.Now;
  try{
   var info=new ProcessStartInfo(System.IO.Path.Combine(root,"runtime","python.exe"),"-u backend.py");
   info.EnvironmentVariables["SCCHAT_HOME"]=home;
   info.WorkingDirectory=root;info.UseShellExecute=false;info.CreateNoWindow=true;info.RedirectStandardOutput=true;info.RedirectStandardError=true;
   backend=new Process();backend.StartInfo=info;
   backend.OutputDataReceived+=delegate(object s,DataReceivedEventArgs e){};
   backend.ErrorDataReceived+=delegate(object s,DataReceivedEventArgs e){if(e.Data!=null)try{File.AppendAllText(DataPath("backend-error.log"),e.Data+Environment.NewLine);}catch(IOException){}};
   backend.Start();childJob=new ChildJob();childJob.Add(backend);backend.BeginOutputReadLine();backend.BeginErrorReadLine();timer.Start();
  }catch(Exception e){hint.Text="无法启动读取程序："+e.Message;}
 }
 void StopReader(){
  if(closing)return;closing=true;timer.Stop();if(settingsWindow!=null)settingsWindow.Close();
  try{File.WriteAllText(DataPath("stop"),"");if(backend!=null&&!backend.HasExited&&!backend.WaitForExit(1500))backend.Kill();if(backend!=null)backend.Dispose();}catch(Exception){}finally{if(childJob!=null)childJob.Dispose();}
 }
 void ToLatest(){
  internalScroll=true;scroll.UpdateLayout();scroll.ScrollToEnd();scroll.UpdateLayout();internalScroll=false;latest.Visibility=Visibility.Collapsed;
 }
 void RenderMessages(IList incoming){
  string signature=json.Serialize(incoming);if(signature==lastSignature)return;lastSignature=signature;renderUpdates++;
  bool stick=following;double offset=scroll.VerticalOffset;double removedAbove=0;
  FrameworkElement anchor=null;double anchorDelta=0;
  if(!stick)foreach(FrameworkElement child in messages.Children){
   double y=child.TransformToAncestor(messages).Transform(new Point(0,0)).Y;
   if(y+child.ActualHeight+child.Margin.Bottom>offset){anchor=child;anchorDelta=offset-y;break;}
  }
  updating=true;
  var ids=new HashSet<string>();foreach(Dictionary<string,object> m in incoming)ids.Add(Str(m,"id"));
  foreach(string id in new List<string>(rows.Keys))if(!ids.Contains(id)){
   var row=rows[id];var at=row.Panel.TransformToAncestor(messages).Transform(new Point(0,0));
   if(at.Y<offset)removedAbove+=row.Panel.ActualHeight+row.Panel.Margin.Top+row.Panel.Margin.Bottom;
   messages.Children.Remove(row.Panel);rows.Remove(id);
  }
  int index=0;
  foreach(Dictionary<string,object> m in incoming){
   string id=Str(m,"id");ChatRow row;
   if(!rows.TryGetValue(id,out row)){row=new ChatRow();rows.Add(id,row);messages.Children.Insert(index,row.Panel);insertedRows++;}
   else if(messages.Children.IndexOf(row.Panel)!=index){messages.Children.Remove(row.Panel);messages.Children.Insert(index,row.Panel);}
   row.Update(m);row.ShowOriginal(showOriginal);index++;
  }
  scroll.UpdateLayout();
  if(stick)ToLatest();else{
   double target=offset-removedAbove;
   if(anchor!=null&&messages.Children.Contains(anchor))target=anchor.TransformToAncestor(messages).Transform(new Point(0,0)).Y+anchorDelta;
   internalScroll=true;scroll.ScrollToVerticalOffset(Math.Max(0,target));scroll.UpdateLayout();internalScroll=false;
  }
  updating=false;following=stick;latest.Visibility=stick?Visibility.Collapsed:Visibility.Visible;
 }
 void UpdateReader(){
  if(closing)return;
  UpdateSendResult();
  try{
   if(File.Exists(DataPath("state.json"))){var stamp=File.GetLastWriteTimeUtc(DataPath("state.json"));if(stamp!=lastWrite){
    var state=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(DataPath("state.json"),Encoding.UTF8));lastWrite=stamp;status=Str(state,"status");engineStatus=Str(state,"translation_backend");groups=(IList)state["groups"];
    string nextGroup=Str(state,"group_id");if(nextGroup!=groupId){groupId=nextGroup;messages.Children.Clear();rows.Clear();lastSignature="";following=true;}
    var incoming=(IList)state["messages"];RenderMessages(incoming);
    hint.Visibility=incoming.Count==0?Visibility.Visible:Visibility.Collapsed;hint.Text=status;
    lockButton.ToolTip=(locked?"已锁定并置顶；点击解锁":"拖动上沿、拉动边缘调整窗口；点击锁定并置顶")+"\n"+status;
    if(smoke&&incoming.Count>=3&&status.StartsWith("已连接 · ")&&!status.EndsWith("正在翻译新消息")&&!smokeQueued){smokeQueued=true;Dispatcher.BeginInvoke(new Action(delegate{FinishSmoke(incoming);}),DispatcherPriority.ApplicationIdle);}
   }}
   if(smoke&&!smokeQueued&&(DateTime.Now-launched).TotalSeconds>90){smokeQueued=true;FinishSmoke(new ArrayList());}
   if(backend!=null&&backend.HasExited&&!closing){timer.Stop();hint.Visibility=Visibility.Visible;hint.Text="读取程序已停止，请重新打开窗口";}
  }catch(IOException){}catch(Exception e){hint.Visibility=Visibility.Visible;hint.Text="界面更新失败："+e.Message;}
 }
 class ChatRow {
  bool chinese;
  public StackPanel Panel=new StackPanel();readonly TextBlock header=new TextBlock(),original=new TextBlock(),translation=new TextBlock();string originalValue="",translatedValue="";
  public ChatRow(){
   Panel.Margin=new Thickness(0,0,0,16);
   header.FontSize=13;header.FontWeight=FontWeights.SemiBold;header.Foreground=BrushOf("#9DDFF2");header.Margin=new Thickness(0,0,0,5);
   original.FontSize=17;original.FontWeight=FontWeights.SemiBold;original.FontFamily=new FontFamily("Bahnschrift, Microsoft YaHei UI");original.TextWrapping=TextWrapping.Wrap;original.LineHeight=25;
   translation.FontSize=17;translation.FontWeight=FontWeights.SemiBold;translation.FontFamily=new FontFamily("Microsoft YaHei UI");translation.Foreground=BrushOf("#A5F1DF");translation.TextWrapping=TextWrapping.Wrap;translation.LineHeight=27;translation.Margin=new Thickness(0,4,0,0);
   foreach(var text in new[]{header,original,translation})text.Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=2,ShadowDepth=1,Opacity=.8};
   Panel.Children.Add(header);Panel.Children.Add(original);Panel.Children.Add(translation);
   var menu=new ContextMenu();var copy=new MenuItem{Header="复制原文"};copy.Click+=delegate{Clipboard.SetText(originalValue);};menu.Items.Add(copy);
   var translated=new MenuItem{Header="复制译文"};translated.Click+=delegate{if(translatedValue.Length>0)Clipboard.SetText(translatedValue);};menu.Items.Add(translated);Panel.ContextMenu=menu;
  }
  public void ShowOriginal(bool value){original.Visibility=chinese||value?Visibility.Visible:Visibility.Collapsed;}
  public void Update(Dictionary<string,object> m){
   string h=Str(m,"nickname")+"  ·  "+Str(m,"time");if(header.Text!=h)header.Text=h;
   originalValue=m.ContainsKey("display_original")?Str(m,"display_original"):Str(m,"original");translatedValue=Str(m,"translation");
   if(original.Text!=originalValue)original.Text=originalValue;if(translation.Text!=translatedValue)translation.Text=translatedValue;
   string terms=Str(m,"term_reference");original.ToolTip=terms.Length>0?"术语参考\n"+terms:null;
   chinese=Bool(m,"is_chinese");original.Foreground=chinese?BrushOf("#F2FFFFFF"):BrushOf("#E8FFFFFF");original.FontSize=17;
   translation.Visibility=chinese||translatedValue.Length==0?Visibility.Collapsed:Visibility.Visible;
  }
 }
 void FinishSmoke(IList incoming){
  timer.Stop();var checks=new Dictionary<string,object>();
  checks["main_channel_picker_removed"]=view.FindName("Channels")==null;checks["lock_is_icon"]=lockButton.Content is System.Windows.Shapes.Path;checks["settings_button"]=view.FindName("Settings")!=null;
  checks["messages"]=rows.Count;checks["transparent"]=AllowsTransparency;checks["background_alpha"]=(int)((SolidColorBrush)backdrop.Background).Color.A;checks["borderless"]=WindowStyle==WindowStyle.None;checks["no_outline"]=backdrop.BorderThickness.Left==0;checks["composer_present"]=composer!=null;checks["hotkeys_visible_pass_through"]=!GameHotkeys.ShouldIntercept(true,1,true,false);checks["hotkeys_unknown_pass_through"]=!GameHotkeys.ShouldIntercept(true,0,true,false);checks["hotkeys_other_apps_pass_through"]=!GameHotkeys.ShouldIntercept(true,2,false,false);checks["hotkeys_hidden_enter"]=GameHotkeys.ShouldIntercept(true,2,true,false);
  checks["composer_starts_hidden"]=composerPanel.Visibility==Visibility.Collapsed&&scroll.Margin.Bottom==14;
  OpenComposerFromGame(IntPtr.Zero);checks["enter_opens_composer"]=composerPanel.Visibility==Visibility.Visible&&composer.IsKeyboardFocused;
  string modeBefore=sendMode;ToggleSendLanguage();checks["language_one_click_switch"]=sendMode!=modeBefore&&(Convert.ToString(sendLanguage.Content)=="中"||Convert.ToString(sendLanguage.Content)=="英")&&sendLanguage.ContextMenu==null;ToggleSendLanguage();
  composer.Text="未发送的测试草稿";SetComposerVisible(false);checks["cancel_preserves_draft"]=composer.Text=="未发送的测试草稿"&&composerPanel.Visibility==Visibility.Collapsed;
  SetComposerVisible(true);pendingSend="__ui_failed_send__";File.WriteAllText(DataPath("send-result.json"),json.Serialize(new Dictionary<string,object>{{"id",pendingSend},{"ok",false},{"message","测试失败"}}),Encoding.UTF8);UpdateSendResult();
  checks["send_failure_keeps_draft_visible"]=composer.Text=="未发送的测试草稿"&&composerPanel.Visibility==Visibility.Visible;
  pendingSend="__ui_success_send__";File.WriteAllText(DataPath("send-result.json"),json.Serialize(new Dictionary<string,object>{{"id",pendingSend},{"ok",true},{"message","已发送"}}),Encoding.UTF8);UpdateSendResult();
  checks["send_success_hides_and_clears"]=composer.Text.Length==0&&composerPanel.Visibility==Visibility.Collapsed&&scroll.Margin.Bottom==14;
  TryDelete(DataPath("send-result.json"));
  double w=Width,h=Height,l=Left,t=Top;bool wasLocked=locked;SetLocked(true);ResizeFrom("BR",30,20);
  checks["locked_topmost"]=Topmost;checks["locked_resize_blocked"]=Width==w&&Height==h;checks["locked_grips_disabled"]=!resizeGrips[0].IsHitTestVisible;
  SetLocked(false);ResizeFrom("BR",20,10);checks["unlocked_resizes"]=Width==w+20&&Height==h+10;
  checks["unlocked_stays_topmost"]=Topmost;
  checks["unlocked_accepts_activation"]=(GetWindowLongPtr(new WindowInteropHelper(this).Handle,-20).ToInt64()&0x08000020)==0;
  bool handled=false;checks["unlocked_mouse_activates"]=WindowMessage(IntPtr.Zero,0x21,IntPtr.Zero,IntPtr.Zero,ref handled).ToInt32()==1;
  double alphaBefore=blackAlpha;blackAlpha=0;UpdateBackground();checks["transparent_background_hits"] = ((SolidColorBrush)backdrop.Background).Color.A>0;blackAlpha=alphaBefore;UpdateBackground();
  HideToTray();checks["tray_hide_keeps_running"]=!IsVisible&&!closing&&tray.Visible;ShowOverlay();checks["tray_restores_window"]=IsVisible;Width=w;Height=h;Left=l;Top=t;
  SetShowOriginal(false);bool onlyTranslations=true;foreach(var row in rows.Values){var original=(TextBlock)row.Panel.Children[1];var translated=(TextBlock)row.Panel.Children[2];if(translated.Visibility==Visibility.Visible&&original.Visibility!=Visibility.Collapsed)onlyTranslations=false;}
  checks["original_toggle_hides_english"]=onlyTranslations;SetShowOriginal(true);checks["hover_hide_delay_seconds"]=controlsTimer.Interval.TotalSeconds;
  RenderMessages(incoming);object first=null;if(messages.Children.Count>0)first=messages.Children[0];
  following=false;internalScroll=true;scroll.ScrollToVerticalOffset(Math.Min(80,scroll.ScrollableHeight));scroll.UpdateLayout();internalScroll=false;double held=scroll.VerticalOffset;
  var changed=(IList)json.DeserializeObject(json.Serialize(incoming));
  if(changed.Count>0){var m=(Dictionary<string,object>)changed[changed.Count-1];m["translation"]=Str(m,"translation")+" ";}
  RenderMessages(changed);checks["history_offset_preserved"]=Math.Abs(held-scroll.VerticalOffset)<1;checks["row_identity_preserved"]=first==null||Object.ReferenceEquals(first,messages.Children[0]);
  following=true;RenderMessages(incoming);ToLatest();checks["latest_at_bottom"]=Math.Abs(scroll.ScrollableHeight-scroll.VerticalOffset)<2;
  var appended=new ArrayList();foreach(object m in incoming)appended.Add(m);
  appended.Add(new Dictionary<string,object>{{"id","__ui_append_test__"},{"nickname","界面验证"},{"time","00:00:00"},{"display_original","仅用于窗口追加测试"},{"translation",""},{"is_chinese",true}});
  int before=insertedRows;RenderMessages(appended);
  checks["append_one_row_only"]=insertedRows==before+1&&(first==null||Object.ReferenceEquals(first,messages.Children[0]));
  checks["append_stays_at_latest"]=Math.Abs(scroll.ScrollableHeight-scroll.VerticalOffset)<2;
  RenderMessages(incoming);ToLatest();
  SetLocked(wasLocked);UpdateLayout();
  Dispatcher.BeginInvoke(new Action(delegate {
  var visual=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);visual.Render(this);
  var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(visual));using(var file=File.Create(DataPath("ui-smoke.png")))png.Save(file);
  checks["result"]="ok";checks["render_updates"]=renderUpdates;checks["inserted_rows"]=insertedRows;
  File.WriteAllText(DataPath("ui-smoke.json"),json.Serialize(checks),Encoding.UTF8);ExitApplication();
  }),DispatcherPriority.ApplicationIdle);
 }
 [STAThread]static void Main(string[] args){
  bool demoMode=Array.IndexOf(args,"--demo")>=0;
  if(demoMode){string demoHome=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SCChatTranslator-Demo");Environment.SetEnvironmentVariable("SCCHAT_HOME",demoHome);}
  bool owns;using(var mutex=new Mutex(true,demoMode?"Local\\SCChatTranslator-Demo":"Local\\SCChatTranslator",out owns)){
   if(!owns){MessageBox.Show("聊天翻译窗口已经打开。");return;}
   var app=new Application();app.DispatcherUnhandledException+=delegate(object s,DispatcherUnhandledExceptionEventArgs e){
    try{File.AppendAllText(System.IO.Path.Combine(Environment.GetEnvironmentVariable("SCCHAT_HOME")??AppDomain.CurrentDomain.BaseDirectory,"data","ui-error.log"),e.Exception.ToString()+Environment.NewLine);}catch(Exception){}
    e.Handled=true;
   };
   var window=new ChatWindow(Array.IndexOf(args,"--smoke-test")>=0,demoMode);
   app.SessionEnding+=delegate{window.exiting=true;};app.Run(window);
  }
 }
}
