using System;
using System.Text;

namespace ReDiv.Server.Chat
{
    /// <summary>
    /// 聊天正文的清洗与长度规则。纯函数，不碰数据库。
    ///
    /// 和角色名（<see cref="ReDiv.Server.Character.CharacterRules"/>）的取舍**完全不同，别照抄**：
    /// 角色名走**白名单**（只放汉字 / 字母 / 数字 / 下划线），因为它要全服唯一，
    /// 视觉混淆会造出「看着同名的两个角色」；聊天正文是一次性的、不做唯一性判定，
    /// 白名单会把标点、括号、数学符号全挡掉 —— 那不是「安全」，那是没法聊天。
    ///
    /// 所以这里走**黑名单**，只挡掉三类真正有害的：
    ///   1. 控制字符 —— 塞进 UI 是乱码，塞进日志能伪造换行；
    ///   2. 零宽字符 —— 肉眼看不见，用来绕过（以后的）敏感词过滤和刷屏；
    ///   3. 双向文本控制符 —— 能让后面的文字反向渲染，伪装成别人说的话。
    /// 其余一律放行，包括标点、emoji、外文。
    ///
    /// **没有敏感词过滤**，和角色名一致（见服务端 README「角色名规则」）。
    /// </summary>
    internal static class ChatRules
    {
        /// <summary>
        /// 正文长度按**显示宽度**算，不按字符数：非 ASCII 算 2、ASCII 算 1。
        /// 等价于中文 30 字 / 英文 60 字。
        ///
        /// 按字符数限制的话，60 个汉字在聊天框里的宽度是 60 个字母的两倍，
        /// 而聊天框那一行是定高 + 超出省略的（<c>MessageSlot</c> 预制体），会直接截没。
        /// 这和角色名用同一套理由，只是宽度上限不同。
        /// </summary>
        public const int MaxDisplayWidth = 60;

        /// <summary>
        /// 输入框旁边那句长度提示。从常量算出来，**别在 prefab 里写死** ——
        /// 改了规则提示要跟着变（角色名那边踩过：prefab 写的「1-10字」和真实规则对不上）。
        /// </summary>
        public static string LengthHint =>
            $"最多 {MaxDisplayWidth / 2} 个汉字（英文 {MaxDisplayWidth} 个字符）";

        /// <summary>
        /// 清洗聊天正文并返回可以入库的字符串。不合法就抛（<see cref="Reject"/>）。
        ///
        /// 做的三件事：
        ///   1. 制表符 / 换行**折成空格**而不是拒绝 —— 玩家从别处粘贴一段文字是常见操作，
        ///      为此弹一句报错很烦；而聊天框那一行是单行显示的，换行本来也显示不出来。
        ///   2. 连续空白压成一个，首尾去掉 —— 「一堆空格顶屏」是最省事的刷屏手段。
        ///   3. 剩下的按黑名单挡掉不可见字符。
        /// </summary>
        public static string NormalizeContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                throw Reject("说点什么再发");
            }

            var builder = new StringBuilder(content.Length);
            bool pendingSpace = false;

            foreach (char c in content)
            {
                if (IsWhitespaceLike(c))
                {
                    // 先记着「这里有个空格」，等真的有下一个字符再补 ——
                    // 这样首尾的空白自然就没了，中间的连续空白也压成了一个
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (IsForbidden(c))
                {
                    throw Reject("消息里有不能显示的字符");
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
                // 原文全是空白 / 全被压掉了
                throw Reject("说点什么再发");
            }

            if (DisplayWidth(text) > MaxDisplayWidth)
            {
                throw Reject($"消息太长了，{LengthHint}");
            }

            return text;
        }

        /// <summary>
        /// 显示宽度：非 ASCII 一律算 2。
        ///
        /// 严格说带音标的拉丁字母（é）也被算成 2、emoji 的两个代理项加起来算 4，
        /// 都偏保守。**故意不做精确的东亚宽度判定**：那要一张 Unicode 宽度表，
        /// 而模块是裁剪过的 AOT wasm，跑在 InvariantGlobalization 下拿不到那些数据。
        /// 上限本来就是排版保护，宁可紧一点。
        /// </summary>
        public static int DisplayWidth(string text)
        {
            int width = 0;

            foreach (char c in text)
            {
                width += c <= 0x7F ? 1 : 2;
            }

            return width;
        }

        /// <summary>可预期的业务失败统一从这里抛，和账号 / 角色系统一致。</summary>
        public static Exception Reject(string message) => new Exception(message);

        /// <summary>
        /// 折成空格的那些：ASCII 空格、制表符、换行、回车、垂直制表、换页，
        /// 外加不换行空格（U+00A0）和全角空格（U+3000）——
        /// 后两个肉眼和普通空格一样，不一起压掉就等于留了个刷屏后门。
        ///
        /// ⚠ 后两个**必须写成 \uXXXX 转义**，不能贴字面字符：
        /// 那样在编辑器里和普通空格长得一模一样，review 时看不出这一行在查什么。
        /// </summary>
        private static bool IsWhitespaceLike(char c) =>
            c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\v' || c == '\f'
            || c == '\u00A0' || c == '\u3000';

        /// <summary>
        /// 黑名单：挡掉的都是**看不见或会篡改渲染**的字符。
        /// 放行的东西（标点、括号、emoji、外文）远多于挡掉的，这是有意的。
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

            // 软连字符：看不见，能把一个词拆开绕过匹配
            if (c == 0x00AD)
            {
                return true;
            }

            // 零宽字符（ZWSP / ZWNJ / ZWJ）+ 左右标记（LRM / RLM）
            if (c >= 0x200B && c <= 0x200F)
            {
                return true;
            }

            // 双向文本嵌入 / 覆盖：能让后面的文字反向渲染
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
