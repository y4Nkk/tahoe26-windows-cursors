using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TahoeCursorStudio.Infrastructure;
using TahoeCursorStudio.Models;

namespace TahoeCursorStudio.Services;

public sealed class CursorPreviewService
{
    public const int RasterSize = 256;
    private const int ProbeSize = 32;

    public CursorPreviewResult Build(ThemePackage package, CursorRole role)
    {
        var targetPath = Path.Combine(package.SourcePath, role.File);
        var currentPath = ReadCurrentCursorPath(role.Registry);

        PixelBuffer current;
        if (role.SystemId is null)
        {
            current = RenderFile(currentPath, RasterSize);
        }
        else if (File.Exists(currentPath) && ActiveHandleMatchesFile(role.SystemId.Value, currentPath))
        {
            current = RenderFile(currentPath, RasterSize);
        }
        else
        {
            var live = RenderSystem(role.SystemId.Value, RasterSize);
            current = live.HasVisiblePixels ? live : RenderFile(currentPath, RasterSize);
        }

        var target = RenderFile(targetPath, RasterSize);
        var diff = Compare(current, target);
        return new CursorPreviewResult(
            ToBitmapSource(current),
            ToBitmapSource(target),
            ToBitmapSource(diff.Buffer),
            diff.Percent);
    }

    public bool CanLoadAtMaximumSize(string path)
    {
        var handle = NativeMethods.LoadImageW(0, path, NativeMethods.ImageCursor, RasterSize, RasterSize, NativeMethods.LrLoadFromFile);
        if (handle == 0) return false;
        NativeMethods.DestroyCursor(handle);
        return true;
    }

    private static string ReadCurrentCursorPath(string registryName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
        return key?.GetValue(registryName) as string ?? string.Empty;
    }

    private bool ActiveHandleMatchesFile(uint systemId, string path)
    {
        var active = RenderSystem(systemId, ProbeSize);
        if (!active.HasVisiblePixels) return false;
        var file = RenderFile(path, ProbeSize);
        return Compare(active, file).Percent <= 1;
    }

