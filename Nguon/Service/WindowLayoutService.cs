using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PersonalInfoManager.Services;

internal static class WindowLayoutService
{
    private const int WmDpiChanged = 0x02E0;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private static readonly ConditionalWeakTable<Window, DpiHookRegistration> DpiHooks = new();
    private static readonly ConditionalWeakTable<Window, WindowLayoutState> LayoutStates = new();

    internal readonly record struct WindowFit(double Width, double Height, double MinWidth, double MinHeight,
        double MaxWidth, double MaxHeight, double Left, double Top);

    internal static void HandleWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window) return;
        FitToWorkArea(window);
        AttachDpiHook(window);
    }

    internal static WindowFit CalculateFit(double width, double height, double minWidth, double minHeight,
        double maxWidth, double maxHeight, double left, double top, Rect workArea)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
            return new(width, height, minWidth, minHeight, maxWidth, maxHeight, left, top);

        double availableWidth = Math.Max(1, workArea.Width * 0.96);
        double availableHeight = Math.Max(1, workArea.Height * 0.94);
        double cappedWidth = Math.Min(availableWidth, maxWidth);
        double cappedHeight = Math.Min(availableHeight, maxHeight);
        double fittingMinWidth = Math.Min(Math.Max(0, minWidth), cappedWidth);
        double fittingMinHeight = Math.Min(Math.Max(0, minHeight), cappedHeight);
        double fittingWidth = Math.Clamp(IsUsable(width) ? width : cappedWidth, fittingMinWidth, cappedWidth);
        double fittingHeight = Math.Clamp(IsUsable(height) ? height : cappedHeight, fittingMinHeight, cappedHeight);
        double minimumLeft = workArea.Left;
        double maximumLeft = Math.Max(minimumLeft, workArea.Right - fittingWidth);
        double minimumTop = workArea.Top;
        double maximumTop = Math.Max(minimumTop, workArea.Bottom - fittingHeight);
        double fittingLeft = Math.Clamp(double.IsFinite(left) ? left : minimumLeft, minimumLeft, maximumLeft);
        double fittingTop = Math.Clamp(double.IsFinite(top) ? top : minimumTop, minimumTop, maximumTop);

        return new(fittingWidth, fittingHeight, fittingMinWidth, fittingMinHeight,
            cappedWidth, cappedHeight, fittingLeft, fittingTop);
    }

    internal static void FitToWorkArea(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Rect workArea = GetWindowWorkArea(window);
        if (workArea.Width <= 0 || workArea.Height <= 0) return;

        WindowLayoutState state = LayoutStates.GetValue(window, CreateLayoutState);
        WindowFit fit = CalculateFit(state.DesiredWidth, state.DesiredHeight,
            state.OriginalMinWidth, state.OriginalMinHeight, state.OriginalMaxWidth, state.OriginalMaxHeight,
            window.Left, window.Top, workArea);

        state.IsApplyingFit = true;
        try
        {
            window.MinWidth = fit.MinWidth;
            window.MinHeight = fit.MinHeight;
            window.MaxWidth = fit.MaxWidth;
            window.MaxHeight = fit.MaxHeight;
            if (window.SizeToContent == SizeToContent.Manual)
            {
                window.Width = fit.Width;
                window.Height = fit.Height;
            }
            window.Left = fit.Left;
            window.Top = fit.Top;
        }
        finally
        {
            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
                new Action(() => state.IsApplyingFit = false));
        }
    }

    private static Rect GetWindowWorkArea(Window window)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                {
                    var workAreaPixels = new Rect(info.Work.Left, info.Work.Top,
                        info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
                    HwndSource? source = HwndSource.FromHwnd(handle);
                    if (source?.CompositionTarget is { } target)
                    {
                        Matrix transform = target.TransformFromDevice;
                        Point topLeft = transform.Transform(workAreaPixels.TopLeft);
                        Point bottomRight = transform.Transform(workAreaPixels.BottomRight);
                        Rect workAreaDips = new(topLeft, bottomRight);
                        if (workAreaDips.Width > 0 && workAreaDips.Height > 0) return workAreaDips;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException or ArgumentException) { }

        return SystemParameters.WorkArea;
    }

    private static WindowLayoutState CreateLayoutState(Window window)
    {
        double desiredWidth = IsUsable(window.Width) ? window.Width : window.ActualWidth;
        double desiredHeight = IsUsable(window.Height) ? window.Height : window.ActualHeight;
        var state = new WindowLayoutState(desiredWidth, desiredHeight, window.MinWidth, window.MinHeight,
            window.MaxWidth, window.MaxHeight);
        window.SizeChanged += (_, args) =>
        {
            if (state.IsApplyingFit || window.WindowState != WindowState.Normal) return;
            state.DesiredWidth = args.NewSize.Width;
            state.DesiredHeight = args.NewSize.Height;
        };
        window.Closed += (_, _) => LayoutStates.Remove(window);
        return state;
    }

    private static void AttachDpiHook(Window window)
    {
        if (DpiHooks.TryGetValue(window, out _)) return;
        HwndSource? source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        if (source == null) return;

        var weakWindow = new WeakReference<Window>(window);
        HwndSourceHook hook = (IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message == WmDpiChanged && weakWindow.TryGetTarget(out Window? target))
                target.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => FitToWorkArea(target)));
            return IntPtr.Zero;
        };

        source.AddHook(hook);
        DpiHooks.Add(window, new DpiHookRegistration(source, hook));
        window.Closed += (_, _) =>
        {
            source.RemoveHook(hook);
            DpiHooks.Remove(window);
        };
    }

    private static bool IsUsable(double value) => double.IsFinite(value) && value > 0;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private sealed record DpiHookRegistration(HwndSource Source, HwndSourceHook Hook);

    private sealed class WindowLayoutState(double desiredWidth, double desiredHeight, double originalMinWidth,
        double originalMinHeight, double originalMaxWidth, double originalMaxHeight)
    {
        public double DesiredWidth { get; set; } = desiredWidth;
        public double DesiredHeight { get; set; } = desiredHeight;
        public double OriginalMinWidth { get; } = originalMinWidth;
        public double OriginalMinHeight { get; } = originalMinHeight;
        public double OriginalMaxWidth { get; } = originalMaxWidth;
        public double OriginalMaxHeight { get; } = originalMaxHeight;
        public bool IsApplyingFit { get; set; }
    }
}
