using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Windows.Networking.Connectivity;

namespace LumaDock;

internal static class InternetConnectivity
{
    internal enum State { Unknown, Offline, Ethernet, Wifi }
    private static volatile State current;
    private static int reading;
    private static long nextRead;
    internal static State? Fixture;
    internal static State Current => Fixture ?? current;
    internal static State Classify(NetworkConnectivityLevel level, bool wifi) => level == NetworkConnectivityLevel.InternetAccess
        ? wifi ? State.Wifi : State.Ethernet : State.Offline;
    internal static string Description(State state) => state switch
    {
        State.Offline => "No internet access", State.Ethernet => "Internet access (Ethernet)",
        State.Wifi => "Internet access (Wi-Fi)", _ => "Checking internet connection…"
    };

    internal static void Refresh()
    {
        if (Environment.TickCount64 < Interlocked.Read(ref nextRead) || Interlocked.CompareExchange(ref reading, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            try
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                current = profile is null ? State.Offline : Classify(profile.GetNetworkConnectivityLevel(), profile.IsWlanConnectionProfile);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
            {
                current = State.Unknown;
                ErrorLog.Write(ex, "Internet connectivity");
            }
            finally { Interlocked.Exchange(ref nextRead, Environment.TickCount64 + 2000); Interlocked.Exchange(ref reading, 0); }
        });
    }
}

// Геометрия вместо растровой картинки: глобус и знак отсутствия сети остаются чёткими при любом DPI.
internal sealed class ConnectivityIcon : FrameworkElement
{
    private readonly Brush ink;
    private InternetConnectivity.State state;
    internal ConnectivityIcon(Brush ink, double size) { this.ink = ink; Width = Height = size; }
    internal void Update()
    {
        var value = InternetConnectivity.Current;
        ToolTip = L10n.T(InternetConnectivity.Description(value));
        System.Windows.Automation.AutomationProperties.SetName(this, (string)ToolTip);
        if (state != value) { state = value; InvalidateVisual(); }
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double size = Math.Min(ActualWidth, ActualHeight);
        dc.PushTransform(new MatrixTransform(size / 24, 0, 0, size / 24, (ActualWidth - size) / 2, (ActualHeight - size) / 2));
        var pen = new Pen(ink, 1.55) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (state == InternetConnectivity.State.Ethernet)
        {
            dc.DrawRoundedRectangle(null, pen, new Rect(2, 3, 18, 13), 1, 1);
            dc.DrawLine(pen, new Point(11, 16), new Point(11, 21));
            dc.DrawLine(pen, new Point(7, 21), new Point(15, 21));
        }
        else if (state == InternetConnectivity.State.Wifi)
        {
            foreach (double radius in new[] { 10d, 6.5, 3.2 })
            {
                var arc = new StreamGeometry();
                using (var g = arc.Open())
                {
                    g.BeginFigure(new Point(12 - radius * .78, 20 - radius * .63), false, false);
                    g.ArcTo(new Point(12 + radius * .78, 20 - radius * .63), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                }
                dc.DrawGeometry(null, pen, arc);
            }
            dc.DrawEllipse(ink, null, new Point(12, 20), 1, 1);
        }
        else
        {
            var badge = new EllipseGeometry(new Point(17.5, 17.5), 6, 6);
            dc.PushClip(state == InternetConnectivity.State.Offline
                ? Geometry.Combine(new RectangleGeometry(new Rect(0, 0, 24, 24)), badge, GeometryCombineMode.Exclude, null)
                : new RectangleGeometry(new Rect(0, 0, 24, 24)));
            dc.DrawEllipse(null, pen, new Point(10.5, 10.5), 8.5, 8.5);
            dc.DrawEllipse(null, pen, new Point(10.5, 10.5), 4, 8.5);
            dc.DrawLine(pen, new Point(2, 7.3), new Point(19, 7.3));
            dc.DrawLine(pen, new Point(2, 13.7), new Point(19, 13.7));
            dc.Pop();
            if (state == InternetConnectivity.State.Offline)
            {
                dc.DrawEllipse(null, pen, new Point(17.5, 17.5), 4.5, 4.5);
                dc.DrawLine(pen, new Point(14.3, 20.7), new Point(20.7, 14.3));
            }
        }
        dc.Pop();
    }
}
