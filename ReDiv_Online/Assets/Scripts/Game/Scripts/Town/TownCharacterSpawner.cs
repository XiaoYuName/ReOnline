using System.Collections.Generic;
using PathologicalGames;
using UnityEngine;
using XFramework;

/// <summary>
/// 城镇角色的取用与回收。<see cref="MainCommonUI"/> 拥有它一份，自己不订阅任何东西。
///
/// 实例统一挂在场景里那个 <b>SkeletonCharacters</b> 节点下（靠 Tag 找，见
/// <see cref="RootTag"/>）—— 那是世界空间节点，城镇角色是世界空间 Spine，
/// 不能挂到 UI 的 Canvas 下面。
///
/// 对象池走工程内置的 <b>PoolManager</b>（PathologicalGames，AudioManager 也用它）：
/// 一个 <see cref="SpawnPool"/>，**每个城镇预制体一个 PrefabPool**。
/// 城镇里同一个角色可能有好几个人在用（都是凯露），池化才不会反复 Instantiate。
///
/// ⚠️ PrefabPool 要的是**预制体的 Transform**，所以预制体得先经 AssetsManager 加载出来。
/// 每个 key 只加载一次，缓存在 <see cref="prefabs"/> 里；<see cref="Release"/>
/// 时把 AA 引用一起还掉。
/// </summary>
public sealed class TownCharacterSpawner
{
    /// <summary>场景里那个世界空间父节点的 Tag。</summary>
    public const string RootTag = "SkeletonCharacters";

    private const string PoolName = "TownCharacters";

    private Transform root;
    private SpawnPool pool;

    /// <summary>Addressable key → 预制体。key 是配置表 <c>CharacterForm.SkeletonTown</c> 那一列。</summary>
    private readonly Dictionary<string, Transform> prefabs = new Dictionary<string, Transform>();

    /// <summary>已经取出去的实例 → 它是从哪个 key 来的。回收时要按 key 找回对应的池。</summary>
    private readonly Dictionary<TownSkeletonController, string> spawned =
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
    /// 按 (JobId, FormId) 取一个城镇角色实例。配置里没配 <c>SkeletonTown</c> 就返回 null
    /// （不当错误 —— 美术可能还没做那个形态的城镇预制体）。
    /// </summary>
    public TownSkeletonController Acquire(uint jobId, uint formId)
    {
        if (!Initialize())
        {
            return null;
        }

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
            Debug.LogError($"[TownSpawner] 池子取不出实例：{form.SkeletonTown}");
            return null;
        }

        var controller = instance.GetComponent<TownSkeletonController>();

        if (controller == null)
        {
            Debug.LogError($"[TownSpawner] {form.SkeletonTown} 上没有 TownSkeletonController 组件");
            pool.Despawn(instance);
            return null;
        }

        controller.Initialize();
        spawned[controller] = form.SkeletonTown;
        return controller;
    }

    /// <summary>把实例还回池子。</summary>
    public void Recycle(TownSkeletonController controller)
    {
        if (controller == null || pool == null)
        {
            return;
        }

        spawned.Remove(controller);
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
        spawned.Clear();

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
            Debug.LogError($"[TownSpawner] 城镇角色预制体加载不出来：{key}");
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
