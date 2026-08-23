using ReDiv.Server;
using ReDiv.Server.Character;
using ReDiv.Server.Config;
using SpacetimeDB;

/// <summary>
/// 专职与形态。
///
/// 三层结构（配置在 <c>Defines/character.xml</c>）：
///   CharacterJob        角色 / 职业（凯露）—— 建角色时选，之后不变
///   JobSpecialization   专职（魔法士…）—— 一个角色多个可用，同时只有一个生效，可切换
///   SpecializationForm  形态 —— 每个专职 3 个（专职名 / 觉醒名 / 一次觉醒名）
///
/// **可用专职和当前形态都不存库**，由配置里的 UnlockLevel 和角色等级现算。
/// 好处是平衡改动只要改 Excel + 重发布，不用写数据迁移；
/// 代价是解锁条件只能是「等级」这种能从现有数据推出来的东西 ——
/// 以后要做「做完任务才觉醒」，就得在角色侧加存储，那时再动。
/// </summary>
public static partial class Module
{
    /// <summary>
    /// 切换当前生效的专职。
    ///
    /// 只允许切到「属于这个角色的职业」且「等级已达到解锁要求」的专职。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void SwitchSpecialization(ReducerContext ctx, ulong characterId, uint specId)
    {
        ulong accountId = RequireAccountId(ctx);
        var character = RequireOwnedCharacter(ctx, accountId, characterId);

        if (character.SpecId == specId)
        {
            return; // 已经是这个专职了，幂等返回
        }

        var spec = ServerConfig.Tables.TbJobSpecialization.GetOrDefault((int)specId);
        if (spec == null)
        {
            throw CharacterRules.Reject("专职不存在");
        }

        // 不能切到别的角色的专职
        if (spec.JobId != (int)character.JobId)
        {
            throw CharacterRules.Reject("这个专职不属于当前角色");
        }

        if (character.Level < (uint)spec.UnlockLevel)
        {
            throw CharacterRules.Reject($"需要等级 {spec.UnlockLevel} 才能使用这个专职");
        }

        character.SpecId = specId;
        ctx.Db.Character.CharacterId.Update(character);

        // 正在游戏里的那条连接也要同步，否则在线列表还显示旧专职的形象
        foreach (var selection in ctx.Db.CharacterSelection.CharacterId.Filter(characterId).ToList())
        {
            var updated = selection;
            updated.SpecId = specId;
            ctx.Db.CharacterSelection.ConnectionId.Update(updated);
        }

        Log.Info($"[Character] 切换专职 character={characterId} spec={specId}");
    }

    // ------------------------------------------------------------------
    // 配置查询（内部）
    // ------------------------------------------------------------------

    /// <summary>
    /// 定出一个角色实际生效的专职 id。
    ///
    /// 会容错两种情况：字段是 0（<c>[Default(0)]</c> 追加字段前建的老角色），
    /// 或者配置里那个专职被删了 —— 两种都退回该职业的 DefaultSpecId。
    /// 这样改配置不会让老角色卡死在一个不存在的专职上。
    /// </summary>
    private static uint ResolveSpecId(Character character)
    {
        if (character.SpecId != 0 &&
            ServerConfig.Tables.TbJobSpecialization.GetOrDefault((int)character.SpecId) is { } spec &&
            spec.JobId == (int)character.JobId)
        {
            return character.SpecId;
        }

        var job = ServerConfig.Tables.TbCharacterJob.GetOrDefault((int)character.JobId);
        return job == null ? 0 : (uint)job.DefaultSpecId;
    }

    /// <summary>
    /// 建角色时定初始专职：取职业配置的 DefaultSpecId，并检查它真的存在且属于这个职业。
    /// 配置写错了就直接拒绝创建 —— 让它在建角色时就炸，比事后发现一堆角色没有专职好查。
    /// </summary>
    private static uint RequireDefaultSpecId(CharacterJob job)
    {
        var spec = ServerConfig.Tables.TbJobSpecialization.GetOrDefault(job.DefaultSpecId);

        if (spec == null || spec.JobId != job.JobId)
        {
            throw CharacterRules.Reject(
                $"职业配置有误：JobId={job.JobId} 的 DefaultSpecId={job.DefaultSpecId} 不是它的专职");
        }

        return (uint)spec.SpecId;
    }

    /// <summary>
    /// 按等级算出某个专职当前处于第几形态（1 = 专职，2 = 觉醒，3 = 一次觉醒…）。
    ///
    /// 取所有 UnlockLevel &lt;= 等级 的形态里 Stage 最大的那个。
    /// 形态表是 list 模式 + 联合主键（SpecId+Stage），所以这里遍历 DataList ——
    /// 配置量很小（每个专职 3 行），不值得为它再建索引。
    /// 一行都不匹配（配置漏了 Stage=1 或 UnlockLevel 写高了）时返回 0，调用方自己判断。
    /// </summary>
    private static int CurrentFormStage(uint specId, uint level)
    {
        int best = 0;

        foreach (var form in ServerConfig.Tables.TbSpecializationForm.DataList)
        {
            if (form.SpecId != (int)specId)
            {
                continue;
            }

            if (form.UnlockLevel <= (int)level && form.Stage > best)
            {
                best = form.Stage;
            }
        }

        return best;
    }
}
