using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

static class SettingsStore {
 public static string Value(Dictionary<string,object> d,string key){object x;return d.TryGetValue(key,out x)&&x!=null?Convert.ToString(x):"";}
 public static string DetectGame(){
  try{foreach(var p in Process.GetProcessesByName("StarCitizen")){try{string dir=Path.GetDirectoryName(p.MainModule.FileName);if(Path.GetFileName(dir).Equals("Bin64",StringComparison.OrdinalIgnoreCase))dir=Path.GetDirectoryName(dir);if(File.Exists(Path.Combine(dir,"loginData.json")))return dir;}catch{}}}catch{}
  foreach(var drive in DriveInfo.GetDrives())if(drive.DriveType==DriveType.Fixed){string p=Path.Combine(drive.Name,"Program Files","Roberts Space Industries","StarCitizen","LIVE");if(File.Exists(Path.Combine(p,"loginData.json")))return p;}
  return "";
 }
 public static Dictionary<string,object> Load(string home){
  var j=new JavaScriptSerializer();var d=new Dictionary<string,object>{{"mode","argos"},{"game_dir",DetectGame()},{"api_endpoint",""},{"api_key",""},{"api_model",""}};
  try{var saved=j.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(home,"settings.json"),Encoding.UTF8));foreach(var pair in saved)d[pair.Key]=pair.Value;return d;}catch{}
  try{var old=j.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(home,"config.json"),Encoding.UTF8));if(Directory.Exists(Value(old,"game_dir")))d["game_dir"]=Value(old,"game_dir");}catch{}
  try{var old=j.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(home,"api.json"),Encoding.UTF8));d["api_endpoint"]=Value(old,"endpoint");d["api_key"]=Value(old,"api_key");d["api_model"]=Value(old,"model");}catch{}
  return d;
 }
 public static void Save(string home,Dictionary<string,object> d){Directory.CreateDirectory(home);string file=Path.Combine(home,"settings.json"),temp=file+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(d),new UTF8Encoding(false));if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}
 public static void Ensure(string home){if(!File.Exists(Path.Combine(home,"settings.json")))Save(home,Load(home));}
 public static string Endpoint(string value){
  value=value.Trim().TrimEnd('/');Uri u;if(!Uri.TryCreate(value,UriKind.Absolute,out u)||(u.Scheme!="http"&&u.Scheme!="https")||u.UserInfo.Length>0||u.Query.Length>0||u.Fragment.Length>0)throw new Exception("请填写有效的 HTTP / HTTPS API 地址");
  if(value.EndsWith("/chat/completions",StringComparison.OrdinalIgnoreCase))return value;
  if(u.AbsolutePath=="/")return value+"/v1/chat/completions";
  return value+"/chat/completions";
 }
}

