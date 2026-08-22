using System;
using Cysharp.Threading.Tasks;
using ReDiv.Net.Bindings;
using SpacetimeDB;
using UnityEngine;

namespace ReDiv.Net
{
    /// <summary>
    /// 一次注册 / 登录 / 登出请求的结果。失败时 <see cref="Message"/> 是可以直接显示给玩家的中文文案。
    /// </summary>
    public readonly struct AuthResult
    {
        public readonly bool Ok;
        public readonly string Message;

        private AuthResult(bool ok, string message)
        {
            Ok = ok;
            Message = message;
        }

        public static AuthResult Success() => new AuthResult(true, string.Empty);

        public static AuthResult Fail(string message) => new AuthResult(false, message);
    }

    /// <summary>
    /// 账号系统的客户端门面：连接状态 + 登录态 + 注册 / 登录 / 登出。UI 只跟这个类打交道。
    ///
    /// 它是**纯 C# 单例**，不是 MonoBehaviour —— 不用往场景里挂东西，也就不会出现
    /// 「忘了挂所以登录不了」。生命周期靠 <c>[RuntimeInitializeOnLoadMethod]</c> 兜：
    /// 在任何场景 Awake 之前就挂好 <see cref="SpacetimeConnection"/> 的静态事件，
    /// 因此绝不会漏掉连接回调（SpacetimeConnection 在 Start 里就发起连接）。
    ///
    /// 职责边界：
    ///   SpacetimeConnection  只管连接生命周期，不认识账号
    ///   AuthManager          账号相关的订阅、表回调、Reducer 回调、登录态
    ///   LoginUI / CommonUI   只读状态、只调方法，不碰 Conn
    ///
    /// 服务端契约见 <c>ReDiv_Server/README.md</c> 的「账号系统」一节。三条要点：
    ///   1. 失败从 Reducer 回调的 <c>ctx.Event.Status</c> 里取（Status.Failed(reason)）；
    ///   2. 成功看 <c>Session</c> 表里有没有自己这条连接的行；
    ///   3. 订阅必须在调 Login **之前**建立，否则成功那一行的 OnInsert 会漏。
    /// </summary>
    public sealed class AuthManager
    {
        /// <summary>一次请求等服务端回应的上限。超时只是放弃等待，不代表服务端没执行。</summary>
        private const float RequestTimeoutSeconds = 15f;

        /// <summary>Reducer 提交成功后，等会话行同步进客户端缓存的上限。</summary>
        private const float SessionRowWaitSeconds = 2f;

        private static AuthManager instance;

