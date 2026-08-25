using System.Collections.Generic;
using PathologicalGames;
using UnityEngine;
using XFramework;

/// <summary>
/// 城镇角色的取用与回收。<see cref="MainCommonUI"/> 拥有它一份，自己不订阅任何东西。
///
/// **两层结构**（用户 2026-08-25 定的）：
/// <code>
/// TownCharacterController（所有角色共用同一个预制体）   ← 名字、以后的血条/称号都在这层
/// └── SkeletonTown
///     └── TownSkeletonController（按 (JobId, FormId) 取，形态不同预制体不同）
/// </code>
/// 所以 <see cref="Acquire"/> 要取**两个**实例再组装起来。
///
/// 实例统一挂在场景里那个 <b>SkeletonCharacters</b> 节点下（靠 Tag 找，见
/// <see cref="RootTag"/>）—— 那是世界空间节点，城镇角色是世界空间 Spine，
/// 不能挂到 UI 的 Canvas 下面。
///
/// 对象池走工程内置的 <b>PoolManager</b>（PathologicalGames，AudioManager 也用它）：
/// 一个 <see cref="SpawnPool"/>，**每个预制体一个 PrefabPool** ——
/// 外层那个占一个池，每种形态的 Spine 各占一个池。
/// 外层是共用的，所以复用率很高；Spine 按形态分池，换形态时旧的还回自己的池。
/// </summary>
public sealed class TownCharacterSpawner
{
    /// <summary>场景里那个世界空间父节点的 Tag。</summary>
    public const string RootTag = "SkeletonCharacters";

    /// <summary>外层控制器预制体。所有角色共用，所以是常量而不是配置列。</summary>
    public const string ControllerPrefabKey =
        "Assets/AddressableAssets/Remote/Prefabs/Town/TownCharacterController.prefab";

    private const string PoolName = "TownCharacters";

    private Transform root;
    private SpawnPool pool;

    /// <summary>Addressable key → 预制体。外层那个和每种形态的 Spine 都在里面。</summary>
    private readonly Dictionary<string, Transform> prefabs = new Dictionary<string, Transform>();

    /// <summary>已经取出去的 Spine → 它是从哪个 key 来的。回收时要按 key 找回对应的池。</summary>
    private readonly Dictionary<TownSkeletonController, string> skeletonKeys =
        new Dictionary<TownSkeletonController, string>();

    public bool IsReady => pool != null;

    /// <summary>
    /// 建池子。找不到 <c>SkeletonCharacters</c> 节点就报错并返回 false ——
    /// 那说明场景里少了东西，静默失败的话表现成「城镇里一个人都没有」，很难查。
    /// </summary>
    public bool Initialize()
    {
        if (pool != null)
        {
            return true;
        }

        GameObject rootObject = FindRoot();

        if (rootObject == null)
        {
            Debug.LogError($"[TownSpawner] 场景里找不到 Tag 为 {RootTag} 的节点，城镇角色没地方挂");
            return false;
        }

        root = rootObject.transform;
        pool = PoolManager.Pools.Create(PoolName, rootObject);
        return true;
    }

    private static GameObject FindRoot()
    {
        try
        {
            return GameObject.FindGameObjectWithTag(RootTag);
        }
        catch (UnityException)
        {
            // Tag 没在 Project Settings 里登记时 FindGameObjectWithTag 会抛
            Debug.LogError($"[TownSpawner] Tag「{RootTag}」没在 Tags & Layers 里登记");
            return null;
        }
    }

