using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace LumaDock;

internal sealed class LiveGlass : IDisposable
{
    private Bitmap? scaled;
    private Bitmap? capture;
    private WriteableBitmap? output;
    private byte[] previous = [];
    private int lastDispersion = int.MinValue, lastRefraction = -1;

    // false, если кадр экрана под доком не изменился с прошлого раза: перерисовывать стекло не нужно
    public bool Changed { get; private set; } = true;
    private byte[] sample = [], refracted = [];
    private int[] sourceColumns = [];

    private int shape = -1;
    private const double GlassFloor = 28;


    // detail: 1 — обычное стекло, 4 — матовое (в четыре раза крупнее размытие); dispersion — сдвиг красного и синего каналов у призмы
    public ImageSource? Update(int x, int y, int width, int height, int detail = 1, int dispersion = 0, int refraction = 50, Bitmap? source = null, int offsetX = 0, int offsetY = 0)
    {
        if (width < 4 || height < 4) return null;
        int columns = Math.Max(4, Math.Min(width, 760 / Math.Max(1, detail)));
        int rows = Math.Max(4, Math.Min(height, 48 / Math.Max(1, detail)));
        if (capture?.Width != width || capture.Height != height)
        {
            capture?.Dispose();
            capture = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        }
        if (scaled?.Width != columns || scaled.Height != rows || shape != detail)
        {
            shape = detail;
            scaled?.Dispose();
            scaled = new Bitmap(columns, rows, PixelFormat.Format32bppRgb);
            output = new WriteableBitmap(columns, rows, 96, 96, PixelFormats.Bgr32, null);
            sample = new byte[columns * rows * 4];
            refracted = new byte[sample.Length];
            previous = new byte[sample.Length];
            lastDispersion = int.MinValue;
            sourceColumns = new int[columns];
            lastRefraction = -1;
        }
        // Степень преломления (0–100): сдвиг картинки у краёв стекла по горизонтали и изгиб по вертикали; 50 — прежнее стекло
        if (refraction != lastRefraction)
        {
            lastRefraction = refraction;
            lastDispersion = int.MinValue;
            for (int column = 0; column < columns; column++)
            {
                double edge = 2d * column / (columns - 1) - 1;
                sourceColumns[column] = Math.Clamp(column - (int)(12 * refraction / 100d * Math.Pow(edge, 7) * columns / 760d), 0, columns - 1);
            }
        }
        try
        {
            // The standard desktop blit omits layered windows, including the WPF dock.
            // source — готовая картинка вместо снимка экрана (обои): снимок с исключением дока из захвата на части систем делает док чёрным
            using (var screen = Graphics.FromImage(capture))
            {
                if (source is null) screen.CopyFromScreen(x, y, 0, 0, capture.Size, CopyPixelOperation.SourceCopy);
                else screen.DrawImage(source, new Rectangle(0, 0, width, height), new Rectangle(offsetX, offsetY, width, height), GraphicsUnit.Pixel);
            }
            using (var resize = Graphics.FromImage(scaled))
            {
                resize.InterpolationMode = InterpolationMode.Bilinear;
                resize.DrawImage(capture, 0, 0, columns, rows);
            }
        }
        catch (ExternalException ex) { ErrorLog.Write(ex, "Glass"); return null; }

        var pixels = scaled.LockBits(new Rectangle(0, 0, columns, rows), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try { Marshal.Copy(pixels.Scan0, sample, 0, sample.Length); }
        finally { scaled.UnlockBits(pixels); }
        // Фон не двигался и настройки те же: готовая картинка уже в output, пересчёт и перерисовка не нужны (главная экономия процессора на статичном рабочем столе)
        Changed = dispersion != lastDispersion || !sample.AsSpan().SequenceEqual(previous);
        if (!Changed) return output;
        lastDispersion = dispersion;
        Buffer.BlockCopy(sample, 0, previous, 0, sample.Length);
        for (int row = 0; row < rows; row++)
        {
            double curvature = 2.0 * row / (rows - 1) - 1;
            int sourceY = Math.Clamp((int)Math.Round(row - 8 * refraction / 100d * curvature * Math.Abs(curvature)), 0, rows - 1);
            int above = Math.Max(0, sourceY - 1);
            int below = Math.Min(rows - 1, sourceY + 1);
            double highlight = 22 * Math.Exp(-row / 2.1) + 7 * Math.Exp(-(rows - 1 - row) / 3d);
            for (int column = 0; column < columns; column++)
            {
                int target = (row * columns + column) * 4;
                for (int component = 0; component < 3; component++)
                {
                    // порядок байтов BGR: синий смещается вправо, красный влево — получается цветная кайма призмы
                    int sourceX = Math.Clamp(sourceColumns[column] + (component == 0 ? dispersion : component == 2 ? -dispersion : 0), 0, columns - 1);
                    int left = Math.Max(0, sourceX - 2);
                    int right = Math.Min(columns - 1, sourceX + 2);
                    int center = (sourceY * columns + sourceX) * 4;
                    int brightness = (sample[center + component] * 4 + sample[(sourceY * columns + left) * 4 + component]
                        + sample[(sourceY * columns + right) * 4 + component] + sample[(above * columns + sourceX) * 4 + component]
                        + sample[(below * columns + sourceX) * 4 + component]) / 8;
                    refracted[target + component] = (byte)Math.Clamp(brightness * .74 + GlassFloor + highlight, 0, 255); // пол яркости: над чёрными обоями стекло остаётся тёмно-серым, а не чёрным
                }
            }
        }
        output!.WritePixels(new Int32Rect(0, 0, columns, rows), refracted, columns * 4, 0);
        return output;
    }

    public void Dispose()
    {
        capture?.Dispose();
        scaled?.Dispose();
    }
}

// Частота обновления монитора по настройкам Windows: по ней дозируется перерисовка живого стекла
internal static class Displays
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TrueTypeOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, Frequency;
        public uint IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string? device, int mode, ref DevMode value);

    // 60, если Windows не ответила или вернула «аппаратную» единицу
    public static int Hz(string? device)
    {
        var mode = new DevMode { DeviceName = "", FormName = "", Size = (ushort)Marshal.SizeOf<DevMode>() };
        return EnumDisplaySettingsW(device, -1, ref mode) && mode.Frequency > 1 ? (int)Math.Clamp(mode.Frequency, 30, 480) : 60;
    }
}
