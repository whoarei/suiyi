using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TranslationFloat.App;

// Short, click-through feedback for headless quick mode. Never takes focus.
internal sealed class QuickNoticeWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };

    public QuickNoticeWindow(string message)
    {
        Title = "随译提示";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        IsHitTestVisible = false;
        Content = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 16, 12),
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = message, FontSize = 13, TextWrapping = TextWrapping.Wrap }
        };
        var surface = (Border)Content;
        surface.SetResourceReference(Border.BackgroundProperty, "Theme.Notice");
        surface.SetResourceReference(Border.BorderBrushProperty, "Theme.AccentBorder");
        ((TextBlock)surface.Child).SetResourceReference(TextBlock.ForegroundProperty, "Theme.OnNotice");
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(handle, -20).ToInt64();
            NativeMethods.SetWindowLongPtr(handle, -20, (nint)(style | 0x08000000 | 0x80 | 0x20));
        };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, area.Right - ActualWidth - 20);
            Top = Math.Max(area.Top, area.Bottom - ActualHeight - 20);
            DiagnosticLog.Write("quick-notice", "shown");
            _timer.Start();
        };
        _timer.Tick += (_, _) => Close();
        Closed += (_, _) => { _timer.Stop(); DiagnosticLog.Write("quick-notice", "closed"); };
    }
}
