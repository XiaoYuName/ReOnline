using System.Linq;
using ReDiv.Server;
using ReDiv.Server.Character;
using SpacetimeDB;

/// <summary>
/// 角色系统的 Reducer：建角色 / 删角色 / 选角色 / 返回选人界面。
///
/// 对外五个入口（客户端绑定是 PascalCase，CLI 是 snake_case）：
///   CheckCharacterName(name)      check_character_name  —— 建角色前查重，**不写库**
///   CreateCharacter(name, jobId)  create_character
///   DeleteCharacter(characterId)  delete_character   —— 软删
///   SelectCharacter(characterId)  select_character   —— 选完才算进城镇
///   LeaveCharacter()              leave_character    —— 回选人界面
///
/// 觉醒（AwakenCharacter / awaken_character）在 CharacterForms.cs 里。
///
/// 鉴权一律走**当前连接的 Session 行**（RequireAccountId）：必须有活会话才能动角色数据。
/// 这比用 IdentityBinding 严 —— 绑定只说明「这台设备登录过」，而会话说明「现在登录着」。
/// 读列表那边（View）用的是 IdentityBinding，因为 ViewContext 没有连接概念；
/// 读宽松、写严格，这个不对称是有意的。
/// </summary>
public static partial class Module
{
    // ------------------------------------------------------------------
    // 对外 Reducer
    // ------------------------------------------------------------------

    /// <summary>
    /// 角色名查重 —— 创建角色界面的「重复」按钮用，玩家点了才发，不是每敲一个字就发。
    ///
    /// **一张表都不写。** 结果靠 Reducer 自己的执行状态回给调用方：
    ///   名字可用   → 正常返回 ⇒ 客户端收到 <c>Status.Committed</c>
    ///   格式不合法 / 已被占用 → 抛异常 ⇒ 客户端在 <c>Status.Failed(reason)</c> 里拿到中文原文
    /// 版本校验 <c>CheckVersion</c> 就是这个形状 —— Reducer 不返回数据，
    /// 所以「问一句 yes/no」这类需求走执行状态，不用为它专门开事件表。
    ///
    /// 要求有活会话：不给未登录的连接当「名字探测器」用。
    ///
    /// ⚠️ 查重结果**没有任何保留效果**。查完到真正 <c>CreateCharacter</c> 之间，
    /// 名字随时可能被别人抢走 —— 所以 CreateCharacter 自己也照查一次，
    /// 这里只是让玩家早点知道，不是前置校验。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void CheckCharacterName(ReducerContext ctx, string name)
    {
        RequireAccountId(ctx);
        RequireNameAvailable(ctx, CharacterRules.NormalizeName(name));
    }

    /// <summary>
    /// 创建角色。校验顺序是从便宜到贵：会话 → 名字格式 → 栏位 → 重名 → 职业配置。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void CreateCharacter(ReducerContext ctx, string name, uint jobId)
    {
        ulong accountId = RequireAccountId(ctx);
        string nameKey = CharacterRules.NormalizeName(name);

        if (ctx.Db.Account.AccountId.Find(accountId) is not { } account)
        {
            throw CharacterRules.Reject("账号数据异常，请重新登录");
        }

        uint used = CountAliveCharacters(ctx, accountId);
        if (used >= account.CharacterSlots)
        {
            throw CharacterRules.Reject($"角色栏位已满（{used}/{account.CharacterSlots}）");
        }

        RequireNameAvailable(ctx, nameKey);

        var job = ServerConfig.Tables.TbCharacterJob.GetOrDefault((int)jobId);
        if (job == null)
        {
            throw CharacterRules.Reject("职业不存在");
        }
        if (!job.Creatable)
        {
            throw CharacterRules.Reject("这个职业暂时不能创建");
        }

        // 初始星级来自职业配置，配置写错了（基础线接不住这个星级）在这里就拒绝，
        // 别让角色建出来取不到形象
        uint star = RequireStartStar(job);

        var inserted = ctx.Db.Character.Insert(new Character
        {
            CharacterId = 0, // AutoInc 占位
            AccountId = accountId,
            NameKey = nameKey,
            Name = name.Trim(),
            JobId = jobId,
            Level = (uint)job.StartLevel,
            Exp = 0,
            CreatedAt = ctx.Timestamp,
            LastPlayedAt = null,
            DeletedAt = null,
            Star = star,
            // 建角色就给满体力。StaminaDay = 0 会让第一次进城镇时再走一次重置，
            // 保证「今天」这个字段也对上
            Stamina = ReDiv.Server.Town.TownRules.MaxStaminaOf((uint)job.StartLevel),
            StaminaDay = 0,
        });

        Log.Info($"[Character] 创建角色 character={inserted.CharacterId} name={inserted.Name} " +
                 $"job={jobId} star={star} account={accountId}");
    }

