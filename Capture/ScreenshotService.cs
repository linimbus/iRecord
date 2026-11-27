// iRecord - screen/window capture and scrolling-stitch algorithms.
// Handles PrintWindow fallback, multi-region merge, and signature-based scroll detection.
using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace iRecord.Capture;

/// <summary>
/// Stateless screenshot helpers: rectangle/window capture, merged multi-regions, and long scrolling windows.
/// </summary>
public static class ScreenshotService
{
    private const int SignatureSegments = 8;

    /// <summary>Returns a window's client area in virtual-screen coordinates.</summary>
    public static Rectangle GetClientRectOnScreen(IntPtr hwnd)
    {
        if (!NativeMethods.GetClientRect(hwnd, out var rect))
            return Rectangle.Empty;

        var point = new NativeMethods.POINT();
        if (!NativeMethods.ClientToScreen(hwnd, ref point))
            return Rectangle.Empty;

        return new Rectangle(point.X, point.Y, rect.Width, rect.Height);
    }

    /// <summary>Copies one screen rectangle through BitBlt/GDI.</summary>
    public static Bitmap CaptureRect(Rectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new ArgumentException("Invalid capture rectangle.", nameof(rect));

        var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var g = Graphics.FromImage(bitmap);
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Prefers PrintWindow (works for occluded windows); falls back to a screen copy
    /// when PrintWindow returns a blank bitmap.
    /// </summary>
    public static Bitmap CaptureWindow(IntPtr hwnd, Rectangle windowRect)
    {
        if (windowRect.Width <= 0 || windowRect.Height <= 0)
            throw new ArgumentException("Invalid window rectangle.", nameof(windowRect));

        try
        {
            var bitmap = new Bitmap(windowRect.Width, windowRect.Height, PixelFormat.Format32bppArgb);
            bool captured;
            using (var g = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = g.GetHdc();
                try
                {
                    captured = NativeMethods.PrintWindow(hwnd, hdc, NativeMethods.PW_RENDERFULLCONTENT);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }

            if (captured && !IsNearlyBlank(bitmap))
                return bitmap;

            bitmap.Dispose();
        }
        catch
        {
        }

        return CaptureRect(windowRect);
    }

    /// <summary>Vertically stacks clipped region crops with an optional gutter and centered widths.</summary>
    public static Bitmap MergeRegions(Bitmap source, IReadOnlyList<Rectangle> regions, int gutter)
    {
        var sourceRect = new Rectangle(0, 0, source.Width, source.Height);
        var valid = new List<Rectangle>();
        foreach (var region in regions)
        {
            var clipped = Rectangle.Intersect(region, sourceRect);
            if (clipped.Width > 0 && clipped.Height > 0)
                valid.Add(clipped);
        }

        if (valid.Count == 0)
            throw new ArgumentException("No valid regions to merge.", nameof(regions));

        gutter = Math.Max(0, gutter);
        int width = valid.Max(r => r.Width);
        int height = valid.Sum(r => r.Height) + gutter * (valid.Count - 1);
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(result))
        {
            g.Clear(Color.White);
            int y = 0;
            foreach (var region in valid)
            {
                int x = (width - region.Width) / 2;
                g.DrawImage(source,
                    new Rectangle(x, y, region.Width, region.Height),
                    region,
                    GraphicsUnit.Pixel);
                y += region.Height + gutter;
            }
        }

        return result;
    }

    /// <summary>
    /// Captures a window while scrolling it with the mouse wheel, detects vertical overlap
    /// from row signatures, and stitches new content until reaching the bottom.
    /// Restores the original scroll position and cursor afterwards.
    /// </summary>
    public static Bitmap CaptureScrollingWindow(IntPtr hwnd)
    {
        var client = GetClientRectOnScreen(hwnd);
        if (client.Width < 32 || client.Height < 32)
            throw new InvalidOperationException("The selected window is too small to capture.");

        var originalCursor = Cursor.Position;
        try
        {
            try { NativeMethods.SetForegroundWindow(hwnd); } catch { }
            Thread.Sleep(220);

            NativeMethods.SetCursorPos(client.Left + client.Width / 2, client.Top + client.Height / 2);
            Thread.Sleep(150);

            var initialFull = CaptureRect(client);
            Bitmap? previousFull = initialFull;
            var segments = new List<Bitmap>();
            int notches = 1;
            int scrollSteps = 0;

            try
            {
                int width = initialFull.Width;
                int height = initialFull.Height;
                int usableWidth = GetUsableWidth(width);
                int contentWidth = usableWidth;
                int contentHeight = height;

                int stripEstimate = Math.Min(36, height / 4);
                int usableScroll = Math.Max(48, height - 100 - stripEstimate);
                notches = Math.Clamp((int)Math.Round(usableScroll * 0.4 / 48.0), 1, 5);
                const int maxFrames = 150;
                const int maxHeight = 30000;

                var initialSignature = BuildRowSignatures(initialFull, usableWidth, SignatureSegments);
                var previousSignature = initialSignature;
                int totalHeight = height;
                int lastOffset = 0;
                bool footerChecked = false;

                for (int frame = 0; frame < maxFrames && totalHeight < maxHeight; frame++)
                {
                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, -120 * notches, UIntPtr.Zero);
                    scrollSteps++;
                    Thread.Sleep(360);

                    var currentFull = CaptureRect(client);
                    if (currentFull.Width != width || currentFull.Height != height)
                    {
                        currentFull.Dispose();
                        break;
                    }

                    var currentSignature = BuildRowSignatures(currentFull, usableWidth, SignatureSegments);

                    if (!footerChecked)
                    {
                        footerChecked = true;

                        if (SignatureDifference(initialSignature, currentSignature, height, SignatureSegments) < 1.2)
                        {
                            scrollSteps--;
                            currentFull.Dispose();
                            return CropRegion(initialFull, contentWidth, height);
                        }

                        int footer = DetectStaticBottom(initialSignature, currentSignature, height, SignatureSegments);
                        contentHeight = height - footer;
                        totalHeight = contentHeight;
                    }
                    else if (SignatureDifference(previousSignature, currentSignature, contentHeight, SignatureSegments) < 1.2)
                    {
                        scrollSteps--;
                        currentFull.Dispose();
                        break;
                    }

                    int offset = FindScrollOffset(previousSignature, currentSignature, contentHeight, SignatureSegments);
                    if (offset > 0)
                        offset = RefineScrollOffset(previousFull!, currentFull, offset, contentHeight, usableWidth);

                    if (offset <= 0)
                    {
                        int fallback = lastOffset > 0 ? lastOffset : Math.Max(48, notches * 48);
                        offset = Math.Min(fallback, contentHeight - 24);
                    }
                    else
                    {
                        lastOffset = offset;
                    }

                    if (offset <= 0 || totalHeight + offset > maxHeight)
                    {
                        currentFull.Dispose();
                        break;
                    }

                    segments.Add(CropBottom(currentFull, contentWidth, contentHeight, offset));
                    totalHeight += offset;

                    if (!ReferenceEquals(previousFull, initialFull))
                        previousFull?.Dispose();
                    previousFull = currentFull;
                    previousSignature = currentSignature;
                }

                var result = new Bitmap(contentWidth, totalHeight, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(result))
                {
                    g.DrawImage(initialFull,
                        new Rectangle(0, 0, contentWidth, contentHeight),
                        new Rectangle(0, 0, contentWidth, contentHeight),
                        GraphicsUnit.Pixel);

                    int y = contentHeight;
                    foreach (var segment in segments)
                    {
                        g.DrawImage(segment, 0, y);
                        y += segment.Height;
                    }
                }
                return result;
            }
            finally
            {
                if (previousFull != null && !ReferenceEquals(previousFull, initialFull))
                    previousFull.Dispose();
                initialFull.Dispose();
                foreach (var segment in segments)
                    segment.Dispose();

                if (scrollSteps > 0 && NativeMethods.IsWindow(hwnd))
                {
                    for (int i = 0; i < scrollSteps; i++)
                    {
                        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, 120 * notches, UIntPtr.Zero);
                        Thread.Sleep(10);
                    }
                }
            }
        }
        finally
        {
            NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
        }
    }

    private static Bitmap CropRegion(Bitmap source, int width, int height)
    {
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.DrawImage(source,
            new Rectangle(0, 0, width, height),
            new Rectangle(0, 0, width, height),
            GraphicsUnit.Pixel);
        return result;
    }

    private static double SignatureDifference(float[] previous, float[] current, int height, int segments)
    {
        double sum = 0;
        int count = 0;

        for (int y = 0; y < height; y += 4)
        {
            int index = y * segments;
            for (int s = 0; s < segments; s++)
            {
                sum += Math.Abs(previous[index + s] - current[index + s]);
                count++;
            }
        }

        return count > 0 ? sum / count : 0;
    }

    private static Bitmap CropBottom(Bitmap source, int width, int contentHeight, int height)
    {
        var segment = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(segment);
        g.DrawImage(source,
            new Rectangle(0, 0, width, height),
            new Rectangle(0, contentHeight - height, width, height),
            GraphicsUnit.Pixel);
        return segment;
    }

    private static int DetectStaticBottom(float[] previous, float[] current, int height, int segments)
    {
        int max = Math.Min(48, height / 3);
        int footer = 0;

        for (int y = height - 1; y >= height - max; y--)
        {
            double diff = CompareRows(previous, current, y, y, 1, segments);
            if (Math.Sqrt(diff) <= 3.5)
                footer++;
            else
                break;
        }

        return footer >= 6 ? footer : 0;
    }

    public static string SavePng(Bitmap bitmap, string folder, string suffix)
    {
        Directory.CreateDirectory(folder);
        string stem = $"iRecord_{DateTime.Now:yyyyMMdd_HHmmss}";
        if (!string.IsNullOrWhiteSpace(suffix))
            stem += "_" + suffix;

        string path = Path.Combine(folder, stem + ".png");
        int index = 2;
        while (File.Exists(path))
            path = Path.Combine(folder, $"{stem}_{index++}.png");

        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    internal static int FindScrollOffset(Bitmap previous, Bitmap current)
    {
        if (previous.Width != current.Width || previous.Height != current.Height)
            return -1;

        int usableWidth = GetUsableWidth(previous.Width);
        var a = BuildRowSignatures(previous, usableWidth, SignatureSegments);
        var b = BuildRowSignatures(current, usableWidth, SignatureSegments);
        return FindScrollOffset(a, b, previous.Height, SignatureSegments);
    }

    private static int FindScrollOffset(float[] previous, float[] current, int height, int segments)
    {
        int strip = Math.Min(36, height / 4);
        if (strip < 8)
            return -1;

        int startY = Math.Max(0, height - 100 - strip);
        if (startY + strip > height - 8)
            startY = Math.Max(0, height - 8 - strip);
        if (startY <= 0)
            return -1;

        double best = double.MaxValue;
        int bestY = -1;

        for (int y = 0; y + strip <= height; y++)
        {
            double diff = CompareRows(previous, current, startY, y, strip, segments);
            if (diff < best)
            {
                best = diff;
                bestY = y;
            }
            if (best < 0.01)
                break;
        }

        if (bestY < 0 || Math.Sqrt(best) > 15)
            return -1;

        int scrolled = startY - bestY;
        if (scrolled < 8 || scrolled > height - strip)
            return -1;

        if (startY >= 180 && bestY >= 120)
        {
            double verify = CompareRows(previous, current, startY - 60, bestY - 60, 24, segments);
            if (Math.Sqrt(verify) > 20)
                return -1;
        }

        return scrolled;
    }

    private static int RefineScrollOffset(Bitmap previous, Bitmap current, int coarse, int contentHeight, int usableWidth)
    {
        if (coarse <= 0 || previous.Width != current.Width || previous.Height != current.Height)
            return coarse;

        int min = Math.Max(8, coarse - 8);
        int max = Math.Min(contentHeight - 24, coarse + 8);
        if (max < min)
            return coarse;

        int bandEnd = Math.Max(min + 32, contentHeight - 8);
        int bandStart = Math.Max(max + 4, bandEnd - 200);
        if (bandEnd - bandStart < 32)
            bandStart = Math.Max(max, bandEnd - 80);
        if (bandEnd - bandStart < 16)
            return coarse;

        int width = Math.Min(usableWidth, Math.Min(previous.Width, current.Width));
        if (width < 16)
            return coarse;

        BitmapData? previousData = null;
        BitmapData? currentData = null;
        byte[]? previousBytes = null;
        byte[]? currentBytes = null;

        try
        {
            previousData = previous.LockBits(new Rectangle(0, 0, previous.Width, previous.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            currentData = current.LockBits(new Rectangle(0, 0, current.Width, current.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            int previousLength = previousData.Stride * previous.Height;
            int currentLength = currentData.Stride * current.Height;
            previousBytes = ArrayPool<byte>.Shared.Rent(previousLength);
            currentBytes = ArrayPool<byte>.Shared.Rent(currentLength);
            Marshal.Copy(previousData.Scan0, previousBytes, 0, previousLength);
            Marshal.Copy(currentData.Scan0, currentBytes, 0, currentLength);

            double best = double.MaxValue;
            int bestOffset = coarse;

            for (int offset = min; offset <= max; offset++)
            {
                double error = CompareBand(previousBytes, previousData.Stride, currentBytes, currentData.Stride,
                    width, bandStart, bandEnd, offset);
                if (error < best)
                {
                    best = error;
                    bestOffset = offset;
                }
            }

            return bestOffset;
        }
        catch
        {
            return coarse;
        }
        finally
        {
            if (previousData != null)
                previous.UnlockBits(previousData);
            if (currentData != null)
                current.UnlockBits(currentData);
            if (previousBytes != null)
                ArrayPool<byte>.Shared.Return(previousBytes);
            if (currentBytes != null)
                ArrayPool<byte>.Shared.Return(currentBytes);
        }
    }

    private static double CompareBand(byte[] previous, int previousStride, byte[] current, int currentStride,
        int width, int startRow, int endRow, int offset)
    {
        int stepX = Math.Max(1, width / 80);
        long sum = 0;
        long count = 0;

        for (int y = startRow; y < endRow; y++)
        {
            int py = y * previousStride;
            int cy = (y - offset) * currentStride;
            if (cy < 0)
                continue;

            for (int x = 0; x < width; x += stepX)
            {
                int i = py + x * 4;
                int j = cy + x * 4;
                sum += Math.Abs(previous[i] - current[j]);
                sum += Math.Abs(previous[i + 1] - current[j + 1]);
                sum += Math.Abs(previous[i + 2] - current[j + 2]);
                count += 3;
            }
        }

        return count > 0 ? (double)sum / count : double.MaxValue;
    }

    private static double CompareRows(float[] a, float[] b, int aStart, int bStart, int rows, int segments)
    {
        double sum = 0;
        for (int r = 0; r < rows; r++)
        {
            int ai = (aStart + r) * segments;
            int bi = (bStart + r) * segments;
            for (int s = 0; s < segments; s++)
            {
                double d = a[ai + s] - b[bi + s];
                sum += d * d;
            }
        }
        return sum / (rows * segments);
    }

    private static float[] BuildRowSignatures(Bitmap bitmap, int usableWidth, int segments)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        int w = Math.Clamp(usableWidth, 1, width);

        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int length = data.Stride * height;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            Marshal.Copy(data.Scan0, bytes, 0, length);
            var signatures = new float[height * segments];
            int stride = data.Stride;

            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int s = 0; s < segments; s++)
                {
                    int x0 = s * w / segments;
                    int x1 = (s + 1) * w / segments;
                    if (x1 <= x0)
                        x1 = Math.Min(w, x0 + 1);

                    int step = Math.Max(1, (x1 - x0) / 16);
                    long sum = 0;
                    int count = 0;
                    for (int x = x0; x < x1; x += step)
                    {
                        int i = row + x * 4;
                        sum += bytes[i] + bytes[i + 1] + bytes[i + 2];
                        count++;
                    }

                    signatures[y * segments + s] = count > 0 ? (float)(sum / (double)(count * 3)) : 0f;
                }
            }

            return signatures;
        }
        finally
        {
            bitmap.UnlockBits(data);
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    private static int GetRightMargin(int width) => Math.Min(24, Math.Max(6, width / 20));

    private static int GetUsableWidth(int width)
    {
        int usable = width - GetRightMargin(width);
        return usable >= 32 ? usable : width;
    }

    private static bool IsNearlyBlank(Bitmap bitmap)
    {
        int first = bitmap.GetPixel(0, 0).ToArgb();
        int stepX = Math.Max(1, bitmap.Width / 16);
        int stepY = Math.Max(1, bitmap.Height / 16);

        for (int y = 0; y < bitmap.Height; y += stepY)
        {
            for (int x = 0; x < bitmap.Width; x += stepX)
            {
                if (bitmap.GetPixel(x, y).ToArgb() != first)
                    return false;
            }
        }

        return true;
    }
}
