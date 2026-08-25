using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// UISystem 底层委托: 用于打开界面后的回调
    /// </summary>
    /// <typeparam name="T">打开界面后的T类型对象</typeparam>
    public delegate void Call<in T>(T cb);
    
    /// <summary>
    /// UI系统总管理器
    /// </summary>
    public class UISystem : MonoOdinSingleton<UISystem>,IGameInitialized
    {
        #region Initialized
        public const string ConfigPath = "Assets/AddressableAssets/Remote/Configs/UI/UIPageConfiguration.asset";

        [BoxGroup("Initialized"), ShowInInspector, LabelText("UI配置表"), ReadOnly]
        private UIPageConfiguration pageConfiguration;

        public async UniTask Initialized()
        {
            pageConfiguration = await AssetsManager.Instance.LoadAssetsUniTask<UIPageConfiguration>(ConfigPath);
            if (pageConfiguration == null)
            {
                Debug.LogError($"没有加载到UI配置表: {ConfigPath},所有界面都会打不开");
            }

            LoadCanvas();
        }

        public async UniTask Release()
        {
            await UniTask.CompletedTask;
        }

        /// <summary>
        /// 按界面ID取配置。取不到会报错并返回 null,调用方直接判空返回即可。
        /// </summary>
        /// <param name="uiPage">界面ID,用 <c>UIKeys</c> 里的常量</param>
        public UIPageData GetPageData(string uiPage)
        {
            if (pageConfiguration == null)
            {
                Debug.LogError($"UI配置表还没加载完就取界面配置: {uiPage}");
                return null;
            }

            UIPageData tableData = pageConfiguration.Get(uiPage);
            if (tableData == null)
            {
                Debug.LogError("配置表中没有对应的UI界面: " + uiPage);
            }

            return tableData;
        }

        /// <summary>
        /// UI层根节点名。层级结构是 UISystem/UILayout/{UIPanel,UIDialogue,UIPop,UITop},
        /// 所有UI默认挂到UI层下对应的子层级。同级的 UIBackground 是背景层,不参与UI自动挂载。
        /// </summary>
        private const string UILayoutRootName = "UILayout";
        /// <summary>
        /// UI的背景层名。UI背景高于场景Ground，低于角色层
        /// </summary>
        private const string UIBackgroundRootName = "UIBackground";

        private void LoadCanvas()
        {
            uiParentDictionary = new Dictionary<UICanvasLayer, Dictionary<UIParentLayer, Transform>>();
            uiCanvasLayer = new Dictionary<UICanvasLayer, Transform>();

            // UI层:所有UI都往里挂,四个子层必须齐,缺了自动补。
            RegisterCanvasLayer(UICanvasLayer.UIPanel, UILayoutRootName, true);
            // 背景层:只登记场景里实际摆了的子层,用到别的再按需补,免得凭空多出几个空节点。
            RegisterCanvasLayer(UICanvasLayer.UIBackground, UIBackgroundRootName, false);
        }

        /// <summary>
        /// 登记一个UICanvas层,以及它下面的子层级。
        /// </summary>
        /// <param name="canvasLayer">Canvas层</param>
        /// <param name="rootName">该Canvas层在UISystem下的节点名</param>
        /// <param name="createMissingParents">子层级缺失时是否立刻补齐</param>
        private void RegisterCanvasLayer(UICanvasLayer canvasLayer, string rootName, bool createMissingParents)
        {
            Transform canvasRoot = transform.Find(rootName);
            uiCanvasLayer[canvasLayer] = canvasRoot;

            Dictionary<UIParentLayer, Transform> parents = new Dictionary<UIParentLayer, Transform>();
            uiParentDictionary[canvasLayer] = parents;

            if (canvasRoot == null)
            {
                Debug.LogError($"没有找到UICanvas层节点 {rootName},{canvasLayer} 下的内容都无法正确挂载");
                return;
            }

            foreach (UIParentLayer parentLayer in Enum.GetValues(typeof(UIParentLayer)))
            {
                Transform parentTransform = canvasRoot.Find(parentLayer.ToString());
                if (parentTransform == null)
                {
                    if (!createMissingParents)
                    {
                        continue;
                    }

                    parentTransform = CreateParentLayer(canvasRoot, parentLayer);
                }

                parents[parentLayer] = parentTransform;
            }
        }

        private Transform CreateParentLayer(Transform canvasRoot, UIParentLayer parentLayer)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"{canvasRoot.name} 下没有找到子层级 {parentLayer},系统将自动创建对应的子层级");
