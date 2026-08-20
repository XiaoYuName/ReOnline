using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 初始化脚本等待加载器
    /// </summary>
    public interface IGameInitialized
    {
        /// <summary>
        /// 初始化脚本函数
        /// </summary>
        /// <returns></returns>
        public UniTask Initialized();
        
        /// <summary>
        /// 释放脚本函数
        /// </summary>
        public UniTask Release()
        {
            return UniTask.CompletedTask;
        }
    }
}

