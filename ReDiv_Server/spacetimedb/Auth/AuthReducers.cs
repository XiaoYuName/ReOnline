using System.Linq;
using ReDiv.Server.Auth;
using ReDiv.Server.Security;
using SpacetimeDB;

/// <summary>
/// 账号系统的 Reducer：注册 / 登录 / 登出，以及会话的建立与关闭。
///
/// 对外只有三个入口（客户端绑定里是 PascalCase，CLI 里是 snake_case）：
///   Register(username, password)  register  —— 注册，成功后直接建会话（自动登录）
///   Login(username, password)     login     —— 登录
///   Logout()                      logout    —— 登出，并解除本设备的免密绑定
///
/// 结果怎么回到客户端：
///   失败 → Reducer 抛异常，事务回滚，调用方在 OnRegister/OnLogin 的
///          ctx.Event.Status 里拿到 Status.Failed(reason)，reason 是中文文案。
///   成功 → Session 表里出现自己那一行（客户端订阅
///          SELECT * FROM Session WHERE identity = 0x&lt;自己的 identity&gt; 即可）。
///   被动关闭 → SessionClosed 事件表推一条，带原因。
/// </summary>
public static partial class Module
{
    // ------------------------------------------------------------------
    // 对外 Reducer
    // ------------------------------------------------------------------

    /// <summary>
    /// 注册。用户名大小写不敏感唯一；成功后直接建会话（不用再调一次 Login）。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void Register(ReducerContext ctx, string username, string password)
    {
        string usernameKey = AuthRules.NormalizeUsername(username);
        AuthRules.ValidatePassword(password);

        if (ctx.Db.Account.UsernameKey.Find(usernameKey) is not null)
        {
            throw AuthRules.Reject("这个用户名已经被注册了");
        }

        byte[] salt = new byte[PasswordHasher.SaltSize];
        ctx.Rng.NextBytes(salt);

        var inserted = ctx.Db.Account.Insert(new Account
        {
            AccountId = 0, // AutoInc：传 0 占位，Insert 返回的行里才是真实 id
            UsernameKey = usernameKey,
            Username = username.Trim(),
            PasswordHash = PasswordHasher.Hash(password, salt, PasswordHasher.CurrentIterations),
            PasswordSalt = System.Convert.ToBase64String(salt),
            HashIterations = PasswordHasher.CurrentIterations,
            CreatedAt = ctx.Timestamp,
            LastLoginAt = ctx.Timestamp,
            // ⚠️ 必须显式赋值。表上的 [Default] 只在**迁移**时给已有行回填，
            // 新插入的行不受它影响 —— 漏了这句新账号的栏位数就是 0，一个角色都建不出来。
            CharacterSlots = DefaultCharacterSlots,
        });

        Log.Info($"[Auth] 注册成功 account={inserted.AccountId} username={inserted.Username}");

        OpenSession(ctx, inserted);
    }

    /// <summary>
    /// 登录。
    ///
    /// 用户名不存在和口令错误回同一句文案，不告诉对方「这个用户名存在但密码错了」——
    /// 否则接口就成了账号枚举器。
    ///
    /// 这里**故意不跑注册那套格式校验**（只做查找归一化）：注册规则以后一收紧，
    /// 复用它就会把按老规则注册的账号锁死在门外。详见 AuthRules.NormalizeForLookup。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void Login(ReducerContext ctx, string username, string password)
    {
        string usernameKey = AuthRules.NormalizeForLookup(username);
        AuthRules.ValidatePasswordForLogin(password);

        if (ctx.Db.Account.UsernameKey.Find(usernameKey) is not { } account)
        {
            throw AuthRules.Reject("用户名或密码不正确");
        }

        if (!PasswordHasher.Verify(password, account.PasswordSalt, account.PasswordHash, account.HashIterations))
        {
            Log.Warn($"[Auth] 登录失败（口令不匹配）account={account.AccountId} identity={ctx.Sender}");
            throw AuthRules.Reject("用户名或密码不正确");
        }

        account.LastLoginAt = ctx.Timestamp;

        // 哈希参数升级过的话，趁现在手里有明文，用新参数重算一遍（渐进迁移）
        if (PasswordHasher.NeedsRehash(account.HashIterations))
        {
            byte[] newSalt = new byte[PasswordHasher.SaltSize];
            ctx.Rng.NextBytes(newSalt);
            account.PasswordSalt = System.Convert.ToBase64String(newSalt);
            account.PasswordHash = PasswordHasher.Hash(password, newSalt, PasswordHasher.CurrentIterations);
            account.HashIterations = PasswordHasher.CurrentIterations;
            Log.Info($"[Auth] 口令哈希参数已升级 account={account.AccountId}");
        }

        ctx.Db.Account.AccountId.Update(account);

        Log.Info($"[Auth] 登录成功 account={account.AccountId} identity={ctx.Sender}");

        OpenSession(ctx, account);
    }