    private static PixelBuffer RenderFile(string path, int renderSize)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return PixelBuffer.Empty(RasterSize);
        var handle = NativeMethods.LoadImageW(0, path, NativeMethods.ImageCursor, renderSize, renderSize, NativeMethods.LrLoadFromFile);
        if (handle == 0) return PixelBuffer.Empty(RasterSize);
        try { return RenderHandle(handle, renderSize); }
        finally { NativeMethods.DestroyCursor(handle); }
    }

    private static PixelBuffer RenderSystem(uint systemId, int renderSize)
    {
        var shared = NativeMethods.LoadCursorW(0, (nint)systemId);
        if (shared == 0) return PixelBuffer.Empty(RasterSize);
        return RenderHandle(shared, renderSize);
    }

    private static PixelBuffer RenderHandle(nint handle, int renderSize)
    {
        using var black = DrawOnOpaqueBackground(handle, renderSize, System.Drawing.Color.Black);
        using var white = DrawOnOpaqueBackground(handle, renderSize, System.Drawing.Color.White);
        var bounds = new Rectangle(0, 0, RasterSize, RasterSize);
        var blackData = black.LockBits(bounds, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var whiteData = white.LockBits(bounds, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var blackPixels = new byte[Math.Abs(blackData.Stride) * RasterSize];
            var whitePixels = new byte[Math.Abs(whiteData.Stride) * RasterSize];
            Marshal.Copy(blackData.Scan0, blackPixels, 0, blackPixels.Length);
            Marshal.Copy(whiteData.Scan0, whitePixels, 0, whitePixels.Length);
            var stride = RasterSize * 4;
            var pixels = new byte[stride * RasterSize];
            for (var y = 0; y < RasterSize; y++)
            {
                var blackRow = y * Math.Abs(blackData.Stride);
                var whiteRow = y * Math.Abs(whiteData.Stride);
                var outputRow = y * stride;
                for (var x = 0; x < RasterSize; x++)
                {
                    var source = x * 4;
                    var output = outputRow + source;
                    var backgroundContribution = (
                        Math.Clamp(whitePixels[whiteRow + source] - blackPixels[blackRow + source], 0, 255)
                        + Math.Clamp(whitePixels[whiteRow + source + 1] - blackPixels[blackRow + source + 1], 0, 255)
                        + Math.Clamp(whitePixels[whiteRow + source + 2] - blackPixels[blackRow + source + 2], 0, 255)) / 3;
                    var alpha = 255 - backgroundContribution;
                    pixels[output + 3] = (byte)alpha;
                    if (alpha == 0) continue;
                    pixels[output] = Unpremultiply(blackPixels[blackRow + source], alpha);
                    pixels[output + 1] = Unpremultiply(blackPixels[blackRow + source + 1], alpha);
                    pixels[output + 2] = Unpremultiply(blackPixels[blackRow + source + 2], alpha);
                }
            }
            return new PixelBuffer(RasterSize, RasterSize, stride, pixels);
        }
        finally
        {
            black.UnlockBits(blackData);
            white.UnlockBits(whiteData);
        }
    }

    private static Bitmap DrawOnOpaqueBackground(nint handle, int renderSize, System.Drawing.Color background)
    {
        var bitmap = new Bitmap(RasterSize, RasterSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(background);
        var offset = (RasterSize - renderSize) / 2;
        var dc = graphics.GetHdc();
        try
        {
            if (!NativeMethods.DrawIconEx(dc, offset, offset, handle, renderSize, renderSize, 0, 0, NativeMethods.DiNormal))
                throw new InvalidOperationException($"无法绘制光标预览，Windows 错误：{Marshal.GetLastWin32Error()}。");
        }
        finally { graphics.ReleaseHdc(dc); }
        return bitmap;
    }

    private static byte Unpremultiply(byte component, int alpha) =>
        (byte)Math.Clamp((component * 255 + alpha / 2) / alpha, 0, 255);

    private static DiffResult Compare(PixelBuffer current, PixelBuffer target)
    {
        var output = new byte[current.Pixels.Length];
        var visible = 0;
        var different = 0;
        for (var y = 0; y < current.Height; y++)
        {
            var row = y * current.Stride;
            for (var x = 0; x < current.Width; x++)
            {
                var p = row + x * 4;
                var delta = Math.Abs(current.Pixels[p + 3] - target.Pixels[p + 3])
                            + Math.Abs(current.Pixels[p + 2] - target.Pixels[p + 2])
                            + Math.Abs(current.Pixels[p + 1] - target.Pixels[p + 1])
                            + Math.Abs(current.Pixels[p] - target.Pixels[p]);
                var pixelVisible = current.Pixels[p + 3] > 0 || target.Pixels[p + 3] > 0;
                if (pixelVisible) visible++;
                if (delta > 48)
                {
                    different++;
                    output[p] = 95;
                    output[p + 1] = 55;
                    output[p + 2] = 255;
                    output[p + 3] = (byte)Math.Min(255, 90 + delta / 5);
                }
                else if (pixelVisible)
                {
                    output[p] = 218;
                    output[p + 1] = 201;
                    output[p + 2] = 190;
                    output[p + 3] = 45;
                }
            }
        }
        var percent = visible == 0 ? 0 : (int)Math.Round(100d * different / visible);
        return new DiffResult(new PixelBuffer(current.Width, current.Height, current.Stride, output), percent);
    }

    private static BitmapSource ToBitmapSource(PixelBuffer buffer)
    {
        var source = BitmapSource.Create(buffer.Width, buffer.Height, 96, 96, PixelFormats.Bgra32, null, buffer.Pixels, buffer.Stride);
        source.Freeze();
        return source;
    }

    private sealed record PixelBuffer(int Width, int Height, int Stride, byte[] Pixels)
    {
        public bool HasVisiblePixels
        {
            get
            {
                for (var index = 3; index < Pixels.Length; index += 4)
                    if (Pixels[index] != 0) return true;
                return false;
            }
        }

        public static PixelBuffer Empty(int size) => new(size, size, size * 4, new byte[size * size * 4]);
    }

    private sealed record DiffResult(PixelBuffer Buffer, int Percent);
}

public sealed record CursorPreviewResult(ImageSource Current, ImageSource Target, ImageSource Diff, int Percent);
