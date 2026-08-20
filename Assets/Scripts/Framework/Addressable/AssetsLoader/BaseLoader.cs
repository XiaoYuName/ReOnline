using System;
using System.Collections;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Object = UnityEngine.Object;

namespace XFramework
{
    /// <summary>
    /// 资源类型的基类
    /// </summary>
    public class BaseLoader
    {
        /// <summary>
        /// 资源Addressable Key键
        /// </summary>
        protected string key;

        /// <summary>
        /// 资源Addressable Key键(只读)
        /// </summary>
        public string Key => key;

        private string assetName;

        /// <summary>
        /// Key 去掉目录和扩展名之后的短名。
        /// Key 一般是完整资源路径(Assets/.../Xxx.prefab),直接拿来当实例名在Hierarchy里又长又难认,
        /// 实例化出来的对象用这个命名。Key 本身就是短名时原样返回。
        /// </summary>
        public string AssetName
        {
            get
            {
                if (string.IsNullOrEmpty(assetName))
                {
                    assetName = System.IO.Path.GetFileNameWithoutExtension(key);
                    if (string.IsNullOrEmpty(assetName))
                    {
                        assetName = key;
                    }
                }

                return assetName;
            }
        }
        /// <summary>
        /// 是否已经下载过
        /// </summary>
        private bool isLoader;
        /// <summary>
        /// 异步的加载回调
        /// </summary>
        private AsyncOperationHandle _handle;
#if UNITY_EDITOR
        private Object editorAsset;
#endif

        /// <summary>
        /// 实例化资源
        /// </summary>
        /// <param name="Key">Key 资源</param>
        protected BaseLoader(string Key)
        {
            this.key = Key;
            isLoader = false;
        }

        /// <summary>
        /// 异步加载资源
        /// </summary>
        /// <param name="OnComplete">加载回调函数</param>
        /// <typeparam name="T">类型</typeparam>
        /// <returns></returns>
        protected virtual void LoadAsync<T>(LoadCallBack<T> OnComplete) where T : Object
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                OnComplete?.Invoke(LoadFromAssetDatabase<T>());
                return;
            }
#endif
            if (isLoader) //如果已经下载过该资源则直接返回该资源
            {
                if (_handle.IsDone)
                {
                    OnComplete?.Invoke(_handle.Result as T);
                }
                else
                {
                    _handle.Completed += (result) =>
                    {
                        if (result.Status == AsyncOperationStatus.Succeeded)
                        {
                            var Obj = result.Result as T;
                            OnComplete?.Invoke(Obj);
                        }
                        else
                        {
                            Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
                            OnComplete?.Invoke(null);
                        }
                    };
                }
            }
            else //如果没有加载过该资源,那么就加载该资源
            {
                isLoader = true;
                _handle = Addressables.LoadAssetAsync<T>(key);
                _handle.Completed += (result) =>
                {
                    if (result.Status == AsyncOperationStatus.Succeeded)
                    {
                        var Obj = result.Result as T;
                        OnComplete?.Invoke(Obj);
                    }
                    else
                    {
                        Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
                        OnComplete?.Invoke(null);
                    }
                };
            }
        }

        /// <summary>
        /// 异步Task 加载资源
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        protected async Task<T> LoadTask<T>() where T : Object
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                return await Task.FromResult(LoadFromAssetDatabase<T>());
            }
#endif
            if (isLoader) //如果已经下载过该资源则直接返回该资源
            {
                if (_handle.IsDone)
                {
                    return _handle.Result as T;
                }
                await _handle.Task;
                if (_handle.IsDone && _handle.Status == AsyncOperationStatus.Succeeded)
                {
                    return _handle.Result as T;
                }
                Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
                return null;
            }
            //如果没有加载过该资源,那么就加载该资源
            isLoader = true;
            _handle = Addressables.LoadAssetAsync<T>(key);
            await _handle.Task;
            if (_handle.IsDone && _handle.Status == AsyncOperationStatus.Succeeded)
            {
                return _handle.Result as T;
            }
            Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
            return _handle.Result as T;
        }

        /// <summary>
        /// 同步加载资源
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        protected virtual T Load<T>() where T : Object
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                return LoadFromAssetDatabase<T>();
            }
