// iRecord - Windows.Media.Ocr bridge.
// Converts a GDI+ Bitmap to a WinRT SoftwareBitmap and returns recognized text.
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace iRecord.App;

/// <summary>
/// Thin adapter around the Windows OCR engine; requires a Windows 10/11 language pack.
/// </summary>
internal static class WindowsOcr
{
    /// <summary>Encodes the bitmap as PNG, decodes it through WinRT, and runs OCR.</summary>
    public static async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        memory.Position = 0;

        using var randomAccessStream = new InMemoryRandomAccessStream();
        var output = randomAccessStream.AsStreamForWrite();
        await memory.CopyToAsync(output);
        await output.FlushAsync();
        randomAccessStream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("No OCR language pack is installed.");
        var result = await engine.RecognizeAsync(softwareBitmap);
        return result.Text?.Trim() ?? string.Empty;
    }
}
