using System.Linq;
using ReDiv.Server.Chat;
using SpacetimeDB;

/// <summary>
/// 聊天消息的 Reducer。
///
/// 两个入口 <see cref="SendNearbyMessage"/> / <see cref="SendWorldMessage"/>，
/// **区别只有一个：写进哪个域**（附近 = 自己所在城镇，世界 = <see cref="ChatWorldScopeTownId"/>）。
/// 鉴权、正文清洗、冷却、滚动裁剪全部共用 <see cref="InsertChatMessage"/> ——
/// 以后加队伍 / 公会频道也照这个形状加一个薄壳子，别再另起一套。
///
/// ⚠️ **冷却是跨频道共享的**（按发送者算，不分频道）：不然「附近发一条、世界发一条」
/// 就能把 1 秒的限流翻倍，等于没限。
/// </summary>
public static partial class Module
{
    /// <summary>
    /// 发一条**附近消息**：只有和自己在同一个城镇的人收得到。
    ///
    /// 「在哪个城镇」**从服务端的选角行读**，不收客户端参数 ——
    /// 那一行是 <c>SelectCharacter</c> 写的，客户端伪造不了。
    /// 让客户端传 townId 的话，改过的客户端就能往任意城镇喊话。
    /// 同理发送者的名字也取选角行里的，不信客户端报上来的。
    ///
    /// ⚠️ 没有选角行时**抛异常而不是静默返回**（和 <c>UpdateTransform</c> 相反）：
    /// 坐标上报是「尽力而为」的状态同步，丢一个包无所谓；
    /// 发消息是玩家的一次明确操作，失败了必须让他看到「怎么没发出去」。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void SendNearbyMessage(ReducerContext ctx, string content)
    {
        var selection = RequireChatSender(ctx);

        // 域 = 自己所在城镇 ⇒ 只有同城镇的人订阅得到
        InsertChatMessage(ctx, selection, ChatChannelNearby, selection.TownId, content);
    }

    /// <summary>
    /// 发一条**世界消息**：所有人在任何地方都收得到。
    ///
    /// 和 <see cref="SendNearbyMessage"/> 唯一的区别是域填
    /// <see cref="ChatWorldScopeTownId"/>（0，一个不存在的城镇 id）——
    /// 于是客户端订阅它的 SQL 和附近是同一个形状，只是数字不同。
    ///
    /// 鉴权照旧要「本连接已经选了角」：世界频道虽然和城镇无关，但发言人得有名字有头像，
    /// 那些都在选角行上。顺带也就挡住了「还在选人界面就往全服喊话」。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void SendWorldMessage(ReducerContext ctx, string content)
    {
        var selection = RequireChatSender(ctx);

        InsertChatMessage(ctx, selection, ChatChannelWorld, ChatWorldScopeTownId, content);
    }

    // ------------------------------------------------------------------
    // 内部
    // ------------------------------------------------------------------

    /// <summary>
    /// 取「谁在说话」。选角行同时完成三件事：鉴权（有它就说明登录过且选了角）、
    /// 给出附近消息的可见域（TownId）、给出显示用的角色名和形象（名字 / 职业 / 形态）。
    ///
    /// ⚠️ 没有选角行时**抛异常而不是静默返回**（和 <c>UpdateTransform</c> 相反）：
    /// 坐标上报是「尽力而为」的状态同步，丢一个包无所谓；
    /// 发消息是玩家的一次明确操作，失败了必须让他看到「怎么没发出去」。
    /// </summary>
    private static CharacterSelection RequireChatSender(ReducerContext ctx)
    {
        if (ctx.ConnectionId is not { } connectionId)
        {
            throw ChatRules.Reject("这个操作只能由客户端连接发起");
        }

        if (ctx.Db.CharacterSelection.ConnectionId.Find(connectionId) is not { } selection)
        {
            throw ChatRules.Reject("请先进入城镇再发言");
        }

        return selection;
    }