    /// <summary>
    /// 删除角色（软删）。行保留，但名字立刻释放出来，且选人界面立刻看不到。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void DeleteCharacter(ReducerContext ctx, ulong characterId)
    {
        ulong accountId = RequireAccountId(ctx);
        var character = RequireOwnedCharacter(ctx, accountId, characterId);

        // 正在被某条连接选中就先踢回选人界面，否则那边会拿着一个已删角色继续玩
        foreach (var selection in ctx.Db.CharacterSelection.CharacterId.Filter(characterId).ToList())
        {
            ctx.Db.CharacterSelection.ConnectionId.Delete(selection.ConnectionId);
            ctx.Db.CharacterTransform.ConnectionId.Delete(selection.ConnectionId);
        }

        character.DeletedAt = ctx.Timestamp;
        // 名字立刻释放：NameKey 换成保留形式（'#' 不在合法字符集里，撞不上真名字），
        // Name 保留原值，将来要做恢复或客服查询都还在。
        character.NameKey = CharacterRules.BuildDeletedNameKey(characterId);
        ctx.Db.Character.CharacterId.Update(character);

        Log.Info($"[Character] 删除角色 character={characterId} name={character.Name} account={accountId}");
    }

    /// <summary>
    /// 选择角色 —— 选完这一步客户端才进城镇。
    /// 选角是**连接级**状态：同一账号的另一条连接选了别的角色互不影响。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void SelectCharacter(ReducerContext ctx, ulong characterId)
    {
        ulong accountId = RequireAccountId(ctx);
        var character = RequireOwnedCharacter(ctx, accountId, characterId);

        if (ctx.ConnectionId is not { } connectionId)
        {
            throw CharacterRules.Reject("这个操作只能由客户端连接发起");
        }

        character.LastPlayedAt = ctx.Timestamp;
        ctx.Db.Character.CharacterId.Update(character);

        // 进城镇：沿用角色记录的城镇，新角色落到配置里的初始城镇（见 Town/TownReducers.cs）
        uint townId = PlaceCharacter(ctx, accountId, characterId);

        // 进城镇时把体力和钱包补齐：两者都是「后加的功能」，老角色 / 老账号没有这些行 / 值。
        // 体力顺便完成每日重置（离线期间跨天的情况靠这里惰性补）
        EnsureStaminaFresh(ctx, characterId);
        EnsureWallet(ctx, accountId);

        // 本连接换角色：先删旧行再插新行（重复选同一个也走这条，幂等）
        ctx.Db.CharacterSelection.ConnectionId.Delete(connectionId);

        ctx.Db.CharacterSelection.Insert(new CharacterSelection
        {
            ConnectionId = connectionId,
            Identity = ctx.Sender,
            AccountId = accountId,
            CharacterId = characterId,
            CharacterName = character.Name,
            JobId = character.JobId,
            Level = character.Level,
            EnteredAt = ctx.Timestamp,
            FormId = CurrentBaseFormId(character.JobId, character.Star),
            TownId = townId,
        });

        Log.Info($"[Character] 选择角色 character={characterId} name={character.Name} " +
                 $"town={townId} account={accountId}");
    }

