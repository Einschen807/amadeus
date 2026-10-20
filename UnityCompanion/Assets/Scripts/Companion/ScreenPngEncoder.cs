using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace KurisuCompanion
{
    // Managed encoding can run beside GDI capture on a worker without touching Unity objects.
    internal static class ScreenPngEncoder
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static string EncodeDataUrl(ScreenFrame frame)
        {
            if (frame == null || frame.Width < 1 || frame.Height < 1 ||
                frame.Bgra == null || frame.Bgra.Length != (long)frame.Width * frame.Height * 4)
                throw new InvalidDataException("屏幕帧尺寸不正确。");
            int stride = checked(frame.Width * 3 + 1);
            byte[] rows = new byte[checked(stride * frame.Height)];
            // GDI's positive-height DIB is bottom-up; PNG scanlines are top-down.
            for (int y = 0; y < frame.Height; y++)
            {
                int source = (frame.Height - y - 1) * frame.Width * 4;
                int target = y * stride + 1;
                for (int x = 0; x < frame.Width; x++)
                {
                    rows[target++] = frame.Bgra[source + 2];
                    rows[target++] = frame.Bgra[source + 1];
                    rows[target++] = frame.Bgra[source];
                    source += 4;
                }
            }
            byte[] deflated;
            using (var compressed = new MemoryStream())
            {
                using (var stream = new DeflateStream(compressed, CompressionLevel.Optimal, true))
                    stream.Write(rows, 0, rows.Length);
                deflated = compressed.ToArray();
            }
            using (var png = new MemoryStream())
            {
                png.Write(Signature, 0, Signature.Length);
                byte[] header = new byte[13];
                Write32(header, 0, (uint)frame.Width);
                Write32(header, 4, (uint)frame.Height);
                header[8] = 8; // RGB, 8 bits per channel
                header[9] = 2;
                WriteChunk(png, "IHDR", header);
                byte[] zlib = new byte[checked(deflated.Length + 6)];
                zlib[0] = 0x78; zlib[1] = 0x9c;
                Buffer.BlockCopy(deflated, 0, zlib, 2, deflated.Length);
                Write32(zlib, zlib.Length - 4, Adler32(rows));
                WriteChunk(png, "IDAT", zlib);
                WriteChunk(png, "IEND", Array.Empty<byte>());
                return "data:image/png;base64," + Convert.ToBase64String(png.ToArray());
            }
        }

        private static void WriteChunk(Stream stream, string name, byte[] data)
        {
            byte[] type = Encoding.ASCII.GetBytes(name);
            byte[] length = new byte[4];
            Write32(length, 0, (uint)data.Length);
            stream.Write(length, 0, 4);
            stream.Write(type, 0, 4);
            stream.Write(data, 0, data.Length);
            uint crc = 0xffffffff;
            foreach (byte value in type) crc = CrcStep(crc, value);
            foreach (byte value in data) crc = CrcStep(crc, value);
            byte[] checksum = new byte[4];
            Write32(checksum, 0, ~crc);
            stream.Write(checksum, 0, 4);
        }

        private static uint CrcStep(uint crc, byte value)
        {
            crc ^= value;
            for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
            return crc;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte value in data) { a = (a + value) % 65521; b = (b + a) % 65521; }
            return (b << 16) | a;
        }

        private static void Write32(byte[] target, int offset, uint value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }
    }
}
