using System;

namespace ReDiv.Server.Security
{
    /// <summary>
    /// 纯托管 SHA-256（FIPS 180-4），一次性哈希。
    ///
    /// 为什么要自己写而不用 System.Security.Cryptography：
    /// 模块跑在 wasi-wasm 上，BCL 的 crypto **链接得过但运行时直接抛**
    /// <c>SystemSecurityCryptography_PlatformNotSupported</c>（2026-08-22 在本机
    /// SpacetimeDB 2.8.2 上实测过 SHA256.HashData / Rfc2898DeriveBytes.Pbkdf2，两者都抛）。
    ///
    /// 这里只有整数运算和数组，没有平台依赖、没有 static 可变状态，结果完全确定，
    /// 满足 Reducer 的确定性要求（事务可能被重放）。
    ///
    /// 正确性由 <c>Module.AuthSelfTest</c> 里的测试向量守住 —— 那些向量是在宿主机上用
    /// .NET 的 System.Security.Cryptography 算出来的，和 RFC 公开值一致。改这个文件后
    /// 必须跑一次 <c>spacetime call rediv auth_self_test</c>。
    /// </summary>
    internal static class Sha256
    {
        /// <summary>摘要长度（字节）。</summary>
        public const int HashSize = 32;

        /// <summary>压缩函数的分组长度（字节），HMAC 的 padding 也按这个来。</summary>
        public const int BlockSize = 64;

        /// <summary>轮常数：前 64 个素数立方根小数部分的前 32 位。</summary>
        private static readonly uint[] K =
        {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
        };

        /// <summary>算 <paramref name="data"/> 的 SHA-256，返回 32 字节摘要。</summary>
        public static byte[] Hash(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return Hash(data, 0, data.Length);
        }

        /// <summary>算 <paramref name="data"/> 中 [offset, offset+count) 这段的 SHA-256。</summary>
        public static byte[] Hash(byte[] data, int offset, int count)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (offset < 0 || count < 0 || offset + count > data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            unchecked
            {
                // 初始哈希值：前 8 个素数平方根小数部分的前 32 位
                uint h0 = 0x6a09e667, h1 = 0xbb67ae85, h2 = 0x3c6ef372, h3 = 0xa54ff53a;
                uint h4 = 0x510e527f, h5 = 0x9b05688c, h6 = 0x1f83d9ab, h7 = 0x5be0cd19;

                // 填充：0x80，若干 0，最后 8 字节是大端的**比特**长度；总长补到 64 的倍数。
                // 至少要多出 9 字节（1 字节 0x80 + 8 字节长度），所以是 count + 9 向上取整。
                ulong bitLength = (ulong)count * 8UL;
                int paddedLength = (count + 9 + (BlockSize - 1)) / BlockSize * BlockSize;

                byte[] message = new byte[paddedLength];
                Buffer.BlockCopy(data, offset, message, 0, count);
                message[count] = 0x80;
                for (int i = 0; i < 8; i++)
                {
                    message[paddedLength - 1 - i] = (byte)(bitLength >> (8 * i));
                }

                uint[] w = new uint[64];

                for (int blockStart = 0; blockStart < paddedLength; blockStart += BlockSize)
                {
                    // 消息调度：前 16 个字直接读大端，后 48 个字由前面推出
                    for (int t = 0; t < 16; t++)
                    {
                        int p = blockStart + (t * 4);
                        w[t] = ((uint)message[p] << 24)
                             | ((uint)message[p + 1] << 16)
                             | ((uint)message[p + 2] << 8)
                             | message[p + 3];
                    }
                    for (int t = 16; t < 64; t++)
                    {
                        uint s0 = Ror(w[t - 15], 7) ^ Ror(w[t - 15], 18) ^ (w[t - 15] >> 3);
                        uint s1 = Ror(w[t - 2], 17) ^ Ror(w[t - 2], 19) ^ (w[t - 2] >> 10);
                        w[t] = w[t - 16] + s0 + w[t - 7] + s1;
                    }

                    uint a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;

                    for (int t = 0; t < 64; t++)
                    {
                        uint bigS1 = Ror(e, 6) ^ Ror(e, 11) ^ Ror(e, 25);
                        uint ch = (e & f) ^ (~e & g);
                        uint t1 = h + bigS1 + ch + K[t] + w[t];

                        uint bigS0 = Ror(a, 2) ^ Ror(a, 13) ^ Ror(a, 22);
                        uint maj = (a & b) ^ (a & c) ^ (b & c);
                        uint t2 = bigS0 + maj;

                        h = g;
                        g = f;
                        f = e;
                        e = d + t1;
                        d = c;
                        c = b;
                        b = a;
                        a = t1 + t2;
                    }

                    h0 += a; h1 += b; h2 += c; h3 += d;
                    h4 += e; h5 += f; h6 += g; h7 += h;
                }

                byte[] digest = new byte[HashSize];
                WriteBigEndian(digest, 0, h0);
                WriteBigEndian(digest, 4, h1);
                WriteBigEndian(digest, 8, h2);
                WriteBigEndian(digest, 12, h3);
                WriteBigEndian(digest, 16, h4);
                WriteBigEndian(digest, 20, h5);
                WriteBigEndian(digest, 24, h6);
                WriteBigEndian(digest, 28, h7);
                return digest;
            }
        }

        private static uint Ror(uint x, int n) => (x >> n) | (x << (32 - n));

        private static void WriteBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
