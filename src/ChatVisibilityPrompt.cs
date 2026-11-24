using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

sealed class ChatVisibilityPrompt:Window {
 public int Choice {get;private set;}
 public ChatVisibilityPrompt(){
  Title="确认原生聊天框状态";Width=430;SizeToContent=SizeToContent.Height;Topmost=true;ShowInTaskbar=false;
  WindowStartupLocation=WindowStartupLocation.CenterScreen;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;
  FontFamily=new FontFamily("Microsoft YaHei UI");Foreground=Brushes.White;
  var body=new StackPanel{Margin=new Thickness(24)};
  body.Children.Add(new TextBlock{Text="游戏聊天框现在隐藏了吗？",FontSize=19,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)});
  body.Children.Add(new TextBlock{Text="按游戏画面选择。未选择时，回车仍交给游戏。",FontSize=12,Foreground=new SolidColorBrush(Color.FromRgb(170,190,201)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,20)});
  var buttons=new Grid();buttons.ColumnDefinitions.Add(new ColumnDefinition());buttons.ColumnDefinitions.Add(new ColumnDefinition());
  var hidden=new Button{Content="已隐藏",Padding=new Thickness(16,11,16,11),FontSize=16,Margin=new Thickness(0,0,7,0),Background=new SolidColorBrush(Color.FromRgb(36,84,78)),Foreground=Brushes.White,BorderThickness=new Thickness(0)};
  var visible=new Button{Content="仍显示",Padding=new Thickness(16,11,16,11),FontSize=16,Margin=new Thickness(7,0,0,0),Background=new SolidColorBrush(Color.FromRgb(47,60,72)),Foreground=Brushes.White,BorderThickness=new Thickness(0)};
  System.Windows.Automation.AutomationProperties.SetAutomationId(hidden,"NativeChatHidden");System.Windows.Automation.AutomationProperties.SetAutomationId(visible,"NativeChatVisible");
  hidden.Click+=delegate{Choice=2;Close();};visible.Click+=delegate{Choice=1;Close();};Grid.SetColumn(visible,1);buttons.Children.Add(hidden);buttons.Children.Add(visible);body.Children.Add(buttons);
  Content=new Border{Background=new SolidColorBrush(Color.FromArgb(248,19,28,37)),CornerRadius=new CornerRadius(12),Child=body};
  KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Close();}};
 }
}