    /// <summary>
    /// 两个频道共用的写入路径：清洗 → 查冷却 → 插入 → 裁剪。
    ///
    /// **发送者的名字 / 职业 / 形态全部取自选角行，不收客户端参数** ——
    /// 那一行是 <c>SelectCharacter</c> 写的，客户端伪造不了。让客户端传的话，
    /// 改过的客户端就能冒充别人说话。同理 <paramref name="townId"/> 也是调用方
    /// 从选角行算出来的，不是参数传进来的。
    /// </summary>
    private static void InsertChatMessage(
        ReducerContext ctx, CharacterSelection selection, uint channel, uint townId, string content)
    {
        // ⚠️ 先清洗、后查冷却。反过来的话，一条本来就不合法的消息
        // （空的 / 超长的）也会把冷却算上，玩家改完再发还得再等一秒
        string text = ChatRules.NormalizeContent(content);

        RequireChatCooldownPassed(ctx, selection.CharacterId);

        ctx.Db.ChatMessage.Insert(new ChatMessage
        {
            Channel = channel,
            TownId = townId,
            SenderCharacterId = selection.CharacterId,
            SenderName = selection.CharacterName,
            Content = text,
            SentAt = ctx.Timestamp,
            SenderJobId = selection.JobId,
            SenderFormId = selection.FormId,
        });

        TrimChatScope(ctx, townId);
    }

    /// <summary>
    /// 冷却检查：同一个角色两条消息之间至少隔 <see cref="ChatCooldownMicros"/>。
    ///
    /// **不另开一张「上次发言时间」表** —— 消息本身就是那个记录。
    /// 按发送者索引 Filter 一遍找最大的 <c>SentAt</c>，扫的行数上限是
    /// <see cref="ChatHistoryPerScope"/>（他自己在保留窗口内的消息，通常远少于这个数）。
    ///
    /// 裁剪把他的消息全删干净了 ⇒ 找不到任何一条 ⇒ 放行。这是**有意的**：
    /// 那说明这个域里已经过去了 50 条消息，冷却早就该过了。
    ///
    /// ⚠️ 「当前时间」只能取 <c>ctx.Timestamp</c>，不能碰挂钟 ——
    /// Reducer 必须确定性（事务可能被重放）。
    /// </summary>
    private static void RequireChatCooldownPassed(ReducerContext ctx, ulong characterId)
    {
        long newest = 0;

        foreach (var row in ctx.Db.ChatMessage.SenderCharacterId.Filter(characterId))
        {
            long at = row.SentAt.MicrosecondsSinceUnixEpoch;

            if (at > newest)
            {
                newest = at;
            }
        }

        if (newest == 0)
        {
            return;
        }

        long elapsed = ctx.Timestamp.MicrosecondsSinceUnixEpoch - newest;

        if (elapsed < ChatCooldownMicros)
        {
            throw ChatRules.Reject("说话太快了，稍等一下再发");
        }
    }

    /// <summary>
    /// 把一个域裁到 <see cref="ChatHistoryPerScope"/> 条以内，删最旧的。
    ///
    /// 「最旧」按 <c>(SentAt, MessageId)</c> 判定，和客户端的显示顺序**用同一把尺子** ——
    /// 两边排序口径不一致的话，会出现「客户端认为还该显示的那条被服务端当成最旧删了」。
    /// 为什么不能只按 MessageId 排，见 <c>ChatMessage.MessageId</c> 的注释。
    ///
    /// ⚠️ Filter 返回的是枚举器，**必须先 <c>ToList()</c>** 再排序和删除：
    /// 一边枚举一边删同一张表是自找麻烦。
    /// </summary>
    private static void TrimChatScope(ReducerContext ctx, uint townId)
    {
        var rows = ctx.Db.ChatMessage.TownId.Filter(townId).ToList();

        int extra = rows.Count - ChatHistoryPerScope;

        if (extra <= 0)
        {
            return;
        }

        rows.Sort((a, b) =>
        {
            int byTime = a.SentAt.MicrosecondsSinceUnixEpoch.CompareTo(b.SentAt.MicrosecondsSinceUnixEpoch);
            return byTime != 0 ? byTime : a.MessageId.CompareTo(b.MessageId);
        });

        for (int i = 0; i < extra; i++)
        {
            ctx.Db.ChatMessage.MessageId.Delete(rows[i].MessageId);
        }
    }
}