    /// <summary>
    /// 登出。关掉本设备（本 Identity）的所有会话，并解除免密绑定 ——
    /// 显式登出的语义就是「别再记住我」，否则下次连上又被自动登录回去。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void Logout(ReducerContext ctx)
    {
        foreach (var session in ctx.Db.Session.Identity.Filter(ctx.Sender).ToList())
        {
            CloseSession(ctx, session, SessionCloseReason.LoggedOut);
        }

        ctx.Db.IdentityBinding.Identity.Delete(ctx.Sender);

        // 登出也要把本设备的选角状态清掉，否则在线列表里还挂着这个角色
        ClearSelectionsOfIdentity(ctx, ctx.Sender);

        Log.Info($"[Auth] 登出 identity={ctx.Sender}");
    }

    // ------------------------------------------------------------------
    // 会话管理（内部）
    // ------------------------------------------------------------------

    /// <summary>
    /// 给当前连接建会话，并按「顶号」策略清理其它会话。
    ///
    /// 顶号的粒度是 **Identity（设备）而不是连接**：同一 Identity 的多条连接允许共存。
    /// 因为 Identity 来自客户端存在本地的 AuthToken，一台机器上跑两份**同一个包**
    /// 就是同一个 Identity；按连接顶号的话它们会互相踢，谁都进不去。
    /// （Unity 编辑器和打包出来的客户端**不是**同一个 Identity —— SDK 的 AuthToken
    /// 在编辑器下会把 Application.dataPath 拼进 PlayerPrefs 的键里，两边各存一份。）
    /// </summary>
    private static void OpenSession(ReducerContext ctx, Account account)
    {
        if (ctx.ConnectionId is not { } connectionId)
        {
            // 只有定时 Reducer 之类没有连接上下文的调用会走到这，正常客户端调用一定有
            throw AuthRules.Reject("这个操作只能由客户端连接发起");
        }

        // 1. 顶号：账号在**其它设备**上的会话全部关掉，连绑定一起清 ——
        //    绑定留着的话，那台机器一重连就会自动登录，把这次登录又顶下去，来回打
        KickOtherDevices(ctx, account.AccountId, ctx.Sender);

        // 2. 同一设备换号登录：本 Identity 上属于别的账号的会话也要收掉
        foreach (var stale in ctx.Db.Session.Identity.Filter(ctx.Sender).ToList())
        {
            if (stale.AccountId != account.AccountId && stale.ConnectionId != connectionId)
            {
                CloseSession(ctx, stale, SessionCloseReason.SwitchedAccount);
            }
        }

        // 3. 绑定当前 Identity，之后重连可以免密恢复
        BindIdentity(ctx, ctx.Sender, account.AccountId);

        // 4. 本连接的旧会话行直接删掉不发通知（重复调 Login 是自己发起的，不算被踢）
        ctx.Db.Session.ConnectionId.Delete(connectionId);

        ctx.Db.Session.Insert(new Session
        {
            ConnectionId = connectionId,
            Identity = ctx.Sender,
            AccountId = account.AccountId,
            Username = account.Username,
            LoginAt = ctx.Timestamp,
        });
    }

