using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace XFramework
{
    public class SceneLoader
    {
        protected string Key;
        private bool isLoader;
        private AsyncOperationHandle<SceneInstance> _handle;
        private LoadSceneMode _mode;
#if UNITY_EDITOR
        private Scene editorScene;
#endif
        
        public SceneLoader(string key, LoadSceneMode mode)
        {
            this.Key = key;
            this._mode = mode;
        }

        /// <summary>
        /// 异步加载场景
        /// </summary>
        /// <param name="OnComplete">加载场景前一秒调用</param>
        public virtual void LoadSceneAsync(Action OnComplete)
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                LoadSceneAsyncInEditor(OnComplete).Forget();
                return;
            }
#endif
            if (isLoader)
            {
                if (_handle.IsDone)
                {
                    OnComplete?.Invoke();
                    _handle.Result.ActivateAsync().completed += (res) =>
                    {
                        if (res.isDone)
                        {
                            SceneManager.SetActiveScene(_handle.Result.Scene);
                        }
                    };
                }
                else
                {
                    _handle.Completed += (result) =>
                    {
                        if (result.Status == AsyncOperationStatus.Succeeded)
                        {
                            OnComplete?.Invoke();
                            _handle.Result.ActivateAsync().completed += (res) =>
                            {
                                if (res.isDone)
                                {
                                    SceneManager.SetActiveScene(_handle.Result.Scene);
                                }
                            };
                        }
                        else
                        {
                            Debug.LogError($"资源下载失败Key : {Key} ,类型为: {typeof(Scene)}");
                        }
                    };
                }
            }
            else
            {
                isLoader = true;
                _handle = Addressables.LoadSceneAsync(Key, _mode,false);
                _handle.Completed += (result) =>
                {
                    if (result.Status == AsyncOperationStatus.Succeeded)
                    {
                        OnComplete?.Invoke();
                        _handle.Result.ActivateAsync().completed += (res) =>
                        {
                            if (res.isDone)
                            {
                                SceneManager.SetActiveScene(_handle.Result.Scene);
                            }
                        };
                        
                    }
                    else
                    {
                        Debug.LogError($"资源下载失败Key : {Key} ,类型为: {typeof(Scene)}");
                    }
                };
            }
        }

        public virtual void LoadScene()
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                isLoader = true;
                string scenePath = GetSceneAssetPath(Key);
                editorScene = EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(_mode));
                if (editorScene.IsValid())
                {
                    SceneManager.SetActiveScene(editorScene);
                    return;
                }

                throw new UnityException($"LoadSceneInPlayMode not isValid: {scenePath}");
            }
#endif
            isLoader = true;
            this._handle = Addressables.LoadSceneAsync(Key, _mode);
            if (_handle.IsValid())
            {
                _handle.WaitForCompletion();
                return;
            }
            throw new UnityException("WaitForCompletion not isValid");
        }

        /// <summary>
        /// 携程加载Scenen
        /// </summary>
        /// <returns></returns>
        public virtual IEnumerator LoadSceneCoroutine()
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                yield return LoadSceneCoroutineInEditor(null);
                yield break;
            }
#endif
            if (isLoader)
            {
                if (_handle.IsDone)
                {
                    var handle = _handle.Result.ActivateAsync();
                    yield return handle;
                    if (handle.isDone)
                    {
                        SceneManager.SetActiveScene(_handle.Result.Scene);
                    }
                }
            }
            else
            {
                isLoader = true;
                _handle = Addressables.LoadSceneAsync(Key, _mode, false);
                yield return _handle;
                if (_handle.Status == AsyncOperationStatus.Succeeded)
                {
                    var handle = _handle.Result.ActivateAsync();
                    yield return handle;
                    if (handle.isDone)
                    {
                        SceneManager.SetActiveScene(_handle.Result.Scene);
                    }
                }
                else
                {
                    Debug.LogError($"资源下载失败Key : {Key} ,类型为: {typeof(Scene)}");
                }
            }
        }

        /// <summary>
        /// UniTask加载Scene
        /// </summary>
        public async UniTask LoadSceneUniTask()
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                await LoadSceneUniTaskInEditor(null);
                return;
            }
