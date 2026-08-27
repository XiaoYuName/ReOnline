using System.Linq;
using ReDiv.Server.Town;
using SpacetimeDB;

/// <summary>
/// 城镇：角色在哪个城镇，以及配置自检。
///
/// 位置有**两个写入口**，别在别处再写第三个：
///   <see cref="PlaceCharacter"/>  进游戏时落位，由 <c>SelectCharacter</c> 调用（不是 Reducer）
///   <see cref="ChangeTown"/>      玩家自己换城镇，由客户端踩到传送触发器时调用
///
/// ⚠️ **现在「所有城镇都能去」**（用户 2026-08-27 定的：只有一个真城镇，做复杂的解锁规则
/// 也验不出来）。要收紧解锁规则时**只改 <see cref="ChangeTown"/> 里那一处标了 TODO 的地方**，
/// 别把条件散到客户端或别的 Reducer —— 客户端那边的触发器是纯表现，改过的客户端
/// 可以对任意城镇调这个 Reducer，所以能不能去只能由服务端说。
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
    /// 玩家自己换城镇（客户端踩到传送触发器时调）。
    ///
    /// 三张表一起动，**漏一张就出 bug**：
    ///   CharacterLocation   权威位置，跨会话保留
    ///   CharacterSelection  公开投影，客户端和「谁在哪个城镇」的在线列表都看它
    ///   CharacterTransform  坐标行，**必须删掉**
    ///
    /// ⚠️ 坐标行为什么是删而不是改：它是按 <c>town_id</c> 订阅的，留着的话
    /// ①旧城镇的人会看到一个停在原地不动的「幽灵」直到我下次上报；
    /// ②新城镇的人会先收到我在**旧城镇坐标**上的那一帧。
    /// 删掉之后客户端进新城镇时会立刻在出生点上报一次（见 MainCommonUI），自然就补回来了。
    ///
    /// ⚠️ **不存坐标**：落点规则是「每次进城镇都站到出生点」（用户 2026-08-25 定的），
    /// 出生点在客户端那张背景预制体上，服务端一个字段都没有。
    ///
    /// 失败一律抛异常（和聊天一样、和 <c>UpdateTransform</c> 相反）：传送是玩家的一次
    /// 明确操作，失败了必须让他看到原因，不能静默吞掉。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void ChangeTown(ReducerContext ctx, uint townId)
    {
        ulong accountId = RequireAccountId(ctx);

        if (ctx.ConnectionId is not { } connectionId)
        {
            throw TownRules.Reject("这个操作只能由客户端连接发起");
        }

        // 鉴权靠「本连接有没有选角行」—— 那行是服务端写的，客户端伪造不了。
        // 顺带挡住了「还在选人界面就传送」
        if (ctx.Db.CharacterSelection.ConnectionId.Find(connectionId) is not { } selection)
        {
            throw TownRules.Reject("请先进入城镇");
        }

        if (!TownRules.TownExists(townId))
        {
            throw TownRules.Reject("目标城镇不存在");
        }

        // TODO 城镇解锁规则。现在是「都能去」（用户 2026-08-27 定的）。
        // 以后要按等级 / 进度收紧就加在这一处，别散到客户端 ——
        // 客户端的触发器是纯表现，改过的客户端能对任意城镇调这个 Reducer。

        // 已经在那儿了：幂等返回，不抛异常也不写表。
        // 客户端理论上不会发（踩到的触发器就在别的城镇里），但两边状态短暂不同步时会
        if (selection.TownId == townId)
        {
            return;
        }

        uint fromTownId = selection.TownId;

        // ① 权威位置
        if (ctx.Db.CharacterLocation.CharacterId.Find(selection.CharacterId) is { } location)
        {
            location.TownId = townId;
            location.EnteredAt = ctx.Timestamp;
            ctx.Db.CharacterLocation.CharacterId.Update(location);
        }
        else
        {
            // 正常走不到（进游戏时 PlaceCharacter 一定建过），但位置行被清过的话
            // 不该让玩家卡在原地 —— 补一行比抛异常有用
            ctx.Db.CharacterLocation.Insert(new CharacterLocation
            {
                CharacterId = selection.CharacterId,
                AccountId = accountId,
                TownId = townId,
                EnteredAt = ctx.Timestamp,
            });
        }

        // ② 公开投影
        selection.TownId = townId;
        selection.EnteredAt = ctx.Timestamp;
        ctx.Db.CharacterSelection.ConnectionId.Update(selection);

        // ③ 坐标行（见上面的注释，必须删）
        ctx.Db.CharacterTransform.ConnectionId.Delete(connectionId);

        Log.Info($"[Town] 角色 {selection.CharacterId} 从城镇 {fromTownId} 传送到 {townId}");
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
