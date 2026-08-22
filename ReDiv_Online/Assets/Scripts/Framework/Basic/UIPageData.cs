using System;
using Sirenix.OdinInspector;

namespace XFramework
{
    /// <summary>
    /// 一个UI界面的配置。原先由 Luban 从表里生成,现在改成 <see cref="UIPageConfiguration"/> 里的一行,
    /// 好处是框架层不用再认识 Luban 和业务的 LubanManager,界面路径也能在 Inspector 里直接选。
    /// </summary>
    [Serializable]
    public class UIPageData : OdinDataItem<UIPageData>
    {
        [LabelText("界面ID"), LabelWidth(60)]
        public string PageID;

        /// <summary>
        /// Addressable 资源根目录。<see cref="PagePath"/> 存的是相对它的路径。
        /// </summary>
        public const string AddressableRoot = "Assets/AddressableAssets/Remote/";

        /// <summary>
        /// 预制体路径，**相对 <see cref="AddressableRoot"/>**（Odin 的 FilePath 会自动去掉
        /// ParentFolder 前缀，Inspector 里选完存下来就是 "Prefabs/UGUI/xxx/xxx.prefab" 这种）。
        ///
        /// ⚠️ 加载时不要直接用它，用 <see cref="PageKey"/> —— 详见那边的注释。
        /// </summary>
        [LabelText("界面路径")]
        [FilePath(ParentFolder = "Assets/AddressableAssets/Remote", Extensions = "prefab")]
        public string PagePath;

        /// <summary>
        /// 真正拿去加载的 Addressable Key。
        ///
        /// 本工程的地址约定是**完整资源路径**（AddressableBuild 的 UseAssetPathAsAddress = true，
        /// 所以 AssetKeys 里全是 "Assets/..." 全路径），而 <see cref="PagePath"/> 因为
        /// Odin 的 FilePath(ParentFolder) 存的是相对路径 —— 两者对不上，直接拿 PagePath 去加载
        /// 在编辑器（AssetDatabase 模式）下会解析不到路径、拿到 null，
        /// 然后在 Instantiate 处炸成一句和资源毫无关系的
        /// "ArgumentException: The Object you want to instantiate is null"。
        ///
        /// 这里补上前缀。两种存法都兼容：已经是 "Assets/" 开头的原样返回，
        /// 所以以后哪天把 FilePath 的 ParentFolder 去掉、改存全路径也不用改代码。
        /// </summary>
        public string PageKey
        {
            get
            {
                if (string.IsNullOrEmpty(PagePath))
                {
                    return PagePath;
                }

                string normalized = PagePath.Replace('\\', '/').TrimStart('/');

                return normalized.StartsWith("Assets/", System.StringComparison.Ordinal)
                    ? normalized
                    : AddressableRoot + normalized;
            }
        }

        [LabelText("子层级")]
        public UIParentLayer UIParent;

        [LabelText("默认动画")]
        public bool IsTween;

        [LabelText("右键关闭")]
        public bool IsMouseRightHide;

        [LabelText("描述")]
        public string Description;

        public override string GetID() => PageID;

        public override string ToString()
        {
            return $"{{ PageID:{PageID}, PagePath:{PagePath}, UIParent:{UIParent}, " +
                   $"IsTween:{IsTween}, IsMouseRightHide:{IsMouseRightHide}, Description:{Description} }}";
        }
    }
}
