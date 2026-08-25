using System.Linq;
using ReDiv.Server.Town;
using SpacetimeDB;

/// <summary>
/// 城镇：角色在哪个城镇，以及配置自检。
///
/// 位置的**写入口只有一处** —— <see cref="PlaceCharacter"/>，由 <c>SelectCharacter</c>
/// （进入游戏）调用。玩家还没有「自己在城镇之间走」的入口，那需要先定清楚
/// 城镇怎么解锁 / 能不能随便去，玩法没定型之前不开这个 Reducer。
/// </summary>
public static partial class Module
{
    /// <summary>
    /// 把角色放进它该在的城镇，并返回城镇 id。
    ///
    /// 已经有位置行就沿用（顺手刷 EnteredAt）；没有就落到配置里的初始城镇 ——
    /// 也就是说**新角色第一次进游戏时才建位置行**，而不是建角色时建。
    /// 这样「建了但从没进过游戏」的角色不会占一行玩法态数据。
    ///
    /// 配置里那个城镇被删掉了（老存档指向不存在的城镇）也退回初始城镇，
    /// 而不是抛错 —— 让玩家能进游戏比精确保留位置重要。
    /// </summary>
    private static uint PlaceCharacter(ReducerContext ctx, ulong accountId, ulong characterId)
    {
        uint startTownId = TownRules.StartTownId();

        if (ctx.Db.CharacterLocation.CharacterId.Find(characterId) is { } location)
        {
            if (TownRules.TownExists(location.TownId))
            {
                location.EnteredAt = ctx.Timestamp;
                ctx.Db.CharacterLocation.CharacterId.Update(location);
                return location.TownId;
            }

            Log.Warn($"[Town] 角色 {characterId} 记录的城镇 {location.TownId} 不在配置里了，" +
                     $"退回初始城镇 {startTownId}");

            location.TownId = startTownId;
            location.EnteredAt = ctx.Timestamp;
            ctx.Db.CharacterLocation.CharacterId.Update(location);
            return startTownId;
        }

        ctx.Db.CharacterLocation.Insert(new CharacterLocation
        {
            CharacterId = characterId,
            AccountId = accountId,
            TownId = startTownId,
            EnteredAt = ctx.Timestamp,
        });

        Log.Info($"[Town] 角色 {characterId} 首次进入游戏，落在初始城镇 {startTownId}");
        return startTownId;
    }

    /// <summary>
    /// 城镇配置自检。改完 Town.xlsx 跑一次 —— 两张表之间没有任何编译期检查，
    /// 配错了只表现成「进不了游戏」或「客户端没背景」。
    ///
    /// ⚠️ 查不了背景控制器的 Addressable 路径对不对：那三列是 <c>group="c"</c>，
    /// 服务端根本看不到。资源那半边只能靠客户端跑起来才知道。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void TownConfigSelfTest(ReducerContext ctx)
    {
        var towns = ReDiv.Server.ServerConfig.Tables.TbTown.DataList;

        if (towns.Count == 0)
        {
            throw TownRules.Reject("城镇表是空的");
        }

        // 恰好一个初始城镇 —— StartTownId 自己就会在不满足时抛
        uint startTownId = TownRules.StartTownId();

        var seen = new System.Collections.Generic.HashSet<int>();
        foreach (var town in towns)
        {
            if (town.TownId <= 0)
            {
                throw TownRules.Reject($"城镇 id 必须是正数，现在有一个 {town.TownId}");
            }

            if (!seen.Add(town.TownId))
            {
                throw TownRules.Reject($"城镇 id {town.TownId} 重复了");
            }
        }

        // 位置行指向的城镇还在不在配置里。指向不存在的城镇不会让玩家卡住
        //（PlaceCharacter 会退回初始城镇），但配置删错了要能一眼看见
        int dangling = ctx.Db.CharacterLocation.Iter()
            .Count(l => !TownRules.TownExists(l.TownId));

        string ids = string.Join(", ", towns.Select(t => t.TownId));

        Log.Info($"[TownConfigSelfTest] PASS 城镇 {towns.Count} 个 [{ids}]，" +
                 $"初始城镇={startTownId}，位置行指向失效城镇的有 {dangling} 条");
    }
}