    /// <summary>返回选人界面。只清本连接的选角状态，不影响登录态。</summary>
    [SpacetimeDB.Reducer]
    public static void LeaveCharacter(ReducerContext ctx)
    {
        RequireAccountId(ctx);

        if (ctx.ConnectionId is { } connectionId)
        {
            ctx.Db.CharacterSelection.ConnectionId.Delete(connectionId);
            // 选角行和坐标行要一起清：留着坐标行，别人城镇里还看得见这个已经离开的角色
            ctx.Db.CharacterTransform.ConnectionId.Delete(connectionId);
        }
    }

    // ------------------------------------------------------------------
    // 内部
    // ------------------------------------------------------------------

    /// <summary>
    /// 取当前连接所属的账号 id。没有活会话就抛「请先登录」。
    /// 所有角色 Reducer 的第一句都是这个 —— 别在别处手写这段判断。
    /// </summary>
    private static ulong RequireAccountId(ReducerContext ctx)
    {
        if (ctx.ConnectionId is not { } connectionId)
        {
            throw CharacterRules.Reject("这个操作只能由客户端连接发起");
        }

        if (ctx.Db.Session.ConnectionId.Find(connectionId) is not { } session)
        {
            throw CharacterRules.Reject("请先登录");
        }

        return session.AccountId;
    }

    /// <summary>
    /// 取一个属于该账号、且没被删除的角色。
    ///
    /// 「不存在」和「不属于你」回同一句文案：否则拿 characterId 挨个试就能探出别人有哪些角色。
    /// </summary>
    private static Character RequireOwnedCharacter(ReducerContext ctx, ulong accountId, ulong characterId)
    {
        if (ctx.Db.Character.CharacterId.Find(characterId) is not { } character ||
            character.AccountId != accountId ||
            character.DeletedAt is not null)
        {
            throw CharacterRules.Reject("角色不存在");
        }

        return character;
    }

    /// <summary>
    /// 名字没被占用才放行。<c>CreateCharacter</c> 和 <c>CheckCharacterName</c> 共用，
    /// 免得两处的判断和文案各写一遍然后慢慢漂开。
    ///
    /// 唯一索引本身也会挡住重名，但那样报出来的是一句数据库层的英文约束错误，
    /// 玩家看不懂 —— 所以先查一次，给一句能直接显示的中文。
    /// </summary>
    private static void RequireNameAvailable(ReducerContext ctx, string nameKey)
    {
        if (ctx.Db.Character.NameKey.Find(nameKey) is not null)
        {
            throw CharacterRules.Reject("这个角色名已经被使用了");
        }
    }

    /// <summary>数一个账号下还活着的角色数（软删的不算）。</summary>
    private static uint CountAliveCharacters(ReducerContext ctx, ulong accountId)
    {
        uint count = 0;
        foreach (var character in ctx.Db.Character.AccountId.Filter(accountId))
        {
            if (character.DeletedAt is null)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// 清掉某条连接的选角状态。给连接断开时调 —— 会话行和选角行必须一起清，
    /// 否则在线列表里会留下一个永远不下线的角色。
    /// </summary>
    private static void ClearSelectionOnDisconnect(ReducerContext ctx)
    {
        if (ctx.ConnectionId is { } connectionId)
        {
            ctx.Db.CharacterSelection.ConnectionId.Delete(connectionId);
        }
    }

    /// <summary>清掉某个 Identity 名下所有连接的选角状态。给登出时调。</summary>
    private static void ClearSelectionsOfIdentity(ReducerContext ctx, Identity identity)
    {
        foreach (var selection in ctx.Db.CharacterSelection.Identity.Filter(identity).ToList())
        {
            ctx.Db.CharacterSelection.ConnectionId.Delete(selection.ConnectionId);
        }
    }
}