#endif
            if (isLoader)
            {
                // 场景可能曾经通过 Single 模式被 Unity 隐式卸载，但 Loader
                // 仍然保留在 AssetsManager 的缓存中。只有场景真实存在时才能复用。
                if (_handle.IsValid() && !_handle.IsDone)
                {
                    await _handle.ToUniTask();
                    if (_handle.Status == AsyncOperationStatus.Succeeded)
                    {
                        var activation = _handle.Result.ActivateAsync();
                        await activation.ToUniTask();
                        if (activation.isDone)
                        {
                            SceneManager.SetActiveScene(_handle.Result.Scene);
                            return;
                        }
                    }
                }

                if (IsAddressableSceneLoaded())
                {
                    SceneManager.SetActiveScene(_handle.Result.Scene);
                    return;
                }

                Debug.LogWarning($"场景Loader缓存已失效，重新加载场景 Key : {Key}");
                ResetStaleAddressableLoadState();
            }

            isLoader = true;
            _handle = Addressables.LoadSceneAsync(Key, _mode, false);
            await _handle.ToUniTask();
            if (_handle.Status == AsyncOperationStatus.Succeeded)
            {
                var activation = _handle.Result.ActivateAsync();
                await activation.ToUniTask();
                if (activation.isDone)
                {
                    SceneManager.SetActiveScene(_handle.Result.Scene);
                }
            }
            else
            {
                Debug.LogError($"资源下载失败Key : {Key} ,类型为: {typeof(Scene)}");
                ResetStaleAddressableLoadState();
            }
        }
        
        /// <summary>
        /// UniTask加载Scene
        /// </summary>
        public async UniTask LoadSceneUniTask(IProgress<float> progress)
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                await LoadSceneUniTaskInEditor(progress);
                return;
            }
#endif
            if (isLoader)
            {
                if (_handle.IsValid() && !_handle.IsDone)
                {
                    await _handle.ToUniTask(progress: progress);
                    if (_handle.Status == AsyncOperationStatus.Succeeded)
                    {
                        var activation = _handle.Result.ActivateAsync();
                        await activation.ToUniTask(progress: progress);
                        if (activation.isDone)
                        {
                            SceneManager.SetActiveScene(_handle.Result.Scene);
                            return;
                        }
                    }
                }

                if (IsAddressableSceneLoaded())
                {
                    SceneManager.SetActiveScene(_handle.Result.Scene);
                    progress?.Report(1f);
                    return;
                }

                Debug.LogWarning($"场景Loader缓存已失效，重新加载场景 Key : {Key}");
                ResetStaleAddressableLoadState();
            }

            isLoader = true;
            _handle = Addressables.LoadSceneAsync(Key, _mode, false);
            await _handle.ToUniTask(progress);
            if (_handle.Status == AsyncOperationStatus.Succeeded)
            {
                var activation = _handle.Result.ActivateAsync();
                await activation.ToUniTask(progress);
                if (activation.isDone)
                {
                    SceneManager.SetActiveScene(_handle.Result.Scene);
                }
            }
            else
            {
                Debug.LogError($"资源下载失败Key : {Key} ,类型为: {typeof(Scene)}");
                ResetStaleAddressableLoadState();
            }
        }

        private bool IsAddressableSceneLoaded()
        {
            if (!_handle.IsValid() || !_handle.IsDone ||
                _handle.Status != AsyncOperationStatus.Succeeded)
            {
                return false;
            }

            Scene scene = _handle.Result.Scene;
            return scene.IsValid() && scene.isLoaded;
        }

        private void ResetStaleAddressableLoadState()
        {
            isLoader = false;

            if (_handle.IsValid())
            {
                Addressables.Release(_handle);
            }

            _handle = default;
        }

        /// <summary>
        /// 同步卸载场景
        /// </summary>
        public virtual void ULoadScene()
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                if (isLoader)
                {
                    Scene scene = GetLoadedEditorScene();
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                        isLoader = false;
                        return;
                    }
                }

                Debug.LogError($"场景卸载失败Key : {Key} ,该场景尚未加载,但却试图卸载它:{typeof(Scene)}");
                return;
            }