        public static AuthManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new AuthManager();
                    instance.Attach();
                }
                return instance;
            }
        }

        /// <summary>
        /// 在场景加载前就把事件挂好，避免漏掉连接回调。
        ///
        /// 关掉域重载（Enter Play Mode Options 里取消 Reload Domain）时静态字段会留着上一轮的
        /// 实例，它的处理器还挂在 SpacetimeConnection 的静态事件上，所以先摘掉再换新的 ——
        /// 否则第二次进 Play 会收到两份回调，UI 还挂在已经销毁的旧实例上。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            instance?.Detach();
            instance = new AuthManager();
            instance.Attach();
        }

        // ------------------------------------------------------------------
        // 对外状态
        // ------------------------------------------------------------------

        /// <summary>服务器连接状态。</summary>
        public ServerLinkState LinkState => SpacetimeConnection.LinkState;

        /// <summary>连接状态变化。</summary>
        public event Action<ServerLinkState> LinkStateChanged;

        /// <summary>
        /// 账号相关的订阅是否已生效。**它为 false 时 <see cref="IsLoggedIn"/> 不可信** ——
        /// 服务端可能已经免密恢复了会话，只是那一行还没同步下来。
        /// </summary>
        public bool IsAuthReady { get; private set; }

        /// <summary>订阅生效（登录态首次可信）时触发。</summary>
        public event Action AuthReady;

        /// <summary>当前账号 id。未登录时为 0。</summary>
        public ulong AccountId { get; private set; }

        /// <summary>当前账号的展示用用户名。未登录时为空串。</summary>
        public string Username { get; private set; } = string.Empty;

        public bool IsLoggedIn => AccountId != 0;

        /// <summary>登录态变化（登录成功、登出、被顶号、掉线）。</summary>
        public event Action LoginStateChanged;

        /// <summary>
        /// 会话被服务端主动关闭。<c>KickedByNewLogin</c> 就是被顶号了。
        /// 注意这个事件只是「通知」，登录态的清理走 <see cref="LoginStateChanged"/>。
        /// </summary>
        public event Action<SessionCloseReason> SessionClosedByServer;

        /// <summary>是否有请求正在等服务端回应。</summary>
        public bool IsBusy => pending != null;

        /// <summary>
        /// 客户端与服务端的版本号是否不一致。连上后立刻校验一次（<c>CheckVersion</c> Reducer）。
        ///
        /// 只有服务端明确回了「不一致」才会置 true —— 超时或掉线属于网络问题，
        /// 不该冤枉成版本问题。
        /// </summary>
        public bool VersionMismatch { get; private set; }

        /// <summary>版本不一致时服务端给的说明（带两边版本号），可直接显示。</summary>
        public string VersionMessage { get; private set; } = string.Empty;

        /// <summary>版本校验发现不一致时触发，参数是可直接显示的说明文案。</summary>
        public event Action<string> VersionMismatched;

        /// <summary>
        /// 版本不一致时是否禁止登录 / 注册。
        ///
        /// 默认 true：版本对不上时继续往下走，只会撞上一堆更难查的错（订阅失败、
        /// 字段对不上、Reducer 参数不匹配）。开发期想临时无视，把它改成 false。
        /// </summary>
        public const bool BlockRequestsOnVersionMismatch = true;

        // ------------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------------

        private DbConnection conn;
        private UniTaskCompletionSource<AuthResult> pending;
        private UniTaskCompletionSource<AuthResult> versionPending;
        private bool attached;

        private void Attach()
        {
            if (attached)
            {
                return;
            }
            attached = true;

            SpacetimeConnection.LinkStateChanged += HandleLinkStateChanged;
            SpacetimeConnection.Connected += HandleConnected;
            SpacetimeConnection.Disconnected += HandleDisconnected;
            SpacetimeConnection.ConnectFailed += HandleConnectFailed;

            // 万一连接已经建好了（热重载、后挂事件），补一次
            if (SpacetimeConnection.IsConnected)
            {
                HandleConnected();
            }
        }

        private void Detach()
        {
            if (!attached)
            {
                return;
            }
            attached = false;

            SpacetimeConnection.LinkStateChanged -= HandleLinkStateChanged;
            SpacetimeConnection.Connected -= HandleConnected;
            SpacetimeConnection.Disconnected -= HandleDisconnected;
            SpacetimeConnection.ConnectFailed -= HandleConnectFailed;

            UnhookConnection();
        }

        // ------------------------------------------------------------------
        // 对外方法
        // ------------------------------------------------------------------

        /// <summary>
        /// 注册。服务端注册成功后会**直接建会话**，所以不用再调一次 <see cref="LoginAsync"/>。
        /// </summary>
        public async UniTask<AuthResult> RegisterAsync(string username, string password)
        {
            string invalid = AuthValidation.CheckUsernameForRegister(username)
                             ?? AuthValidation.CheckPasswordForRegister(password);
            if (invalid != null)
            {
                return AuthResult.Fail(invalid);
            }

            var precheck = CheckCanSend();
            if (!precheck.Ok)
            {
                return precheck;
            }

            return await SendAsync(() => conn.Reducers.Register(username.Trim(), password), username.Trim());
        }

        /// <summary>登录。</summary>
        public async UniTask<AuthResult> LoginAsync(string username, string password)
        {
            string invalid = AuthValidation.CheckForLogin(username, password);
            if (invalid != null)
            {
                return AuthResult.Fail(invalid);
            }

            var precheck = CheckCanSend();
            if (!precheck.Ok)
            {
                return precheck;
            }

            return await SendAsync(() => conn.Reducers.Login(username.Trim(), password), username.Trim());
        }

        /// <summary>
        /// 登出。服务端会同时**解除免密绑定**，所以下次连上不会被自动登录回去。
        /// </summary>
        public async UniTask<AuthResult> LogoutAsync()
        {
            if (!IsLoggedIn)
            {
                return AuthResult.Success();
            }

            var precheck = CheckCanSend();
            if (!precheck.Ok)
            {
                return precheck;
            }

            return await SendAsync(() => conn.Reducers.Logout(), null);
        }

        /// <summary>断线 / 连接失败后重连。</summary>
        public void RetryConnect()
        {
            if (SpacetimeConnection.Instance == null)
            {
                Debug.LogError("[Auth] 场景里没有 SpacetimeConnection，连不上服务器");
                return;
            }

            SpacetimeConnection.Instance.Connect();
        }

        // ------------------------------------------------------------------
        // 请求收发
        // ------------------------------------------------------------------

        private AuthResult CheckCanSend()
        {
            if (LinkState != ServerLinkState.Connected || conn == null || !conn.IsActive)
            {
                return AuthResult.Fail("还没连上服务器，请稍后再试");
            }

            if (VersionMismatch && BlockRequestsOnVersionMismatch)
            {
                return AuthResult.Fail(VersionMessage);
            }

            if (!IsAuthReady)
            {
                return AuthResult.Fail("正在同步账号数据，请稍后再试");
            }

            if (IsBusy)
            {
                return AuthResult.Fail("正在处理上一次请求，请稍等");
            }

            return AuthResult.Success();
        }

        /// <summary>
        /// 调 Reducer 并等结果。
        ///
        /// 结果只可能从三个地方来：Reducer 回调（成功/失败）、连接断开、超时。
        /// 三条路都会把 <see cref="pending"/> 兑掉，所以不会卡死在 await 上。
        /// </summary>
        private async UniTask<AuthResult> SendAsync(Action call, string fallbackUsername)
        {
            pending = new UniTaskCompletionSource<AuthResult>();

            try
            {
                call();
            }
            catch (Exception ex)
            {
                pending = null;
                Debug.LogError($"[Auth] 调用 Reducer 失败：{ex}");
                return AuthResult.Fail("请求发送失败，请检查网络");
            }

            // 两参数的 WhenAny 返回 (赢的是第几个, 第一个的结果, 第二个的结果)
            var (winner, answer, timeout) = await UniTask.WhenAny(pending.Task, TimeoutAsync());
            var result = winner == 0 ? answer : timeout;
            pending = null;

            if (!result.Ok)
            {
                return result;
            }

            // Reducer 提交成功了，但 UI 要显示用户名，得等会话行进缓存。
            // 正常情况下它和 Reducer 回调在同一个事务消息里，这里只是兜底。
            if (fallbackUsername != null)
            {
                await WaitForSessionRowAsync(fallbackUsername);
            }

            return AuthResult.Success();
        }

        private static async UniTask<AuthResult> TimeoutAsync()
        {
            await UniTask.Delay(TimeSpan.FromSeconds(RequestTimeoutSeconds), DelayType.Realtime);
            return AuthResult.Fail("服务器没有响应，请检查网络后重试");
        }

        private async UniTask WaitForSessionRowAsync(string fallbackUsername)
        {
            float deadline = Time.realtimeSinceStartup + SessionRowWaitSeconds;
            while (!IsLoggedIn && Time.realtimeSinceStartup < deadline)
            {
                await UniTask.Yield();
            }

            if (IsLoggedIn)
            {
                return;
            }

            // 走到这说明订阅没把会话行推下来（订阅条件写错、或者服务端行为变了）。
            // 请求本身是成功的，所以先用玩家输入的名字顶着，别让界面显示成未登录。
            Debug.LogWarning($"[Auth] 登录已提交但会话行没同步下来，先用输入的用户名占位：{fallbackUsername}");
            Username = fallbackUsername;
        }

        // ------------------------------------------------------------------
        // 连接生命周期
        // ------------------------------------------------------------------

        private void HandleLinkStateChanged(ServerLinkState state)
        {
            LinkStateChanged?.Invoke(state);
        }

        private void HandleConnected()
        {
            conn = SpacetimeConnection.Conn;
            if (conn == null)
            {
                return;
            }

            IsAuthReady = false;
            VersionMismatch = false;
            VersionMessage = string.Empty;
            ClearAccount();

            HookConnection();
            SubscribeAuthTables();
            CheckVersionAsync().Forget();
        }

        /// <summary>
        /// 连上就对一次版本号。
        ///
        /// 用独立的等待槽（不是 <see cref="pending"/>）：版本校验和玩家点的登录可能撞在一起，
        /// 共用一个槽会互相把结果吃掉。
        /// </summary>
        private async UniTask CheckVersionAsync()
        {
            versionPending = new UniTaskCompletionSource<AuthResult>();

            try
            {
                conn.Reducers.CheckVersion(Application.version);
            }
            catch (Exception ex)
            {
                versionPending = null;
                Debug.LogError($"[Version] 版本校验没发出去：{ex.Message}");
                return;
            }

            var (winner, answer, _) = await UniTask.WhenAny(versionPending.Task, TimeoutAsync());
            versionPending = null;

            if (winner != 0)
            {
                // 超时：可能是网卡了，也可能是服务端还没有 CheckVersion 这个 Reducer
                // （服务端没重新 publish）。这两种都不是「版本不一致」，只记日志。
                Debug.LogWarning("[Version] 版本校验超时，跳过（不当作版本不一致）");
                return;
            }

            if (answer.Ok)
            {
                Debug.Log($"[Version] 版本一致：{Application.version}");
                return;
            }

            VersionMismatch = true;
            VersionMessage = answer.Message;
            Debug.LogError($"[Version] {answer.Message.Replace('\n', ' ')}");
            VersionMismatched?.Invoke(VersionMessage);
        }

        private void HandleDisconnected(Exception ex)
        {
            // 登录态是**跟着连接**的：连接没了，服务端那边的 Session 行也被清了。
            // 免密绑定还在，所以重连后会自动恢复，但当下必须显示成未登录。
            UnhookConnection();
            IsAuthReady = false;
            ClearAccount();

            pending?.TrySetResult(AuthResult.Fail("与服务器的连接已断开"));
        }

        private void HandleConnectFailed(Exception ex)
        {
            UnhookConnection();
            IsAuthReady = false;
            ClearAccount();

            pending?.TrySetResult(AuthResult.Fail("连不上服务器"));
        }

        private void HookConnection()
        {
            conn.Db.Session.OnInsert += HandleSessionInsert;
            conn.Db.Session.OnDelete += HandleSessionDelete;
            conn.Db.SessionClosed.OnInsert += HandleSessionClosed;

            conn.Reducers.OnRegister += HandleRegisterResult;
            conn.Reducers.OnLogin += HandleLoginResult;
            conn.Reducers.OnLogout += HandleLogoutResult;
            conn.Reducers.OnCheckVersion += HandleCheckVersionResult;
        }

        private void UnhookConnection()
        {
            if (conn == null)
            {
                return;
            }

            conn.Db.Session.OnInsert -= HandleSessionInsert;
            conn.Db.Session.OnDelete -= HandleSessionDelete;
            conn.Db.SessionClosed.OnInsert -= HandleSessionClosed;

            conn.Reducers.OnRegister -= HandleRegisterResult;
            conn.Reducers.OnLogin -= HandleLoginResult;
            conn.Reducers.OnLogout -= HandleLogoutResult;
            conn.Reducers.OnCheckVersion -= HandleCheckVersionResult;

            conn = null;
        }

        /// <summary>
        /// 建立账号相关的订阅。
        ///
        /// 只订自己 identity 的行：session 表是公开的（全服在线列表），全订下来白占带宽，
        /// session_closed 全订下来还会收到别人被顶号的通知。
        /// identity 在 SQL 里是十六进制字面量，要带 <c>0x</c> 前缀；
        /// <c>Identity.ToString()</c> 给的是不带前缀的大写 hex（大小写都能匹配）。
        /// </summary>
        private void SubscribeAuthTables()
        {
            string identityHex = "0x" + SpacetimeConnection.LocalIdentity;

            conn.SubscriptionBuilder()
                .OnApplied(HandleAuthSubscriptionApplied)
                .OnError(HandleAuthSubscriptionError)
                .Subscribe(new[]
                {
                    $"SELECT * FROM session WHERE identity = {identityHex}",
                    $"SELECT * FROM session_closed WHERE identity = {identityHex}",
                });
        }

        private void HandleAuthSubscriptionApplied(SubscriptionEventContext ctx)
        {
            // 订阅生效时缓存里就有初始数据了：有自己这条连接的会话行，
            // 说明服务端已经按 Identity 免密恢复了登录态。
            RefreshFromCache();

            IsAuthReady = true;
            Debug.Log(IsLoggedIn
                ? $"[Auth] 免密恢复登录：{Username}（account={AccountId}）"
                : "[Auth] 未登录，等玩家输入账号");

            AuthReady?.Invoke();
        }

        private void HandleAuthSubscriptionError(ErrorContext ctx, Exception ex)
        {
            Debug.LogError($"[Auth] 账号订阅失败：{ex.Message}");

            // 订阅没成，登录态就永远不可信，直接把等待中的请求兑掉，别让 UI 卡住
            IsAuthReady = false;
            pending?.TrySetResult(AuthResult.Fail("账号数据同步失败，请重连"));
        }

        // ------------------------------------------------------------------
        // 表回调 / Reducer 回调
        // ------------------------------------------------------------------

        /// <summary>
        /// 从缓存里找**自己这条连接**的会话行。
        ///
        /// 按 ConnectionId 而不是 Identity 找：服务端的会话是按连接建的，同一个 Identity
        /// 可能有多条连接（同一台机器开了两个客户端），别人的行不代表我登录了。
        /// </summary>
        private void RefreshFromCache()
        {
            if (conn == null)
            {
                ClearAccount();
                return;
            }

            if (conn.Db.Session.ConnectionId.Find(conn.ConnectionId) is { } session)
            {
                SetAccount(session);
            }
            else
            {
                ClearAccount();
            }
        }

        private void HandleSessionInsert(EventContext ctx, Session row)
        {
            if (conn != null && row.ConnectionId == conn.ConnectionId)
            {
                SetAccount(row);
            }
        }

        private void HandleSessionDelete(EventContext ctx, Session row)
        {
            if (conn != null && row.ConnectionId == conn.ConnectionId)
            {
                ClearAccount();
            }
        }

        private void HandleSessionClosed(EventContext ctx, SessionClosed row)
        {
            // 事件表的行只会推一次（提交时推给订阅者然后立刻删除），表里查不到历史
            if (conn != null && row.ConnectionId != conn.ConnectionId)
            {
                // 同一 Identity 的**另一条**连接被关了，跟本客户端无关
                return;
            }

            Debug.Log($"[Auth] 会话被服务端关闭：{row.Reason}");
            SessionClosedByServer?.Invoke(row.Reason);
        }

        private void HandleRegisterResult(ReducerEventContext ctx, string username, string password)
        {
            CompletePending(ctx, "注册");
        }

        private void HandleLoginResult(ReducerEventContext ctx, string username, string password)
        {
            CompletePending(ctx, "登录");
        }

        private void HandleLogoutResult(ReducerEventContext ctx)
        {
            CompletePending(ctx, "登出");
        }

        /// <summary>
        /// 版本校验的结果。只有服务端明确抛错（Failed）才算版本不一致：
        /// OutOfEnergy 这类故意不兑结果，让它走超时逻辑 —— 那是服务器状态问题，不是版本问题。
        /// </summary>
        private void HandleCheckVersionResult(ReducerEventContext ctx, string clientVersion)
        {
            if (ctx.Event.CallerIdentity != SpacetimeConnection.LocalIdentity)
            {
                return;
            }

            switch (ctx.Event.Status)
            {
                case Status.Committed:
                    versionPending?.TrySetResult(AuthResult.Success());
                    break;

                case Status.Failed(var reason):
                    versionPending?.TrySetResult(AuthResult.Fail(reason));
                    break;
            }
        }

        /// <summary>
        /// 把 Reducer 的执行结果兑给等待中的请求。
        ///
        /// 2.x 起没有全局 Reducer 回调了，这里收到的**只会是自己发起的调用**，
        /// 但还是核一下 CallerIdentity，免得以后协议变了埋个坑。
        /// </summary>
        private void CompletePending(ReducerEventContext ctx, string what)
        {
            if (ctx.Event.CallerIdentity != SpacetimeConnection.LocalIdentity)
            {
                return;
            }

            switch (ctx.Event.Status)
            {
                case Status.Committed:
                    pending?.TrySetResult(AuthResult.Success());
                    break;

                case Status.Failed(var reason):
                    // reason 就是服务端 AuthRules.Reject 抛的那句中文，可以直接显示
                    Debug.Log($"[Auth] {what}失败：{reason}");
                    pending?.TrySetResult(AuthResult.Fail(reason));
                    break;

                case Status.OutOfEnergy:
                    Debug.LogError($"[Auth] {what}失败：服务端能量不足");
                    pending?.TrySetResult(AuthResult.Fail("服务器繁忙，请稍后再试"));
                    break;
            }
        }

        private void SetAccount(Session session)
        {
            if (AccountId == session.AccountId && Username == session.Username)
            {
                return;
            }

            AccountId = session.AccountId;
            Username = session.Username;
            LoginStateChanged?.Invoke();
        }

        private void ClearAccount()
        {
            if (AccountId == 0 && string.IsNullOrEmpty(Username))
            {
                return;
            }

            AccountId = 0;
            Username = string.Empty;
            LoginStateChanged?.Invoke();
        }
    }
}
