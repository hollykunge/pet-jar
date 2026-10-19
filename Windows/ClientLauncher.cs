using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace PetJar;

internal sealed class PetClickGesture
{
    private Client? pressed,lastClick;
    private int count;
    internal void Begin(Client? client,int clickCount) {pressed=client;count=clickCount;}
    internal Client? Release(Client? client,bool dragged)
    {
        if(dragged||client==null||client!=pressed) {lastClick=null;pressed=null;return null;}
        var target=count==2&&lastClick==client?client:null;
        lastClick=target==null?client:null;pressed=null;return target;
    }
}

internal sealed record LaunchResult(bool Opened,bool NeedsBinding,string Message);
internal sealed class ClientLauncher(Settings settings)
{
    internal async Task<LaunchResult> OpenAsync(Client client)
    {
        if(!settings.Executables.TryGetValue(client,out var path)||!File.Exists(path))
            return new(false,true,$"先选择 {client.Name()} 对应的客户端。");
        try
        {
            // Opening a client never touches a draft, clipboard or send control.
            // Use the frontmost matching application window; conversation title
            // restrictions continue to apply only to the separate delivery flow.
            var matches=Native.Matching(path,"");
            if(matches.Count==0) {
                if(settings.AppUserModelIds.TryGetValue(client,out var appId)&&settings.AutoBindings.GetValueOrDefault(client)==path)Native.ActivatePackage(appId);
                else Process.Start(new ProcessStartInfo(path) {UseShellExecute=true})?.Dispose();
                for(int i=0;i<50&&matches.Count==0;i++) {await Task.Delay(200);matches=Native.Matching(path,"");}
            }
            if(matches.Count==0)return new(false,false,$"已请求打开 {client.Name()}，尚未确认客户端窗口。");
            var target=matches[0];
            if(Native.IsIconic(target.Handle))Native.ShowWindow(target.Handle,9);
            Native.SetForegroundWindow(target.Handle);await Task.Delay(100);
            bool opened=Native.GetForegroundWindow()==target.Handle;
            return new(opened,false,opened?$"已打开 {client.Name()}":$"{client.Name()} 已运行，请切换到客户端窗口。");
        }
        catch(Exception e) when(e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {return new(false,false,$"{client.Name()} 打开失败，请检查客户端绑定。");}
    }
}