#endif
            if (isLoader)
            {
                if(_handle.IsDone)
                {
                    var Operation = Addressables.UnloadSceneAsync(_handle.Result,false);
                    Operation.WaitForCompletion();
                }
            }
            else
            {
                Debug.LogError($"场景卸载失败Key : {Key} ,该场景尚未加载,但却试图卸载它:{typeof(Scene)}");
            }
        }

        /// <summary>
        /// 卸载当前场景
        /// </summary>
        public virtual void ULoadSceneAsync()
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                if (isLoader)
                {
                    Scene scene = GetLoadedEditorScene();
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        SceneManager.UnloadSceneAsync(scene);
                        isLoader = false;
                        return;
                    }
                }

                Debug.LogError($"场景卸载失败Key : {Key} ,该场景尚未加载,但却试图卸载它:{typeof(Scene)}");
                return;
            }
#endif
            if (isLoader)
            {
                if (_handle.IsDone)
                {
                    Addressables.UnloadSceneAsync(_handle.Result,false);
                }
                else
                {
                    _handle.Completed += (result) =>
                    {
                        if (result.Status == AsyncOperationStatus.Succeeded)
                        {
                            Addressables.UnloadSceneAsync(_handle.Result, false);
                        }
                        else
                        {
                            Debug.LogError($"资源下载失败Key : {Key} ,类型为: {typeof(Scene)}");
                        }
                    };
                }
            }
            else
            {
                Debug.LogError($"场景卸载失败Key : {Key} ,该场景尚未加载,但却试图卸载它:{typeof(Scene)}");
            }
        }

        /// <summary>
        /// 协程卸载场景
        /// </summary>
        /// <returns></returns>
        public virtual IEnumerator ULoadSceneCoroutine()
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                if (isLoader)
                {
                    Scene scene = GetLoadedEditorScene();
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        yield return SceneManager.UnloadSceneAsync(scene);
                        isLoader = false;
                        yield break;
                    }
                }

                Debug.LogError($"场景卸载失败Key : {Key} ,该场景尚未加载,但却试图卸载它:{typeof(Scene)}");
                yield break;
            }
#endif
            if (isLoader)
            {
                if (_handle.IsDone)
                {
                    AsyncOperationHandle<SceneInstance> operationHandle = Addressables.UnloadSceneAsync(_handle.Result, false);
                    yield return operationHandle.Task;
                }
                else
                {
                    yield return _handle.Task;
                    AsyncOperationHandle<SceneInstance> operationHandle = Addressables.UnloadSceneAsync(_handle.Result, false);
                    yield return operationHandle.Task;
                }
            }
            else
            {
                Debug.LogError($"场景卸载失败Key : {Key} ,该场景尚未加载,但却试图卸载它:{typeof(Scene)}");
            }
        }
        
        /// <summary>
        /// UniTask 卸载场景
        /// </summary>
        public async UniTask ULoadSceneUniTask()
        {
#if UNITY_EDITOR
            if (AssetsManager.Instance.UseLocalAssetDatabase)
            {
                if (isLoader)
                {
                    Scene scene = GetLoadedEditorScene();
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
                        if (operation != null)
                        {
                            await operation.ToUniTask();
                        }

                        isLoader = false;
                    }
                }
                return;
            }
#endif
            if (isLoader)
            {
                if (_handle.IsDone)
                {
                    var operationHandle = Addressables.UnloadSceneAsync(_handle.Result, false);
                    await operationHandle.ToUniTask();
                }
            }
        }

        /// <summary>
        /// 释放当前场景资源
        /// </summary>
        public virtual void Release()
        {
            if (this.isLoader)
            {
                this.isLoader = false;
#if UNITY_EDITOR
                editorScene = default;
#endif
                if (_handle.IsValid() && _handle.Status == AsyncOperationStatus.Succeeded)
                {
                    Addressables.Release(_handle);
                }
            }
        }

