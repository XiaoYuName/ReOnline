namespace ReDiv.Net
{
    /// <summary>
    /// 用户名 / 口令的本地格式校验。
    ///
    /// 这是服务端 <c>ReDiv_Server/spacetimedb/Auth/AuthRules.cs</c> 的**镜像**，
    /// 只为了少一次往返、让玩家立刻看到提示。**服务端那份才是权威**，
    /// 这里放过去的东西照样会被服务端拒掉，所以两边改要一起改。
    ///
    /// 注册和登录的严格程度**故意不一样**（和服务端一致）：
    /// 登录只查空值，不查格式 —— 否则以后一收紧规则，按老规则注册的号会被客户端
    /// 拦在门外，连试都试不了。
    /// </summary>
    public static class AuthValidation
    {
        public const int UsernameMinLength = 3;
        public const int UsernameMaxLength = 16;
        public const int PasswordMinLength = 6;
        public const int PasswordMaxLength = 64;

        /// <summary>注册用：校验用户名。通过返回 null，否则返回可直接显示的中文文案。</summary>
        public static string CheckUsernameForRegister(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return "请输入用户名";
            }

            string trimmed = username.Trim();

            if (trimmed.Length < UsernameMinLength || trimmed.Length > UsernameMaxLength)
            {
                return $"用户名长度需为 {UsernameMinLength}~{UsernameMaxLength} 个字符";
            }

            if (!IsAsciiLetter(trimmed[0]))
            {
                return "用户名必须以英文字母开头";
            }

            foreach (char c in trimmed)
            {
                if (!IsAsciiLetter(c) && !IsAsciiDigit(c) && c != '_')
                {
                    return "用户名只能包含英文字母、数字和下划线";
                }
            }

            return null;
        }

        /// <summary>注册用：校验口令。通过返回 null。</summary>
        public static string CheckPasswordForRegister(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return "请输入密码";
            }

            if (password.Length < PasswordMinLength || password.Length > PasswordMaxLength)
            {
                return $"密码长度需为 {PasswordMinLength}~{PasswordMaxLength} 个字符";
            }

            foreach (char c in password)
            {
                if (c < 0x20 || c == 0x7f)
                {
                    return "密码不能包含控制字符";
                }
            }

            return null;
        }

        /// <summary>登录用：只查空值，不查格式。通过返回 null。</summary>
        public static string CheckForLogin(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return "请输入用户名";
            }

            if (string.IsNullOrEmpty(password))
            {
                return "请输入密码";
            }

            return null;
        }

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
    }
}