    /// <summary>
    /// 取一个装好的城镇角色。
    ///
    /// 配置里没配 <c>SkeletonTown</c> 时**照样返回外层**（只是没有形象）——
    /// 名字之类的还得显示，而且这样「美术还没做那个形态」不会让人整个消失。
    /// </summary>
    public TownCharacterController Acquire(uint jobId, uint formId)
    {
        if (!Initialize())
        {
            return null;
        }

        Transform controllerPrefab = GetPrefab(ControllerPrefabKey);

        if (controllerPrefab == null)
        {
            return null;
        }

        Transform instance = pool.Spawn(controllerPrefab);

        if (instance == null)
        {
            Debug.LogError($"[TownSpawner] 池子取不出外层实例：{ControllerPrefabKey}");
            return null;
        }

        var controller = instance.GetComponent<TownCharacterController>();

        if (controller == null)
        {
            Debug.LogError($"[TownSpawner] {ControllerPrefabKey} 上没有 TownCharacterController 组件");
            pool.Despawn(instance);
            return null;
        }

        controller.Bind(AcquireSkeleton(jobId, formId));
        return controller;
    }

    /// <summary>
    /// 取一个 Spine。取不到返回 null —— 调用方要容忍（外层照样能用）。
    /// </summary>
    private TownSkeletonController AcquireSkeleton(uint jobId, uint formId)
    {
        var form = LubanManager.Instance.TbCharacterForm?.Get((int)jobId, (int)formId);

        if (form == null)
        {
            Debug.LogWarning($"[TownSpawner] 配置里没有 (JobId={jobId}, FormId={formId}) 这个形态");
            return null;
        }

        if (string.IsNullOrEmpty(form.SkeletonTown))
        {
            // 这个形态还没配城镇预制体。静默 —— 运行时反复刷日志没意义
            return null;
        }

        Transform prefab = GetPrefab(form.SkeletonTown);

        if (prefab == null)
        {
            return null;
        }

        Transform instance = pool.Spawn(prefab);

        if (instance == null)
        {
            Debug.LogError($"[TownSpawner] 池子取不出 Spine：{form.SkeletonTown}");
            return null;
        }

        var skeleton = instance.GetComponent<TownSkeletonController>();

        if (skeleton == null)
        {
            Debug.LogError($"[TownSpawner] {form.SkeletonTown} 上没有 TownSkeletonController 组件");
            pool.Despawn(instance);
            return null;
        }

        skeletonKeys[skeleton] = form.SkeletonTown;
        return skeleton;
    }

    /// <summary>把整只角色还回池子（外层 + 里面的 Spine）。</summary>
    public void Recycle(TownCharacterController controller)
    {
        if (controller == null || pool == null)
        {
            return;
        }

        // 先摘 Spine：不摘的话它会跟着外层一起被 Despawn 到池子根节点下，
        // 下次取外层时里面还挂着上一个形态
        TownSkeletonController skeleton = controller.Unbind();

        if (skeleton != null)
        {
            skeletonKeys.Remove(skeleton);
            pool.Despawn(skeleton.transform);
        }

        pool.Despawn(controller.transform);
    }

    /// <summary>
    /// 拆池子并把所有 AA 引用还掉。离开城镇（MainCommonUI 关闭）时调。
    ///
    /// ⚠️ 顺序不能反：先拆池子再还引用 —— 反过来的话池子里还留着已经卸掉的预制体，
    /// 下次 Spawn 会拿到空引用（AudioManager 那边踩的是同一类顺序问题）。
    /// </summary>
    public void Release()
    {
        skeletonKeys.Clear();

        if (pool != null)
        {
            PoolManager.Pools.Destroy(pool.poolName);
            pool = null;
        }

        foreach (string key in prefabs.Keys)
        {
            AssetsManager.Instance.FreeAsset(key);
        }

        prefabs.Clear();
        root = null;
    }

    private Transform GetPrefab(string key)
    {
        if (prefabs.TryGetValue(key, out Transform cached))
        {
            return cached;
        }

        var loaded = AssetsManager.Instance.LoadAssets<GameObject>(key);

        if (loaded == null)
        {
            Debug.LogError($"[TownSpawner] 预制体加载不出来：{key}");
            return null;
        }

        // 每个预制体一个 PrefabPool。preloadAmount 给 1：城镇里同一个形态
        // 至少有一个人（自己），多的按需生成
        pool.CreatePrefabPool(new PrefabPool(loaded.transform)
        {
            preloadAmount = 1,
        });

        prefabs[key] = loaded.transform;
        return loaded.transform;
    }
}
