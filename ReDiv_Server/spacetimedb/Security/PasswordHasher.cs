using System;
using System.Text;

namespace ReDiv.Server.Security
{
    /// <summary>
    /// 口令哈希：PBKDF2-HMAC-SHA256 + 每账号独立随机盐。
    ///
    /// 存储格式故意拆成三列（Hash / Salt / Iterations）而不是一个 PHC 串，
    /// 好处是将来提高迭代次数时可以按行判断「这行是旧参数」，在用户下次登录成功后
    /// 用新参数重算一遍（见 <see cref="NeedsRehash"/>）。
    /// </summary>
    internal static class PasswordHasher
    {
        /// <summary>当前的迭代次数。提高它是安全加固，旧账号靠 <see cref="NeedsRehash"/> 渐进迁移。</summary>
        public const uint CurrentIterations = 10_000;

        /// <summary>盐长度（字节）。</summary>
        public const int SaltSize = 16;

        /// <summary>导出密钥长度（字节），取一个 SHA-256 分组。</summary>
        public const int DerivedKeySize = 32;

        /// <summary>算哈希。返回 Base64，直接落 <c>Account.PasswordHash</c>。</summary>
        public static string Hash(string password, byte[] salt, uint iterations)
        {
            byte[] derived = Pbkdf2Sha256.Derive(
                Encoding.UTF8.GetBytes(password), salt, (int)iterations, DerivedKeySize);
            return Convert.ToBase64String(derived);
        }

        /// <summary>
        /// 校验口令。盐 / 哈希用 Base64 传入（就是表里存的原样）。
        /// 表里的值坏掉（不是合法 Base64）时返回 false，不抛异常 —— 校验失败就是校验失败，
        /// 不该让一行脏数据把 Reducer 打成事务回滚。
        /// </summary>
        public static bool Verify(string password, string saltBase64, string expectedHashBase64, uint iterations)
        {
            byte[] salt;
            byte[] expected;
            try
            {
                salt = Convert.FromBase64String(saltBase64);
                expected = Convert.FromBase64String(expectedHashBase64);
            }
            catch (FormatException)
            {
                return false;
            }

            if (iterations < 1 || expected.Length == 0)
            {
                return false;
            }

            byte[] actual = Pbkdf2Sha256.Derive(
                Encoding.UTF8.GetBytes(password), salt, (int)iterations, expected.Length);

            return FixedTimeEquals(actual, expected);
        }

        /// <summary>这行的哈希参数是否已经落后于当前参数（落后就该在登录成功后重算）。</summary>
        public static bool NeedsRehash(uint iterations) => iterations < CurrentIterations;

        /// <summary>
        /// 定长比较：不管哪一位先对不上都走完全程，避免用响应时间试出哈希前缀。
        /// BCL 的 CryptographicOperations.FixedTimeEquals 在 wasi-wasm 上不可用，所以自己写。
        /// </summary>
        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }
            return diff == 0;
        }
    }
}