    /// <summary>关掉该账号在 <paramref name="keepIdentity"/> 之外所有设备上的会话与绑定。</summary>
    private static void KickOtherDevices(ReducerContext ctx, ulong accountId, Identity keepIdentity)
    {
        foreach (var session in ctx.Db.Session.AccountId.Filter(accountId).ToList())
        {
            if (session.Identity != keepIdentity)
            {
                CloseSession(ctx, session, SessionCloseReason.KickedByNewLogin);
            }
        }

        foreach (var binding in ctx.Db.IdentityBinding.AccountId.Filter(accountId).ToList())
        {
            if (binding.Identity != keepIdentity)
            {
                ctx.Db.IdentityBinding.Identity.Delete(binding.Identity);
            }
        }
    }

    /// <summary>删会话行 + 往事件表推一条通知，让那一端知道自己为什么被踢下线。</summary>
    private static void CloseSession(ReducerContext ctx, Session session, SessionCloseReason reason)
    {
        ctx.Db.Session.ConnectionId.Delete(session.ConnectionId);

        // 会话没了，那条连接的选角状态也不该留着（被顶号 / 同设备换号都会走到这）
        ctx.Db.CharacterSelection.ConnectionId.Delete(session.ConnectionId);

        ctx.Db.SessionClosed.Insert(new SessionClosed
        {
            Identity = session.Identity,
            ConnectionId = session.ConnectionId,
            Reason = reason,
        });

        Log.Info($"[Auth] 会话关闭 account={session.AccountId} identity={session.Identity} reason={reason}");
    }

    /// <summary>写入或更新 Identity → 账号 的绑定。</summary>
    private static void BindIdentity(ReducerContext ctx, Identity identity, ulong accountId)
    {
        if (ctx.Db.IdentityBinding.Identity.Find(identity) is { } existing)
        {
            ctx.Db.IdentityBinding.Identity.Update(existing with
            {
                AccountId = accountId,
                BoundAt = ctx.Timestamp,
            });
            return;
        }

        ctx.Db.IdentityBinding.Insert(new IdentityBinding
        {
            Identity = identity,
            AccountId = accountId,
            BoundAt = ctx.Timestamp,
        });
    }

    /// <summary>
    /// 连接建立时尝试免密恢复登录态。给 ClientConnected 钩子调。
    /// 没有绑定就什么都不做（客户端会停在登录界面，等玩家输账号）。
    /// </summary>
    private static void TryRestoreSession(ReducerContext ctx)
    {
        // ClientConnected 里抛异常会**拒绝这条连接**，所以这条路径上一个异常都不能漏出去。
        // 这里先自己确认有 ConnectionId，别指望 OpenSession 去抛。
        if (ctx.ConnectionId is null)
        {
            return;
        }

        if (ctx.Db.IdentityBinding.Identity.Find(ctx.Sender) is not { } binding)
        {
            return;
        }

        if (ctx.Db.Account.AccountId.Find(binding.AccountId) is not { } account)
        {
            // 账号被删了，绑定成了悬空指针，顺手清掉
            ctx.Db.IdentityBinding.Identity.Delete(ctx.Sender);
            Log.Warn($"[Auth] 绑定指向的账号不存在，已清理 identity={ctx.Sender} account={binding.AccountId}");
            return;
        }

        OpenSession(ctx, account);
        Log.Info($"[Auth] 免密恢复会话 account={account.AccountId} identity={ctx.Sender}");
    }

    /// <summary>连接断开时清掉这条连接的会话行。绑定要留着 —— 那是免密重连的凭据。</summary>
    private static void CloseSessionOnDisconnect(ReducerContext ctx)
    {
        if (ctx.ConnectionId is not { } connectionId)
        {
            return;
        }

        // 客户端自己断开的，不用发 SessionClosed（它已经收不到了）
        ctx.Db.Session.ConnectionId.Delete(connectionId);
    }
}
