using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AuraShell.Services
{
    /// <summary>
    /// Zero-dependency pure C# QR Code generator (Byte mode, ECC Level L, Versions 1..6, up to 134 bytes)
    /// rendering directly to a crisp WPF WriteableBitmap.
    /// </summary>
    public static class QrCodeGenerator
    {
        // Version 1..6 specs for ECC Level L (single block for V1..V5, 2 blocks for V6 -> we support V1..V5 single-block up to 106 bytes cleanly)
        private static readonly (int Size, int DataCodewords, int EccCodewords, int[] AlignCenters)[] VersionSpecs =
        {
            (0, 0, 0, Array.Empty<int>()),
            (21, 19, 7, Array.Empty<int>()),
            (25, 34, 10, new[] { 6, 18 }),
            (29, 55, 15, new[] { 6, 22 }),
            (33, 80, 20, new[] { 6, 26 }),
            (37, 108, 26, new[] { 6, 30 })
        };

        private static readonly byte[] GfExp = new byte[512];
        private static readonly byte[] GfLog = new byte[256];

        static QrCodeGenerator()
        {
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                GfExp[i] = (byte)x;
                GfLog[x] = (byte)i;
                x <<= 1;
                if ((x & 0x100) != 0) x ^= 0x11D;
            }
            for (int i = 255; i < 512; i++)
                GfExp[i] = GfExp[i - 255];
        }

        public static BitmapSource GenerateBitmap(string text, int pixelsPerModule = 6)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            if (utf8.Length > 106)
            {
                Array.Resize(ref utf8, 106);
            }

            int version = 1;
            while (version < VersionSpecs.Length - 1 && utf8.Length + 2 > VersionSpecs[version].DataCodewords)
                version++;

            bool[,] matrix = EncodeMatrix(utf8, version);
            int size = matrix.GetLength(0);
            int quiet = 3;
            int totalModules = size + quiet * 2;
            int imgSize = totalModules * pixelsPerModule;

            byte[] pixels = new byte[imgSize * imgSize * 4];
            // Fill white background
            for (int i = 0; i < pixels.Length; i++) pixels[i] = 255;

            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    if (!matrix[r, c]) continue;
                    int py0 = (r + quiet) * pixelsPerModule;
                    int px0 = (c + quiet) * pixelsPerModule;
                    for (int dy = 0; dy < pixelsPerModule; dy++)
                    {
                        int rowOffset = (py0 + dy) * imgSize * 4;
                        for (int dx = 0; dx < pixelsPerModule; dx++)
                        {
                            int idx = rowOffset + (px0 + dx) * 4;
                            pixels[idx + 0] = 10;  // B
                            pixels[idx + 1] = 7;   // G
                            pixels[idx + 2] = 6;   // R
                            pixels[idx + 3] = 255; // A
                        }
                    }
                }
            }

            var wb = new WriteableBitmap(imgSize, imgSize, 96, 96, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, imgSize, imgSize), pixels, imgSize * 4, 0);
            wb.Freeze();
            return wb;
        }

        private static bool[,] EncodeMatrix(byte[] payload, int version)
        {
            var spec = VersionSpecs[version];
            int size = spec.Size;

            // 1. Build bit stream: Byte mode (0100) + 8-bit length + payload + terminator + pad
            var bits = new List<bool>(spec.DataCodewords * 8);
            AppendBits(bits, 0b0100, 4);
            AppendBits(bits, payload.Length, 8);
            foreach (byte b in payload) AppendBits(bits, b, 8);

            int maxBits = spec.DataCodewords * 8;
            for (int i = 0; i < 4 && bits.Count < maxBits; i++) bits.Add(false);
            while (bits.Count % 8 != 0) bits.Add(false);

            byte[] data = new byte[spec.DataCodewords];
            int byteCount = bits.Count / 8;
            for (int i = 0; i < byteCount; i++)
            {
                byte val = 0;
                for (int b = 0; b < 8; b++)
                    val = (byte)((val << 1) | (bits[i * 8 + b] ? 1 : 0));
                data[i] = val;
            }

            byte[] padBytes = { 0xEC, 0x11 };
            for (int i = byteCount; i < spec.DataCodewords; i++)
                data[i] = padBytes[(i - byteCount) % 2];

            // 2. Reed-Solomon ECC
            byte[] ecc = ComputeReedSolomon(data, spec.EccCodewords);
            byte[] allCodewords = new byte[data.Length + ecc.Length];
            Buffer.BlockCopy(data, 0, allCodewords, 0, data.Length);
            Buffer.BlockCopy(ecc, 0, allCodewords, data.Length, ecc.Length);

            // 3. Place function patterns
            bool[,] modules = new bool[size, size];
            bool[,] isFunction = new bool[size, size];

            DrawFinder(modules, isFunction, 0, 0);
            DrawFinder(modules, isFunction, size - 7, 0);
            DrawFinder(modules, isFunction, 0, size - 7);

            // Timing patterns
            for (int i = 8; i < size - 8; i++)
            {
                SetFunction(modules, isFunction, 6, i, i % 2 == 0);
                SetFunction(modules, isFunction, i, 6, i % 2 == 0);
            }

            // Alignment patterns
            foreach (int r in spec.AlignCenters)
            {
                foreach (int c in spec.AlignCenters)
                {
                    if ((r == 6 && c == 6) || (r == 6 && c == size - 7) || (r == size - 7 && c == 6))
                        continue;
                    DrawAlignment(modules, isFunction, r, c);
                }
            }

            // Reserve format info areas + dark module
            for (int i = 0; i < 9; i++)
            {
                if (i != 6)
                {
                    isFunction[8, i] = true;
                    isFunction[i, 8] = true;
                }
            }
            for (int i = 0; i < 8; i++)
            {
                isFunction[8, size - 1 - i] = true;
                isFunction[size - 1 - i, 8] = true;
            }
            SetFunction(modules, isFunction, size - 8, 8, true);

            // 4. Place data bits in zigzag
            int bitIndex = 0;
            int totalBits = allCodewords.Length * 8;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                bool upward = ((right + 1) & 2) == 0;
                for (int vert = 0; vert < size; vert++)
                {
                    int row = upward ? size - 1 - vert : vert;
                    for (int j = 0; j < 2; j++)
                    {
                        int col = right - j;
                        if (isFunction[row, col]) continue;
                        bool bit = false;
                        if (bitIndex < totalBits)
                        {
                            byte cw = allCodewords[bitIndex >> 3];
                            bit = ((cw >> (7 - (bitIndex & 7))) & 1) != 0;
                            bitIndex++;
                        }
                        modules[row, col] = bit;
                    }
                }
            }

            // 5. Apply Mask 0 ((r + c) % 2 == 0) & write Format Info (ECC L = 01, Mask 0 = 000)
            const int mask = 0;
            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    if (!isFunction[r, c] && ((r + c) % 2 == 0))
                        modules[r, c] = !modules[r, c];
                }
            }

            DrawFormatBits(modules, size, 0b01, mask);
            return modules;
        }

        private static void DrawFormatBits(bool[,] modules, int size, int eccBits, int mask)
        {
            int data = (eccBits << 3) | mask;
            int rem = data;
            for (int i = 0; i < 10; i++)
                rem = (rem << 1) ^ (((rem >> 9) & 1) * 0x537);
            int bits = ((data << 10) | rem) ^ 0x5412;

            for (int i = 0; i <= 5; i++) modules[8, i] = ((bits >> (14 - i)) & 1) != 0;
            modules[8, 7] = ((bits >> 8) & 1) != 0;
            modules[8, 8] = ((bits >> 7) & 1) != 0;
            modules[7, 8] = ((bits >> 6) & 1) != 0;
            for (int i = 0; i <= 5; i++) modules[5 - i, 8] = ((bits >> (5 - i)) & 1) != 0;

            for (int i = 0; i <= 7; i++) modules[8, size - 1 - i] = ((bits >> i) & 1) != 0;
            for (int i = 0; i <= 6; i++) modules[size - 7 + i, 8] = ((bits >> (14 - i)) & 1) != 0;
        }

        private static void DrawFinder(bool[,] m, bool[,] f, int r0, int c0)
        {
            int size = m.GetLength(0);
            for (int dr = -1; dr <= 7; dr++)
            {
                for (int dc = -1; dc <= 7; dc++)
                {
                    int r = r0 + dr, c = c0 + dc;
                    if (r < 0 || c < 0 || r >= size || c >= size) continue;
                    bool dark = dr >= 0 && dr <= 6 && dc >= 0 && dc <= 6 &&
                                (dr == 0 || dr == 6 || dc == 0 || dc == 6 || (dr >= 2 && dr <= 4 && dc >= 2 && dc <= 4));
                    SetFunction(m, f, r, c, dark);
                }
            }
        }

        private static void DrawAlignment(bool[,] m, bool[,] f, int cr, int cc)
        {
            for (int dr = -2; dr <= 2; dr++)
            {
                for (int dc = -2; dc <= 2; dc++)
                {
                    bool dark = Math.Max(Math.Abs(dr), Math.Abs(dc)) != 1;
                    SetFunction(m, f, cr + dr, cc + dc, dark);
                }
            }
        }

        private static void SetFunction(bool[,] m, bool[,] f, int r, int c, bool dark)
        {
            m[r, c] = dark;
            f[r, c] = true;
        }

        private static void AppendBits(List<bool> bits, int value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
                bits.Add(((value >> i) & 1) != 0);
        }

        private static byte[] ComputeReedSolomon(byte[] data, int eccCount)
        {
            byte[] gen = { 1 };
            for (int i = 0; i < eccCount; i++)
            {
                byte[] next = new byte[gen.Length + 1];
                byte factor = GfExp[i];
                for (int j = 0; j < gen.Length; j++)
                {
                    next[j] ^= gen[j];
                    next[j + 1] ^= GfMul(gen[j], factor);
                }
                gen = next;
            }

            byte[] rem = new byte[eccCount];
            foreach (byte b in data)
            {
                byte factor = (byte)(b ^ rem[0]);
                Array.Copy(rem, 1, rem, 0, eccCount - 1);
                rem[eccCount - 1] = 0;
                for (int j = 0; j < eccCount; j++)
                    rem[j] ^= GfMul(gen[j + 1], factor);
            }
            return rem;
        }

        private static byte GfMul(byte a, byte b)
        {
            if (a == 0 || b == 0) return 0;
            return GfExp[GfLog[a] + GfLog[b]];
        }
    }
}
