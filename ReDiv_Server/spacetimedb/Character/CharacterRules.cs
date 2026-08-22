using System;
using System.Text;

namespace ReDiv.Server.Character
{
    /// <summary>
    /// 角色名的格式规则与归一化。纯函数，不碰数据库。
    ///
    /// 和用户名（<see cref="ReDiv.Server.Auth.AuthRules"/>）的取舍**不一样**，别照抄：
    /// 用户名是登录凭据、只收 ASCII；角色名是给人看的，必须允许中文。
    /// </summary>
    internal static class CharacterRules
    {
        /// <summary>
        /// 名字长度按**显示宽度**算，不按字符数：汉字算 2、ASCII 算 1。
        /// 按字符数限制的话，「16 个汉字」在 UI 上的宽度是「16 个字母」的两倍，排版必炸。
        /// 这个区间等价于：中文 2~8 字，英文 4~16 字。
        /// </summary>
        public const int MinDisplayWidth = 4;

        public const int MaxDisplayWidth = 16;

        /// <summary>
        /// 软删角色的保留名字前缀。<c>#</c> 不在合法字符集里，所以这种键永远撞不上真名字，
        /// 软删时把 NameKey 改成这个形式就能立刻把名字释放出来。
        /// </summary>
        public const string DeletedNameKeyPrefix = "#del#";

        /// <summary>
        /// 校验角色名并返回全服唯一性判定用的键。
        ///
        /// 允许的字符：ASCII 字母 / 数字 / 下划线，以及 CJK 基本区汉字（U+4E00–U+9FFF）。
        ///
        /// 白名单而不是黑名单，是因为放宽比收紧容易 —— 一旦允许了再想禁掉，
        /// 已经建出来的角色就成了历史包袱。被这份白名单挡在外面的都是**有意**挡的：
        ///   · emoji、零宽字符（U+200B 之类）、RTL 控制符 —— 用来伪装和刷屏的老套路
        ///   · 全角字母数字（Ａ１）、假名 —— 和半角/汉字视觉混淆，会出现「看着同名的两个角色」
        ///   · 空格（含全角空格 U+3000）—— 「张 三」和「张三」肉眼难分
        /// 需要放宽（比如加假名、加「·」）时改这里一处，并想清楚混淆风险。
        ///
        /// 归一化只做 trim + ASCII 大小写折叠，**不做 Unicode 折叠**：模块是 trim 过的
        /// AOT wasm，很可能跑在 InvariantGlobalization 下，Unicode 大小写/规范化不可靠。
        /// 白名单已经把容易混淆的区段挡掉了，所以字节精确匹配在这里是够用的。
        /// </summary>
        public static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw Reject("请输入角色名");
            }

            string trimmed = name.Trim();

            int width = 0;
            bool hasLetterOrHan = false;
            var normalized = new StringBuilder(trimmed.Length);

            foreach (char c in trimmed)
            {
                if (IsHan(c))
                {
                    width += 2;
                    hasLetterOrHan = true;
                    normalized.Append(c);
                    continue;
                }

                if (IsAsciiLetter(c))
                {
                    width += 1;
                    hasLetterOrHan = true;
                    normalized.Append(c >= 'A' && c <= 'Z' ? (char)(c + 32) : c);
                    continue;
                }

                if (IsAsciiDigit(c) || c == '_')
                {
                    width += 1;
                    normalized.Append(c);
                    continue;
                }

                throw Reject("角色名只能用汉字、英文字母、数字和下划线");
            }

            if (width < MinDisplayWidth || width > MaxDisplayWidth)
            {
                throw Reject($"角色名长度需为 {MinDisplayWidth / 2}~{MaxDisplayWidth / 2} 个汉字"
                             + $"（英文 {MinDisplayWidth}~{MaxDisplayWidth} 个字符）");
            }

            // 纯数字 / 纯下划线的名字既难认又容易和 id 混，挡掉
            if (!hasLetterOrHan)
            {
                throw Reject("角色名至少要有一个汉字或英文字母");
            }

            return normalized.ToString();
        }

        /// <summary>软删时给角色算一个不会与真名字冲突的保留键。</summary>
        public static string BuildDeletedNameKey(ulong characterId) => DeletedNameKeyPrefix + characterId;

        /// <summary>
        /// 可预期的业务失败统一从这里抛，和账号系统一致：
        /// Reducer 抛异常 ⇒ 事务回滚 + 消息经 Status.Failed 回给调用方，可直接显示给玩家。
        /// </summary>
        public static Exception Reject(string message) => new Exception(message);

        /// <summary>CJK 统一表意文字基本区。扩展区（生僻字）没放开，需要时再说。</summary>
        private static bool IsHan(char c) => c >= 0x4E00 && c <= 0x9FFF;

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
    }
}
