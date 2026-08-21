using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 奥丁特用脚本
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class OdinScriptableObject<T> : SerializedScriptableObject where T : OdinDataItem<T>
    {
        [BoxGroup("基本数据"),LabelText("配置列表"),TableList(CellPadding = 3),
         Searchable(FilterOptions =  SearchFilterOptions.All)]
        public List<T> DataList;
    
        public T GetDataByID(string ID)
        {
            return DataList.FindLast(temp => temp.GetID() == ID);
        }
    
        public static OdinScriptableObject<T> Instance;
    
        void OnEnable() => Instance = this;

        void OnDisable() => Instance = null;

        private void OnDestroy()
        {
            Instance = null;
        }
    }

    [System.Serializable]
    public abstract class OdinDataItem<T>
    {
        public abstract string GetID();
    }
}