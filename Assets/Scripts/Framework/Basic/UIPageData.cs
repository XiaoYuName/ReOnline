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
        /// 预制体的Addressable Key,UISystem 拿它去 AssetsManager 加载。
        /// </summary>
        [LabelText("界面路径")]
        [FilePath(ParentFolder = "Assets/AddressableAssets/Remote", Extensions = "prefab")]
        public string PagePath;

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
