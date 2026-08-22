using System;

namespace ReDiv.Server.Auth
{
    /// <summary>
    /// 用户名 / 口令的格式规则与归一化。纯函数，不碰数据库。
    ///
    /// 校验失败一律抛异常：Reducer 抛异常 = 事务回滚 + 把消息回给**调用方那一个客户端**
    /// （Unity 侧在 <c>Conn.Reducers.OnRegister/OnLogin</c> 的 <c>ctx.Event.Status</c> 里
    /// 拿到 <c>Status.Failed(reason)</c>，reason 就是这里的 message）。
    /// 所以这些 message 是直接给玩家看的文案。
    /// </summary>
    internal static class AuthRules
    {
        public const int UsernameMinLength = 3;
        public const int UsernameMaxLength = 16;
        public const int PasswordMinLength = 6;
        public const int PasswordMaxLength = 64;

        /// <summary>
        /// 登录路径上只用来挡离谱输入的上限。故意比注册上限宽松很多：
        /// 注册规则以后收紧，老账号也得还能登进来。
        /// </summary>
        public const int LookupMaxLength = 256;

        /// <summary>
        /// 校验用户名并返回归一化后的查找键（ASCII 小写）。
        ///
        /// 规则：3~16 个字符，只允许 ASCII 字母 / 数字 / 下划线，且必须以字母开头。
        /// 只收 ASCII 是有意的 —— 模块是 trim 过的 AOT wasm，很可能跑在
        /// InvariantGlobalization 下，Unicode 大小写折叠和规范化（NFKC、土耳其语 i 之类）
        /// 在这种环境里不可靠。用户名要做唯一性判断，折叠规则一旦不确定就会出「看起来同名
        /// 却是两个账号」。所以这里手写 ASCII 小写转换，不用 ToLowerInvariant。
        /// 需要中文昵称的话，以后单开一个 Profile 表放 DisplayName，别动这个登录键。
        /// </summary>
        public static string NormalizeUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw Reject("请输入用户名");
            }

            string trimmed = username.Trim();

            if (trimmed.Length < UsernameMinLength || trimmed.Length > UsernameMaxLength)
            {
                throw Reject($"用户名长度需为 {UsernameMinLength}~{UsernameMaxLength} 个字符");
            }

            if (!IsAsciiLetter(trimmed[0]))
            {
                throw Reject("用户名必须以英文字母开头");
            }

            var normalized = new char[trimmed.Length];
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (!IsAsciiLetter(c) && !IsAsciiDigit(c) && c != '_')
                {
                    throw Reject("用户名只能包含英文字母、数字和下划线");
                }
                normalized[i] = c >= 'A' && c <= 'Z' ? (char)(c + 32) : c;
            }

            return new string(normalized);
        }

        /// <summary>
        /// 登录用的用户名归一化：只做 trim + ASCII 小写，**不校验格式规则**。
        ///
        /// 为什么不复用 <see cref="NormalizeUsername"/>：那是注册规则。规则以后一收紧
        /// （比如最短改成 4 个字符），复用它就会把按老规则注册的账号直接锁死在门外。
        /// 登录只需要拿到查找键；键查不到，自然会回「用户名或密码不正确」。
        /// 必须和 <see cref="NormalizeUsername"/> 的归一化方式保持一致，否则查不到账号。
        /// </summary>
        public static string NormalizeForLookup(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw Reject("请输入用户名");
            }

            string trimmed = username.Trim();

            // 只挡明显离谱的输入，避免拿超长串来喂查询
            if (trimmed.Length > LookupMaxLength)
            {
                throw Reject("用户名或密码不正确");
            }

            var normalized = new char[trimmed.Length];
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                normalized[i] = c >= 'A' && c <= 'Z' ? (char)(c + 32) : c;
            }
            return new string(normalized);
        }

        /// <summary>
        /// 登录用的口令检查：只挡空值和离谱长度，**不校验格式规则**。
        /// 理由同 <see cref="NormalizeForLookup"/> —— 口令规则收紧后，老口令还得能登进来
        /// （要强制换口令是产品决定，不该由登录校验顺手实现成「登不上」）。
        /// </summary>
        public static void ValidatePasswordForLogin(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw Reject("请输入密码");
            }

            if (password.Length > LookupMaxLength)
            {
                throw Reject("用户名或密码不正确");
            }
        }

        /// <summary>
        /// 校验口令格式（**注册用**）。
        ///
        /// 不 Trim、不限制字符集（除控制字符）—— 口令里的空格是玩家有意输入的内容，
        /// 服务端替他修掉会导致「明明输对了却登不上」。
        /// </summary>
        public static void ValidatePassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw Reject("请输入密码");
            }

            if (password.Length < PasswordMinLength || password.Length > PasswordMaxLength)
            {
                throw Reject($"密码长度需为 {PasswordMinLength}~{PasswordMaxLength} 个字符");
            }

            foreach (char c in password)
            {
                if (c < 0x20 || c == 0x7f)
                {
                    throw Reject("密码不能包含控制字符");
                }
            }
        }

        /// <summary>
        /// 所有「可预期的业务失败」都从这里抛，方便以后统一改回报方式。
        ///
        /// 现在是抛异常（事务回滚，消息走 Status.Failed 回给调用方）。
        /// 注意这条路**无法**顺带记账：想做「连续失败 N 次锁定」的话，计数写在同一个事务里
        /// 会被回滚一起吃掉，必须改成不抛异常、把结果写进事件表回报。见 README 的说明。
        /// </summary>
        public static Exception Reject(string message) => new Exception(message);

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
    }
}
