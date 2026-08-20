using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XFramework;

namespace XFramework
{
    public partial class LubanManager : MonoSingleton<LubanManager>
    {
        /// <summary>
        /// 通用加载 Luban 表
        /// </summary>
        protected T LoadTable<T>(ref T cache, string assetKey, Func<JArray, T> creator) where T : class
        {
            if (cache != null)
            {
                return cache;
            }

            TextAsset json = AssetsManager.Instance.LoadAssets<TextAsset>(assetKey);

            if (json == null)
            {
                Debug.LogError($"Luban 配置加载失败: {assetKey}");
                return null;
            }

            try
            {
                cache = creator.Invoke(JArray.Parse(json.text));
                return cache;
            }
            catch (Exception e)
            {
                Debug.LogError($"Luban 配置解析失败: {assetKey}\n{e}");
                return null;
            }
        }

        /// <summary>
        /// 清空所有 Luban 表缓存。
        /// 热更配置后可以调用。
        /// </summary>
        public void ClearCache()
        {
            ClearGeneratedCache();
        }

        /// <summary>
        /// 自动生成文件里实现。
        /// </summary>
        partial void ClearGeneratedCache();
    }
}
