using System;
using PixelRect = System.Drawing.Rectangle;

namespace FlowBar
{
    internal enum LensProfile { Off, Liquid, Ios }

    internal struct LensSettings
    {
        public LensProfile Profile;
        public float Width, Shift;
        public int Blur;
        public bool Enabled { get { return Profile != LensProfile.Off && Shift > 0 && Width > 0; } }
        public int Padding { get { return (int)Math.Ceiling(Shift) + Blur + 2; } }

        public bool SameAs(LensSettings other)
        {
            return Profile == other.Profile && Width == other.Width && Shift == other.Shift && Blur == other.Blur;
        }
    }

    // Converts a rounded glass contour into a map from destination pixels to background samples.
    // The preview owns a clean image. Live glass samples only outside the panel.
    internal sealed class GlassLensMap
    {
        readonly int sourceWidth, sourceHeight, targetWidth, targetHeight;
        readonly int[] indexes;
        readonly ushort[] fractions;
        readonly byte[] alpha;
        public int[] Pixels { get; private set; }

        public GlassLensMap(PixelRect target, PixelRect source, PixelRect screen, double radius, LensSettings lens, PixelRect covered = default(PixelRect))
        {
            sourceWidth = source.Width; sourceHeight = source.Height;
            targetWidth = target.Width; targetHeight = target.Height;
            int count = target.Width * target.Height;
            indexes = new int[count]; fractions = new ushort[count]; alpha = new byte[count]; Pixels = new int[count];
            double halfWidth = target.Width * 0.5, halfHeight = target.Height * 0.5;
            radius = Math.Max(0, Math.Min(radius, Math.Min(halfWidth, halfHeight)));
            double band = Math.Min(lens.Width, Math.Min(halfWidth, halfHeight));
            int left = target.Left - source.Left, top = target.Top - source.Top;
            bool flipLeft = target.Left - screen.Left < lens.Shift, flipRight = screen.Right - target.Right < lens.Shift;
            bool flipTop = target.Top - screen.Top < lens.Shift, flipBottom = screen.Bottom - target.Bottom < lens.Shift;
            for (int y = 0; y < target.Height; y++)
            {
                double py = y + 0.5 - halfHeight, qy = Math.Abs(py) - (halfHeight - radius);
                for (int x = 0; x < target.Width; x++)
                {
                    double px = x + 0.5 - halfWidth, qx = Math.Abs(px) - (halfWidth - radius);
                    double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
                    double outside = Math.Sqrt(ox * ox + oy * oy);
                    double distance = outside + Math.Min(Math.Max(qx, qy), 0) - radius;
                    int i = y * target.Width + x;
                    if (distance >= 0.5) continue;
                    double opacity = Math.Min(1, 0.5 - distance);
                    double sx = x + left, sy = y + top;
                    if (distance > -band)
                    {
                        double depth = Math.Max(0, -distance) / band;
                        double t = 1 - depth;
                        double shift = lens.Shift * t * t;
                        if (lens.Profile == LensProfile.Ios) shift *= t;
                        double nx = outside > 0 ? ox / outside : qx >= qy ? 1 : 0;
                        double ny = outside > 0 ? oy / outside : qx < qy ? 1 : 0;
                        if (covered.IsEmpty)
                        {
                            sx += shift * nx * (px < 0 ? (flipLeft ? 1 : -1) : (flipRight ? -1 : 1));
                            sy += shift * ny * (py < 0 ? (flipTop ? 1 : -1) : (flipBottom ? -1 : 1));
                        }
                        else
                        {
                            int edgeX = px < 0 ? covered.Left - 1 : covered.Right;
                            int edgeY = py < 0 ? covered.Top - 1 : covered.Bottom;
                            if (edgeX < source.Left || edgeX >= source.Right) nx = 0;
                            if (edgeY < source.Top || edgeY >= source.Bottom) ny = 0;
                            if (nx == 0 && ny == 0) continue;
                            opacity *= 0.55 * t * Math.Min(1, lens.Shift / 4) * Math.Max(nx, ny);
                            if (nx >= ny)
                                sx = edgeX - source.Left + (px < 0 ? -shift : shift);
                            else
                                sy = edgeY - source.Top + (py < 0 ? -shift : shift);
                        }
                    }
                    else if (!covered.IsEmpty) continue;
                    alpha[i] = (byte)Math.Round(255 * opacity);
                    sx = Math.Max(0, Math.Min(sourceWidth - 1, sx));
                    sy = Math.Max(0, Math.Min(sourceHeight - 1, sy));
                    int ix = (int)sx, iy = (int)sy;
                    indexes[i] = iy * sourceWidth + ix;
                    fractions[i] = (ushort)((int)((sx - ix) * 256) | (int)((sy - iy) * 256) << 8);
                }
            }
        }

