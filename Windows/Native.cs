using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PetJar;

internal static class Native
{
    [ComImport,Guid("2e941141-7f97-4756-ba1d-9decde894a3d"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,[MarshalAs(UnmanagedType.LPWStr)] string arguments,uint options,out uint processId);
    }
    internal static void ActivatePackage(string appUserModelId)
    {
        object manager=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"))!)!;
        try {Marshal.ThrowExceptionForHR(((IApplicationActivationManager)manager).ActivateApplication(appUserModelId,"",2,out _));}
        finally {Marshal.FinalReleaseComObject(manager);}
    }
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window,int id);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    private delegate bool WindowCallback(IntPtr window,IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback,IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count,INPUT[] inputs,int size);
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public INPUTUNION data; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT keyboard;
        [FieldOffset(0)] public MOUSEINPUT mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT
    { public ushort key,scan; public uint flags,time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
    { public int x,y; public uint mouseData,flags,time; public UIntPtr extra; }
    internal sealed record Target(IntPtr Handle,string Title,string Executable);
    internal static List<Target> Windows()
    {
        var targets=new List<Target>();
        EnumWindows((window,_) => {
            if (!IsWindowVisible(window)) return true;
            var title=new StringBuilder(1024); GetWindowText(window,title,title.Capacity);
            if (title.Length==0) return true;
            try
            {
                GetWindowThreadProcessId(window,out var id);
                if (id==Environment.ProcessId) return true;
                using var process=Process.GetProcessById((int)id);
                if (process.MainModule?.FileName is string path) targets.Add(new(window,title.ToString(),path));
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { }
            return true;
        },IntPtr.Zero);
        return targets;
    }
    internal static List<Target> Matching(string path,string title) => Windows().FindAll(target =>
        string.Equals(Path.GetFullPath(target.Executable),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase)
        && (title.Length==0 || target.Title.Contains(title,StringComparison.OrdinalIgnoreCase)));
    internal static bool Paste()
    {
        static INPUT Key(ushort key,bool up) => new() { type=1, data=new() { keyboard=new() { key=key,flags=up?2u:0u } } };
        // Never synthesize Enter. Only paste into a separately verified editor.
        var inputs=new[] { Key(0x11,false),Key(0x56,false),Key(0x56,true),Key(0x11,true) };
        return SendInput(4,inputs,Marshal.SizeOf<INPUT>())==4;
    }
}
