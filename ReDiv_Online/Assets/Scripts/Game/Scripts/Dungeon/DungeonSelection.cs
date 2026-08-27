using System;
using System.Collections.Generic;
using UnityEngine;
using XFramework;

/// <summary>
/// 副本界面上的**当前选择**：每个副本各自选了几星、以及现在点选的是哪个副本。
///
/// ⚠️⚠️ **这个类存在的唯一理由是为组队留口子**（用户 2026-08-27 提前交代的）：
/// 以后组队时，**队长在 <c>PopDungeonUI</c> 上的操作要同步给队员**。
/// 那意味着「选了哪个副本 / 几星」不能是界面自己的字段，得是一份**可替换的状态源**：
///
/// <list type="bullet">
///   <item><b>现在（单人）</b>：状态就在本地内存里，<see cref="CanEdit"/> 恒为 true；</item>
///   <item><b>以后（组队）</b>：把本类内部换成「<see cref="Select"/> / <see cref="SetStar"/>
///         调服务端 Reducer + 订阅队伍那张表」，队员的 <see cref="CanEdit"/> 返回 false。
///         <b>界面代码一行都不用改</b> —— 它只会读这几个属性、听 <see cref="Changed"/>、
///         点之前问一句 <see cref="CanEdit"/>。</item>
/// </list>
///
/// 所以**界面里不要再自己存「当前几星」**，一律走这里。两份状态迟早对不上，
/// 而且组队那天要改的地方就从一处变成一堆。
///
/// 生命周期：单例、纯内存，**换角色 / 关界面时要 <see cref="Reset"/>** ——
/// 星级默认值取自「当前能选的最高星」，而那个值是跟着角色的通关进度走的。
/// </summary>
public sealed class DungeonSelection
{
    private static DungeonSelection instance;

    public static DungeonSelection Instance => instance ??= new DungeonSelection();

    private DungeonSelection()
    {
    }

    /// <summary>选择变了（换了副本 / 改了星级 / 被 <see cref="Reset"/> 清了）。界面听这个重画。</summary>
    public event Action Changed;

    /// <summary>
    /// 我能不能改这些选择。
    ///
    /// **现在恒为 true**（单人）。以后组队时非队长要返回 false ——
    /// 界面据此把箭头画灰 / 拦住点击，而不是各处 if 判断「我是不是队长」。
    /// </summary>
    public bool CanEdit => true;

    /// <summary>当前点选的副本 id。0 = 还没选。</summary>
    public int SelectedDungeonId { get; private set; }

    /// <summary>每个副本各自选的星级。没记录过的副本按「当前能选的最高星」算。</summary>
    private readonly Dictionary<int, int> stars = new Dictionary<int, int>();

    /// <summary>
    /// 这个副本当前选了几星。
    ///
    /// 没选过就给**当前能选的最高星**（打过 3 星就默认停在 4 星）——
    /// 玩家每次都想打能打的最高难度，默认停在 1 星等于每次都要点一堆箭头。
    ///
    /// 已经选过的也会**夹回当前可选范围**：进度是会变的（打完一把就多解一星），
    /// 而这里的记录是上一次的，不夹的话会出现「选着 5 星但其实只解到 4 星」。
    /// </summary>
    public int StarOf(Dungeon config)
    {
        if (config == null)
        {
            return 1;
        }

        int max = DungeonProgress.MaxSelectableStar(config);

        if (!stars.TryGetValue(config.DungeonId, out int star))
        {
            return max;
        }

        return Mathf.Clamp(star, 1, max);
    }

    /// <summary>
    /// 改某个副本的星级。会夹到 `1 ~ 当前可选上限`，越界就夹住**而不是拒绝** ——
    /// 界面上按箭头到头了本来就该没反应，不需要弹窗。
    ///
    /// 返回是不是真的改了（没权限 / 值没变都返回 false，调用方据此决定要不要出音效）。
    /// </summary>
    public bool SetStar(Dungeon config, int star)
    {
        if (config == null || !CanEdit)
        {
            return false;
        }

        int max = DungeonProgress.MaxSelectableStar(config);
        int clamped = Mathf.Clamp(star, 1, max);

        if (StarOf(config) == clamped)
        {
            return false;
        }

        stars[config.DungeonId] = clamped;
        Changed?.Invoke();
        return true;
    }

    /// <summary>点选一个副本（以后组队时这一步要同步给队员）。</summary>
    public bool Select(int dungeonId)
    {
        if (!CanEdit || SelectedDungeonId == dungeonId)
        {
            return false;
        }

        SelectedDungeonId = dungeonId;
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// 清空选择。**换角色、关界面时调** —— 星级的默认值和上限都跟着角色的通关进度走，
    /// 留着上一个角色的选择会显示成「他解到 5 星」。
    /// </summary>
    public void Reset()
    {
        if (stars.Count == 0 && SelectedDungeonId == 0)
        {
            return;
        }

        stars.Clear();
        SelectedDungeonId = 0;
        Changed?.Invoke();
    }
}
