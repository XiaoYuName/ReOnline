using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace XFramework
{
    public class AssetsLoader: BaseLoader
    {
        /// <summary>
        /// 资源引用数
        /// </summary>
        private int count;
        
        public AssetsLoader(string Key) : base(Key)
        {
            this.key = Key;
            count = 0;
        }

        /// <summary>
        /// 同步加载资源
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T LoadAsset<T>() where T : Object
        {
            count++;
            return base.Load<T>();
        }
        
        
        /// <summary>
        /// 异步加载资源
        /// </summary>
        /// <param name="OnComplete"></param>
        /// <typeparam name="T"></typeparam>
        public void LoadAssetAsync<T>(LoadCallBack<T> OnComplete) where T : Object
        {
            base.LoadAsync<T>((t) =>
            {
                count++;
                OnComplete?.Invoke(t);
            });
        }

        /// <summary>
        /// 异步Task 加载资源
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public Task<T> LoadAssetTask<T>() where T : Object
        {
            count++;
            return base.LoadTask<T>();
        }

        public UniTask<T> LoadAssetUniTask<T>() where T : Object
        {
            count++;
            return base.LoadUniTask<T>();
        }

        /// <summary>
        /// 携程加载资源
        /// </summary>
        /// <param name="OnComplete">加载完成后的回调函数</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public IEnumerator LoadAssetCoroutine<T>(LoadCallBack<T> OnComplete) where T : Object
        {
            yield return base.LoadCoroutine(OnComplete);
            count++;
        }


        /// <summary>
        /// 释放资源,资源引用数减1
        /// </summary>
        public void Free()
        {
            count--;
            if (count > 0) return;
            base.Release();
            AssetsManager.Instance.RemoveAssetsDic(key);
        }
    }
}
