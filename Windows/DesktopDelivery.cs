using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;

namespace PetJar;

public sealed class DesktopDelivery(Settings settings)
{
    public async Task<DeliveryResult> Deliver(Client client,string text,DeliveryMode mode)
    {
        if (!DeliveryRules.Valid(text)) return new("内容为空或超过 100 KB，未投递。");
        // Called on WPF's STA dispatcher. Source drafts are never cleared automatically.
        bool copied=false;
        if (mode!=DeliveryMode.NewChat)
        {
            try { Clipboard.SetText(text); copied=true; }
            catch (System.Runtime.InteropServices.COMException) { return new("剪贴板正被占用，未尝试填入；草稿仍保留。"); }
        }
        if (!settings.Executables.TryGetValue(client,out var path) || !File.Exists(path))
            return new(copied?"文字已复制。请先为这只宠物绑定 Windows 客户端，再手动粘贴。":"请先为这只宠物绑定 Windows 客户端。",Copied:copied);
        try
        {
            if (mode==DeliveryMode.NewChat)
            {
                if (client!=Client.ChatGPT) return new("这个入口只支持 ChatGPT。");
                var start=new ProcessStartInfo(path) { UseShellExecute=true };
                start.ArgumentList.Add(DeliveryRules.NewChatUrl(text)); Process.Start(start)?.Dispose();
                return new("已向绑定客户端请求打开新草稿，请在客户端确认文字。未确认填入，未点击发送。",Opened:true);
            }
            var title=settings.WindowTitles.GetValueOrDefault(client,"");
            var matches=Native.Matching(path,title);
            if (matches.Count==0)
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute=true })?.Dispose();
                for (int i=0;i<12 && matches.Count==0;i++) { await Task.Delay(250); matches=Native.Matching(path,title); }
            }
            if (matches.Count!=1)
                return new(matches.Count>1?"文字已复制；发现多个目标窗口。请在绑定设置中填写窗口标题以锁定会话，未尝试填入。":"文字已复制，已请求启动客户端；尚未找到唯一目标窗口，未尝试填入。",Copied:true);
            var target=matches[0];
            if (Native.IsIconic(target.Handle)) Native.ShowWindow(target.Handle,9);
            Native.SetForegroundWindow(target.Handle);
            await Task.Delay(500);
            bool active=Native.GetForegroundWindow()==target.Handle;
            if (mode==DeliveryMode.Copy)
                return new(active?"文字已复制，目标窗口已打开。请在目标会话中手动粘贴并发送。":"文字已复制；系统未确认目标窗口处于前台，请手动打开并粘贴。",Copied:true,Opened:active);
            if (!active) return new("文字已复制；目标窗口没有处于前台，未尝试填入。",Copied:true);
            // UIA may be slow in third-party providers. Keep it off the UI dispatcher,
            // and hold the recipient lock until the worker actually finishes.
            return await OnAutomationThread(() => Insert(target,text,mode));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        { return new("客户端打开失败，草稿仍保留。"+e.Message,Copied:copied); }
    }

    private static Task<T> OnAutomationThread<T>(Func<T> work)
    {
        var completion=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(() => { try { completion.SetResult(work()); } catch (Exception e) { completion.SetException(e); } });
        thread.IsBackground=true; thread.SetApartmentState(ApartmentState.MTA); thread.Start(); return completion.Task;
    }
    private static string Descriptor(AutomationElement element) => element.Current.Name+" "+element.Current.HelpText+" "+element.Current.AutomationId;
    private static bool Within(AutomationElement element,AutomationElement root)
    {
        for (int i=0;i<64 && element!=null;i++,element=TreeWalker.RawViewWalker.GetParent(element)) if (element.Equals(root)) return true;
        return false;
    }
    private static List<AutomationElement> Descendants(AutomationElement root)
    {
        var result=new List<AutomationElement>(); var stack=new Stack<AutomationElement>(); stack.Push(root);
        var clock=Stopwatch.StartNew();
        while (stack.Count>0 && result.Count<1500 && clock.ElapsedMilliseconds<700)
        {
            var next=stack.Pop(); result.Add(next);
            for (var child=TreeWalker.ControlViewWalker.GetFirstChild(next);child!=null;child=TreeWalker.ControlViewWalker.GetNextSibling(child))
            { stack.Push(child); if (stack.Count+result.Count>1500 || clock.ElapsedMilliseconds>=700) break; }
        }
        // An incomplete traversal cannot establish uniqueness.
        if (stack.Count>0 || clock.ElapsedMilliseconds>=700) throw new InvalidOperationException("目标控件过多或响应过慢，无法确认唯一输入框。");
        return result;
    }
    private static bool Editable(AutomationElement element) => element.Current.IsEnabled && !element.Current.IsOffscreen && !element.Current.IsPassword
        && DeliveryRules.IsComposer(Descriptor(element)) && !DeliveryRules.Excluded(Descriptor(element))
        && element.TryGetCurrentPattern(ValuePattern.Pattern,out var value) && !((ValuePattern)value).Current.IsReadOnly;
    private static bool ReadValue(AutomationElement element,out string value)
    {
        value="";
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern,out var pattern)) return false;
        value=((ValuePattern)pattern).Current.Value; return true;
    }
    private static bool Active(Native.Target target) => Native.GetForegroundWindow()==target.Handle;
    private static DeliveryResult Insert(Native.Target target,string text,DeliveryMode mode)
    {
        bool attempted=false,inserted=false;
        DeliveryResult Stop(string message) => new(message+" 源草稿仍保留。",Copied:true,Opened:true,Inserted:inserted);
        try
        {
            if (!Active(target)) return Stop("目标窗口已切换，未尝试填入。");
            var root=AutomationElement.FromHandle(target.Handle);
            var focused=AutomationElement.FocusedElement;
            AutomationElement? composer=null;
            if (focused!=null && Within(focused,root) && Editable(focused)) composer=focused;
            else
            {
                var candidates=Descendants(root).FindAll(Editable);
                if (candidates.Count==1) composer=candidates[0];
            }
            if (composer==null) return Stop("没有唯一识别出可读写的消息输入框，请在客户端选择会话。未尝试填入。");
            if (!ReadValue(composer,out var current) || !DeliveryRules.CanInsert(current,Descriptor(composer),false))
                return Stop("目标输入框已有草稿或无法读取，未覆盖原文。");
            composer.SetFocus();
            if (!Active(target) || !composer.Equals(AutomationElement.FocusedElement)
                || !ReadValue(composer,out current) || !DeliveryRules.Empty(current)) return Stop("焦点或目标草稿发生变化，未尝试填入。");
            // Verify the clipboard has not been replaced during client activation.
            var clipboardMatches=Application.Current.Dispatcher.Invoke(() => Clipboard.ContainsText() && Clipboard.GetText()==text);
            if (!clipboardMatches || !Active(target) || !composer.Equals(AutomationElement.FocusedElement)) return Stop("剪贴板或焦点发生变化，未尝试填入。");
            attempted=true;
            if (!Native.Paste()) return Stop("系统未完整接受粘贴按键，请检查客户端，勿重复投递。");
            Thread.Sleep(450);
            if (!Active(target) || !ReadValue(composer,out current) || current.Replace("\r\n","\n")!=text.Replace("\r\n","\n"))
                return Stop("已尝试粘贴，但未确认输入结果。请查看客户端，勿重复投递。");
            inserted=true;
            if (mode!=DeliveryMode.Send) return new("已确认文字填入，等待你在客户端发送。",true,true,true);
            // Search a small ancestor scope, never the whole window, and never guess Enter.
            var parent=composer; var buttons=new List<AutomationElement>();
            for (int i=0;i<4;i++)
            {
                parent=TreeWalker.ControlViewWalker.GetParent(parent);
                if (parent==null || parent.Equals(root) || parent.Current.ControlType==ControlType.Window) break;
                buttons=Descendants(parent).FindAll(e => e.Current.ControlType==ControlType.Button && e.Current.IsEnabled
                    && !e.Current.IsOffscreen && DeliveryRules.IsSend(e.Current.Name) && e.TryGetCurrentPattern(InvokePattern.Pattern,out _));
                if (buttons.Count>0) break;
            }
            if (buttons.Count!=1 || !Active(target) || !composer.Equals(AutomationElement.FocusedElement)
                || !ReadValue(composer,out current) || current.Replace("\r\n","\n")!=text.Replace("\r\n","\n"))
                return Stop("文字已填入；未能确认唯一发送按钮或焦点，未点击发送。");
            ((InvokePattern)buttons[0].GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            return new("已点击发送；服务端是否接收、回复内容请查看客户端。",true,true,true,true);
        }
        catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        { return Stop((attempted?"自动操作已停止，请检查客户端后再操作。":"未尝试填入。")+e.Message); }
    }
}
