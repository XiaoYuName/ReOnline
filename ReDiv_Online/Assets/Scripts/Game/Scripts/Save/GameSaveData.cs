using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using System;

namespace XFramework
{
    /// <summary>
    /// GameSave 框架核心
    /// </summary>
    [Serializable]
    public partial class GameSaveData
    {
        [LabelText("存档版本号")]
        public int Version = SaveVersion;
        
        public const int SaveVersion = 1;


        public static GameSaveData Create()
        {
            return new();
        }
    }
}

