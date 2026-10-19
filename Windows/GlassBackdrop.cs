using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace PetJar;

// Native background blur samples the windows behind the composer, rather than
// blurring the editor or the pet artwork. This HWND is exactly the input
// rectangle; the overlapping avatar has its own per-pixel transparent HWND.
// Older systems keep a legible tint.
internal static class GlassBackdrop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left,Right,Top,Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int State,Flags; public uint Tint; public int Animation; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionAttribute { public int Attribute; public IntPtr Data; public UIntPtr Size; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private delegate bool SetCompositionAttribute(IntPtr window,ref CompositionAttribute attribute);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr window,ref Margins margins);

    internal static void Attach(Window window,Border surface)
    {
        bool supported=OperatingSystem.IsWindowsVersionAtLeast(10,0,22621)&&!SystemParameters.HighContrast;
        window.AllowsTransparency=!supported;
        surface.Background=Tint(!supported);
        if(!supported)return;
        WindowChrome.SetWindowChrome(window,new WindowChrome {
            CaptionHeight=0,ResizeBorderThickness=new(0),GlassFrameThickness=new(-1),
            CornerRadius=new(8),UseAeroCaptionButtons=false
        });
        void Apply()
        {
            var handle=new WindowInteropHelper(window).Handle;
            var source=HwndSource.FromHwnd(handle);
            if(source?.CompositionTarget is {} target)target.BackgroundColor=Colors.Transparent;
            var margins=new Margins {Left=-1,Right=-1,Top=-1,Bottom=-1};
            var frameResult=DwmExtendFrameIntoClientArea(handle,ref margins);
            int light=0,rounded=2,none=1,acrylic=3;
            DwmSetWindowAttribute(handle,20,ref light,sizeof(int));
            DwmSetWindowAttribute(handle,33,ref rounded,sizeof(int));
            DwmSetWindowAttribute(handle,38,ref none,sizeof(int));
            // Blur without the system preset's opaque luminosity layer. The
            // WPF surface supplies only a light translucent tint. Retain the
            // documented Acrylic preset if the optional blur entry is absent.
            bool transparent=frameResult>=0&&TryTransparentBlur(handle);
            int backdropResult=transparent?0:DwmSetWindowAttribute(handle,38,ref acrylic,sizeof(int));
            surface.Background=Tint(frameResult<0||backdropResult<0);
        }
        window.SourceInitialized+=(_,_)=>Apply();
        window.Loaded+=(_,_)=>Apply();
    }
    private static bool TryTransparentBlur(IntPtr window)
    {
        if(!NativeLibrary.TryLoad("user32.dll",out var library))return false;
        IntPtr data=IntPtr.Zero;
        try
        {
            if(!NativeLibrary.TryGetExport(library,"SetWindowCompositionAttribute",out var export))return false;
            var set=Marshal.GetDelegateForFunctionPointer<SetCompositionAttribute>(export);
            // ACCENT_ENABLE_BLURBEHIND. Keep the backdrop visible beneath
            // the WPF tint; Acrylic fixes its luminosity too close to white.
            var policy=new AccentPolicy {State=3};
            int size=Marshal.SizeOf<AccentPolicy>();data=Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(policy,data,false);
            var attribute=new CompositionAttribute {Attribute=19,Data=data,Size=new UIntPtr((uint)size)};
            return set(window,ref attribute);
        }
        finally
        {
            if(data!=IntPtr.Zero)Marshal.FreeHGlobal(data);
            NativeLibrary.Free(library);
        }
    }
    private static Brush Tint(bool fallback)=>new LinearGradientBrush(
        Color.FromArgb(fallback?(byte)248:(byte)96,255,255,255),
        Color.FromArgb(fallback?(byte)240:(byte)64,232,239,240),90);
}
