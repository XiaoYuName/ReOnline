using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace XFramework
{
    /// <summary>
    /// 编辑器下资源加载模式。
    /// LocalAssetDatabase 用 AssetDatabase/EditorSceneManager 直接读本地资源，速度快，适合日常开发。
    /// Addressables 使用真实 Addressables 加载链路，适合验证分组、Key、依赖、远程包和发布环境问题。
    /// </summary>
    public enum AssetsLoadMode
    {
        LocalAssetDatabase,
        Addressables,
    }

    /// <summary>
    /// 数据加载完成后的回调委托
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public delegate void LoadCallBack<in T>(T t);
    
    /// <summary>
    /// 全局单例资源管理器
    /// </summary>
    public class AssetsManager : Singleton<AssetsManager>
    {
        #region LoadMode

#if UNITY_EDITOR
        private AssetsLoadMode _loadMode = AssetsLoadMode.LocalAssetDatabase;
#else
        private AssetsLoadMode _loadMode = AssetsLoadMode.Addressables;
#endif

        /// <summary>
        /// 当前资源加载模式。打包后始终会使用 Addressables。
        /// </summary>
        public AssetsLoadMode LoadMode => _loadMode;

        /// <summary>
        /// 编辑器下是否使用本地 AssetDatabase/EditorSceneManager 加载。
        /// </summary>
        public bool UseLocalAssetDatabase
        {
            get
            {
#if UNITY_EDITOR
                return _loadMode == AssetsLoadMode.LocalAssetDatabase;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 设置资源加载模式。建议在任何资源加载前设置，避免已有缓存和新模式混用。
        /// </summary>
        public void SetLoadMode(AssetsLoadMode loadMode)
        {
#if UNITY_EDITOR
            if (_loadMode == loadMode)
            {
                return;
            }

            if (HasLoadedCache())
            {
                Debug.LogWarning("切换 AssetsManager 加载模式时已有资源缓存，建议重新进入 Play 或在游戏初始化前设置，避免本地资源和 Addressables 资源混用。");
            }

            _loadMode = loadMode;
#else
            _loadMode = AssetsLoadMode.Addressables;
#endif
        }

        private bool HasLoadedCache()
        {
            return pools.Count > 0
                   || lookup.Count > 0
                   || AssetsDic.Count > 0
                   || SceneDic.Count > 0
                   || AssetReferenceDic.Count > 0;
        }

        #endregion

        #region UnitygGameObject
        /// <summary>
        /// 缓存对象的跟节点
        /// </summary>
        public Transform PoolRoot;
        /// <summary>
        /// GameObjectLoader 对象池
        /// </summary>
        private Dictionary<string, GameObjectLoader> pools = new Dictionary<string, GameObjectLoader>();
        /// <summary>
        /// 缓存查找表
        /// </summary>
        private Dictionary<GameObject, GameObjectLoader> lookup = new Dictionary<GameObject, GameObjectLoader>();
        /// <summary>
        /// 异步实例化的等待队列:Key还在加载中时后续请求的回调都挂在这里,加载完一次性发出去
        /// </summary>
        private Dictionary<string, List<LoadCallBack<GameObject>>> pendingInstantiate =
            new Dictionary<string, List<LoadCallBack<GameObject>>>();
        public AssetsManager()
        {
            UnityEngine.Transform poolNode = new GameObject("[Asset Pool]").transform;
            poolNode.transform.localPosition = Vector3.zero;
            poolNode.transform.localScale = Vector3.one;
            poolNode.transform.localRotation = Quaternion.identity;
            Object.DontDestroyOnLoad(poolNode);
            PoolRoot = poolNode;
            //TODO: 开启定时器,定时清理缓存
        }
        /// <summary>
        /// 定时清理缓存
        /// </summary>
        public void UpdateTimeReleaseAll()
        {
            foreach (var item in this.pools.Values)
            {
                item.Release();
            }
        }
        /// <summary>
        /// 同步实例化GameObject
        /// </summary>
        /// <param name="key">键</param>
        /// <returns></returns>
        public GameObject Instantiate(string key)
        {
            GameObjectLoader loader;
            if (this.pools.TryGetValue(key, out loader)) //如果对象池中有该对象
            {
                var obj = loader.Instantiate();
                this.lookup.Add(obj,loader);
                return obj;
            }
            else //如果池中没有该对象,则实例化后放入池中
            {
                loader = new GameObjectLoader(key);
                var obj = loader.Instantiate();
                this.pools.Add(key,loader);
                this.lookup.Add(obj,loader);
                return obj;
            }
        }
        /// <summary>
        /// 异步实例化GameObject
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="OnComponet">回调函数</param>
        public void InstantiateAsync(string key, LoadCallBack<GameObject> OnComponet)
        {
            if (this.pools.TryGetValue(key, out GameObjectLoader loader)) //如果对象池中有该对象
            {
                var obj = loader.Instantiate();
                this.lookup.Add(obj, loader);
                // 原来这个分支忘了回调,导致第二次之后异步实例化同一个Key回调永远不触发
                OnComponet?.Invoke(obj);
                return;
            }

            // 同一个Key的首次加载还没回来时又被请求:把回调排队等加载完一起发。
            // 原来两次都会走到下面各自 new 一个Loader,第二个 pools.Add 会抛重复Key异常,
            // 而且先创建的那个Loader句柄会泄漏。
            // 注意:同一Key上同时混用同步 Instantiate 和异步 InstantiateAsync 不在支持范围内。
            if (this.pendingInstantiate.TryGetValue(key, out List<LoadCallBack<GameObject>> waiting))
            {
                waiting.Add(OnComponet);
                return;
            }

            this.pendingInstantiate.Add(key, new List<LoadCallBack<GameObject>> { OnComponet });
            loader = new GameObjectLoader(key);
            loader.InstantiateAsync((OBJGame) =>
            {
                this.pools[key] = loader;
                this.lookup.Add(OBJGame, loader);

                List<LoadCallBack<GameObject>> callbacks = this.pendingInstantiate[key];
                this.pendingInstantiate.Remove(key);

                // 加载出来的这一份给第一个等待者,排队的其余请求各自再从池里实例化一份
                callbacks[0]?.Invoke(OBJGame);
                for (int i = 1; i < callbacks.Count; i++)
                {
                    GameObject obj = loader.Instantiate();
                    this.lookup.Add(obj, loader);
                    callbacks[i]?.Invoke(obj);
                }
            });
        }
        /// <summary>
        /// 获取预制体对象
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public GameObject GetTemplate(string key)
        {
            if (this.pools.TryGetValue(key, out var loader))
            {
                return loader.prefab;
            }
            return null;
        }
        /// <summary>
        /// 将资源释放回缓存池
        /// </summary>
        /// <param name="obj"></param>
        public void FreeGameObject(GameObject obj)
        {
            GameObjectLoader loader;
            if (lookup.TryGetValue(obj, out loader))
            {
                loader.Free(obj);
                lookup.Remove(obj);
            }
        }

        /// <summary>
        /// 彻底释放实例:直接Destroy掉,不像 FreeGameObject 那样只是隐藏进缓存池。
        /// 该Key下所有实例都释放完之后(引用计数为0),缓存池里的备用实例也一并销毁,
        /// 并把Addressables引用卸掉、Loader从对象池里摘掉。
        /// 还要复用的用 FreeGameObject,确定不再需要了才用这个。
        /// </summary>
        /// <param name="obj">要释放的实例</param>
        public void ReleaseGameObject(GameObject obj)
        {
            if (obj == null)
            {
                return;
            }

            if (!lookup.TryGetValue(obj, out GameObjectLoader loader))
            {
                // 不是从对象池实例化出来的,没有引用可卸,直接销毁
                Object.Destroy(obj);
                return;
            }

            lookup.Remove(obj);
            if (loader.ReleaseInstance(obj))
            {
                RemovePools(loader.Key);
            }
        }

        /// <summary>
        /// 按Key彻底释放:把该Key在用的实例和缓存池里的备用实例全部Destroy,并卸掉Addressables引用。
        /// 整个界面/场景收尾时用,不用一个个传实例。
        /// </summary>
        /// <param name="key">Addressable Key</param>
        public void ReleaseGameObject(string key)
        {
            if (!pools.TryGetValue(key, out GameObjectLoader loader))
            {
                return;
            }

            // 先把 lookup 里属于这个Loader的实例摘掉,再交给Loader统一销毁
            List<GameObject> owned = new List<GameObject>();
            foreach (KeyValuePair<GameObject, GameObjectLoader> pair in lookup)
            {
                if (pair.Value == loader)
                {
                    owned.Add(pair.Key);
                }
            }

            for (int i = 0; i < owned.Count; i++)
            {
                lookup.Remove(owned[i]);
                loader.ReleaseInstance(owned[i]);
            }

            loader.Release();
            RemovePools(key);
        }

        public void RemovePools(string key)
        {
            if (pools.ContainsKey(key))
            {
                pools.Remove(key);
            }
        }

        #endregion

        #region AssetsLoader

        private Dictionary<string, AssetsLoader> AssetsDic = new Dictionary<string, AssetsLoader>();
        

        /// <summary>
        /// 同步加载资源
        /// </summary>
        /// <param name="key">键</param>
        /// <typeparam name="T">类型</typeparam>
        /// <returns></returns>
        public T LoadAssets<T>(string key) where T : Object
        {
            AssetsLoader loader;
            if (AssetsDic.TryGetValue(key, out loader))
            {
                T Assets = loader.LoadAsset<T>();
                return Assets;
            }
            else
            {
                loader = new AssetsLoader(key);
                T Assets = loader.LoadAsset<T>();
                AssetsDic.Add(key,loader);
                return Assets;
            }
        }
        
        /// <summary>
        /// UniTask 异步加载资源
        /// </summary>
        /// <param name="key"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public UniTask<T> LoadAssetsUniTask<T>(string key) where T : Object
        {
            AssetsLoader loader;
            if (AssetsDic.TryGetValue(key, out loader))
            {
                return loader.LoadAssetUniTask<T>();
            }
            else
            {
                loader = new AssetsLoader(key);
                UniTask<T> Assets = loader.LoadAssetUniTask<T>();
                AssetsDic.Add(key,loader);
                return Assets;
            }
        }

        /// <summary>
        /// 异步加载资源
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="OnComplete">回调函数</param>
        /// <typeparam name="T"></typeparam>
        public void LoadAssetsAsync<T>(string key,LoadCallBack<T> OnComplete) where T : Object
        {
            AssetsLoader loader;
            if (AssetsDic.TryGetValue(key, out loader))
            {
                loader.LoadAssetAsync(OnComplete);
            }
            else
            {
                loader = new AssetsLoader(key);
                loader.LoadAssetAsync<T>((t) =>
                {
                    OnComplete?.Invoke(t);
                });
                AssetsDic.Add(key,loader);
            }
        }

        /// <summary>
        /// Task异步加载资源
        /// </summary>
        /// <param name="key"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public Task<T> LoadAssetTask<T>(string key) where T : Object
        {
            AssetsLoader loader;
            if (AssetsDic.TryGetValue(key, out loader))
            {
                return loader.LoadAssetTask<T>();
            }
            loader = new AssetsLoader(key);
            AssetsDic.Add(key,loader);
            return loader.LoadAssetTask<T>();//??
        }

        /// <summary>
        /// 携程加载资源
        /// </summary>
        /// <param name="key">Addressable Key键</param>
        /// <param name="OnComplete">加载完成后的回调</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public IEnumerator LoadAssetsCoroutine<T>(string key, LoadCallBack<T> OnComplete) where T : Object
        {
            AssetsLoader loader;
            if (AssetsDic.TryGetValue(key, out loader))
            {
                yield return loader.LoadAssetCoroutine(OnComplete);
            }
            else
            {
                loader = new AssetsLoader(key);
                yield return loader.LoadAssetCoroutine(OnComplete);
                AssetsDic.Add(key, loader);
            }
        }
        
        /// <summary>
        /// 释放资源
        /// </summary>
        /// <param name="key"></param>
        public void FreeAsset(string key)
        {
            AssetsLoader loader;
            if (AssetsDic.TryGetValue(key,out loader))
            {
                loader.Free();
            }
        }



        /// <summary>
        /// 释放 AssetReference 资源
        /// </summary>
        public void FreeAsset(AssetReference assetReference)
        {
            if (assetReference == null)
            {
                return;
            }

            var key = GetAssetReferenceKey(assetReference);
            AssetReferenceLoader loader;
            if (AssetReferenceDic.TryGetValue(key, out loader))
            {
                loader.Free();
            }
        }
        /// <summary>
        /// 资源池移除对象(非释放方法)
        /// </summary>
        /// <param name="key"></param>
        public void RemoveAssetsDic(string key)
        {
            if (AssetsDic.ContainsKey(key))
            {
                AssetsDic.Remove(key);
            }
        }

        public void FreeAssets()
        {
            for (int i = 0; i < AssetsDic.Count; i++)
            {
                (string key, AssetsLoader loader) = AssetsDic.ElementAt(i);
                loader.Release();
            }
            AssetsDic.Clear();
        }

        internal void RemoveAssetReferenceDic(string key)
        {
            if (AssetReferenceDic.ContainsKey(key))
            {
                AssetReferenceDic.Remove(key);
            }
        }

        #endregion

        #region SceneLoader

        private Dictionary<string, SceneLoader> SceneDic = new Dictionary<string, SceneLoader>();

        public void LoadScene(string key, LoadSceneMode _mode)
        {
            if (SceneDic.TryGetValue(key, out var loader))
            {
                loader.LoadScene();
            }
            else
            {
                loader = new SceneLoader(key, _mode);
                loader.LoadScene();
                SceneDic.Add(key,loader);
            }
        }

        /// <summary>
        /// 异步加载场景,并将它设置为活动场景
        /// </summary>
        /// <param name="key">Addressable Key</param>
        /// <param name="OnComplete">加载回调</param>
        /// <param name="_mode">加载模式</param>
        public void LoadSceneAsync(string key, Action OnComplete,LoadSceneMode _mode)
        {
            if (SceneDic.TryGetValue(key, out var loader))
            {
                loader.LoadSceneAsync(OnComplete);
            }
            else
            {
                loader = new SceneLoader(key, _mode);
                loader.LoadSceneAsync(OnComplete);
                SceneDic.Add(key,loader);
            }
        }
        
        /// <summary>
        /// 携程加载场景
        /// </summary>
        /// <param name="key">Addressable Key</param>
        /// <param name="_mode">加载模式</param>
        /// <returns></returns>
        public IEnumerator LoadSceneCoroutine(string key, LoadSceneMode _mode)
        {
            if (SceneDic.TryGetValue(key, out var loader))
            {
                yield return loader.LoadSceneCoroutine();
            }
            else
            {
                loader = new SceneLoader(key, _mode);
                yield return loader.LoadSceneCoroutine();
                SceneDic.Add(key,loader);
            }
        }
        
        /// <summary>
        /// UniTask异步加载场景
        /// </summary>
        /// <param name="key"></param>
        /// <param name="_mode"></param>
        public async UniTask LoadSceneUniTask(string key, LoadSceneMode _mode)
        {
            if (SceneDic.TryGetValue(key, out var loader))
            {
                await loader.LoadSceneUniTask();
            }
            else
            {
                loader = new SceneLoader(key, _mode);
                await loader.LoadSceneUniTask();
                SceneDic.Add(key,loader);
            }
        }

        public async UniTask LoadSceneUniTask(string key, LoadSceneMode _mode,IProgress<float> progress)
        {
            if (SceneDic.TryGetValue(key, out var loader))
            {
                await loader.LoadSceneUniTask(progress);
            }
            else
            {
                loader = new SceneLoader(key, _mode);
                await loader.LoadSceneUniTask(progress);
                SceneDic.Add(key,loader);
            }
        }

        public void ULoadScene(string key)
        {
            if (SceneDic.TryGetValue(key, out var loader))
            {
                loader.ULoadSceneAsync();
                SceneDic.Remove(key);
                loader.Release();
            }
        }

        /// <summary>
        /// 丢弃场景Loader缓存,但不走卸载流程。
        /// 用于场景已经被 LoadSceneMode.Single 隐式卸载的情况:这时候再去卸载会报错,
        /// 只需要释放 Addressables 句柄并清掉缓存,否则下次加载会命中失效的Loader。
        /// </summary>
        /// <param name="key"></param>
        public void DiscardSceneLoader(string key)
        {
            if (SceneDic.TryGetValue(key, out var loader))
            {
                SceneDic.Remove(key);
                loader.Release();
            }
        }

        /// <summary>
        /// 异步卸载场景
        /// </summary>
        /// <param name="key"></param>
        public void ULoadSceneAsync(string key)
        {
            if (SceneDic.TryGetValue(key, out var loader))
            {
                loader.ULoadSceneAsync();
                SceneDic.Remove(key);
                loader.Release();
            }
        }
        
        /// <summary>
        /// 协程卸载场景
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public IEnumerator ULoadSceneCoroutine(string key)
        {
            if (SceneDic.TryGetValue(key, out var loader))
            {
                yield return loader.ULoadSceneCoroutine();
                SceneDic.Remove(key);
                loader.Release();
            }
        }

        /// <summary>
        /// UniTask卸载场景
        /// </summary>
        /// <param name="key"></param>
        public async UniTask ULoadSceneUniTask(string key)
        {
            if (SceneDic.TryGetValue(key, out var loader))
            {
                await loader.ULoadSceneUniTask();
                SceneDic.Remove(key);
                loader.Release();
            }
        }


        #endregion

        #region AssetReferenceLoader
        private Dictionary<string, AssetReferenceLoader> AssetReferenceDic = new Dictionary<string, AssetReferenceLoader>();
        
        /// <summary>
        /// 同步加载 AssetReference 资源
        /// </summary>
        public T LoadAssets<T>(AssetReference assetReference) where T : Object
        {
            if (assetReference == null)
            {
                return null;
            }

            var key = GetAssetReferenceKey(assetReference);
            AssetReferenceLoader loader;
            if (AssetReferenceDic.TryGetValue(key, out loader))
            {
                return loader.LoadAsset<T>();
            }

            loader = new AssetReferenceLoader(assetReference);
            AssetReferenceDic.Add(key, loader);
            return loader.LoadAsset<T>();
        }

        /// <summary>
        /// UniTask 异步加载 AssetReference 资源
        /// </summary>
        public UniTask<T> LoadAssetsUniTask<T>(AssetReference assetReference) where T : Object
        {
            if (assetReference == null)
            {
                return UniTask.FromResult<T>(null);
            }

            var key = GetAssetReferenceKey(assetReference);
            AssetReferenceLoader loader;
            if (AssetReferenceDic.TryGetValue(key, out loader))
            {
                return loader.LoadAssetUniTask<T>();
            }

            loader = new AssetReferenceLoader(assetReference);
            AssetReferenceDic.Add(key, loader);
            return loader.LoadAssetUniTask<T>();
        }

        /// <summary>
        /// 异步加载 AssetReference 资源
        /// </summary>
        public void LoadAssetsAsync<T>(AssetReference assetReference, LoadCallBack<T> OnComplete) where T : Object
        {
            if (assetReference == null)
            {
                OnComplete?.Invoke(null);
                return;
            }

            var key = GetAssetReferenceKey(assetReference);
            AssetReferenceLoader loader;
            if (AssetReferenceDic.TryGetValue(key, out loader))
            {
                loader.LoadAssetAsync(OnComplete);
            }
            else
            {
                loader = new AssetReferenceLoader(assetReference);
                AssetReferenceDic.Add(key, loader);
                loader.LoadAssetAsync(OnComplete);
            }
        }

        /// <summary>
        /// Task 异步加载 AssetReference 资源
        /// </summary>
        public Task<T> LoadAssetTask<T>(AssetReference assetReference) where T : Object
        {
            if (assetReference == null)
            {
                return Task.FromResult<T>(null);
            }

            var key = GetAssetReferenceKey(assetReference);
            AssetReferenceLoader loader;
            if (AssetReferenceDic.TryGetValue(key, out loader))
            {
                return loader.LoadAssetTask<T>();
            }

            loader = new AssetReferenceLoader(assetReference);
            AssetReferenceDic.Add(key, loader);
            return loader.LoadAssetTask<T>();
        }

        /// <summary>
        /// 携程加载 AssetReference 资源
        /// </summary>
        public IEnumerator LoadAssetsCoroutine<T>(AssetReference assetReference, LoadCallBack<T> OnComplete) where T : Object
        {
            if (assetReference == null)
            {
                OnComplete?.Invoke(null);
                yield break;
            }

            var key = GetAssetReferenceKey(assetReference);
            AssetReferenceLoader loader;
            if (AssetReferenceDic.TryGetValue(key, out loader))
            {
                yield return loader.LoadAssetCoroutine(OnComplete);
            }
            else
            {
                loader = new AssetReferenceLoader(assetReference);
                AssetReferenceDic.Add(key, loader);
                yield return loader.LoadAssetCoroutine(OnComplete);
            }
        }

        private static string GetAssetReferenceKey(AssetReference assetReference)
        {
            return assetReference == null ? string.Empty : assetReference.AssetGUID;
        }

        #endregion
    }
}