        public void Render(int[] input)
        {
            Render(input, new PixelRect(0, 0, targetWidth, targetHeight));
        }

        public void Render(int[] input, PixelRect region)
        {
            for (int row = region.Top; row < region.Bottom; row++)
            {
                int end = row * targetWidth + region.Right;
                for (int i = row * targetWidth + region.Left; i < end; i++)
                {
                    int a = alpha[i];
                    if (a == 0) continue;
                    int index = indexes[i], frac = fractions[i], x = frac & 255, y = frac >> 8;
                    uint rgb = (uint)input[index] & 0xFFFFFF;
                    if (frac != 0)
                    {
                        if (x != 0) rgb = Mix(rgb, (uint)input[index + 1], x);
                        if (y != 0)
                        {
                            int next = index + sourceWidth;
                            uint lower = x == 0 ? (uint)input[next] : Mix((uint)input[next], (uint)input[next + 1], x);
                            rgb = Mix(rgb, lower, y);
                        }
                    }
                    if (a < 255) rgb = Mix(0, rgb, a);
                    Pixels[i] = (int)(rgb | ((uint)a << 24));
                }
            }
        }

        static uint Mix(uint a, uint b, int amount)
        {
            uint t = (uint)amount, r = 256u - t;
            return (((a & 0xFF00FF) * r + (b & 0xFF00FF) * t) >> 8 & 0xFF00FF)
                 | (((a & 0x00FF00) * r + (b & 0x00FF00) * t) >> 8 & 0x00FF00);
        }

        public static void Soften(int[] input, int[] scratch, int[] output, int width, int height, int blur, ulong[] columns = null)
        {
            Soften(input, scratch, output, width, height, blur, new PixelRect(0, 0, width, height), columns);
        }

        public static void Soften(int[] input, int[] scratch, int[] output, int width, int height, int blur, PixelRect changed, ulong[] columns)
        {
            uint count = (uint)(blur * 2 + 1);
            // Rounded-up reciprocal in 24-bit fixed point, exact for the largest channel sum (97 * 255).
            uint reciprocal = ((1u << 24) + count - 1) / count;
            int left = Math.Max(0, changed.Left - blur), right = Math.Min(width, changed.Right + blur);
            for (int y = changed.Top; y < changed.Bottom; y++)
            {
                int start = y * width;
                ulong sum = 0;
                for (int k = left - blur; k <= left + blur; k++) sum += Pack(input[start + Math.Max(0, Math.Min(width - 1, k))]);
                for (int x = left; x < right; x++)
                {
                    scratch[start + x] = Average(sum, reciprocal);
                    sum += Pack(input[start + Math.Min(width - 1, x + blur + 1)]) - Pack(input[start + Math.Max(0, x - blur)]);
                }
            }
            // Traverse the vertical pass by contiguous rows, with one running sum per column.
            columns = columns ?? new ulong[width];
            Array.Clear(columns, left, right - left);
            int top = Math.Max(0, changed.Top - blur), bottom = Math.Min(height, changed.Bottom + blur);
            for (int k = top - blur; k <= top + blur; k++)
            {
                int row = Math.Max(0, Math.Min(height - 1, k)) * width;
                for (int x = left; x < right; x++) columns[x] += Pack(scratch[row + x]);
            }
            for (int y = top; y < bottom; y++)
            {
                int row = y * width, add = Math.Min(height - 1, y + blur + 1) * width, remove = Math.Max(0, y - blur) * width;
                for (int x = left; x < right; x++)
                {
                    output[row + x] = Average(columns[x], reciprocal);
                    columns[x] += Pack(scratch[add + x]) - Pack(scratch[remove + x]);
                }
            }
        }

        static ulong Pack(int pixel)
        {
            ulong rgb = (uint)pixel;
            return (rgb & 255) | (rgb & 0xFF00) << 8 | (rgb & 0xFF0000) << 16;
        }
        static int Average(ulong sum, uint reciprocal)
        {
            return (int)(((uint)(sum & 65535) * reciprocal >> 24)
                | ((uint)(sum >> 16 & 65535) * reciprocal >> 24) << 8
                | ((uint)(sum >> 32) * reciprocal >> 24) << 16);
        }
    }
}
