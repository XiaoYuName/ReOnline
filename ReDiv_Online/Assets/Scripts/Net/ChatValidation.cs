namespace ReDiv.Net
{
    /// <summary>
    /// 聊天正文的本地清洗与校验。
    ///
    /// 清洗部分（折空白、压连续空格、挡不可见字符）是服务端
    /// <c>ReDiv_Server/spacetimedb/Chat/ChatRules.cs</c> 的**镜像**，文案一字不差 ——
    /// 免得同一个错误在本地和服务端说法不一样。
    ///
    /// ⚠️ **长度规则两边故意不一样，这不是漂移**（用户 2026-08-26 定的
    /// 「20 个字，这个客户端限制就可以了」）：
    ///   · 客户端 <see cref="MaxChars"/> = 20 个字 —— 这是**策划规则**，
    ///     跟着气泡的排版走，以后要调就调这里；
    ///   · 服务端 <c>ChatRules.MaxDisplayWidth</c> = 60（中文 30 字）—— 那是
    ///     **防滥用的天花板**，比策划规则宽松。
    /// 客户端严于服务端 ⇒ 正常客户端放过去的东西服务端一定不会拒。
    /// 反过来，改过的客户端能发到 30 个汉字 —— 气泡有最大宽度会换行，不会顶炸排版，
    /// 所以先这样。真要卡死就把服务端那个常量也改成按字符数 20。
    ///
    /// 和角色名（<see cref="CharacterValidation"/>）的取舍**完全不同，别照抄**：
    /// 角色名走白名单（只放汉字 / 字母 / 数字 / 下划线），因为它要全服唯一；
    /// 聊天正文走**黑名单**，只挡控制字符 / 零宽字符 / 双向文本控制符，
    /// 标点、括号、emoji、外文一律放行 —— 白名单会让人没法聊天。
    /// </summary>
    public static class ChatValidation
    {
        /// <summary>
        /// 一条消息最多多少个字。**按字符数算，不按显示宽度**（用户 2026-08-26 定的
        /// 「暂定 20 个字」）—— 中文和英文一样都是 20 个。
        ///
        /// 和角色名那边不一样，这里不按显示宽度：气泡有最大宽度、超了会自动换行，
        /// 所以「中英文占不同宽度」不会像定宽的名字栏那样把排版顶炸。
        ///
        /// ⚠️ 数的是 UTF-16 单元，所以一个 emoji（代理对）算 2 个字。
        /// 精确的「字符」计数要走 <c>StringInfo</c>，那是 globalization 相关的，
        /// 对一个上限值不值得。
        /// </summary>
        public const int MaxChars = 20;

        /// <summary>
        /// 输入框的长度提示。从常量算出来，**别在 prefab 里写死** ——
        /// 改了规则提示要跟着变（角色名那边踩过这个）。
        /// </summary>
        public static string LengthHint => $"最多 {MaxChars} 个字";

        /// <summary>
        /// 清洗正文。返回值是**要真正发出去的字符串**（已折空白、压连续空格、去首尾）；
        /// 不合法时返回 null，并把可直接显示给玩家的中文原因写进 <paramref name="error"/>。
        ///
        /// 之所以让本地也做清洗、而不是只做校验：不清洗的话本地判断的宽度和服务端
        /// 清洗后判断的宽度不一样（比如粘贴进来一堆空格），会出现「本地说太长、
        /// 服务端说没问题」这种对不上的情况。
        /// </summary>
        public static string Normalize(string content, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(content))
            {
                error = "说点什么再发";
                return null;
            }

            var builder = new System.Text.StringBuilder(content.Length);
            bool pendingSpace = false;

            foreach (char c in content)
            {
                if (IsWhitespaceLike(c))
                {
                    // 先记着「这里有个空格」，等真的有下一个字符再补 ——
                    // 首尾空白自然就没了，中间的连续空白也压成一个
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (IsForbidden(c))
                {
                    error = "消息里有不能显示的字符";
                    return null;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(c);
            }

            string text = builder.ToString();

            if (text.Length == 0)
            {
                error = "说点什么再发";
                return null;
            }

            if (text.Length > MaxChars)
            {
                error = $"消息太长了，{LengthHint}";
                return null;
            }

            return text;
        }

        /// <summary>
        /// 折成空格的那些。⚠️ 后两个（U+00A0 不换行空格 / U+3000 全角空格）
        /// **必须写成 \uXXXX 转义**，不能贴字面字符 —— 那样和普通空格长得一模一样，
        /// review 时看不出这一行在查什么。服务端那份也是这么写的。
        /// </summary>
        private static bool IsWhitespaceLike(char c) =>
            c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\v' || c == '\f'
            || c == '\u00A0' || c == '\u3000';

        /// <summary>
        /// 黑名单：只挡**看不见或会篡改渲染**的字符。和服务端 <c>ChatRules.IsForbidden</c> 逐条对应。
        /// </summary>
        private static bool IsForbidden(char c)
        {
            // C0 控制字符（空白已经在上一步折掉了）和 DEL
            if (c < 0x20 || c == 0x7F)
            {
                return true;
            }

            // C1 控制字符
            if (c >= 0x80 && c <= 0x9F)
            {
                return true;
            }

            // 软连字符：看不见，能把词拆开绕过匹配
            if (c == 0x00AD)
            {
                return true;
            }

            // 零宽字符 + 左右标记
            if (c >= 0x200B && c <= 0x200F)
            {
                return true;
            }

            // 双向文本嵌入 / 覆盖
            if (c >= 0x202A && c <= 0x202E)
            {
                return true;
            }

            // 词连接符 + 不可见运算符
            if (c >= 0x2060 && c <= 0x2064)
            {
                return true;
            }

            // 双向文本隔离符
            if (c >= 0x2066 && c <= 0x2069)
            {
                return true;
            }

            // BOM / 零宽不换行空格
            if (c == 0xFEFF)
            {
                return true;
            }

            return false;
        }
    }
}
