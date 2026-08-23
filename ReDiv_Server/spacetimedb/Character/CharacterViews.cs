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

        /// <summary>当前生效的专职。客户端据此显示形象、并高亮专职选择卡。</summary>
        public uint SpecId;

        /// <summary>
        /// 当前形态（1 = 专职，2 = 觉醒，3 = 一次觉醒…）。
        ///
        /// 由服务端按配置的 UnlockLevel 和角色等级现算，**客户端别自己再算一遍** ——
        /// 两份实现迟早对不上。客户端拿它去 TbSpecializationForm.Get(SpecId, FormStage)
        /// 取名字、立绘、头像。0 表示配置有问题（比如漏了 Stage=1）。
        /// </summary>
        public int FormStage;

        public uint Level;
        public ulong Exp;
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

            // 老角色可能还没有专职字段，或者配置里那个专职被删了，统一走容错解析
            uint specId = ResolveSpecId(character);

            rows.Add(new MyCharacterRow
            {
                CharacterId = character.CharacterId,
                Name = character.Name,
                JobId = character.JobId,
                SpecId = specId,
                FormStage = CurrentFormStage(specId, character.Level),
                Level = character.Level,
                Exp = character.Exp,
                CreatedAt = character.CreatedAt,
                LastPlayedAt = character.LastPlayedAt,
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
