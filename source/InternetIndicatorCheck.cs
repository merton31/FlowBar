using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Windows.Networking.Connectivity;

namespace LumaDock;

internal static partial class Program
{
    private static void VerifyInternetIndicator(DockModel model, string folder)
    {
        foreach (var level in Enum.GetValues<NetworkConnectivityLevel>())
            foreach (bool wifi in new[] { false, true })
            {
                var expected = level == NetworkConnectivityLevel.InternetAccess
                    ? wifi ? InternetConnectivity.State.Wifi : InternetConnectivity.State.Ethernet : InternetConnectivity.State.Offline;
                if (InternetConnectivity.Classify(level, wifi) != expected)
                    throw new InvalidOperationException("A local or captive network was incorrectly reported as internet access.");
            }
        try
        {
            var page = new StackPanel { Background = Visuals.Brush("#151515"), Margin = new Thickness(12) };
            var face = Widgets.Face(Widgets.Available["Quick Toggles"], model, false, out var update);
            var network = ((Panel)face).Children.OfType<Border>().Single(tile => tile.Child is ConnectivityIcon);
            foreach (var state in new[] { InternetConnectivity.State.Offline, InternetConnectivity.State.Ethernet,
                InternetConnectivity.State.Wifi, InternetConnectivity.State.Unknown, InternetConnectivity.State.Offline })
            {
                InternetConnectivity.Fixture = state;
                update();
                if (!Equals(network.ToolTip, L10n.T(InternetConnectivity.Description(state))))
                    throw new InvalidOperationException("The dock network status did not follow a disconnect/reconnect.");
                var icon = new ConnectivityIcon(Brushes.White, 40) { Margin = new Thickness(10) };
                icon.Update();
                page.Children.Add(Visuals.RowStack(icon, Visuals.Text(InternetConnectivity.Description(state), 14)));
            }
            page.Measure(new Size(420, 500));
            page.Arrange(new Rect(0, 0, 420, page.DesiredSize.Height));
            page.UpdateLayout();
            CaptureElement(page, Path.Combine(folder, "internet-indicators.png"));
        }
        finally { InternetConnectivity.Fixture = null; }
    }
}
