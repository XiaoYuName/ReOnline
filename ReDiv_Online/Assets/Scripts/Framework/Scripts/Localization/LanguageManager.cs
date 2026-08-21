using System;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.SmartFormat.Extensions;
using UnityEngine.Localization.SmartFormat.PersistentVariables;
using XFramework;
using Object = UnityEngine.Object;

namespace XFramework
{
    /// <summary>
    /// 多语言管理器
    /// </summary>
    public class LanguageManager : MonoSingleton<LanguageManager>,IGameInitialized
    {
        [LabelText("当前选中Index")]
        public int LanguageIndex { get; private set; }
        
        private Action OnLanguageChanged;

        /// <summary>
        /// 初始化脚本函数
        /// </summary>
        /// <returns></returns>
        public async UniTask Initialized()
        {
            OnLanguageChanged = null;
            LanguageIndex = PlayerPrefs.GetInt("LanguageIndex",0);
            SetLocalization(LanguageIndex);
            LocalizationSettings.Instance.OnSelectedLocaleChanged += OnSelectedLocaleChanged; 
            await UniTask.CompletedTask;
        }

        /// <summary>
        /// 释放脚本函数
        /// </summary>
        public async UniTask Release()
        {
            OnLanguageChanged = null;
            LocalizationSettings.Instance.OnSelectedLocaleChanged -= OnSelectedLocaleChanged;
            await UniTask.CompletedTask;
        }

        private void OnSelectedLocaleChanged(Locale locale)
        {
            OnLanguageChanged?.Invoke();
        }
        
        
        /// <summary>
        /// 设置多语言本地化
        /// </summary>
        /// <param name="index">语言索引</param>
        public void SetLocalization(int index)
        {
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.Locales[index];
            PlayerPrefs.SetInt("LanguageIndex",index);
        }

        /// <summary>
        /// 获取指定表中指定键的本地化字符串
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <param name="key">键</param>
        /// <returns></returns>
        public string GetLocalizedString(string tableName,string key)
        {
            var str = LocalizationSettings.StringDatabase.
                GetLocalizedString(tableName, key);
           return str;
        }

        /// <summary>
        /// 获取指定表中指定键的本地化资产
        /// </summary>
        /// <param name="tableName">表名</param>
        /// <param name="key">键</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T GetLocalizedAsset<T>(string tableName, string key)  where T : Object
        {
            var str = LocalizationSettings.AssetDatabase.GetLocalizedAsset<T>(tableName, key);
            return str;
        }

        #region 设置全局

        public void SetGlobalVariablesSource(string group,string source,string value)
        {
            var PersistentVariablesSource = LocalizationSettings.StringDatabase.SmartFormatter
                .GetSourceExtension<PersistentVariablesSource>();
            if (PersistentVariablesSource[group][source] is StringVariable variable)
            {
                variable.Value = value;
            }

        }

        #endregion


        #region Event
        public void AddOnLanguageChanged(Action action)
        {
            OnLanguageChanged += action;
        }
        public void RemoveOnLanguageChanged(Action action)
        {
            OnLanguageChanged -= action;
        }
        #endregion
        
    }
}