#endif
            isLoader = true;
            this._handle = Addressables.LoadAssetAsync<T>(key);
            if (_handle.IsValid())
            {
                T Obj = this._handle.WaitForCompletion() as T;
                return Obj;
            }
            throw new UnityException("WaitForCompletion not isValid");
        }

        protected async UniTask<T> LoadUniTask<T>() where T : Object
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                return await UniTask.FromResult(LoadFromAssetDatabase<T>());
            }
#endif
            if (isLoader) //如果已经下载过该资源则直接返回该资源
            {
                if (_handle.IsDone)
                {
                    return _handle.Result as T;
                }
                await _handle.Task;
                if (_handle is { IsDone: true, Status: AsyncOperationStatus.Succeeded })
                {
                    return _handle.Result as T;
                }
                Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
                return null;
            }
            //如果没有加载过该资源,那么就加载该资源
            isLoader = true;
            _handle = Addressables.LoadAssetAsync<T>(key);
            await _handle.Task;
            if (_handle is { IsDone: true, Status: AsyncOperationStatus.Succeeded })
            {
                return _handle.Result as T;
            }
            Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
            return _handle.Result as T;
        }

        /// <summary>
        /// 协程加载资源
        /// </summary>
        /// <param name="OnComplete">加载完成后的回调</param>
        /// <typeparam name="T">类型</typeparam>
        /// <returns></returns>
        public virtual IEnumerator LoadCoroutine<T>(LoadCallBack<T> OnComplete) where T : Object
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                OnComplete?.Invoke(LoadFromAssetDatabase<T>());
                yield break;
            }
#endif
            if (isLoader)
            {
                if (_handle.IsDone)
                {
                    OnComplete?.Invoke(_handle.Result as T);
                }
                else
                {
                    yield return _handle;
                    if (_handle.Status == AsyncOperationStatus.Succeeded)
                    {
                        var Obj = _handle.Result as T;
                        OnComplete?.Invoke(Obj);
                    }
                    else
                    {
                        Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
                        OnComplete?.Invoke(null);
                    }
                }
            }
            else
            {
                isLoader = true;
                _handle = Addressables.LoadAssetAsync<T>(key);
                yield return _handle;
                if (_handle.Status == AsyncOperationStatus.Succeeded)
                {
                    var Obj = _handle.Result as T;
                    OnComplete?.Invoke(Obj);
                }
                else
                {
                    Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
                    OnComplete?.Invoke(null);
                }
            }
        }

        public virtual void Release()
        {
            if (this.isLoader)
            {
                this.isLoader = false;
#if UNITY_EDITOR
                editorAsset = null;
#endif
                if (_handle.IsValid())
                {
                    Addressables.Release(_handle);
                }
            }
        }

#if UNITY_EDITOR
        private T LoadFromAssetDatabase<T>() where T : Object
        {
            if (isLoader && editorAsset != null)
            {
                return editorAsset as T;
            }

            isLoader = true;
            string assetPath = GetAssetDatabasePath(key);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
                return null;
            }

            editorAsset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (editorAsset == null)
            {
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                {
                    if (asset is T typedAsset)
                    {
                        editorAsset = typedAsset;
                        break;
                    }
                }
            }

            if (editorAsset == null)
            {
                Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
            }

            return editorAsset as T;
        }

        private static string GetAssetDatabasePath(string assetKey)
        {
            if (string.IsNullOrEmpty(assetKey))
            {
                return string.Empty;
            }

            if (assetKey.StartsWith("Assets/") || assetKey.StartsWith("Packages/"))
            {
                return assetKey;
            }

            string path = AssetDatabase.GUIDToAssetPath(assetKey);
            return string.IsNullOrEmpty(path) ? assetKey : path;
        }
#endif
    }
}
