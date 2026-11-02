using System;
using System.Collections.Generic;
using System.Text;

namespace Cyclopea.Core
{
    /// <summary>
    /// Stereo Secrets: a word, a difficulty and a seed packed into a short code a friend can
    /// type. No server. The recipient's app renders the word as a disparity-only shape in the
    /// static on their nearest table or wall.
    ///
    /// Layout (bits): 4 version/length | up to 12 chars × 6 bits | 4 disparity index | 16 seed | 8 CRC
    /// Encoded in Crockford base32 as CYC-XXXXX-XXXXX-XXXXX-XX.
    /// </summary>
    public static class SecretCode
    {
        public const int MaxChars = 12;
        const string Alphabet = " ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!?'-,.♥"; // 44 symbols < 64
        const string B32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";                  // Crockford

        /// <summary>Disparity choices a sender can pick; index stored in 4 bits.</summary>
        public static readonly double[] DisparityLevels =
            { 15, 20, 30, 45, 60, 90, 120, 180, 240, 360, 480, 720, 960, 1440, 1920, 2880 };

        public sealed class Secret
        {
            public string Text;
            public int DisparityIndex;
            public ushort Seed;
            public double DisparityArcsec => DisparityLevels[DisparityIndex];
        }

        public static string Encode(string text, int disparityIndex, ushort seed)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            text = text.ToUpperInvariant().Trim();
            if (text.Length == 0 || text.Length > MaxChars) throw new ArgumentException("1–12 characters");
            if (disparityIndex < 0 || disparityIndex >= DisparityLevels.Length) throw new ArgumentOutOfRangeException(nameof(disparityIndex));

            var bits = new BitWriter();
            bits.Write(text.Length, 4);
            foreach (char c in text)
            {
                int i = Alphabet.IndexOf(c);
                if (i < 0) throw new ArgumentException("Unsupported character: " + c);
                bits.Write(i, 6);
            }
            bits.Write(disparityIndex, 4);
            bits.Write(seed, 16);
            byte[] payload = bits.ToBytes();
            bits.Write(Crc8(payload), 8);

            string raw = ToBase32(bits.ToBytes(), bits.BitCount);
            return "CYC-" + Group(raw);
        }

        public static bool TryDecode(string code, out Secret secret)
        {
            secret = null;
            if (string.IsNullOrWhiteSpace(code)) return false;
            string s = code.ToUpperInvariant().Replace("CYC", "").Replace("-", "").Replace(" ", "")
                           .Replace('O', '0').Replace('I', '1').Replace('L', '1'); // Crockford forgiveness
            byte[] bytes;
            try { bytes = FromBase32(s); } catch { return false; }

            var r = new BitReader(bytes);
            int len = r.Read(4);
            if (len == 0 || len > MaxChars) return false;
            int payloadBits = 4 + len * 6 + 4 + 16;
            if (bytes.Length * 8 < payloadBits + 8) return false;

            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
            {
                int idx = r.Read(6);
                if (idx >= Alphabet.Length) return false;
                sb.Append(Alphabet[idx]);
            }
            int disp = r.Read(4);
            int seed = r.Read(16);
            int crc = r.Read(8);

            // Recompute the CRC over exactly the payload bits.
            var w = new BitWriter();
            w.Write(len, 4);
            foreach (char c in sb.ToString()) w.Write(Alphabet.IndexOf(c), 6);
            w.Write(disp, 4);
            w.Write(seed, 16);
            if (Crc8(w.ToBytes()) != crc) return false;

            secret = new Secret { Text = sb.ToString(), DisparityIndex = disp, Seed = (ushort)seed };
            return true;
        }

        static string Group(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && i % 5 == 0) sb.Append('-');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        static byte Crc8(byte[] data)
        {
            byte crc = 0;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1);
            }
            return crc;
        }

        static string ToBase32(byte[] bytes, int bitCount)
        {
            var r = new BitReader(bytes);
            var sb = new StringBuilder();
            int remaining = bitCount;
            while (remaining > 0)
            {
                int take = Math.Min(5, remaining);
                int v = r.Read(take) << (5 - take);
                sb.Append(B32[v]);
                remaining -= take;
            }
            return sb.ToString();
        }

        static byte[] FromBase32(string s)
        {
            var w = new BitWriter();
            foreach (char c in s)
            {
                int v = B32.IndexOf(c);
                if (v < 0) throw new FormatException();
                w.Write(v, 5);
            }
            return w.ToBytes();
        }

        sealed class BitWriter
        {
            readonly List<byte> bytes = new List<byte>();
            int bitCount;
            public int BitCount => bitCount;
            public void Write(int value, int bits)
            {
                for (int i = bits - 1; i >= 0; i--)
                {
                    if (bitCount % 8 == 0) bytes.Add(0);
                    if (((value >> i) & 1) != 0) bytes[bitCount / 8] |= (byte)(0x80 >> (bitCount % 8));
                    bitCount++;
                }
            }
            public byte[] ToBytes() => bytes.ToArray();
        }

        sealed class BitReader
        {
            readonly byte[] bytes; int pos;
            public BitReader(byte[] b) { bytes = b; }
            public int Read(int bits)
            {
                int v = 0;
                for (int i = 0; i < bits; i++)
                {
                    int bit = pos / 8 < bytes.Length ? (bytes[pos / 8] >> (7 - pos % 8)) & 1 : 0;
                    v = (v << 1) | bit; pos++;
                }
                return v;
            }
        }
    }
}
