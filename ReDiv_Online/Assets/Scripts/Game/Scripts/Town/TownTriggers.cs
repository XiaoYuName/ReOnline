using System.Collections.Generic;
using UnityEngine;
using XFramework;

/// <summary>
/// 城镇触发器的**纯数据查询 + 几何判定**（和 <see cref="TownGround"/> 一个路子：静态、
/// 不开物理、不持有状态）。数据源是配置表 <c>TbTownTrigger</c>（<c>TownTrigger.xlsx</c>）。
///
/// 一行一个**矩形触发区**，玩家走进去就生效：
/// <code>
/// Kind=1  传送  → TargetId 是**对端传送点的 TriggerId**（成对的传送阵）
///                 目标城镇 = 对端那个传送点的 TownId，走服务端 ChangeTown
///                 落点     = 对端那个传送点的位置 + 它自己的 ArriveOffset
/// Kind=2  副本  → TargetId 是副本组 id（副本还没设计，现在填 0）
/// </code>
///
/// **为什么目标城镇不单独存一列**：那样会出现「城镇写 B、对端传送阵却在 C」这种不一致，
/// 而且两处都得改。现在只有一个真相：对端是哪个传送点。
///
/// ⚠️ **不开物理**：角色身上没有 <c>Rigidbody2D</c> / <c>Collider2D</c>（见
/// <see cref="TownGround"/>），一个城镇的触发器也就个位数，所以判定就是拿脚下那一点
/// 做矩形包含 —— 比挂一堆 <c>OnTriggerEnter2D</c> 便宜，而且不受 FixedUpdate 节奏影响。
///
/// ⚠️ **触发器是纯客户端表现**：服务端不认识它们。「能不能去那个城镇」是服务端
/// <c>ChangeTown</c> 说的（改过的客户端可以对任意城镇调那个 Reducer），
/// 所以这里查出来的东西只决定「客户端要不要发起这次请求」，不是权限判断。
/// </summary>
public static class TownTriggers
{
    /// <summary>传送到别的城镇。</summary>
    public const int KindChangeTown = 1;

    /// <summary>打开副本界面。</summary>
    public const int KindDungeon = 2;

    /// <summary>
    /// 某个城镇的所有**有效**触发器，顺带把配错的报出来。
    ///
    /// 为什么每次现读而不缓存：一个城镇的触发器个位数，而这个方法只在
    /// 「换城镇 / 进城镇」时调一次（不是每帧）。缓存反而要管失效。
    ///
    /// 配错的行**跳过 + 报错**，不是静默忽略 —— 静默的话表现成「这个传送点踩了没反应」，
    /// 从现象根本看不出是配漏了（出生点和边界那边同样的处理，见 TownGroundController）。
    /// </summary>
    public static List<TownTrigger> InTown(uint townId)
    {
        var result = new List<TownTrigger>();

        if (townId == 0)
        {
            return result;
        }

        TbTownTrigger table = LubanManager.Instance.TbTownTrigger;

        if (table == null)
        {
            return result;
        }

        foreach (TownTrigger row in table.DataList)
        {
            if (row.TownId != townId)
            {
                continue;
            }

            if (!IsValid(row))
            {
                continue;
            }

            result.Add(row);
        }

        return result;
    }

    /// <summary>
    /// 这一点落在哪个触发器里。不在任何触发器里返回 null。
    ///
    /// 重叠时返回 <c>TriggerId</c> 最小的那个 —— 重叠本来就是配置问题，
    /// 但结果得**稳定**：随机挑一个的话表现成「有时传送有时开副本」。
    /// </summary>
    public static TownTrigger FindAt(IReadOnlyList<TownTrigger> triggers, Vector2 point)
    {
        TownTrigger best = null;

        for (int i = 0; i < triggers.Count; i++)
        {
            TownTrigger row = triggers[i];

            if (!Contains(row, point))
            {
                continue;
            }

            if (best == null || row.TriggerId < best.TriggerId)
            {
                best = row;
            }
        }

        return best;
    }

    /// <summary>点在不在这个触发器的矩形里。矩形以 (PosX, PosY) 为**中心**。</summary>
    public static bool Contains(TownTrigger row, Vector2 point)
    {
        if (row == null)
        {
            return false;
        }

        float halfWidth = row.Width * 0.5f;
        float halfHeight = row.Height * 0.5f;

        return point.x >= row.PosX - halfWidth && point.x <= row.PosX + halfWidth
            && point.y >= row.PosY - halfHeight && point.y <= row.PosY + halfHeight;
    }

