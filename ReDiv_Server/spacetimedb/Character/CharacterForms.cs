using System.Linq;
using ReDiv.Server;
using ReDiv.Server.Character;
using ReDiv.Server.Config;
using SpacetimeDB;

/// <summary>
/// 形态与觉醒。
///
/// 两层结构（配置在 <c>Defines/character.xml</c>）：
///   CharacterJob   角色 / 职业（凯露）—— 建角色时选，之后不变
///   CharacterForm  形态 —— 分两条线，美术资源都在这一层
///
/// ── 基础线（FormType=1）────────────────────────────────────────────
///   1~2 星  基础形态   UnlockStar=1，建完角色就在这
///   3~5 星  一觉形态   UnlockStar=3
///   6   星  二觉形态   UnlockStar=6（**部分角色没有二觉**，配置里不填这行就行）
///
/// 当前形态 = 基础线里 UnlockStar ≤ 角色星级的那些行中 UnlockStar 最高的一行
/// （见 <see cref="CurrentBaseFormId"/>）。中间星级（4 / 5）不用单独配行。
///
/// **觉醒是永久的、回不去**，条件是「等级到 <c>UnlockLevel</c> + 完成觉醒任务」。
/// 任务完成与否推不出来，所以星级**存在角色行上**（<c>Character.Star</c>），
/// 不像 2026-08-24 之前的专职形态那样能纯靠等级现算。
///
/// ── 爆发线（FormType=2）────────────────────────────────────────────
/// 一个角色可以有**多个**爆发形态，**不分阶段**。它们的解锁和切换发生在
/// **战斗中装备爆发宝石**时，和星级 / 等级无关，所以这里一行代码都没有 ——
/// 服务端目前只把它们当配置数据存着，客户端选人界面按 SortOrder 列出来给玩家翻。
/// 宝石那套装备系统等玩法定型后再做（别自己先建表，见 ../../CLAUDE.md 第 0 节）。
///
/// ⚠️ 2026-08-24 改过设定：原来中间还有一层「专职」（JobSpecialization / SpecId /
/// SwitchSpecialization / FormStage），**现在没有专职了**，那套已经删干净。
/// </summary>
public static partial class Module
{
    /// <summary>形态线：基础（含觉醒）。</summary>
    public const int FormTypeBase = 1;

    /// <summary>形态线：爆发（战斗中装备爆发宝石切换）。</summary>
    public const int FormTypeBurst = 2;

    // ------------------------------------------------------------------
    // 对外 Reducer
    // ------------------------------------------------------------------

    /// <summary>
    /// 觉醒：把角色推到基础线的下一档形态（1~2 星 → 一觉，3~5 星 → 二觉）。
    ///
    /// ⚠️ 现在**只校验等级**。设定上还要求「完成觉醒任务」，但任务系统还不存在 ——
    /// 那一条留成下面的 TODO，任务系统做好后只改那一处，别把条件散到别的地方。
    ///
    /// 觉醒不可逆：星级只增不减，没有反向 Reducer，这是设定，不是漏了。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void AwakenCharacter(ReducerContext ctx, ulong characterId)
    {
        ulong accountId = RequireAccountId(ctx);
        var character = RequireOwnedCharacter(ctx, accountId, characterId);

        var job = ServerConfig.Tables.TbCharacterJob.GetOrDefault((int)character.JobId);
        if (job == null)
        {
            throw CharacterRules.Reject("职业配置有误，请联系管理员");
        }

        var next = NextAwakenForm(character.JobId, character.Star);
        if (next == null)
        {
            throw CharacterRules.Reject("已经是最终形态了");
        }

        if (character.Level < (uint)next.UnlockLevel)
        {
            throw CharacterRules.Reject($"需要等级 {next.UnlockLevel} 才能觉醒成「{Describe(next)}」");
        }

        // TODO(任务系统): 这里还要校验「觉醒任务已完成」。任务系统做好后在这一处加，
        // 别把条件散到客户端或别的 Reducer 里 —— 服务端是唯一权威。

        if (next.UnlockStar > job.MaxStar)
        {
            // 配置自相矛盾（形态要 6 星但职业上限是 5 星）。自检 Reducer 会查这条，
            // 这里再挡一次，免得配置改坏时把角色推到超出上限的星级。
            throw CharacterRules.Reject("角色配置有误，请联系管理员");
        }

        character.Star = (uint)next.UnlockStar;
        ctx.Db.Character.CharacterId.Update(character);

        // 正在游戏里的那条连接也要同步，否则在线列表还显示觉醒前的形象
        SyncSelectionForm(ctx, characterId, (uint)next.FormId);

        Log.Info($"[Character] 觉醒 character={characterId} star={character.Star} form={next.FormId}");
    }

