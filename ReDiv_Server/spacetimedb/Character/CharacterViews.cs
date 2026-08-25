using System.Collections.Generic;
using SpacetimeDB;

/// <summary>
/// 角色系统对客户端暴露的 View。
///
/// 为什么用 View 而不是把 Character 设成公开表让客户端自己按 AccountId 订阅：
/// AccountId 是自增整数，太好猜了 —— 公开表的话，改一句订阅 SQL
/// （<c>WHERE account_id = 2</c>）就能看到别人的角色列表。
/// View 的过滤发生在服务端、以**订阅者自己的 Identity** 为准，客户端伪造不了。
///
/// View 的两个硬约束（写这里的代码时必须记住）：
///   1. 里面**不能 Iter()**，只能走索引 Find / Filter ⇒ Character.AccountId 的索引是必需的；
///   2. 返回的行类型可以是自定义 [Type]，所以能只暴露想给的字段 ——
///      口令哈希那种根本不会出现在客户端绑定里。
///
/// 实测过：per-subscriber（ctx.Sender）、两跳索引查找、声明主键、
/// 底层表一变会实时推送（增删角色客户端立刻收到），客户端当普通表订阅即可。
/// </summary>
public static partial class Module
{
    /// <summary>选人界面用的角色行。只放选人界面真正要显示的字段。</summary>
    [SpacetimeDB.Type]
    public partial struct MyCharacterRow
    {
        public ulong CharacterId;
        public string Name;
        public uint JobId;

        /// <summary>
        /// 角色星级（1~6）。客户端画「3/6 星」那排星要用它，上限从配置
        /// <c>TbCharacterJob.MaxStar</c> 取（有二觉 6、没二觉 5）。
        /// </summary>
        public uint Star;

        /// <summary>
        /// 当前基础形态的 FormId，由服务端按星级算好（基础 1 星 / 一觉 3 星 / 二觉 6 星）。
        ///
        /// **客户端别自己再按星级算一遍** —— 两份实现迟早对不上。
        /// 拿它去 <c>TbCharacterForm.Get(JobId, FormId)</c> 取名字、立绘、头像、Spine。
        /// 0 表示配置有问题（基础线一行都没配到）。
        ///
        /// 爆发形态不在这里：它是战斗中装备爆发宝石才切的，选人界面要列的话
        /// 直接从配置里筛 <c>FormType=2</c> 按 SortOrder 排。
        /// </summary>
        public uint FormId;

        public uint Level;
        public ulong Exp;

        /// <summary>
        /// 当前体力。上限**不在这里** —— 客户端按 <c>Level</c> 查配置
        /// <c>TbLevelExp.MaxStamina</c>，那张表两端都有，不用白占同步量。
        /// 经验条的分母（<c>ExpToNext</c>）同理。
        /// </summary>
        public uint Stamina;

        public Timestamp CreatedAt;
        public Timestamp? LastPlayedAt;
    }

    /// <summary>账号层的角色相关信息（栏位数）。客户端画选人格子要用。</summary>
    [SpacetimeDB.Type]
    public partial struct MyAccountProfileRow
    {
        public ulong AccountId;
        public string Username;
        public uint CharacterSlots;
    }

    /// <summary>
    /// 当前设备已登录账号的角色列表（已软删的不下发）。
    ///
    /// 用 IdentityBinding 而不是 Session 定位账号：View 只有 ctx.Sender，没有连接概念。
    /// 登出会删掉绑定，所以登出后这个 View 自然返回空 —— 正是想要的效果。
    /// </summary>
    [SpacetimeDB.View(Accessor = "MyCharacter", Public = true,
        PrimaryKey = nameof(MyCharacterRow.CharacterId))]
    public static List<MyCharacterRow> MyCharacter(ViewContext ctx)
    {
        var rows = new List<MyCharacterRow>();

        if (ctx.Db.IdentityBinding.Identity.Find(ctx.Sender) is not { } binding)
        {
            return rows;
        }

        foreach (var character in ctx.Db.Character.AccountId.Filter(binding.AccountId))
        {
            if (character.DeletedAt is not null)
            {
                continue;
            }

            rows.Add(new MyCharacterRow
            {
                CharacterId = character.CharacterId,
                Name = character.Name,
                JobId = character.JobId,
                Star = character.Star,
                FormId = CurrentBaseFormId(character.JobId, character.Star),
                Level = character.Level,
                Exp = character.Exp,
                Stamina = character.Stamina,
                CreatedAt = character.CreatedAt,
                LastPlayedAt = character.LastPlayedAt,
            });
        }

        return rows;
    }

    /// <summary>账号钱包。金币钻石全角色共享，所以是账号级的。</summary>
    [SpacetimeDB.Type]
    public partial struct MyWalletRow
    {
        public ulong AccountId;
        public ulong Coin;
        public ulong Gem;
    }

    /// <summary>
    /// 当前设备已登录账号的钱包。
    ///
    /// **单独一个 View 而不是塞进 <c>MyAccountProfile</c>**：账号名和栏位数基本不变，
    /// 钱包是会频繁变的。合成一行的话每次加钱都会把账号名一起重推给客户端。
    /// 还没有钱包行（老账号）时返回空 —— 客户端显示 0，进城镇时服务端会补建。
    /// </summary>
    [SpacetimeDB.View(Accessor = "MyWallet", Public = true,
        PrimaryKey = nameof(MyWalletRow.AccountId))]
    public static List<MyWalletRow> MyWallet(ViewContext ctx)
    {
        var rows = new List<MyWalletRow>();

        if (ctx.Db.IdentityBinding.Identity.Find(ctx.Sender) is { } binding &&
            ctx.Db.AccountWallet.AccountId.Find(binding.AccountId) is { } wallet)
        {
            rows.Add(new MyWalletRow
            {
                AccountId = wallet.AccountId,
                Coin = wallet.Coin,
                Gem = wallet.Gem,
            });
        }

        return rows;
    }

    /// <summary>当前设备已登录账号的栏位信息。</summary>
    [SpacetimeDB.View(Accessor = "MyAccountProfile", Public = true,
        PrimaryKey = nameof(MyAccountProfileRow.AccountId))]
    public static List<MyAccountProfileRow> MyAccountProfile(ViewContext ctx)
    {
        var rows = new List<MyAccountProfileRow>();

        if (ctx.Db.IdentityBinding.Identity.Find(ctx.Sender) is { } binding &&
            ctx.Db.Account.AccountId.Find(binding.AccountId) is { } account)
        {
            rows.Add(new MyAccountProfileRow
            {
                AccountId = account.AccountId,
                Username = account.Username,
                CharacterSlots = account.CharacterSlots,
            });
        }

        return rows;
    }
}
