using System.Linq;
using ReDiv.Server;
using ReDiv.Server.Town;
using SpacetimeDB;

/// <summary>
/// 城镇里玩家状态相关的 Reducer：坐标上报、体力每日重置、钱包。
/// </summary>
public static partial class Module
{
    /// <summary>
    /// 上报自己在城镇里的坐标。**客户端每 ~100ms 且位置真的变了才发**。
    ///
    /// 服务端**只转发**：不校验速度，不算移动（用户 2026-08-25 定的模型）。
    /// 所以这个 Reducer 要尽量便宜 —— 它是整个模块里调用最频繁的一个。
    ///
    /// 没有选角行（还没进城镇）就直接返回**而不是抛异常**：
    /// 玩家点「返回选人界面」的瞬间客户端可能还有一两个包在路上，
    /// 那不是错误，抛异常只会在日志里刷一堆没用的东西。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void UpdateTransform(ReducerContext ctx, float x, float y, int facing, bool moving)
    {
        if (ctx.ConnectionId is not { } connectionId)
        {
            return;
        }

        // 必须已经选了角色才有坐标可言。这里顺便也就完成了鉴权 ——
        // 选角行是服务端写的，客户端伪造不了
        if (ctx.Db.CharacterSelection.ConnectionId.Find(connectionId) is not { } selection)
        {
            return;
        }

        // 朝向只认 ±1，别让客户端塞任意值进来（收到的人拿它翻 scale.x）
        int normalizedFacing = facing >= 0 ? 1 : -1;

        if (ctx.Db.CharacterTransform.ConnectionId.Find(connectionId) is { } existing)
        {
            existing.TownId = selection.TownId;
            existing.CharacterId = selection.CharacterId;
            existing.X = x;
            existing.Y = y;
            existing.Facing = normalizedFacing;
            existing.Moving = moving;
            existing.UpdatedAt = ctx.Timestamp;
            ctx.Db.CharacterTransform.ConnectionId.Update(existing);
            return;
        }

        ctx.Db.CharacterTransform.Insert(new CharacterTransform
        {
            ConnectionId = connectionId,
            TownId = selection.TownId,
            CharacterId = selection.CharacterId,
            Identity = ctx.Sender,
            X = x,
            Y = y,
            Facing = normalizedFacing,
            Moving = moving,
            UpdatedAt = ctx.Timestamp,
        });
    }

    /// <summary>
    /// 清掉某条连接的坐标行。连接断开 / 返回选人界面时调 ——
    /// 不清的话城镇里会留下一个永远不动的"幽灵"。
    /// </summary>
    private static void ClearTransformOnDisconnect(ReducerContext ctx)
    {
        if (ctx.ConnectionId is { } connectionId)
        {
            ctx.Db.CharacterTransform.ConnectionId.Delete(connectionId);
        }
    }

    // ------------------------------------------------------------------
    // 体力
    // ------------------------------------------------------------------

    /// <summary>
    /// 体力**每日重置**（用户 2026-08-25 定的，像 DNF 疲劳值）。
    /// 上限按等级查配置 <c>TbLevelExp.MaxStamina</c>。
    ///
    /// 判定用「服务器本地日」，和时段共用同一个时区偏移
    /// （<see cref="ServerUtcOffsetHours"/>）—— 两套时区口径迟早对不上。
    ///
    /// 返回值是「有没有真的改过这一行」，调用方据此决定要不要 Update。
    /// </summary>
    private static bool RefreshStamina(ReducerContext ctx, ref Character character)
    {
        int today = TownRules.LocalDayNumber(
            ctx.Timestamp.MicrosecondsSinceUnixEpoch, ServerUtcOffsetHours);

        uint maxStamina = TownRules.MaxStaminaOf(character.Level);

        if (character.StaminaDay == today)
        {
            // 同一天内不重置，但上限可能因为升级变大了 —— 这时候不补满，
            // 只是别让当前值超过新上限（降级不存在，所以基本走不到）
            if (character.Stamina <= maxStamina)
            {
                return false;
            }

            character.Stamina = maxStamina;
            return true;
        }

        character.Stamina = maxStamina;
        character.StaminaDay = today;
        return true;
    }

    /// <summary>
    /// 保证某个角色的体力是当天的。给「读体力之前」用（进城镇、以后进副本扣体力）。
    /// </summary>
    private static void EnsureStaminaFresh(ReducerContext ctx, ulong characterId)
    {
        if (ctx.Db.Character.CharacterId.Find(characterId) is not { } character)
        {
            return;
        }

        if (RefreshStamina(ctx, ref character))
        {
            ctx.Db.Character.CharacterId.Update(character);
        }
    }

    /// <summary>
    /// 给**当前在线**的角色刷一遍体力。由世界时间的定时器每分钟带一次 ——
    /// 玩家挂在城镇里跨过零点时也能立刻回满。
    ///
    /// 只扫在线的（`CharacterSelection` 一条连接一行，量很小），
    /// **不扫全表** —— 那是每分钟一次的 O(全部角色)，随账号数线性变差。
    /// 离线角色靠进城镇时的 <see cref="EnsureStaminaFresh"/> 惰性补。
    /// </summary>
    private static void RefreshStaminaForOnline(ReducerContext ctx)
    {
        foreach (var selection in ctx.Db.CharacterSelection.Iter().ToList())
        {
            EnsureStaminaFresh(ctx, selection.CharacterId);
        }
    }

    // ------------------------------------------------------------------
    // 钱包
    // ------------------------------------------------------------------

    /// <summary>
    /// 保证账号有钱包行。<c>Register</c> 时就建，但**老账号是在这个功能之前注册的**，
    /// 所以进城镇时也兜一次（和世界时间那一行同一个道理：Init / Register 只跑一次，
    /// 功能是后加的）。
    /// </summary>
    private static void EnsureWallet(ReducerContext ctx, ulong accountId)
    {
        if (ctx.Db.AccountWallet.AccountId.Find(accountId) is not null)
        {
            return;
        }

        ctx.Db.AccountWallet.Insert(new AccountWallet
        {
            AccountId = accountId,
            Coin = StartingCoin,
            Gem = StartingGem,
        });

        Log.Info($"[Wallet] 账号 {accountId} 建了钱包 coin={StartingCoin} gem={StartingGem}");
    }

    /// <summary>新账号的初始金币。**占位数值**，等经济系统定了再说。</summary>
    public const ulong StartingCoin = 0;

    /// <summary>新账号的初始钻石。**占位数值**。</summary>
    public const ulong StartingGem = 0;
}
