using System.Linq;
using ReDiv.Server.Town;
using SpacetimeDB;

/// <summary>
/// 世界时间（早 / 中 / 晚三段）。
///
/// 一个定时 Reducer 每分钟重算一次时段，**只有段变了才写表** ——
/// 于是订阅者一天只收到 3 次推送，而不是每分钟一次。
///
/// 边界写在配置表 <c>TbTimeBand.StartHour</c> 里（用户 2026-08-25 定的：不写死），
/// 时区偏移是 <c>Module.ServerUtcOffsetHours</c> 常量。
/// </summary>
public static partial class Module
{
    /// <summary>定时器间隔。见 WorldTimeTimer 的注释：为什么是轮询而不是精确排点。</summary>
    private const int WorldTimeTickSeconds = 60;

    /// <summary>
    /// 定时重算时段。段没变就什么都不做（不写表 ⇒ 不推送）。
    ///
    /// 定时 Reducer 默认是私有的，客户端调不到，所以不用自己校验 sender。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void TickWorldTime(ReducerContext ctx, WorldTimeTimer timer)
    {
        // Debug 级别：每分钟一条，Info 会把日志刷满。要确认定时器活着就看这条
        Log.Debug("[WorldTime] tick");

        ApplyWorldTime(ctx);
    }

    /// <summary>
    /// 按当前时间算时段并写进 <c>WorldTime</c>。行不存在就插，段变了才更新。
    ///
    /// 抽出来是因为有三个调用点：Init（首次建库）、EnsureWorldTime（老库补齐）、
    /// 定时器。三处的语义完全一样，别各写一遍。
    /// </summary>
    private static void ApplyWorldTime(ReducerContext ctx)
    {
        uint overrideBandId = ctx.Db.WorldTimeControl.Id.Find(WorldTimeControlRowId)?.OverrideBandId ?? 0;
        uint bandId = overrideBandId is >= 1 and <= TownRules.BandCount
            ? overrideBandId
            : TownRules.CurrentBandId(
                ctx.Timestamp.MicrosecondsSinceUnixEpoch, ServerUtcOffsetHours);

        if (ctx.Db.WorldTime.Id.Find(WorldTimeRowId) is not { } row)
        {
            ctx.Db.WorldTime.Insert(new WorldTime
            {
                Id = WorldTimeRowId,
                BandId = bandId,
                ChangedAt = ctx.Timestamp,
            });

            Log.Info($"[WorldTime] 初始化时段 band={bandId}");
            return;
        }

        if (row.BandId == bandId)
        {
            return;
        }

        uint before = row.BandId;
        row.BandId = bandId;
        row.ChangedAt = ctx.Timestamp;
        ctx.Db.WorldTime.Id.Update(row);

        string mode = overrideBandId == 0 ? "自动" : "GM锁定";
        Log.Info($"[WorldTime] 时段切换 {before} -> {bandId}（{mode}）");
    }

    /// <summary>
    /// 立即按当前 GM 控制状态刷新公开时段行。
    ///
    /// 这个 Reducer 本身不修改 GM 控制状态，所以玩家即使调用也只能让服务端重新执行
    /// 同一套权威计算；真正的锁定值只存在私有表里，由数据库 owner 修改。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void RefreshWorldTime(ReducerContext ctx)
    {
        ApplyWorldTime(ctx);
    }

