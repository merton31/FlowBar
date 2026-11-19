using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LumaDock;

// Перестановка значков мышью: снимок плитки идёт за указателем, остальные плитки плавно раздвигаются и освобождают место,
// при отпускании плитка встаёт перед ближайшей справа (или в конец). Esc или потеря мыши отменяют перенос.
internal sealed partial class DockWindow
{
    // плитки, которые можно переставлять (закреплённые в доке), и их идентификаторы
    private readonly Dictionary<Border, string> dragTiles = [];
    private Border? dragTile;
    private string? dragId;
    private bool dragOpen; // переносится значок открытой (незакреплённой) программы: переставляется только среди таких же
    private readonly List<string> openOrder = []; // пути открытых программ в порядке, выбранном переносом (до закрытия панели)
    private Image? ghost;
    private TranslateTransform ghostMove = new();
    private List<Border> dragOthers = [];
    private int dragFrom;
    private double dragGrab, dragStartLeft, dragSlot, dragMouse, dragOrigin;

    private void WireDrag()
    {
        MouseMove += (_, e) => { if (dragTile is not null) dragMouse = e.GetPosition(dockRoot).X; };
        MouseLeftButtonUp += (_, _) => { if (dragTile is not null) EndDrag(true); };
        // событие приходит и от плитки, когда мышь переходит от неё к окну, поэтому отменяем, только если мышь взяло не окно
        LostMouseCapture += (_, _) => { if (dragTile is not null && !IsMouseCaptured) EndDrag(false); };
        PreviewKeyDown += (_, e) => { if (dragTile is not null && e.Key == Key.Escape) EndDrag(false); };
    }

    private void BeginDrag(Border tile, string id, double grab)
    {
        if (magnetRow is null) return;
        // увеличение и сдвиги сбрасываются: раскладку на время переноса ведёт только DragStep
        foreach (var item in magnets) { item.Scale.ScaleX = item.Scale.ScaleY = 1; item.Shift.X = 0; }
        magnetX = null;
        UpdateLayout();
        var rect = tile.TransformToAncestor(dockRoot).TransformBounds(new Rect(0, 0, tile.ActualWidth, tile.ActualHeight));
        if (rect.Width < 1 || rect.Height < 1) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(rect.Width * dpi.DpiScaleX), (int)Math.Ceiling(rect.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var snapshot = new DrawingVisual();
        using (var dc = snapshot.RenderOpen()) dc.DrawRectangle(new VisualBrush(tile), null, new Rect(0, 0, rect.Width, rect.Height));
        bitmap.Render(snapshot);
        dragOrigin = magnetRow.TransformToAncestor(dockRoot).Transform(default).X;
        dragOpen = !Items.Contains(id);
        dragOthers = dragTiles.Keys.Where(other => other != tile && !Items.Contains(dragTiles[other]) == dragOpen).OrderBy(RowOffset).ToList();
        dragFrom = dragOthers.Count(other => RowOffset(other) < RowOffset(tile));
        dragSlot = tile.ActualWidth + tile.Margin.Left + tile.Margin.Right;
        dragStartLeft = rect.X;
        dragGrab = Math.Clamp(grab, 0, rect.Width); // точка плитки, за которую взялись
        dragMouse = rect.X + dragGrab;
        ghostMove = new TranslateTransform();
        ghost = new Image
        {
            Source = bitmap, Width = rect.Width, Height = rect.Height, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(rect.X, rect.Y, 0, 0), RenderTransform = ghostMove
        };
        Panel.SetZIndex(ghost, 1000);
        dockRoot.Children.Add(ghost);
        tile.Opacity = 0;
        dragTile = tile;
        dragId = id;
        CaptureMouse();
    }

    // число плиток слева от середины снимка: перед какой плиткой он окажется, если отпустить сейчас
    private int DropIndex()
    {
        double left = Math.Clamp(dragMouse - dragGrab, 0, Math.Max(0, dockRoot.ActualWidth - ghost!.Width));
        double middle = left + ghost.Width / 2;
        return dragOthers.Count(other => dragOrigin + RowOffset(other) + other.ActualWidth / 2 < middle);
    }

    private void DragStep(double dt)
    {
        if (ghost is null) return;
        ghostMove.X = Math.Clamp(dragMouse - dragGrab, 0, Math.Max(0, dockRoot.ActualWidth - ghost.Width)) - dragStartLeft;
        int target = DropIndex();
        double follow = 1 - Math.Exp(-dt * 24);
        for (int index = 0; index < dragOthers.Count; index++)
        {
            // плитки между прежним и новым местом сдвигаются на ширину переносимой, образуя промежуток там, куда она встанет
            double want = target < dragFrom && index >= target && index < dragFrom ? dragSlot
                : target > dragFrom && index >= dragFrom && index < target ? -dragSlot : 0;
            var shift = magnets.First(item => item.Tile == dragOthers[index]).Shift;
            shift.X += (want - shift.X) * follow;
            if (Math.Abs(want - shift.X) < .05) shift.X = want;
        }
    }

    private void EndDrag(bool commit)
    {
        if (dragTile is null) return;
        int target = ghost is null ? dragFrom : DropIndex();
        string? id = dragId, before = target < dragOthers.Count ? dragTiles[dragOthers[target]] : null;
        dragTile.Opacity = 1;
        dragTile = null;
        dragId = null;
        if (ghost is not null) dockRoot.Children.Remove(ghost);
        ghost = null;
        foreach (var item in magnets) item.Shift.X = 0;
        ReleaseMouseCapture();
        if (!commit || id is null || target == dragFrom) return;
        if (!dragOpen) { model.MoveItem(id, before, Items); return; }
        openOrder.RemoveAll(path => path.Equals(id[4..], StringComparison.OrdinalIgnoreCase));
        int at = before is null ? -1 : openOrder.FindIndex(path => path.Equals(before[4..], StringComparison.OrdinalIgnoreCase));
        if (at < 0) openOrder.Add(id[4..]); else openOrder.Insert(at, id[4..]);
        Refresh();
    }
}
