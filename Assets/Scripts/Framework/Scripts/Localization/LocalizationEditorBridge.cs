#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace XFramework
{
    /// <summary>
    /// 编辑器下查本地化表的入口。
    ///
    /// 表信息只有 <c>UnityEditor.Localization.LocalizationEditorSettings</c> 能查,而它在
    /// Unity.Localization.Editor 里 —— 运行时程序集(UnityFramework)引用不到编辑器程序集,
    /// 所以这里只留委托,由 UnityFramework.Editor 在加载时填上具体实现。
    ///
    /// 委托没被填(编辑器程序集还没加载完)时,调用方按"查不到"处理,不要报错:
    /// 这些只服务于 Inspector 的下拉和预览,拿不到数据不影响任何实际逻辑。
    /// </summary>
    public static class LocalizationEditorBridge
    {
        /// <summary>
        /// 取所有字符串表的表名,已去重并排序。
        /// </summary>
        public static Func<IReadOnlyList<string>> GetTableNames;

        /// <summary>
        /// 判断 (表名, Key) 在表里是否存在。
        /// </summary>
        public static Func<string, string, bool> HasKey;

        /// <summary>
        /// 取 (表名, Key) 的预览文本。查不到时返回说明性文字而不是空串,方便在 Inspector 上看出是哪一步没对上。
        /// </summary>
        public static Func<string, string, string> GetPreviewText;

        /// <summary>
        /// 在 Project 窗口里选中并高亮指定的本地化表。
        /// </summary>
        public static Action<string> PingTable;
    }
}
#endif
