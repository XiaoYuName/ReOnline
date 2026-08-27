using System;
using System.Collections.Generic;
using UnityEngine;
using XFramework;

/// <summary>
/// 副本的**通关进度** —— 「这个角色在这个副本上最高打过几星」。
/// 星级解锁靠它：**打过 N 星才能选 N+1 星**（用户 2026-08-27 定的，像 DNF 的难度递进）。
///
/// ⚠️⚠️ **这是本地占位实现，权威以后在服务端。**
/// 本轮（2026-08-27）副本是**纯客户端**的：没有战斗、没有结算，也就没有「谁来写通关记录」。
/// 所以进度暂时存在 PlayerPrefs 里，只为了让界面能真的跑起来、能试玩。
/// 改过存档的玩家可以把星级全解开 —— **这不是漏洞，是本轮就没有服务端那一半**。
///
/// 等副本结算做完，把这里换成服务端表（角色 × 副本 × 最高通关星级）的 View：
/// <list type="bullet">
///   <item>只改这一个类的内部实现，<see cref="MaxClearedStar"/> / <see cref="MaxSelectableStar"/>
///         这两个签名不用动，界面代码一行都不用改；</item>
///   <item>那时 <c>Dungeon.MaxStar</c> 这一列要开给服务端（现在整表 <c>group=c</c>），
///         否则服务端没法校验「你选的星级超没超上限」。</item>
/// </list>
///
/// 存的是**每个角色各一份**（key 里带 CharacterId）—— 一个账号多个角色，进度是角色级的。
/// </summary>
public static class DungeonProgress
{
    /// <summary>PlayerPrefs 的 key 前缀。带角色 id，见类注释。</summary>
    private const string KeyPrefix = "ReDiv.DungeonProgress.";

    /// <summary>进度变了（通关 / 调试改过）。界面听这个重画星级。</summary>
    public static event Action Changed;

    /// <summary>
    /// 本地缓存，省掉每帧读 PlayerPrefs。key 是 <c>(characterId, dungeonId)</c>。
    /// 换角色不用清 —— key 里带着 characterId，本来就不会串。
    /// </summary>
    private static readonly Dictionary<(ulong, int), int> cache = new Dictionary<(ulong, int), int>();

    /// <summary>
    /// 这个副本最高打过几星。0 = 没打过。
    ///
    /// 没有角色（还没进游戏）时返回 0：那时界面本来也不该开。
    /// </summary>
    public static int MaxClearedStar(int dungeonId)
    {
        ulong characterId = CurrentCharacterId;

        if (characterId == 0 || dungeonId <= 0)
        {
            return 0;
        }

        var key = (characterId, dungeonId);

        if (cache.TryGetValue(key, out int cached))
        {
            return cached;
        }

        int value = PlayerPrefs.GetInt(PrefsKey(characterId, dungeonId), 0);
        cache[key] = value;
        return value;
    }

    /// <summary>
    /// 这个副本**现在最高能选几星**。
    ///
    /// 规则：`已通关最高星 + 1`，但不超过配置的 <c>MaxStar</c>，也至少是 1
    /// （没打过的副本也得能打 1 星，不然永远进不去）。
    /// </summary>
    public static int MaxSelectableStar(Dungeon config)
    {
        if (config == null)
        {
            return 1;
        }

        int configMax = Mathf.Max(1, config.MaxStar);
        return Mathf.Clamp(MaxClearedStar(config.DungeonId) + 1, 1, configMax);
    }

    /// <summary>
    /// 记一次通关。**以后由副本结算调**（现在没有结算，所以只有调试入口会调）。
    ///
    /// 只往上记：拿低星去打不该把高星进度冲掉。
    /// </summary>
    public static void MarkCleared(int dungeonId, int star)
    {
        ulong characterId = CurrentCharacterId;

        if (characterId == 0 || dungeonId <= 0 || star <= 0)
        {
            return;
        }

        if (star <= MaxClearedStar(dungeonId))
        {
            return;
        }

        Write(characterId, dungeonId, star);
    }

    /// <summary>
    /// **调试用**：直接把某个副本的通关星级设成任意值（可以往下改，也可以填 0 清掉）。
    ///
    /// 本轮没有战斗，所以想试「星级解锁」只能靠它。用法：
    /// <code>
    /// unity command eval --code 'DungeonProgress.DebugSetCleared(31006, 5); return "ok";'
    /// </code>
    /// </summary>
    public static void DebugSetCleared(int dungeonId, int star)
    {
        ulong characterId = CurrentCharacterId;

        if (characterId == 0)
        {
            Debug.LogWarning("[DungeonProgress] 还没进游戏（没有当前角色），改不了进度");
            return;
        }

        Write(characterId, dungeonId, Mathf.Max(0, star));
    }

    /// <summary>**调试用**：把当前角色的所有副本进度清空。</summary>
    public static void DebugClearAll()
    {
        ulong characterId = CurrentCharacterId;

        if (characterId == 0)
        {
            return;
        }

        Dungeon[] all = AllDungeons();

        foreach (Dungeon row in all)
        {
            Write(characterId, row.DungeonId, 0);
        }

        Debug.Log($"[DungeonProgress] 已清掉角色 {characterId} 的 {all.Length} 个副本进度");
    }

    private static void Write(ulong characterId, int dungeonId, int star)
    {
        if (star <= 0)
        {
            PlayerPrefs.DeleteKey(PrefsKey(characterId, dungeonId));
        }
        else
        {
            PlayerPrefs.SetInt(PrefsKey(characterId, dungeonId), star);
        }

        cache[(characterId, dungeonId)] = star;
        PlayerPrefs.Save();

        Changed?.Invoke();
    }

    private static string PrefsKey(ulong characterId, int dungeonId) =>
        $"{KeyPrefix}{characterId}.{dungeonId}";

    /// <summary>当前在玩哪个角色。进度是角色级的，所以 key 里要带它。</summary>
    private static ulong CurrentCharacterId => ReDiv.Net.TownManager.Instance.CurrentCharacterId;

    private static Dungeon[] AllDungeons()
    {
        TbDungeon table = LubanManager.Instance.TbDungeon;

        if (table == null)
        {
            return Array.Empty<Dungeon>();
        }

        var list = new List<Dungeon>(table.DataList);
        return list.ToArray();
    }
}
