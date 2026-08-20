using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 存储数据的接口,所有要存储数据的类都将要实现该接口
    /// </summary>
    public interface ISaveable
    {
        string GUID { get; }

        void RegisterSaveable()
        {
            SaveGameManager.Instance.RegisterSaveable(this);
        }
        void SaveData(GameSaveData data);
        void LoadData(GameSaveData data);
    }
}

