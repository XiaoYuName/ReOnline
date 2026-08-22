using System;

namespace ReDiv.Server.Security
{
    /// <summary>
    /// 纯托管 HMAC-SHA256 与 PBKDF2（RFC 2104 / RFC 8018），建在 <see cref="Sha256"/> 上。
    /// 自己写的原因同 <see cref="Sha256"/>：wasi-wasm 上 BCL crypto 运行时不可用。
    /// </summary>
    internal static class Pbkdf2Sha256
    {
        /// <summary>
        /// PBKDF2-HMAC-SHA256。
        /// </summary>
        /// <param name="password">口令原文字节（UTF-8）。</param>
        /// <param name="salt">盐。</param>
        /// <param name="iterations">迭代次数，必须 &gt;= 1。</param>
        /// <param name="length">要导出的字节数，必须 &gt;= 1。</param>
        public static byte[] Derive(byte[] password, byte[] salt, int iterations, int length)
        {
            if (password == null)
            {
                throw new ArgumentNullException(nameof(password));
            }
            if (salt == null)
            {
                throw new ArgumentNullException(nameof(salt));
            }
            if (iterations < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations));
            }
            if (length < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            // HMAC 的 key 在整个推导过程里不变，所以 ipad / opad 只算一次 —— 否则每次
            // 迭代都要重算两个 64 字节的异或，白烧一倍 CPU（这函数会跑上万次迭代）。
            byte[] ipadKey = BuildPaddedKey(password, 0x36);
            byte[] opadKey = BuildPaddedKey(password, 0x5c);

            byte[] output = new byte[length];
            int outputOffset = 0;
            uint blockIndex = 1;

            while (outputOffset < length)
            {
                // U1 = HMAC(P, S || INT_32_BE(i))
                byte[] first = new byte[salt.Length + 4];
                Buffer.BlockCopy(salt, 0, first, 0, salt.Length);
                first[salt.Length] = (byte)(blockIndex >> 24);
                first[salt.Length + 1] = (byte)(blockIndex >> 16);
                first[salt.Length + 2] = (byte)(blockIndex >> 8);
                first[salt.Length + 3] = (byte)blockIndex;

                byte[] u = Hmac(ipadKey, opadKey, first);
                byte[] accumulated = (byte[])u.Clone();

                // T_i = U1 ^ U2 ^ ... ^ Uc
                for (int iteration = 1; iteration < iterations; iteration++)
                {
                    u = Hmac(ipadKey, opadKey, u);
                    for (int i = 0; i < accumulated.Length; i++)
                    {
                        accumulated[i] ^= u[i];
                    }
                }

                int take = Math.Min(accumulated.Length, length - outputOffset);
                Buffer.BlockCopy(accumulated, 0, output, outputOffset, take);
                outputOffset += take;
                blockIndex++;
            }

            return output;
        }

        /// <summary>HMAC-SHA256（RFC 2104）。给自检用，正式路径走预算好 pad 的重载。</summary>
        public static byte[] Hmac(byte[] key, byte[] message)
        {
            return Hmac(BuildPaddedKey(key, 0x36), BuildPaddedKey(key, 0x5c), message);
        }

        /// <summary>HMAC(key, message) = H(opadKey || H(ipadKey || message))。</summary>
        private static byte[] Hmac(byte[] ipadKey, byte[] opadKey, byte[] message)
        {
            byte[] inner = new byte[Sha256.BlockSize + message.Length];
            Buffer.BlockCopy(ipadKey, 0, inner, 0, Sha256.BlockSize);
            Buffer.BlockCopy(message, 0, inner, Sha256.BlockSize, message.Length);
            byte[] innerHash = Sha256.Hash(inner);

            byte[] outer = new byte[Sha256.BlockSize + Sha256.HashSize];
            Buffer.BlockCopy(opadKey, 0, outer, 0, Sha256.BlockSize);
            Buffer.BlockCopy(innerHash, 0, outer, Sha256.BlockSize, Sha256.HashSize);
            return Sha256.Hash(outer);
        }

        /// <summary>
        /// 把 key 规整成一个分组长（64 字节）并异或上 pad 字节。
        /// 超过一个分组的 key 先自己哈希一遍（RFC 2104 规定），短的补零。
        /// </summary>
        private static byte[] BuildPaddedKey(byte[] key, byte pad)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            byte[] normalized = key.Length > Sha256.BlockSize ? Sha256.Hash(key) : key;

            byte[] block = new byte[Sha256.BlockSize];
            for (int i = 0; i < Sha256.BlockSize; i++)
            {
                block[i] = (byte)((i < normalized.Length ? normalized[i] : (byte)0) ^ pad);
            }
            return block;
        }
    }
}
