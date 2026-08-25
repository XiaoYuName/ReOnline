using ReDiv.Net;
using UnityEngine;
using XFramework;

/// <summary>
/// 按「当前城镇 + 当前时段」把背景控制器预制体挂到 <c>UIBackground</c> 层下。
///
/// 数据全部来自 <see cref="TownManager"/>（城镇来自服务端的选角行、时段来自服务端的
/// <c>world_time</c> 表），这里**不碰 Conn、也不自己算时段**。
///
/// 它是**纯 C# 单例**，不是 MonoBehaviour —— 不用往场景里挂东西，也就不会出现
/// 「忘了挂所以没背景」。生命周期靠 <c>[RuntimeInitializeOnLoadMethod]</c> 兜，
/// 和 <see cref="AuthManager"/> 一个套路。
///
/// 换背景的两个触发点：
///   · 服务端切了时段  → <see cref="TownManager.WorldTimeChanged"/>
///   · 玩家换了城镇 / 进出游戏 → <see cref="TownManager.LocationChanged"/>
/// 两个都归到同一个 <see cref="Refresh"/>，因为「该显示哪个预制体」只由这两者的组合决定。
/// </summary>
public sealed class TownBackgroundLoader
{
    private static TownBackgroundLoader instance;

    public static TownBackgroundLoader Instance
    {
        get
        {
            instance ??= new TownBackgroundLoader();
            instance.Attach();
            return instance;
        }
    }

    private TownBackgroundLoader()
    {
    }

    /// <summary>
    /// 场景加载前就把事件挂好，免得漏掉第一次同步。
    ///
    /// 关掉域重载（Enter Play Mode Options 取消 Reload Domain）时静态字段会留着上一轮的
    /// 实例、它的处理器还挂在 TownManager 上，所以先摘掉再换新的 ——
    /// 否则第二次进 Play 会收到两份回调、生成两个背景。AuthManager 那边同理。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        instance?.Detach();
        instance = new TownBackgroundLoader();
        instance.Attach();
    }

    // ------------------------------------------------------------------
    // 状态
    // ------------------------------------------------------------------

    private bool attached;

    /// <summary>当前挂着的背景实例。换的时候要销毁，否则会越叠越多。</summary>
    private GameObject current;

    /// <summary>当前背景用的那个 Addressable key。和新的一样就不用重建。</summary>
    private string currentKey = string.Empty;

    private void Attach()
    {
        if (attached)
        {
            return;
        }
        attached = true;

        TownManager.Instance.Ready += Refresh;
        TownManager.Instance.WorldTimeChanged += Refresh;
        TownManager.Instance.LocationChanged += Refresh;
    }

    private void Detach()
    {
        if (!attached)
        {
            return;
        }
        attached = false;

        TownManager.Instance.Ready -= Refresh;
        TownManager.Instance.WorldTimeChanged -= Refresh;
        TownManager.Instance.LocationChanged -= Refresh;
    }

    // ------------------------------------------------------------------
    // 换背景
    // ------------------------------------------------------------------

    /// <summary>
    /// 按当前状态刷背景。**幂等** —— 该显示的 key 没变就什么都不做，
    /// 所以时段推送和位置推送先后到达（会连着触发两次）也不会重建两遍。
    /// </summary>
    public void Refresh()
    {
        string key = TownManager.Instance.CurrentBackgroundKey;

        if (key == currentKey)
        {
            return;
        }

        Clear();
        currentKey = key;

        if (string.IsNullOrEmpty(key))
        {
            // 不在城镇里（还没进游戏），或者这个城镇的这个时段还没配背景。
            // 都不是错误 —— 配置窗口 / 自检会把没配的报出来，运行时别反复刷日志
            return;
        }

        // UISystem 是场景里的 MonoSingleton。理论上事件到这儿时场景早起来了，
        // 但退出 Play / 切场景的瞬间可能收到最后一次推送，判一下别抛 NRE
        if (UISystem.Instance == null)
        {
            currentKey = string.Empty;
            return;
        }

        Transform parent = UISystem.Instance.GetUILayer(UICanvasLayer.UIBackground, UIParentLayer.UIPanel);

        if (parent == null)
        {
            Debug.LogError("[TownBackground] 取不到 UIBackground 层，背景挂不上去");
            currentKey = string.Empty;
            return;
        }

        var prefab = AssetsManager.Instance.LoadAssets<GameObject>(key);

        if (prefab == null)
        {
            Debug.LogError($"[TownBackground] 背景预制体加载不出来：{key}");
            currentKey = string.Empty;
            return;
        }

        current = Object.Instantiate(prefab, parent, false);

        // 铺满背景层。预制体自己要控高宽比的话挂 FitBackgroundToCamera / AspectRatioFitter
        if (current.transform is RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        var controller = current.GetComponent<TownBackgroundController>();

        if (controller == null)
        {
            // 不当错误：预制体就算只有一张图也能当背景用。但派生了控制器才能做入场表现
            Debug.LogWarning($"[TownBackground] {key} 上没有 TownBackgroundController 组件，" +
                             "只会静态显示，播不了入场表现");
        }
        else
        {
            controller.Show();
        }

        Debug.Log($"[TownBackground] 城镇={TownManager.Instance.CurrentTownId} " +
                  $"时段={TownManager.Instance.CurrentBandId} 背景={key}");
    }

    /// <summary>销毁当前背景并归还资源。</summary>
    private void Clear()
    {
        if (current != null)
        {
            current.GetComponent<TownBackgroundController>()?.Hide();

            // ⚠️ 先 SetActive(false) 再 Destroy：Unity 的 Destroy 延迟到帧末，
            // 同一帧里紧接着 Instantiate 新背景的话，旧的还在、两张会叠着显示
            //（创角界面的全屏立绘就是这么叠出过 3 张）
            current.SetActive(false);
            Object.Destroy(current);
            current = null;
        }

        if (!string.IsNullOrEmpty(currentKey))
        {
            // 这里不走 UIBase.LoadAsset 的托管释放（本类不是 UIBase），所以要自己配对归还
            AssetsManager.Instance.FreeAsset(currentKey);
            currentKey = string.Empty;
        }
    }
}
