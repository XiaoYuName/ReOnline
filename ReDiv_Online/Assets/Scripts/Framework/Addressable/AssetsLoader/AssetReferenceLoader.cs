    using System.Collections;
    using System.Threading.Tasks;
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using XFramework;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>
    /// AssetReference 加载器
    /// </summary>
    internal sealed class AssetReferenceLoader
    {
        private readonly string key;
        private readonly AssetReference assetReference;
        private int count;
        private bool isLoader;
        private AsyncOperationHandle _handle;
#if UNITY_EDITOR
        private Object editorAsset;
#endif

        public AssetReferenceLoader(AssetReference assetReference)
        {
            this.assetReference = assetReference;
            key = assetReference.AssetGUID;
            count = 0;
            isLoader = false;
        }

        public T LoadAsset<T>() where T : Object
        {
            count++;
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                return LoadFromAssetDatabase<T>();
            }
#endif
            if (isLoader)
            {
                if (_handle.IsDone)
                {
                    return _handle.Result as T;
                }
            }
            else
            {
                isLoader = true;
                _handle = assetReference.LoadAssetAsync<T>();
                if (_handle.IsValid())
                {
                    return _handle.WaitForCompletion() as T;
                }
                throw new UnityException("WaitForCompletion not isValid");
            }

            return _handle.Result as T;
        }

        public void LoadAssetAsync<T>(LoadCallBack<T> onComplete) where T : Object
        {
            count++;
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                onComplete?.Invoke(LoadFromAssetDatabase<T>());
                return;
            }
#endif
            if (isLoader)
            {
                if (_handle.IsDone)
                {
                    onComplete?.Invoke(_handle.Result as T);
                }
                else
                {
                    _handle.Completed += result =>
                    {
                        if (result.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                        {
                            onComplete?.Invoke(result.Result as T);
                        }
                        else
                        {
                            Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
                            onComplete?.Invoke(null);
                        }
                    };
                }
                return;
            }

            isLoader = true;
            _handle = assetReference.LoadAssetAsync<T>();
            _handle.Completed += result =>
            {
                if (result.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                {
                    onComplete?.Invoke(result.Result as T);
                }
                else
                {
                    Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
                    onComplete?.Invoke(null);
                }
            };
        }

        public Task<T> LoadAssetTask<T>() where T : Object
        {
            count++;
            return LoadAssetTaskInternal<T>();
        }

        private async Task<T> LoadAssetTaskInternal<T>() where T : Object
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                return await Task.FromResult(LoadFromAssetDatabase<T>());
            }
#endif
            if (isLoader)
            {
                if (_handle.IsDone)
                {
                    return _handle.Result as T;
                }

                await _handle.Task;
                if (_handle.IsDone && _handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                {
                    return _handle.Result as T;
                }

                Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
                return null;
            }

            isLoader = true;
            _handle = assetReference.LoadAssetAsync<T>();
            await _handle.Task;
            if (_handle.IsDone && _handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
            {
                return _handle.Result as T;
            }

            Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
            return null;
        }

        public UniTask<T> LoadAssetUniTask<T>() where T : Object
        {
            count++;
            return LoadAssetUniTaskInternal<T>();
        }

        private async UniTask<T> LoadAssetUniTaskInternal<T>() where T : Object
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                return await UniTask.FromResult(LoadFromAssetDatabase<T>());
            }
#endif
            if (isLoader)
            {
                if (_handle.IsDone)
                {
                    return _handle.Result as T;
                }

                await _handle.ToUniTask();
                if (_handle is { IsDone: true, Status: UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded })
                {
                    return _handle.Result as T;
                }

                Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
                return null;
            }

            isLoader = true;
            _handle = assetReference.LoadAssetAsync<T>();
            await _handle.ToUniTask();
            if (_handle is { IsDone: true, Status: UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded })
            {
                return _handle.Result as T;
            }

            Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
            return null;
        }

        public IEnumerator LoadAssetCoroutine<T>(LoadCallBack<T> onComplete) where T : Object
        {
            count++;
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                onComplete?.Invoke(LoadFromAssetDatabase<T>());
                yield break;
            }
#endif
            if (isLoader)
            {
                if (_handle.IsDone)
                {
                    onComplete?.Invoke(_handle.Result as T);
                    yield break;
                }

                yield return _handle;
                if (_handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                {
                    onComplete?.Invoke(_handle.Result as T);
                }
                else
                {
                    Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
                    onComplete?.Invoke(null);
                }

                yield break;
            }

            isLoader = true;
            _handle = assetReference.LoadAssetAsync<T>();
            yield return _handle;
            if (_handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
            {
                onComplete?.Invoke(_handle.Result as T);
            }
            else
            {
                Debug.LogError($"资源下载失败Key : {key} ,类型为: {typeof(T)}");
                onComplete?.Invoke(null);
            }
        }

        public void Free()
        {
            count--;
            if (count > 0)
            {
                return;
            }

            isLoader = false;
#if UNITY_EDITOR
            editorAsset = null;
#endif
            if (_handle.IsValid())
            {
                Addressables.Release(_handle);
            }
            AssetsManager.Instance.RemoveAssetReferenceDic(key);
        }

#if UNITY_EDITOR
        private T LoadFromAssetDatabase<T>() where T : Object
        {
            if (isLoader && editorAsset != null)
            {
                return editorAsset as T;
            }

            isLoader = true;
            string assetPath = AssetDatabase.GUIDToAssetPath(key);
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
#endif
    }
