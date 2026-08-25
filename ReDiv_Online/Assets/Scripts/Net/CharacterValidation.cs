namespace ReDiv.Net
{
    /// <summary>
    /// 角色名的本地格式校验。
    ///
    /// 这是服务端 <c>ReDiv_Server/spacetimedb/Character/CharacterRules.cs</c> 的**镜像**，
    /// 只为了少一次往返、让玩家立刻看到提示。**服务端那份才是权威** ——
    /// 这里放过去的东西照样会被服务端拒掉，所以两边改要一起改。
    /// 文案也保持一字不差，免得同一个错误在本地和服务端说法不一样。
    ///
    /// 和用户名（<see cref="AuthValidation"/>）的取舍**不一样**，别照抄：
    /// 用户名是登录凭据、只收 ASCII；角色名是给人看的，必须允许中文。
    ///
    /// ⚠️ 这里只查**格式**。重名查不了 —— 角色表是私有表，客户端订阅不到，
    /// 只能问服务端（<c>CharacterManager.CheckNameAsync</c>）。
    /// </summary>
    public static class CharacterValidation
    {
        /// <summary>
        /// 名字长度按**显示宽度**算，不按字符数：汉字算 2、ASCII 算 1。
        /// 这个区间等价于：中文 2~8 字，英文 4~16 字。
        /// </summary>
        public const int MinDisplayWidth = 4;

        public const int MaxDisplayWidth = 16;

        /// <summary>
        /// 输入框上那句长度提示。从常量算出来，改了规则提示会跟着变 ——
        /// 别在 prefab 里写死（prefab 里原来那句「请输入1-10字」和真实规则对不上）。
        /// </summary>
        public static string LengthHint =>
            $"请输入 {MinDisplayWidth / 2}~{MaxDisplayWidth / 2} 个汉字（英文 {MinDisplayWidth}~{MaxDisplayWidth} 个字符）";

        /// <summary>
        /// 校验角色名。通过返回 null，否则返回可直接显示给玩家的中文文案。
        /// </summary>
        public static string CheckName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "请输入角色名";
            }

            string trimmed = name.Trim();

            int width = 0;
            bool hasLetterOrHan = false;

            foreach (char c in trimmed)
            {
                if (IsHan(c))
                {
                    width += 2;
                    hasLetterOrHan = true;
                    continue;
                }

                if (IsAsciiLetter(c))
                {
                    width += 1;
                    hasLetterOrHan = true;
                    continue;
                }

                if (IsAsciiDigit(c) || c == '_')
                {
                    width += 1;
                    continue;
                }

                // 白名单之外的一律挡掉：emoji、零宽字符、RTL 控制符、全角字母数字、
                // 假名、空格 —— 理由见服务端 CharacterRules 的注释
                return "角色名只能用汉字、英文字母、数字和下划线";
            }

            if (width < MinDisplayWidth || width > MaxDisplayWidth)
            {
                return $"角色名长度需为 {MinDisplayWidth / 2}~{MaxDisplayWidth / 2} 个汉字"
                       + $"（英文 {MinDisplayWidth}~{MaxDisplayWidth} 个字符）";
            }

            if (!hasLetterOrHan)
            {
                return "角色名至少要有一个汉字或英文字母";
            }

            return null;
        }

        /// <summary>CJK 统一表意文字基本区。扩展区（生僻字）没放开，和服务端一致。</summary>
        private static bool IsHan(char c) => c >= 0x4E00 && c <= 0x9FFF;

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
    }
}