    /// <summary>
    /// 保证 <c>WorldTime</c> 那一行和定时器都在。
    ///
    /// ⚠️ 为什么不能只在 <c>Init</c> 里建：Init **只在首次 publish 或清库后跑一次**。
    /// 世界时间是往一个**已经有数据的库**上加的功能，那个库的 Init 早就跑过了 ——
    /// 只写在 Init 里的话，不清库就永远没有这一行。所以这里做成幂等的，
    /// 由 ClientConnected 兜一次。
    ///
    /// 幂等 + 极便宜（两次主键 Find），放在连接钩子里不会有性能问题。
    ///
    /// ⚠️ 它**只保证「行和定时器存在」，不重算时段** —— 重算是定时器的职责。
    /// 一开始这里也调了 ApplyWorldTime，结果每条新连接都会把时段算一遍；
    /// 而 `spacetime sql` / `spacetime call` 每次都是一条新连接，
    /// 于是「用 SQL 改 world_time 看客户端反应」这种调试手段当场失效
    /// （刚改完，下一条 CLI 命令的连接钩子就给算回去了）。实测踩过。
    ///
    /// ⚠️ ClientConnected 里抛异常会**拒绝连接**，所以这里绝不能抛 ——
    /// 配置缺失之类的问题让定时器去报，别让玩家连不进来。
    /// </summary>
    private static void EnsureWorldTime(ReducerContext ctx)
    {
        try
        {
            if (!ctx.Db.WorldTimeTimer.Iter().Any())
            {
                ctx.Db.WorldTimeTimer.Insert(new WorldTimeTimer
                {
                    ScheduledId = 0, // AutoInc 占位
                    ScheduledAt = new ScheduleAt.Interval(
                        new TimeDuration(WorldTimeTickSeconds * 1_000_000L)),
                });

                Log.Info($"[WorldTime] 已挂上定时器，每 {WorldTimeTickSeconds} 秒重算一次时段");
            }

            if (ctx.Db.WorldTimeControl.Id.Find(WorldTimeControlRowId) is null)
            {
                ctx.Db.WorldTimeControl.Insert(new WorldTimeControl
                {
                    Id = WorldTimeControlRowId,
                    OverrideBandId = 0,
                });

                Log.Info("[WorldTime] 已初始化 GM 时段控制：自动模式");
            }

            // 只在行不存在时建。已经有行了就交给定时器去维护
            if (ctx.Db.WorldTime.Id.Find(WorldTimeRowId) is null)
            {
                ApplyWorldTime(ctx);
            }
        }
        catch (System.Exception ex)
        {
            // 只记日志。这里抛出去等于把玩家挡在门外
            Log.Warn($"[WorldTime] 初始化失败（不影响连接）：{ex.Message}");
        }
    }

    /// <summary>
    /// 世界时间自检：把配置和当前算出来的时段打进日志，方便手动核对。
    /// 配置错了（段数不对 / StartHour 越界 / 重复）直接抛。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void WorldTimeSelfTest(ReducerContext ctx)
    {
        var bands = TownRules.SortedBands();

        if (bands.Count != TownRules.BandCount)
        {
            throw TownRules.Reject(
                $"时间段必须恰好 {TownRules.BandCount} 段（城镇表是三列背景），现在有 {bands.Count} 段");
        }

        var seenHours = new System.Collections.Generic.HashSet<int>();
        var seenIds = new System.Collections.Generic.HashSet<int>();

        foreach (var band in bands)
        {
            if (band.StartHour < 0 || band.StartHour > 23)
            {
                throw TownRules.Reject($"时段 {band.BandId} 的 StartHour={band.StartHour} 越界，必须是 0~23");
            }

            if (!seenHours.Add(band.StartHour))
            {
                throw TownRules.Reject($"有两个时段的 StartHour 都是 {band.StartHour}，边界必须互不相同");
            }

            if (!seenIds.Add(band.BandId))
            {
                throw TownRules.Reject($"时段 id {band.BandId} 重复了");
            }
        }

        int hour = TownRules.LocalHour(
            ctx.Timestamp.MicrosecondsSinceUnixEpoch, ServerUtcOffsetHours);
        uint current = TownRules.CurrentBandId(
            ctx.Timestamp.MicrosecondsSinceUnixEpoch, ServerUtcOffsetHours);

        string layout = string.Join(" / ", bands.Select(b => $"{b.BandId}:{b.StartHour}点起"));

        Log.Info($"[WorldTimeSelfTest] PASS 边界=[{layout}] " +
                 $"服务器本地时={hour}点(UTC+{ServerUtcOffsetHours}) 当前时段={current}");
    }
}