    /// <summary>按 id 找一行。找不到返回 null（校验会报出来）。</summary>
    public static TownTrigger Find(int triggerId) =>
        triggerId <= 0 ? null : LubanManager.Instance.TbTownTrigger?.GetOrDefault(triggerId);

    /// <summary>
    /// 传送点的对端。只对 <see cref="KindChangeTown"/> 有意义，取不到返回 null。
    /// </summary>
    public static TownTrigger PairOf(TownTrigger row) =>
        row == null || row.Kind != KindChangeTown ? null : Find(row.TargetId);

    /// <summary>
    /// **从这个传送点出来时站的位置** = 它的中心 + 它自己的 ArriveOffset。
    ///
    /// 偏移是给「别直接又踩到这个传送点」用的。⚠️ 其实不给偏移也不会来回弹
    /// （落地时 <c>MainCommonUI.SyncCurrentTrigger</c> 会把「现在站在哪」当成初始状态、
    /// 不触发），但站在传送阵正中间说不清楚，而且玩家想马上回去还得先走出来。
    ///
    /// ⚠️ **偏移别指到可行走边界外面**：传送是直接写坐标、不走边界判定，
    /// 落在墙外之后移动的扫掠查询会从「已经嵌在碰撞体里」开始，可能一步都走不动。
    /// 用摆位窗口拖那个「出口点」，对着背景和边界看着摆。
    /// </summary>
    public static Vector2 ArrivePosition(TownTrigger row) =>
        new Vector2(row.PosX + row.ArriveOffsetX, row.PosY + row.ArriveOffsetY);

    /// <summary>触发器的矩形（世界坐标）。摆位窗口和 Gizmo 画框用。</summary>
    public static Rect RectOf(TownTrigger row) =>
        new Rect(row.PosX - row.Width * 0.5f, row.PosY - row.Height * 0.5f, row.Width, row.Height);

    /// <summary>
    /// 配置校验。**只在 <see cref="InTown"/> 里调**（换城镇时一次），所以可以放心报错。
    /// </summary>
    private static bool IsValid(TownTrigger row)
    {
        if (row.Width <= 0f || row.Height <= 0f)
        {
            Debug.LogError($"[TownTrigger] 触发器 {row.TriggerId}（{row.Name}）的宽高是 " +
                           $"{row.Width}×{row.Height}，踩不到。用「NPC / 触发器摆位」窗口改一下");
            return false;
        }

        switch (row.Kind)
        {
            case KindChangeTown:
                if (row.TargetId <= 0)
                {
                    Debug.LogError($"[TownTrigger] 传送点 {row.TriggerId}（{row.Name}）" +
                                   $"没连对端传送点（TargetId={row.TargetId}）");
                    return false;
                }

                if (row.TargetId == row.TriggerId)
                {
                    Debug.LogError($"[TownTrigger] 传送点 {row.TriggerId}（{row.Name}）连的是它自己");
                    return false;
                }

                TownTrigger pair = Find(row.TargetId);

                if (pair == null)
                {
                    Debug.LogError($"[TownTrigger] 传送点 {row.TriggerId}（{row.Name}）连的对端 " +
                                   $"{row.TargetId} 在表里不存在");
                    return false;
                }

                if (pair.Kind != KindChangeTown)
                {
                    Debug.LogError($"[TownTrigger] 传送点 {row.TriggerId}（{row.Name}）连的对端 " +
                                   $"{pair.TriggerId}（{pair.Name}）不是传送点（Kind={pair.Kind}）");
                    return false;
                }

                if (pair.TownId == row.TownId)
                {
                    Debug.LogError($"[TownTrigger] 传送点 {row.TriggerId}（{row.Name}）连的对端 " +
                                   $"{pair.TriggerId}（{pair.Name}）在同一个城镇 {row.TownId} 里 —— " +
                                   $"那不是传送，是原地挪位置");
                    return false;
                }

                if (LubanManager.Instance.TbTown?.GetOrDefault(pair.TownId) == null)
                {
                    Debug.LogError($"[TownTrigger] 传送点 {row.TriggerId}（{row.Name}）的对端在城镇 " +
                                   $"{pair.TownId}，而那个城镇不在 Town 表里");
                    return false;
                }

                return true;

            case KindDungeon:
                return true;

            default:
                Debug.LogError($"[TownTrigger] 触发器 {row.TriggerId}（{row.Name}）的 Kind={row.Kind} " +
                               $"不认识（1=传送 2=副本）");
                return false;
        }
    }
}
