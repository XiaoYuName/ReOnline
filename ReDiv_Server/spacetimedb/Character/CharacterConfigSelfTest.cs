using System.Collections.Generic;
using System.Linq;
using ReDiv.Server;
using SpacetimeDB;

/// <summary>
/// 角色配置表自检。
///
/// 为什么需要它：角色配置分在两张 Excel 里，靠 <c>JobId</c> / <c>FormId</c> /
/// <c>UnlockStar</c> 互相引用。这些引用**没有任何编译期检查** —— 配错了不会报错，
/// 只会在运行时表现成「建不出角色」「觉醒不了」「客户端取不到资源」，全都很难往回追。
///
/// 所以改完 Excel（尤其改了 id 或星级门槛）跑一次：
/// <code>spacetime call rediv character_config_self_test</code>
/// 全过打一行 PASS；有问题就抛异常，CLI 直接看到是哪条配错了。
///
/// 它查的是**表之间的引用关系和结构**。查不了的：Addressable 路径字符串对不对
/// （服务端根本看不到那些列，它们是 group="c"）—— 那个只能靠客户端跑起来才知道。
/// </summary>
public static partial class Module
{
    [SpacetimeDB.Reducer]
    public static void CharacterConfigSelfTest(ReducerContext ctx)
    {
        var problems = new List<string>();

        var jobs = ServerConfig.Tables.TbCharacterJob.DataList;
        var forms = ServerConfig.Tables.TbCharacterForm.DataList;

        if (jobs.Count == 0)
        {
            problems.Add("CharacterJob 表是空的，一个角色都建不出来");
        }

        foreach (var dup in FindDuplicateInts(jobs.Select(j => j.JobId)))
        {
            problems.Add($"JobId 重复: {dup}");
        }

        // ---- 职业层 ----
        foreach (var job in jobs)
        {
            if (job.StartLevel <= 0)
            {
                problems.Add($"JobId={job.JobId} 的 StartLevel={job.StartLevel} 不合法（至少 1）");
            }

            if (job.StartStar <= 0)
            {
                problems.Add($"JobId={job.JobId} 的 StartStar={job.StartStar} 不合法（至少 1）");
            }

            var ownForms = forms.Where(f => f.JobId == job.JobId).ToList();
            var baseForms = ownForms.Where(f => f.FormType == FormTypeBase).ToList();

            // 同一个角色内 FormId 必须唯一（爆发线和基础线共用一个编号空间）
            foreach (var dup in FindDuplicateInts(ownForms.Select(f => f.FormId)))
            {
                problems.Add($"JobId={job.JobId} 的 FormId 重复: {dup}");
            }

            if (baseForms.Count == 0)
            {
                problems.Add($"JobId={job.JobId} 一个基础形态（FormType={FormTypeBase}）都没配，" +
                             "建出来的角色没有形象");
                continue;
            }

            // 关键一条：建完角色就落在 StartStar 上，那个星级必须能接住一行基础形态，
            // 否则客户端取不到任何资源。CreateCharacter 会直接拒绝创建。
            if (baseForms.All(f => f.UnlockStar > job.StartStar))
            {
                int lowest = baseForms.Min(f => f.UnlockStar);
                problems.Add($"JobId={job.JobId} 的初始星级是 {job.StartStar}，" +
                             $"但它最低的基础形态要 {lowest} 星 —— 刚建出来的角色没有形象");
            }

            // 星级上限必须够得着最高的那个形态，否则那一档永远觉醒不到
            int topStar = baseForms.Max(f => f.UnlockStar);
            if (job.MaxStar < topStar)
            {
                problems.Add($"JobId={job.JobId} 的 MaxStar={job.MaxStar} 低于它最高形态要求的 " +
                             $"{topStar} 星 —— 那一档永远觉醒不到");
            }

            // 设定：一觉是 3 星、二觉是 6 星，没二觉就封顶 5 星。
            // 这里只提醒明显对不上的（上限既不是 5 也不是 6），不强行限定门槛数值。
            if (job.MaxStar != 5 && job.MaxStar != 6)
            {
                problems.Add($"JobId={job.JobId} 的 MaxStar={job.MaxStar} 不是 5 或 6 —— " +
                             "设定上有二觉填 6、没二觉填 5");
            }

            foreach (var dup in FindDuplicateInts(baseForms.Select(f => f.UnlockStar)))
            {
                problems.Add($"JobId={job.JobId} 的基础线有两行 UnlockStar 都是 {dup}，" +
                             "同一个星级只能对应一个形态");
            }

            // 觉醒是"等级到了 + 做任务"，所以越高的形态等级要求不该反而更低 ——
            // 那样配出来玩家会看到「6 星只要 30 级、3 星却要 60 级」这种怪事。
            var ordered = baseForms.OrderBy(f => f.UnlockStar).ToList();
            for (int i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].UnlockLevel < ordered[i - 1].UnlockLevel)
                {
                    problems.Add($"JobId={job.JobId} 的基础线等级门槛倒挂：" +
                                 $"{ordered[i].UnlockStar} 星要 {ordered[i].UnlockLevel} 级，" +
                                 $"比 {ordered[i - 1].UnlockStar} 星的 {ordered[i - 1].UnlockLevel} 级还低");
                }
            }
        }

        // ---- 形态层 ----
        foreach (var form in forms)
        {
            string where = $"形态 (JobId={form.JobId}, FormId={form.FormId})";

            if (jobs.All(j => j.JobId != form.JobId))
            {
                problems.Add($"{where} 的 JobId 在职业表里不存在");
            }

            if (form.FormType != FormTypeBase && form.FormType != FormTypeBurst)
            {
                problems.Add($"{where} 的 FormType={form.FormType} 不合法" +
                             $"（{FormTypeBase}=基础 / {FormTypeBurst}=爆发）");
            }

            if (form.UnlockStar <= 0)
            {
                problems.Add($"{where} 的 UnlockStar 必须 >= 1");
            }

            if (form.FormType == FormTypeBurst && form.UnlockLevel != 0)
            {
                // 爆发形态靠宝石解锁，等级门槛不参与判定。填了非 0 说明理解错了配置含义。
                problems.Add($"{where} 是爆发形态，UnlockLevel 应该填 0（它靠宝石解锁，不看等级），" +
                             $"现在填的是 {form.UnlockLevel}");
            }
        }

        if (problems.Count > 0)
        {
            throw new System.Exception($"[CharacterConfig] 自检发现 {problems.Count} 个问题：\n  "
                                       + string.Join("\n  ", problems));
        }

        int baseCount = forms.Count(f => f.FormType == FormTypeBase);
        int burstCount = forms.Count(f => f.FormType == FormTypeBurst);
        Log.Info($"[CharacterConfig] 自检 PASS —— {jobs.Count} 个角色、" +
                 $"{baseCount} 个基础线形态、{burstCount} 个爆发形态");
    }

    private static IEnumerable<int> FindDuplicateInts(IEnumerable<int> values)
    {
        return values.GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key);
    }
}
