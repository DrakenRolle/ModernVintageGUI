using System.IO.Compression;
using Cairo;

namespace ModernVintageGUI.Designer.Rendering
{
    /// <summary>
    /// Encodes a Cairo surface as a PNG in memory.
    ///
    /// cairo-sharp can only write a PNG to a file path, and the designer redraws on every
    /// keystroke in the property grid - going through the disk for each of those would be both
    /// slower and messier than the eighty lines it takes to write the format directly.
    /// </summary>
    public static class PngEncoder
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static byte[] Encode(ImageSurface surface)
        {
            surface.Flush();

            int width = surface.Width;
            int height = surface.Height;
            int stride = surface.Stride;

            byte[] source = new byte[stride * height];
            System.Runtime.InteropServices.Marshal.Copy(surface.DataPtr, source, 0, source.Length);

            // One filter byte (0 = none) plus RGBA per pixel, per scanline.
            byte[] raw = new byte[height * (1 + width * 4)];

            for (int y = 0; y < height; y++)
            {
                int rowStart = y * (1 + width * 4);
                raw[rowStart] = 0;

                for (int x = 0; x < width; x++)
                {
                    int src = y * stride + x * 4;

                    // Argb32 is native endian and alpha premultiplied; in memory on a little
                    // endian machine that is B, G, R, A. PNG wants straight RGBA.
                    byte b = source[src + 0];
                    byte g = source[src + 1];
                    byte r = source[src + 2];
                    byte a = source[src + 3];

                    if (a != 0 && a != 255)
                    {
                        r = (byte)Math.Min(255, r * 255 / a);
                        g = (byte)Math.Min(255, g * 255 / a);
                        b = (byte)Math.Min(255, b * 255 / a);
                    }

                    int dst = rowStart + 1 + x * 4;
                    raw[dst + 0] = r;
                    raw[dst + 1] = g;
                    raw[dst + 2] = b;
                    raw[dst + 3] = a;
                }
            }

            using var output = new MemoryStream();
            output.Write(Signature, 0, Signature.Length);

            var header = new byte[13];
            WriteBigEndian(header, 0, width);
            WriteBigEndian(header, 4, height);
            header[8] = 8;  // bit depth
            header[9] = 6;  // colour type: truecolour with alpha
            header[10] = 0; // deflate
            header[11] = 0; // adaptive filtering
            header[12] = 0; // no interlace

            WriteChunk(output, "IHDR", header);
            WriteChunk(output, "IDAT", Deflate(raw));
            WriteChunk(output, "IEND", Array.Empty<byte>());

            return output.ToArray();
        }

        /// <summary>The raw scanlines wrapped in a zlib stream, which is what IDAT holds.</summary>
        private static byte[] Deflate(byte[] raw)
        {
            using var compressed = new MemoryStream();

            compressed.WriteByte(0x78); // deflate, 32k window
            compressed.WriteByte(0x01); // no preset dictionary, fastest

            using (var deflate = new DeflateStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                deflate.Write(raw, 0, raw.Length);
            }

            uint adler = Adler32(raw);
            compressed.WriteByte((byte)(adler >> 24));
            compressed.WriteByte((byte)(adler >> 16));
            compressed.WriteByte((byte)(adler >> 8));
            compressed.WriteByte((byte)adler);

            return compressed.ToArray();
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, data.Length);
            stream.Write(length, 0, 4);

            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            stream.Write(typeBytes, 0, 4);
            stream.Write(data, 0, data.Length);

            uint crc = Crc32(typeBytes, data);
            var crcBytes = new byte[4];
            WriteBigEndian(crcBytes, 0, (int)crc);
            stream.Write(crcBytes, 0, 4);
        }

        private static void WriteBigEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset + 0] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];

            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
                }
                table[n] = c;
            }

            return table;
        }

        private static uint Crc32(byte[] type, byte[] data)
        {
            uint c = 0xffffffffu;

            foreach (byte b in type)
                c = CrcTable[(c ^ b) & 0xff] ^ (c >> 8);

            foreach (byte b in data)
                c = CrcTable[(c ^ b) & 0xff] ^ (c >> 8);

            return c ^ 0xffffffffu;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;

            foreach (byte value in data)
            {
                a = (a + value) % 65521;
                b = (b + a) % 65521;
            }

            return (b << 16) | a;
        }
    }
}
