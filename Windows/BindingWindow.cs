using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PetJar;

internal sealed class BindingWindow : Window
{
    internal BindingWindow(Settings settings,Client client)
    {
        Title=$"绑定 {client.Name()} 客户端";Width=460;Height=320;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=DesktopTheme.Cream;FontFamily=new("Microsoft YaHei UI");Topmost=true;
        var stack=new StackPanel {Margin=new(20)};
        stack.Children.Add(new TextBlock {Text="选择这个角色对应的本地程序或已打开的窗口。",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)});
        var path=new TextBox {Text=settings.Executables.GetValueOrDefault(client,""),IsReadOnly=true,Margin=new(0,0,0,8)};stack.Children.Add(path);
        var choose=new Button {Content="选择 .exe 文件…",HorizontalAlignment=HorizontalAlignment.Left};choose.Click+=(_,_)=> {
            var dialog=new Microsoft.Win32.OpenFileDialog {Filter="Windows 应用程序 (*.exe)|*.exe",CheckFileExists=true};if(dialog.ShowDialog(this)==true) path.Text=dialog.FileName;
        };stack.Children.Add(choose);
        var windows=Native.Windows().Where(w=>!w.Executable.EndsWith("\\PetJar.exe",StringComparison.OrdinalIgnoreCase)).ToList();
        var picker=new ComboBox {ItemsSource=windows,DisplayMemberPath="Title",Margin=new(0,10,0,8)};stack.Children.Add(picker);
        picker.SelectionChanged+=(_,_)=>{if(picker.SelectedItem is Native.Target target)path.Text=target.Executable;};
        stack.Children.Add(new TextBlock {Text="账号额度来源（请与客户端使用同一登录账号）：",FontSize=11});
        var quotaChoices=new[] {new {Source=QuotaSource.Auto,Name="自动识别客户端"},new {Source=QuotaSource.Codex,Name="本机 Codex 登录账号"},new {Source=QuotaSource.Claude,Name="本机 Claude Code 登录账号"},new {Source=QuotaSource.Unavailable,Name="暂不接入额度"}};
        var quota=new ComboBox {ItemsSource=quotaChoices,DisplayMemberPath="Name",SelectedValuePath="Source",SelectedValue=settings.QuotaSources.GetValueOrDefault(client,QuotaSource.Auto),Margin=new(0,6,0,12)};stack.Children.Add(quota);
        var save=new Button {Content="保存绑定",HorizontalAlignment=HorizontalAlignment.Right};save.Click+=(_,_)=> {
            if(!File.Exists(path.Text)||!path.Text.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)) {MessageBox.Show(this,"请选择有效的 .exe 客户端。");return;}
            settings.Executables[client]=path.Text;
            settings.AutoBindings.Remove(client);settings.AppUserModelIds.Remove(client);
            settings.QuotaSources[client]=quota.SelectedValue is QuotaSource source?source:QuotaSource.Auto;
            if(settings.Save())Close();else MessageBox.Show(this,"绑定已在本次运行生效，但保存失败，请检查本地目录权限。");
        };stack.Children.Add(save);Content=stack;
    }
}