#endif
            GameObject parentObj = new GameObject(parentLayer.ToString());
            parentObj.transform.SetParent(canvasRoot, false);
            parentObj.transform.localPosition = Vector3.zero;
            return parentObj.transform;
        }

        /// <summary>
        /// 获取UI的生成父级。默认取UI层(UIPanel)下的子层级,普通UI都挂在这里。
        /// </summary>
        /// <param name="parentLayer">子渲染层级</param>
        /// <returns></returns>
        public Transform GetUILayer(UIParentLayer parentLayer)
        {
            return GetUILayer(UICanvasLayer.UIPanel, parentLayer);
        }

        /// <summary>
        /// 获取指定Canvas层下指定子层级的生成父级。
        /// 场景里没摆的子层级会按需创建出来,所以背景层要加新的子层直接传进来就行,不用先去场景里建节点。
        /// </summary>
        /// <param name="canvasLayer">Canvas层</param>
        /// <param name="parentLayer">子渲染层级</param>
        /// <returns></returns>
        public Transform GetUILayer(UICanvasLayer canvasLayer, UIParentLayer parentLayer)
        {
            if (uiParentDictionary == null
                || !uiParentDictionary.TryGetValue(canvasLayer, out Dictionary<UIParentLayer, Transform> parents))
            {
                Debug.LogError($"没有登记过UICanvas层: {canvasLayer}");
                return null;
            }

            if (parents.TryGetValue(parentLayer, out Transform layerTransform) && layerTransform != null)
            {
                return layerTransform;
            }

            Transform canvasRoot = GetUICanvas(canvasLayer);
            if (canvasRoot == null)
            {
                return null;
            }

            layerTransform = CreateParentLayer(canvasRoot, parentLayer);
            parents[parentLayer] = layerTransform;
            return layerTransform;
        }

        /// <summary>
        /// 取UI实例化时的挂载父级。子层级找不到时退到UISystem根节点,保证UI至少能显示出来。
        /// </summary>
        private Transform GetUIParent(UIPageData tableData)
        {
            Transform parent = GetUILayer(tableData.UIParent);
            if (parent == null)
            {
                Debug.LogError($"没有找到UI子层级 {tableData.UIParent},界面将挂到UISystem根节点: {tableData.PageID}");
                return transform;
            }

            return parent;
        }

        /// <summary>
        /// 取UICanvas 层级
        /// </summary>
        /// <param name="uiCanvasLayer"></param>
        /// <returns></returns>
        private Transform GetUICanvas(UICanvasLayer uiCanvasLayer)
        {
            if (this.uiCanvasLayer.TryGetValue(uiCanvasLayer, out var canvas))
            {
                return canvas;
            }

            return null;
        }

        /// <summary>
        /// 取转场渐变遮罩该用的Sorting Layer名。用的是两个专门的渐变层,不是UI自己的层:
        /// Scene → SceneFade:排在所有UI层之前,遮住整个场景(含UI背景层),普通UI照常显示。
        /// All   → UIFade:  排在所有层最后,连UI一起遮掉。
        /// </summary>
        public string GetFadeSortingLayerName(FadeLayer fadeLayer)
        {
            string sortingLayerName;
            switch (fadeLayer)
            {
                case FadeLayer.Scene:
                    sortingLayerName = "SceneFade";
                    break;
                case FadeLayer.All:
                    sortingLayerName = "UIFade";
                    break;
                default:
                    Debug.LogError($"没有为 FadeLayer.{fadeLayer} 配置渐变层,先按遮住全部处理");
                    sortingLayerName = "UIFade";
                    break;
            }

#if UNITY_EDITOR
            // sortingLayerName 赋一个不存在的层名时 Unity 会静默忽略、保留原值,不报任何错,
            // 表现是"渐变突然遮错东西了"极难查,所以在编辑器下先兜住。
            // 必须写全限定名:SmartSlicer2D 插件在全局命名空间也声明了一个 SortingLayer 类,会把 UnityEngine 的盖掉。
            if (System.Array.TrueForAll(UnityEngine.SortingLayer.layers,
                    sortingLayer => sortingLayer.name != sortingLayerName))
            {
                Debug.LogError($"Sorting Layer 不存在: {sortingLayerName},请在 Project Settings > Tags and Layers 里补上");
            }
#endif
            return sortingLayerName;
        }

        #endregion

        #region Datas

        [ReadOnly,LabelText("UI列表"),BoxGroup("列表")]
        private Dictionary<string, GameObject> uiDictionary = new Dictionary<string, GameObject>();
        [ReadOnly,LabelText("UIRoots"),BoxGroup("列表")]
        private Dictionary<UICanvasLayer, Dictionary<UIParentLayer, Transform>> uiParentDictionary;
        [ReadOnly,LabelText("UICanvas"),BoxGroup("列表")]
        private Dictionary<UICanvasLayer,Transform>  uiCanvasLayer;
        
        #endregion

        #region 底层框架

        public void AddUI(string uiPage,UIBase uiBase)
        {
            if (!uiDictionary.ContainsKey(uiPage))
            {
                uiDictionary.Add(uiPage, uiBase.gameObject);
                return;
            }
           
        }

        /// <summary>
        /// 获取UI
        /// </summary>
        /// <param name="uiPage">UI Key</param>
        /// <typeparam name="T">UI 组件对象,该组件必须继承自UIBase</typeparam>
        /// <returns></returns>
        public T GetUI<T>(string uiPage) where T: UIBase
        {
            if (!uiDictionary.ContainsKey(uiPage))
            {
                return LoadUI(uiPage).GetComponent<T>();
            }
            return uiDictionary[uiPage].GetComponent<T>();
        }

        /// <summary>
        /// 取<b>已经加载过</b>的UI,没加载过返回null。
        /// 和 <see cref="GetUI{T}"/> 的区别:那个在界面没加载过时会顺手实例化一个出来,
        /// 只想"看看界面在不在"的场合(引导找目标节点、状态判断)必须用这个,不然会凭空多出一个没打开的界面。
        /// </summary>
        /// <param name="uiPage">UI Key</param>
        /// <typeparam name="T">UI 组件对象,该组件必须继承自UIBase</typeparam>
        public T GetLoadedUI<T>(string uiPage) where T : UIBase
        {
            if (string.IsNullOrEmpty(uiPage) || !uiDictionary.TryGetValue(uiPage, out GameObject obj) || obj == null)
            {
                return null;
            }

            return obj.GetComponent<T>();
        }

        /// <summary>
        /// 异步获取UI
        /// </summary>
        /// <param name="uiPage"></param>
        /// <param name="action">回调函数</param>
        /// <typeparam name="T">组件对象,该对象必须继承UIBase</typeparam>
        public void GetUIAsync<T>(string uiPage,Call<T> action) where T:UIBase
        {
            if (!uiDictionary.ContainsKey(uiPage))
            {
                LoadUIAsync(uiPage,action);
            }
            else
            {
                action?.Invoke(uiDictionary[uiPage].GetComponent<T>());
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="uiPage"></param>
        /// <param name="action"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public IEnumerator GetUICoroutine<T>(string uiPage, Call<T> action)
        {
            if (!uiDictionary.ContainsKey(uiPage))
            {
                yield return LoadUIEnumerator(uiPage, action);
            }
            else
            {
                action?.Invoke(uiDictionary[uiPage].GetComponent<T>());
            }
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="uiPage"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public async UniTask<T> GetUIUniTask<T>(string uiPage) where T : UIBase
        {
            if (!uiDictionary.ContainsKey(uiPage))
            {
               return await loadUiUniTask<T>(uiPage);
            }
            return uiDictionary[uiPage].GetComponent<T>();
        }

        /// <summary>
        /// 打开UI界面
        /// </summary>
        /// <param name="uiPage">界面名称</param>
        public void OpenUI(string uiPage)
        {
            UIBase ui = GetUI<UIBase>(uiPage);
            if (ui == null) return;
            if (ui.isOpen) return;
            ui.Open();
            ui.transform.SetAsLastSibling();
        }

        /// <summary>
        /// 打开UI界面
        /// </summary>
        /// <param name="uiPage">界面</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T OpenUI<T>(string uiPage) where T : UIBase
        {
            UIBase ui = GetUI<UIBase>(uiPage);
            if (ui == null) return null;
            if (!ui.isOpen)
            {
                ui.Open();
            }
            ui.transform.SetAsLastSibling();
            return ui.GetComponent<T>();
        }
        
        /// <summary>
        /// 异步打开UI界面
        /// </summary>
        /// <param name="uiPage">界面名称</param>
        /// <param name="action">打开后的回调函数</param>
        /// <typeparam name="T">界面类型,该类型必须继承UIBase</typeparam>
        public void OpenUIAsync<T>(string uiPage, Call<T> action) where T:UIBase
        {
            GetUIAsync(uiPage, delegate(T cb)
            {
                if(!cb.isOpen)
                    cb.Open();
                cb.transform.SetAsLastSibling();
                action?.Invoke(cb);
            });
        }
        
        /// <summary>
        /// 异步打开UI界面
        /// </summary>
        /// <param name="uiPage">界面名称</param>
        /// <typeparam name="T">界面类型,该类型必须继承UIBase</typeparam>
        public void OpenUIAsync<T>(string uiPage) where T:UIBase
        {
            GetUIAsync(uiPage, delegate(T cb)
            {
                if(!cb.isOpen)
                    cb.Open();
                cb.transform.SetAsLastSibling();
            });
        }
        
        /// <summary>
        /// 协程打开UI
        /// </summary>
        /// <param name="uiPage">界面名称</param>
        /// <param name="action">打开后的回调函数</param>
        /// <typeparam name="T">界面类型,该类型必须继承UIBase</typeparam>
        /// <returns></returns>
        public IEnumerator OpenUICoroutine<T>(string uiPage, Call<T> action) where T:UIBase
        {
            yield return GetUICoroutine(uiPage, delegate(T cb)
            {
                if(!cb.isOpen)
                    cb.Open();
                cb.transform.SetAsLastSibling();
                action?.Invoke(cb);
            });
        }
        
        /// <summary>
        /// 关闭UI
        /// </summary>
        /// <param name="uiPage">ui名称</param>
        public void CloseUI(string uiPage)
        {
            if(!uiDictionary.ContainsKey(uiPage))return;
            UIBase Obj = uiDictionary[uiPage].GetComponent<UIBase>();
            Obj.transform.SetAsFirstSibling();
            Obj.Close();
        }

        public void CloseUI(UIBase uiBase)
        {
            if (uiBase == null) return;
            uiBase.transform.SetAsFirstSibling();
            uiBase.Close();
        }




        /// <summary>
        /// 同步加载UI
        /// </summary>
        /// <param name="uiPage"></param>
        /// <returns></returns>
        private GameObject LoadUI(string uiPage)
        {
            UIPageData tableData = GetPageData(uiPage);
            if (tableData == null)
            {
                return null;
            }
            
            GameObject Prefab = AssetsManager.Instance.LoadAssets<GameObject>(tableData.PageKey);
            
            var Obj = Instantiate(Prefab, GetUIParent(tableData));
            UIBase uiBase = Obj.GetComponent<UIBase>();
            if (uiBase != null)
            {
                uiBase.uiPageData = tableData;
                uiBase.Init();
            }
            uiDictionary.Add(uiPage,Obj);
            return Obj;
        }
        
        /// <summary>
        /// 同步加载UI
        /// </summary>
        /// <param name="uiPage"></param>
        /// <returns></returns>
        private T LoadUI<T>(string uiPage) where T:UIBase
        {
            UIPageData tableData = GetPageData(uiPage);
            if (tableData == null)
            {
                return null;
            }
            GameObject Prefab = AssetsManager.Instance.LoadAssets<GameObject>(tableData.PageKey);
            var Obj = Instantiate(Prefab, GetUIParent(tableData));
            T uiBase = Obj.GetComponent<T>();
            if (uiBase != null)
            {
                uiBase.uiPageData = tableData;
                uiBase.Init();
            }
            uiDictionary.Add(uiPage,Obj);
            return uiBase;
        }
        
        /// <summary>
        /// 异步加载UI
        /// </summary>
        /// <param name="uiPage"></param>
        /// <param name="action"></param>
        /// <typeparam name="T"></typeparam>
        private void LoadUIAsync<T>(string uiPage,Call<T> action)
        {
            UIPageData tableData = GetPageData(uiPage);
            if (tableData == null)
            {
                return;
            }
            AssetsManager.Instance.LoadAssetsAsync(tableData.PageKey, delegate(GameObject prefab)
            {
                var Obj = Instantiate(prefab, GetUIParent(tableData));
                UIBase uiBase = Obj.GetComponent<UIBase>();
                if (uiBase != null)
                {
                    uiBase.uiPageData = tableData;
                    uiBase.Init();
                }

                if (!uiDictionary.ContainsKey(uiPage))
                {
                    uiDictionary.Add(uiPage,Obj);
                }
                action?.Invoke(uiBase.GetComponent<T>());
            });
        }
        
        /// <summary>
        /// 协程加载UI
        /// </summary>
        /// <param name="uiPage"></param>
        /// <param name="action"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        private IEnumerator LoadUIEnumerator<T>(string uiPage,Call<T> action)
        {
            UIPageData tableData = GetPageData(uiPage);
            if (tableData == null)
            {
                yield break;
            }
            yield return AssetsManager.Instance.LoadAssetsCoroutine(tableData.PageKey, delegate(GameObject prefab)
            {
                var Obj = Instantiate(prefab, GetUIParent(tableData));
                UIBase uiBase = Obj.GetComponent<UIBase>();
                if (uiBase != null)
                {
                    uiBase.uiPageData = tableData;
                    uiBase.Init();
                }
                uiDictionary.Add(uiPage,Obj);
                action?.Invoke(uiBase.GetComponent<T>());
            });
        }
        
        /// <summary>
        /// 协程加载UI
        /// </summary>
        /// <param name="uiPage"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        private async UniTask<T> loadUiUniTask<T>(string uiPage) where T:UIBase
        {
            UIPageData tableData = GetPageData(uiPage);
            if (tableData == null)
            {
                return null;
            }
            var prefab =  await AssetsManager.Instance.LoadAssetsUniTask<GameObject>(tableData.PageKey);
            var Obj = Instantiate(prefab, GetUIParent(tableData));
            T uiBase = Obj.GetComponent<T>();
            if (uiBase != null)
            {
                uiBase.uiPageData = tableData;
                uiBase.Init();
            }
            uiDictionary.Add(uiPage,Obj);
            return uiBase;
        }

        #endregion

        #region 背景管理

        /// <summary>
        /// 加载背景。挂到背景层(UIBackground)下对应的子层级里,默认是 UIPanel。
        /// 背景以后要分层(比如远景/近景各一层)就传不同的 parentLayer,子层不存在会自动创建。
        /// </summary>
        /// <param name="backgroundKey">背景预制体的Addressable Key</param>
        /// <param name="parentLayer">背景层下的子层级</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T LoadUIBackground<T>(string backgroundKey, UIParentLayer parentLayer = UIParentLayer.UIPanel)
            where T : UIBackground
        {
            Transform parent = GetUILayer(UICanvasLayer.UIBackground, parentLayer);
            if (parent == null)
            {
                Debug.LogError($"没有找到背景层的挂载父级 {parentLayer},背景加载失败: {backgroundKey}");
                return null;
            }

            var obj = AssetsManager.Instance.Instantiate(backgroundKey);
            // 用 SetParent(parent, false) 而不是保留世界坐标:
            // 从对象池里复用出来的实例在 PoolRoot 下被改过局部坐标,不重置的话位置会带过来
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localScale = Vector3.one;
            if (obj.TryGetComponent<T>(out T background))
            {
                background.Init();
                // Init 之后要跟一次 Open:HideUIBackground / ReleaseUIBackground 那边调的是
                // Close(),只 Init 不 Open 的话 isOpen 一直是 false,等于对一个"从没打开过"的
                // 面板做收尾(Release 资源、走离场分支),背景派生类重写 Open 也永远不会被调到。
                background.Open();
                return background;
            }

            Debug.LogError($"背景预制体上没有找到 {typeof(T).Name} 组件: {backgroundKey}");
            return null;
        }

        /// <summary>
        /// 隐藏背景:只是回收进对象池,之后 LoadUIBackground 同一个Key会直接复用,不会重新加载。
        /// </summary>
        /// <param name="uiBackground"></param>
        public void HideUIBackground(UIBackground uiBackground)
        {
            uiBackground.Close();
            AssetsManager.Instance.FreeGameObject(uiBackground.gameObject);
        }

        /// <summary>
        /// 释放背景:直接Destroy,该Key下没有实例在用之后连Addressables引用一起卸掉。
        /// 确定这个背景短期内不会再用了才调,还要复用的用 HideUIBackground。
        /// </summary>
        /// <param name="uiBackground"></param>
        public void ReleaseUIBackground(UIBackground uiBackground)
        {
            uiBackground.Close();
            AssetsManager.Instance.ReleaseGameObject(uiBackground.gameObject);
        }

        #endregion
        
        #region 弹窗管理

        // 当栈用,末尾是栈顶。用List而不是Stack是为了能移除栈中任意位置的UI:
        // 玩家点面板自己的关闭按钮、代码直接CloseUI某个面板、批量关闭,都不是从栈顶关的。
        private readonly List<UIBase> uiStack = new List<UIBase>();

        /// <summary>
        /// 关闭栈顶UI(右键/Esc)。顺手清掉已经关闭或被销毁的僵尸引用,
        /// 否则会出现"按Esc没反应,要连按几次"。
        /// </summary>
        public void CloseStackUI()
        {
            TryCloseStackUI();
        }

        /// <summary>
        /// 关闭栈顶UI,返回这次有没有真的关掉一个界面。
        /// 取消类输入(右键/Esc)的接入方拿返回值判断要不要把这次输入消费掉 ——
        /// 框架层不认识具体的输入系统,由业务层订阅自己的输入事件再调这里。
        /// </summary>
        public bool TryCloseStackUI()
        {
            while (uiStack.Count > 0)
            {
                int topIndex = uiStack.Count - 1;
                UIBase top = uiStack[topIndex];
                uiStack.RemoveAt(topIndex);
                if (top == null || !top.isOpen)
                {
                    continue;
                }

                CloseUI(top);
                return true;
            }

            return false;
        }

        public void PushStackUI(UIBase uiBase)
        {
            if (uiBase == null)
            {
                return;
            }

            // 关闭动画期间 isOpen 已经是 false,这时候再打开会二次入栈,先移除再压入
            uiStack.Remove(uiBase);
            uiStack.Add(uiBase);
        }

        public void RemoveStackUI(UIBase uiBase)
        {
            if (uiBase == null)
            {
                return;
            }

            uiStack.Remove(uiBase);
        }

        /// <summary>
        /// 清空关闭栈。批量关闭UI时用,避免栈里留下一堆已关闭UI的引用。
        /// </summary>
        public void ClearStackUI()
        {
            uiStack.Clear();
        }

        #endregion

        #region KeepUIPage

        /// <summary>
        /// 系统级常驻UI:永远不参与批量关闭/恢复。
        /// PopLoadingUI 是转场黑幕本身,批量关闭时把它关掉会直接露出场景卸载加载的过程,
        /// 而且渐变结束后它又会被打开,进而在下一次快照里被当成普通UI恢复出来。
        /// 以后有别的常驻系统UI(飘字、Toast 之类)也往这里加。
        /// </summary>
        private static readonly string[] SystemUIPages = { "PopLoadingUI" };

        /// <summary>
        /// 常驻HUD:一直开着,不算"玩家打开了界面"。
        /// 和 SystemUIPages 分开是因为它照常参与批量关闭/恢复(进独占小游戏时要关掉)。
        /// </summary>
        private static readonly string[] HudUIPages = { "MainUI" };

        /// <summary>
        /// 除了常驻UI(转场黑幕 + 主界面HUD)之外,还有没有打开着的界面。
        /// 给"没开界面时右键才生效"这类判断用。
        /// </summary>
        /// <param name="ignoreUIPage">
        /// 额外不算"打开了界面"的界面。给场景默认UI(GarmentMakingCommonUI 之类)用:
        /// 它跟着场景一起开关,是场景的一部分,不是玩家自己点开的界面。
        /// 由调用方传进来,免得UI框架去反查当前在哪个场景。
        /// </param>
        public bool HasOpenUIExceptPersistent(string ignoreUIPage = null)
        {
            foreach (KeyValuePair<string, GameObject> pair in uiDictionary)
            {
                if (pair.Value == null
                    || pair.Key == ignoreUIPage
                    || ContainsUIPage(SystemUIPages, pair.Key)
                    || ContainsUIPage(HudUIPages, pair.Key))
                {
                    continue;
                }

                UIBase uiBase = pair.Value.GetComponent<UIBase>();
                if (uiBase != null && uiBase.isOpen)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 关闭当前所有打开的UI(keepUIPages 除外),返回快照,之后用 RestoreUI 原样恢复。
        /// 给"进入独占场景(全屏小游戏等)"用:场景只保留自己的UI,退出时把玩家原来开着的界面还回去。
        /// </summary>
        /// <param name="keepUIPages">不参与关闭也不进快照的UI,一般是这个场景自己的UI</param>
        /// <returns>被关闭的UI快照,按层级从低到高排列</returns>
        public List<string> CloseAllUIAndSnapshot(IReadOnlyList<string> keepUIPages)
        {
            // CloseUI 里有 SetAsFirstSibling,一关层级就乱了,所以必须先把层级信息全部采集完再关
            List<(string uiPage, int siblingIndex)> openedUI = new();
            foreach (KeyValuePair<string, GameObject> pair in uiDictionary)
            {
                if (pair.Value == null
                    || ContainsUIPage(SystemUIPages, pair.Key)
                    || ContainsUIPage(keepUIPages, pair.Key))
                {
                    continue;
                }

                UIBase uiBase = pair.Value.GetComponent<UIBase>();
                if (uiBase == null || !uiBase.isOpen)
                {
                    continue;
                }

                openedUI.Add((pair.Key, uiBase.transform.GetSiblingIndex()));
            }

            // 按原本的层级从低到高排。恢复时按这个顺序 Open,
            // OpenUI 里的 SetAsLastSibling 会在各自父级内重新追加,把层级关系还原回去。
            // 不需要再按父级分组:同一父级下 siblingIndex 唯一,全局升序就保证了每个父级内部也是升序,
            // 而跨父级之间的先后顺序对结果没有影响。
            openedUI.Sort((a, b) => a.siblingIndex.CompareTo(b.siblingIndex));

            // 这里不能 ClearStackUI:keepUIPages 里的UI(场景自己的UI)可能已经在栈里了,
            // 清掉的话进场景后 Esc/右键就关不掉它。逐个 CloseUI 走 RemoveStackUI 已经能清干净。
            List<string> snapshot = new(openedUI.Count);
            for (int i = 0; i < openedUI.Count; i++)
            {
                snapshot.Add(openedUI[i].uiPage);
                CloseUI(openedUI[i].uiPage);
            }

            return snapshot;
        }

        /// <summary>
        /// 按 CloseAllUIAndSnapshot 的快照恢复UI。已经被其它流程打开的会跳过,避免把它顶到最上层。
        /// </summary>
        public void RestoreUI(IReadOnlyList<string> uiPages)
        {
            if (uiPages == null)
            {
                return;
            }

            for (int i = 0; i < uiPages.Count; i++)
            {
                if (!uiDictionary.TryGetValue(uiPages[i], out GameObject uiObj) || uiObj == null)
                {
                    continue;
                }

                UIBase uiBase = uiObj.GetComponent<UIBase>();
                if (uiBase == null || uiBase.isOpen)
                {
                    continue;
                }

                OpenUI(uiPages[i]);
            }
        }

        private static bool ContainsUIPage(IReadOnlyList<string> uiPages, string uiPage)
        {
            if (uiPages == null)
            {
                return false;
            }

            for (int i = 0; i < uiPages.Count; i++)
            {
                if (uiPages[i] == uiPage)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}