#if UNITY_EDITOR
        private async UniTaskVoid LoadSceneAsyncInEditor(Action onComplete)
        {
            await LoadSceneUniTaskInEditor(null, onComplete);
        }

        private IEnumerator LoadSceneCoroutineInEditor(Action onComplete)
        {
            if (isLoader)
            {
                Scene scene = GetLoadedEditorScene();
                if (scene.IsValid() && scene.isLoaded)
                {
                    onComplete?.Invoke();
                    SceneManager.SetActiveScene(scene);
                    yield break;
                }

                Debug.LogWarning($"编辑器场景Loader缓存已失效，重新加载场景 Key : {Key}");
                isLoader = false;
                editorScene = default;
            }

            isLoader = true;
            string scenePath = GetSceneAssetPath(Key);
            AsyncOperation operation = EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(_mode));
            if (operation == null)
            {
                isLoader = false;
                Debug.LogError($"资源下载失败Key : {Key} ,类型为: {typeof(Scene)}");
                yield break;
            }

            operation.allowSceneActivation = false;
            while (operation.progress < 0.9f)
            {
                yield return null;
            }

            onComplete?.Invoke();
            operation.allowSceneActivation = true;
            yield return operation;

            editorScene = GetLoadedEditorScene(scenePath);
            if (editorScene.IsValid() && editorScene.isLoaded)
            {
                SceneManager.SetActiveScene(editorScene);
            }
            else
            {
                isLoader = false;
                editorScene = default;
                Debug.LogError($"资源下载失败Key : {Key} ,类型为: {typeof(Scene)}");
            }
        }

        private async UniTask LoadSceneUniTaskInEditor(IProgress<float> progress, Action onComplete = null)
        {
            if (isLoader)
            {
                Scene scene = GetLoadedEditorScene();
                if (scene.IsValid() && scene.isLoaded)
                {
                    progress?.Report(1f);
                    onComplete?.Invoke();
                    SceneManager.SetActiveScene(scene);
                    return;
                }

                Debug.LogWarning($"编辑器场景Loader缓存已失效，重新加载场景 Key : {Key}");
                isLoader = false;
                editorScene = default;
            }

            isLoader = true;
            string scenePath = GetSceneAssetPath(Key);
            AsyncOperation operation = EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(_mode));
            if (operation == null)
            {
                isLoader = false;
                Debug.LogError($"资源下载失败Key : {Key} ,类型为: {typeof(Scene)}");
                return;
            }

            operation.allowSceneActivation = false;
            while (operation.progress < 0.9f)
            {
                progress?.Report(operation.progress);
                await UniTask.Yield();
            }

            progress?.Report(0.9f);
            onComplete?.Invoke();
            operation.allowSceneActivation = true;
            await operation.ToUniTask();

            editorScene = GetLoadedEditorScene(scenePath);
            if (editorScene.IsValid() && editorScene.isLoaded)
            {
                SceneManager.SetActiveScene(editorScene);
                progress?.Report(1f);
            }
            else
            {
                isLoader = false;
                editorScene = default;
                Debug.LogError($"资源下载失败Key : {Key} ,类型为: {typeof(Scene)}");
            }
        }

        private Scene GetLoadedEditorScene()
        {
            return GetLoadedEditorScene(GetSceneAssetPath(Key));
        }

        private static Scene GetLoadedEditorScene(string scenePath)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.path == scenePath)
                {
                    return scene;
                }
            }

            return default;
        }

        private static string GetSceneAssetPath(string sceneKey)
        {
            if (string.IsNullOrEmpty(sceneKey))
            {
                return string.Empty;
            }

            if (sceneKey.StartsWith("Assets/") || sceneKey.StartsWith("Packages/"))
            {
                return sceneKey;
            }

            string path = AssetDatabase.GUIDToAssetPath(sceneKey);
            return string.IsNullOrEmpty(path) ? sceneKey : path;
        }
#endif
    }
}