    // ------------------------------------------------------------------
    // 配置查询（内部）
    // ------------------------------------------------------------------

    /// <summary>
    /// 按星级算出角色当前处于基础线的哪一个形态，返回它的 FormId。
    ///
    /// 取基础线里所有 <c>UnlockStar ≤ star</c> 的行中 UnlockStar 最高的那个 ——
    /// 所以 4 / 5 星不用单独配行，形象跟着 3 星那行走。
    ///
    /// 形态表是 list 模式 + 联合主键（JobId+FormId），所以这里遍历 DataList：
    /// 配置量很小（每个角色几行），不值得为它再建索引。
    /// 一行都不匹配（配置漏了基础形态那行）时返回 0，调用方自己判断。
    /// </summary>
    private static uint CurrentBaseFormId(uint jobId, uint star)
    {
        int bestStar = -1;
        uint bestForm = 0;

        foreach (var form in ServerConfig.Tables.TbCharacterForm.DataList)
        {
            if (form.JobId != (int)jobId || form.FormType != FormTypeBase)
            {
                continue;
            }

            if (form.UnlockStar <= (int)star && form.UnlockStar > bestStar)
            {
                bestStar = form.UnlockStar;
                bestForm = (uint)form.FormId;
            }
        }

        return bestForm;
    }

    /// <summary>
    /// 基础线里「下一档觉醒形态」：UnlockStar 严格大于当前星级的行中，UnlockStar 最低的那个。
    /// 已经是最高档（或者根本没有更高的形态，比如没二觉的角色）就返回 null。
    /// </summary>
    private static CharacterForm NextAwakenForm(uint jobId, uint star)
    {
        CharacterForm best = null;

        foreach (var form in ServerConfig.Tables.TbCharacterForm.DataList)
        {
            if (form.JobId != (int)jobId || form.FormType != FormTypeBase)
            {
                continue;
            }

            if (form.UnlockStar > (int)star && (best == null || form.UnlockStar < best.UnlockStar))
            {
                best = form;
            }
        }

        return best;
    }

    /// <summary>
    /// 建角色时定初始星级：取职业配置的 StartStar，并检查基础线里真有一行能接住它。
    /// 配置写错了就直接拒绝创建 —— 让它在建角色时就炸，比事后发现一堆角色取不到形象好查。
    /// </summary>
    private static uint RequireStartStar(CharacterJob job)
    {
        uint star = (uint)job.StartStar;

        if (star == 0 || CurrentBaseFormId((uint)job.JobId, star) == 0)
        {
            throw CharacterRules.Reject(
                $"职业配置有误：JobId={job.JobId} 在 {job.StartStar} 星时没有可用的基础形态");
        }

        return star;
    }

    /// <summary>
    /// 把某个角色的当前形态同步到它在线的那些连接上（公开表 CharacterSelection）。
    /// 觉醒后不同步的话，在线列表会一直显示旧形象。
    /// </summary>
    private static void SyncSelectionForm(ReducerContext ctx, ulong characterId, uint formId)
    {
        foreach (var selection in ctx.Db.CharacterSelection.CharacterId.Filter(characterId).ToList())
        {
            var updated = selection;
            updated.FormId = formId;
            ctx.Db.CharacterSelection.ConnectionId.Update(updated);
        }
    }

    /// <summary>拼一句能直接给玩家看的形态说明。形态名是 group="c" 的列，服务端看不到，所以只能报 id。</summary>
    private static string Describe(CharacterForm form) => $"{form.UnlockStar} 星形态";
}
