using System;
using System.Text;
using ReDiv.Server.Security;
using SpacetimeDB;

/// <summary>
/// 手写密码学实现的测试向量自检。
///
/// 为什么需要它：wasi-wasm 上 System.Security.Cryptography 运行时不可用
/// （实测抛 SystemSecurityCryptography_PlatformNotSupported），SHA-256 / HMAC / PBKDF2
/// 只能自己写。自己写的哈希一旦算错，症状是「密码永远验不过」或者更糟 ——
/// 「所有密码都验得过」，光看日志根本发现不了。所以拿测试向量钉住。
///
/// 向量来源：在宿主机上用 .NET 的 System.Security.Cryptography 现算的
/// （2026-08-22），和 RFC 6234 / RFC 7914 附录的公开值一致。
///
/// 改了 Security/ 下任何文件后跑一次：
///     spacetime call rediv auth_self_test
/// 全过会 Log.Info 一行 PASS；任何一条不过就抛异常，CLI 直接看到错在哪条。
/// </summary>
public static partial class Module
{
    [SpacetimeDB.Reducer]
    public static void AuthSelfTest(ReducerContext ctx)
    {
        int checks = 0;

        // ---- SHA-256（含空串、单分组、跨分组、多分组三种长度）----
        ExpectHash("sha256('')", Sha256.Hash(Utf8("")),
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", ref checks);

        ExpectHash("sha256('abc')", Sha256.Hash(Utf8("abc")),
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ref checks);

        // 55 字节：填充刚好塞不进本分组的临界情况（count + 9 > 64）
        ExpectHash("sha256(55B)", Sha256.Hash(Utf8("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq")),
            "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1", ref checks);

        ExpectHash("sha256('a'x1000)", Sha256.Hash(Utf8(new string('a', 1000))),
            "41edece42d63e8d9bf515a9ba6932e1c20cbc9f5a5d134645adb5db1b9737ea3", ref checks);

        // ---- HMAC-SHA256（短 key 走补零，长 key 走「先哈希一遍」那条分支）----
        ExpectHash("hmac(key)", Pbkdf2Sha256.Hmac(Utf8("key"), Utf8("The quick brown fox jumps over the lazy dog")),
            "f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8", ref checks);

        ExpectHash("hmac(100B key)", Pbkdf2Sha256.Hmac(Utf8(new string('a', 100)), Utf8("msg")),
            "1b110355f805afa1c9cbb6cf7065062139d2fb7b9eb28c7ae7581ea99cff6b8e", ref checks);

        // ---- PBKDF2-HMAC-SHA256 ----
        ExpectHash("pbkdf2(c=1)", Pbkdf2Sha256.Derive(Utf8("password"), Utf8("salt"), 1, 32),
            "120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b", ref checks);

        // c=2 才会真正走到 U1^U2 的异或累加
        ExpectHash("pbkdf2(c=2)", Pbkdf2Sha256.Derive(Utf8("password"), Utf8("salt"), 2, 32),
            "ae4d0c95af6b46d32d0adff928f06dd02a303f8ef3c251dfd6e2d85a95474c43", ref checks);

        ExpectHash("pbkdf2(c=4096)", Pbkdf2Sha256.Derive(Utf8("password"), Utf8("salt"), 4096, 32),
            "c5e478d59288c841aa530db6845c4c8d962893a001ce4e11a4963873aa98134a", ref checks);

        // 64 字节输出：唯一会走到「多个 T_i 块拼接」那条分支的用例
        ExpectHash("pbkdf2(dkLen=64)", Pbkdf2Sha256.Derive(Utf8("passwd"), Utf8("salt"), 1, 64),
            "55ac046e56e3089fec1691c22544b605f94185216dde0465e68b9d57c20dacbc"
            + "49ca9cccf179b645991664b39d77ef317c71b845b1e30bd509112041d3a19783", ref checks);

        // 非 ASCII 口令：确认 UTF-8 编码路径没被平台差异影响
        ExpectHash("pbkdf2(中文口令)", Pbkdf2Sha256.Derive(Utf8("密码123"), Utf8("ReDivSalt16Bytes"), 10_000, 32),
            "5820b9b2ee8b474c895ea0a8e526bc8eb6a1c507d5c0201d1390a00b1429b63e", ref checks);

        // ---- PasswordHasher 的存取往返 ----
        byte[] salt = new byte[PasswordHasher.SaltSize];
        ctx.Rng.NextBytes(salt);
        string stored = PasswordHasher.Hash("correct horse", salt, PasswordHasher.CurrentIterations);
        string storedSalt = Convert.ToBase64String(salt);

        Expect("verify(正确口令)",
            PasswordHasher.Verify("correct horse", storedSalt, stored, PasswordHasher.CurrentIterations),
            ref checks);
        Expect("verify(错误口令)",
            !PasswordHasher.Verify("correct hors", storedSalt, stored, PasswordHasher.CurrentIterations),
            ref checks);
        Expect("verify(盐不对)",
            !PasswordHasher.Verify("correct horse", Convert.ToBase64String(new byte[16]), stored, PasswordHasher.CurrentIterations),
            ref checks);
        Expect("verify(迭代次数不对)",
            !PasswordHasher.Verify("correct horse", storedSalt, stored, PasswordHasher.CurrentIterations - 1),
            ref checks);
        Expect("verify(哈希字段是脏数据)",
            !PasswordHasher.Verify("correct horse", storedSalt, "not-base64!!", PasswordHasher.CurrentIterations),
            ref checks);

        // 同一口令两次注册必须得到不同的哈希（盐真的在起作用）
        byte[] otherSalt = new byte[PasswordHasher.SaltSize];
        ctx.Rng.NextBytes(otherSalt);
        Expect("同口令不同盐 → 不同哈希",
            PasswordHasher.Hash("correct horse", otherSalt, PasswordHasher.CurrentIterations) != stored,
            ref checks);

        Log.Info($"[AuthSelfTest] PASS，{checks} 项全部通过");
    }

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static void ExpectHash(string name, byte[] actual, string expectedHex, ref int checks)
    {
        checks++;
        string actualHex = Convert.ToHexString(actual).ToLowerInvariant();
        if (actualHex != expectedHex)
        {
            throw new Exception($"[AuthSelfTest] {name} 不匹配：期望 {expectedHex}，实际 {actualHex}");
        }
    }

    private static void Expect(string name, bool condition, ref int checks)
    {
        checks++;
        if (!condition)
        {
            throw new Exception($"[AuthSelfTest] {name} 断言失败");
        }
    }
}
