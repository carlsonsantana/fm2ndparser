using System;
using System.IO;

namespace Fm2ndParser.Tests
{
    /// <summary>A single pixel color. Alpha is kept but not used for equality.</summary>
    public readonly record struct Rgb(byte R, byte G, byte B);

    /// <summary>
    /// A decoded, uncompressed BMP addressed in top-down logical coordinates:
    /// <c>this[x, y]</c> with <c>y = 0</c> at the top row and <c>x = 0</c> at the left.
    /// Supports 8-bit indexed, 24-bit and 32-bit BI_RGB bitmaps, which covers both what
    /// the exporter writes (8-bit indexed) and what image editors typically save.
    /// </summary>
    public sealed class Bmp
    {
        public int Width { get; }
        public int Height { get; }
        private readonly Rgb[,] _pixels; // [x, y], y=0 is the top row

        private Bmp(int width, int height, Rgb[,] pixels)
        {
            Width = width;
            Height = height;
            _pixels = pixels;
        }

        public Rgb this[int x, int y] => _pixels[x, y];

        public static Bmp Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 54 || bytes[0] != 'B' || bytes[1] != 'M')
                throw new InvalidDataException($"Not a BMP file: {path}");

            int dataOffset = BitConverter.ToInt32(bytes, 10);
            int dibSize = BitConverter.ToInt32(bytes, 14);
            int width = BitConverter.ToInt32(bytes, 18);
            int rawHeight = BitConverter.ToInt32(bytes, 22);
            short bpp = BitConverter.ToInt16(bytes, 28);
            int compression = BitConverter.ToInt32(bytes, 30);

            if (compression != 0)
                throw new NotSupportedException($"Only uncompressed (BI_RGB) BMPs are supported: {path}");

            bool bottomUp = rawHeight > 0;
            int height = Math.Abs(rawHeight);

            // Color table (present for <= 8bpp). Entries are 4 bytes each (B,G,R,reserved).
            int paletteStart = 14 + dibSize;
            int colorsUsed = BitConverter.ToInt32(bytes, 46);
            Rgb[] palette = Array.Empty<Rgb>();
            if (bpp <= 8)
            {
                int entries = colorsUsed != 0 ? colorsUsed : 1 << bpp;
                palette = new Rgb[entries];
                for (int i = 0; i < entries; i++)
                {
                    int o = paletteStart + i * 4;
                    palette[i] = new Rgb(bytes[o + 2], bytes[o + 1], bytes[o + 0]);
                }
            }

            int bytesPerPixel = bpp / 8;
            int rowStride = ((width * bpp + 31) / 32) * 4; // rows padded to 4-byte boundary
            var pixels = new Rgb[width, height];

            for (int row = 0; row < height; row++)
            {
                int fileRow = bottomUp ? (height - 1 - row) : row; // logical top row -> file row
                int rowStart = dataOffset + fileRow * rowStride;
                for (int x = 0; x < width; x++)
                {
                    Rgb color;
                    if (bpp == 8)
                    {
                        color = palette[bytes[rowStart + x]];
                    }
                    else if (bpp == 24 || bpp == 32)
                    {
                        int o = rowStart + x * bytesPerPixel; // stored B,G,R(,A)
                        color = new Rgb(bytes[o + 2], bytes[o + 1], bytes[o + 0]);
                    }
                    else
                    {
                        throw new NotSupportedException($"Unsupported bpp {bpp}: {path}");
                    }
                    pixels[x, row] = color;
                }
            }

            return new Bmp(width, height, pixels);
        }
    }
}
