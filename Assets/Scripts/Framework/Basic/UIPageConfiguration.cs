using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// UI界面配置表。UISystem 在 Initialized 时通过 AssetsManager 加载这份资产,
    /// 之后所有 OpenUI 都从这里取界面配置 —— 框架层因此不再依赖 Luban / LubanManager。
    /// </summary>
    [CreateAssetMenu(fileName = "UIPageConfiguration", menuName = "Configs/UI/UIPageConfiguration")]
    public class UIPageConfiguration : OdinScriptableObject<UIPageData>
    {
        /// <summary>
        /// PageID 索引。DataList 是给 Inspector 编辑的,查表走字典,免得每次开界面都线性扫一遍。
        /// </summary>
        private Dictionary<string, UIPageData> pageMap;

        /// <summary>
        /// 按界面ID取配置,取不到返回 null(调用方负责报错)。
        /// </summary>
        public UIPageData Get(string pageID)
        {
            if (string.IsNullOrEmpty(pageID))
            {
                return null;
            }

            EnsureMap();
            return pageMap.TryGetValue(pageID, out UIPageData data) ? data : null;
        }

        public bool Contains(string pageID) => Get(pageID) != null;

        private void EnsureMap()
        {
            if (pageMap != null)
            {
                return;
            }

            pageMap = new Dictionary<string, UIPageData>(DataList?.Count ?? 0);

            if (DataList == null)
            {
                return;
            }

            foreach (UIPageData data in DataList)
            {
                if (data == null || string.IsNullOrEmpty(data.PageID))
                {
                    continue;
                }

                // 重复ID只留第一条并报错。静默覆盖的话表现是"某个界面打开的是另一个预制体",极难查。
                if (!pageMap.TryAdd(data.PageID, data))
                {
                    Debug.LogError($"UI配置表里有重复的界面ID: {data.PageID},请改成唯一值", this);
                }
            }
        }

        /// <summary>
        /// 在 Inspector 里改过表之后重建索引,免得编辑器下改完不生效还以为是代码问题。
        /// </summary>
        private void OnValidate()
        {
            pageMap = null;
        }

        [Button("重建索引"), PropertyOrder(-1)]
        private void RebuildMap()
        {
            pageMap = null;
            EnsureMap();
        }
    }
}
