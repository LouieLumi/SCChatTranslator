using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

class Bootstrap {
 [STAThread]static void Main(string[] args){
  var app=new Application();var text=new TextBlock{Text="正在准备聊天翻译…",Foreground=Brushes.White,FontSize=16,Margin=new Thickness(22,22,22,14)};
  var progress=new ProgressBar{Height=5,Margin=new Thickness(22,0,22,22),Minimum=0,Maximum=100,IsIndeterminate=true,Foreground=new SolidColorBrush(Color.FromRgb(110,222,195))};
  var panel=new StackPanel();panel.Children.Add(text);panel.Children.Add(progress);
  var window=new Window{Title="星际公民聊天翻译",Width=360,Height=132,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=new SolidColorBrush(Color.FromRgb(20,28,35)),Content=panel};
  bool closed=false;window.Closed+=delegate{closed=true;};
  window.ContentRendered+=delegate{
   new Thread(delegate(){string staging=null;try{
    string home=Environment.GetEnvironmentVariable("SCCHAT_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SCChatTranslator");
    Directory.CreateDirectory(home);string hash;
    using(var reader=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("SCChat.Hash")))hash=reader.ReadToEnd().Trim();
    string versions=Path.Combine(home,"versions"),release=Path.Combine(versions,hash.Substring(0,16));Directory.CreateDirectory(versions);
    using(var gate=new Mutex(false,"Local\\SCChatPackageInstall")){
     try{gate.WaitOne();}catch(AbandonedMutexException){}
     try{
      if(!File.Exists(Path.Combine(release,".complete"))){
       staging=release+".install-"+Process.GetCurrentProcess().Id;Directory.CreateDirectory(staging);
       using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("SCChat.Payload")){
        using(var sha=SHA256.Create()){string actual=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();if(actual!=hash)throw new Exception("程序包校验失败，请重新复制 EXE");}
        stream.Position=0;
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)){
         int n=0;foreach(var entry in zip.Entries){
          if(closed)return;string target=Path.GetFullPath(Path.Combine(staging,entry.FullName));
          if(!target.StartsWith(staging+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("程序包路径无效");
          if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(target);continue;}
          Directory.CreateDirectory(Path.GetDirectoryName(target));using(var input=entry.Open())using(var output=File.Create(target))input.CopyTo(output);
          int percent=++n*100/zip.Entries.Count;
          if(n%15==0)window.Dispatcher.BeginInvoke(new Action(delegate{progress.IsIndeterminate=false;progress.Value=percent;text.Text="首次准备 "+percent+"%";}));
         }
        }
       }
       File.WriteAllText(Path.Combine(staging,".complete"),hash);if(Directory.Exists(release))Directory.Delete(release,true);Directory.Move(staging,release);staging=null;
      }
     }finally{gate.ReleaseMutex();}
    }
    if(closed)return;
    var info=new ProcessStartInfo(Path.Combine(release,"SCChatUI.exe"));info.UseShellExecute=false;info.WorkingDirectory=release;info.EnvironmentVariables["SCCHAT_HOME"]=home;
    // Only recognized application switches are forwarded, never shell command text.
    if(Array.IndexOf(args,"--smoke-test")>=0)info.Arguments="--smoke-test";
    if(Array.IndexOf(args,"--demo")>=0)info.Arguments+=" --demo";
    Process.Start(info);window.Dispatcher.BeginInvoke(new Action(window.Close));
   }catch(Exception e){if(!closed)window.Dispatcher.BeginInvoke(new Action(delegate{text.Text="启动失败";MessageBox.Show(window,e.Message,"聊天翻译",MessageBoxButton.OK,MessageBoxImage.Error);window.Close();}));}
    finally{if(staging!=null)try{Directory.Delete(staging,true);}catch{}}
   }){IsBackground=true}.Start();
  };app.Run(window);
 }
}