class SettingsWindow:Window {
 public bool Applied {get;private set;}
 readonly string home;readonly Grid view;readonly JavaScriptSerializer json=new JavaScriptSerializer();
 readonly Func<IList> groups;readonly Func<string> groupId,status;readonly Action<string> selectChannel;
 readonly DispatcherTimer timer=new DispatcherTimer();
 ComboBox channels,mode,apiModel;TextBox endpoint,gameDir;PasswordBox key;TextBlock feedback,engine;
 string groupSignature="";bool channelsTouched,updating;
 class Choice {public string Id;public string Name;public override string ToString(){return Name;}}
 T Get<T>(string name)where T:class{return view.FindName(name)as T;}
 public SettingsWindow(string root,string appHome,Func<IList> getGroups,Func<string> getGroupId,Func<string> getStatus,Action<string> choose,double initialAlpha,Action<double> previewAlpha,bool initialOriginal,Action<bool> previewOriginal,bool initialHotkeys,int initialChatState,Action<bool,int> previewHotkeys){
  home=appHome;groups=getGroups;groupId=getGroupId;status=getStatus;selectChannel=choose;
  Title="聊天翻译设置";Width=570;Height=Math.Min(780,SystemParameters.WorkArea.Height-60);MinHeight=400;
  WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontFamily=new FontFamily("Microsoft YaHei UI");
  view=(Grid)XamlReader.Parse(File.ReadAllText(Path.Combine(root,"Settings.xaml"),Encoding.UTF8));Content=view;
  channels=Get<ComboBox>("Channels");mode=Get<ComboBox>("Mode");apiModel=Get<ComboBox>("ApiModel");endpoint=Get<TextBox>("Endpoint");gameDir=Get<TextBox>("GameDir");key=Get<PasswordBox>("ApiKey");feedback=Get<TextBlock>("Feedback");engine=Get<TextBlock>("EngineStatus");
  var cfg=SettingsStore.Load(home);string selected=SettingsStore.Value(cfg,"mode");mode.SelectedIndex=selected=="hy_local"?1:selected=="api"?2:0;
  endpoint.Text=SettingsStore.Value(cfg,"api_endpoint");key.Password=SettingsStore.Value(cfg,"api_key");apiModel.Text=SettingsStore.Value(cfg,"api_model");gameDir.Text=SettingsStore.Value(cfg,"game_dir");
  mode.SelectionChanged+=delegate{ShowMode();};ShowMode();
  try{var glossary=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"glossary.json"),Encoding.UTF8));Get<TextBlock>("GlossaryInfo").Text="已启用星际公民中英术语库 · "+glossary["count"]+" 条";}catch{}
  var alpha=Get<Slider>("Transparency");alpha.Value=Math.Round((1-initialAlpha)*100);Get<TextBlock>("AlphaValue").Text=((int)alpha.Value)+"%";
  alpha.ValueChanged+=delegate{Get<TextBlock>("AlphaValue").Text=((int)alpha.Value)+"%";previewAlpha(1-alpha.Value/100);};
  var original=Get<CheckBox>("ShowOriginal");original.IsChecked=initialOriginal;original.Checked+=delegate{previewOriginal(true);};original.Unchecked+=delegate{previewOriginal(false);};
  var enableKeys=Get<CheckBox>("GameHotkeys");var chatState=Get<ComboBox>("NativeChatState");enableKeys.IsChecked=initialHotkeys;chatState.SelectedIndex=initialChatState;
  Action keysChanged=delegate{previewHotkeys(enableKeys.IsChecked==true,Math.Max(0,chatState.SelectedIndex));};enableKeys.Checked+=delegate{keysChanged();};enableKeys.Unchecked+=delegate{keysChanged();};chatState.SelectionChanged+=delegate{keysChanged();};
  channels.SelectionChanged+=delegate{if(!updating)channelsTouched=true;};
  Get<Grid>("TitleBar").MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e){if(e.OriginalSource is TextBlock||e.OriginalSource==Get<Grid>("TitleBar"))DragMove();};
  Get<Button>("Cancel").Click+=delegate{Close();};Get<Button>("Save").Click+=delegate{Apply();};
  Get<Button>("Browse").Click+=delegate{var dialog=new Microsoft.Win32.OpenFileDialog{Title="选择游戏登录文件或程序",Filter="游戏文件|loginData.json;StarCitizen.exe",CheckFileExists=true};if(dialog.ShowDialog(this)==true){string p=Path.GetDirectoryName(dialog.FileName);if(Path.GetFileName(p).Equals("Bin64",StringComparison.OrdinalIgnoreCase))p=Path.GetDirectoryName(p);gameDir.Text=p;}};
  Get<Button>("FetchModels").Click+=delegate{ApiAction(false);};Get<Button>("TestApi").Click+=delegate{ApiAction(true);};
  timer.Interval=TimeSpan.FromSeconds(1);timer.Tick+=delegate{Refresh();};Loaded+=delegate{Refresh();timer.Start();};Closed+=delegate{timer.Stop();};
 }
 void ShowMode(){int i=mode.SelectedIndex;Height=Math.Min(i==2?1020:i==1?790:720,SystemParameters.WorkArea.Height-60);if(IsLoaded)Top=Math.Max(SystemParameters.WorkArea.Top,Math.Min(Top,SystemParameters.WorkArea.Bottom-Height));Get<TextBlock>("ArgosInfo").Visibility=i==0?Visibility.Visible:Visibility.Collapsed;Get<StackPanel>("HyPanel").Visibility=i==1?Visibility.Visible:Visibility.Collapsed;Get<StackPanel>("ApiPanel").Visibility=i==2?Visibility.Visible:Visibility.Collapsed;}
 void Refresh(){
  engine.Text=status();
  if(channels.IsDropDownOpen)return;var gs=groups();string signature=json.Serialize(gs);if(signature==groupSignature&&channels.Items.Count>0)return;groupSignature=signature;
  string selected=channelsTouched&&channels.SelectedItem!=null?((Choice)channels.SelectedItem).Id:"";
  if(!channelsTouched){try{selected=SettingsStore.Value(json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(home,"data","selection.json"),Encoding.UTF8)),"id");}catch{}}
  updating=true;channels.Items.Clear();channels.Items.Add(new Choice{Id="",Name="自动跟随当前服务器"});Choice picked=null;
  foreach(Dictionary<string,object> g in gs){var c=new Choice{Id=SettingsStore.Value(g,"id"),Name=SettingsStore.Value(g,"name")};channels.Items.Add(c);if(c.Id==selected)picked=c;}
  channels.SelectedItem=picked??channels.Items[0];updating=false;
 }
 void Apply(){try{
  string m=mode.SelectedIndex==1?"hy_local":mode.SelectedIndex==2?"api":"argos";string url=endpoint.Text.Trim();
  if(m=="api"){url=SettingsStore.Endpoint(url);if(string.IsNullOrWhiteSpace(apiModel.Text))throw new Exception("请填写或选择 API 模型");}
  string game=gameDir.Text.Trim().Trim('"');if(game.Length>0&&!Directory.Exists(game))throw new Exception("游戏目录不存在，请重新选择 LIVE 目录");
  var cfg=new Dictionary<string,object>{{"mode",m},{"game_dir",game},{"api_endpoint",url},{"api_key",key.Password},{"api_model",apiModel.Text.Trim()}};
  SettingsStore.Save(home,cfg);if(channels.SelectedItem!=null)selectChannel(((Choice)channels.SelectedItem).Id);Applied=true;Close();
 }catch(Exception e){feedback.Text=e.Message;}}
 void ApiAction(bool translate){
  string url,secret=key.Password,model=apiModel.Text.Trim();try{url=SettingsStore.Endpoint(endpoint.Text);if(translate&&model.Length==0)throw new Exception("先填写 API 模型");}catch(Exception e){feedback.Text=e.Message;return;}
  Get<Button>("FetchModels").IsEnabled=false;Get<Button>("TestApi").IsEnabled=false;feedback.Text=translate?"正在测试翻译…":"正在读取模型列表…";
  ThreadPool.QueueUserWorkItem(delegate{
   string error=null,result=null;List<string> models=new List<string>();
   try{
    string address=translate?url:url.Substring(0,url.Length-"chat/completions".Length)+"models";
    var request=(HttpWebRequest)WebRequest.Create(address);request.Timeout=12000;request.ReadWriteTimeout=12000;request.AllowAutoRedirect=false;request.Proxy=null;
    if(secret.Length>0)request.Headers[HttpRequestHeader.Authorization]="Bearer "+secret;
    if(translate){request.Method="POST";request.ContentType="application/json";byte[] body=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new {model=model,messages=new[]{new {role="user",content="将以下文本翻译为简体中文，只输出译文：Hello, pilot."}},max_tokens=80,stream=false}));using(var s=request.GetRequestStream())s.Write(body,0,body.Length);}
    string raw;using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){char[] b=new char[1024*1024];int n=reader.ReadBlock(b,0,b.Length);raw=new string(b,0,n);}
    var d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(raw);
    if(translate){var choices=(IList)d["choices"];var choice=(Dictionary<string,object>)choices[0];result=SettingsStore.Value((Dictionary<string,object>)choice["message"],"content");if(result.Length==0)throw new Exception("接口没有返回译文");}
    else{foreach(Dictionary<string,object> item in (IList)d["data"]){string id=SettingsStore.Value(item,"id");if(id.Length>0)models.Add(id);}}
   }catch(WebException e){var response=e.Response as HttpWebResponse;error=response==null?"无法连接，请检查地址与网络":"接口返回 HTTP "+(int)response.StatusCode;}
    catch{error="响应格式不兼容，请使用 Chat Completions 接口";}
   Dispatcher.BeginInvoke(new Action(delegate{if(!IsLoaded)return;Get<Button>("FetchModels").IsEnabled=true;Get<Button>("TestApi").IsEnabled=true;
    if(error!=null)feedback.Text=error;else if(translate)feedback.Text="连接成功："+result;else{apiModel.Items.Clear();foreach(string id in models)apiModel.Items.Add(id);apiModel.Text=model.Length>0?model:models.Count>0?models[0]:"";feedback.Text=models.Count>0?"已读取 "+models.Count+" 个模型，也可手动填写。":"接口未提供模型列表，可以手动填写模型名称。";}
   }));
  });
 }
}
