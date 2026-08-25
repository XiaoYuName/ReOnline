using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using ReDiv.Net.Bindings;
using SpacetimeDB;
using SpacetimeDB.ClientApi;
using UnityEngine;

namespace ReDiv.Net
{
    /// <summary>
    /// 一次角色请求（查重 / 创建 / 删除）的结果。
    /// 失败时 <see cref="Message"/> 是可以直接显示给玩家的中文文案 ——
    /// 要么来自 <see cref="CharacterValidation"/>，要么就是服务端抛的那句原文。
    ///
    /// 和 <see cref="AuthResult"/> 形状一样但**故意分开**：两个门面各自演进，
    /// 一个叫 AuthResult 的东西从角色接口返回读起来别扭。
    /// </summary>
    public readonly struct CharacterResult
    {
        public readonly bool Ok;
        public readonly string Message;

        private CharacterResult(bool ok, string message)
        {
            Ok = ok;
            Message = message;
        }

        public static CharacterResult Success() => new CharacterResult(true, string.Empty);

        public static CharacterResult Fail(string message) => new CharacterResult(false, message);
    }

    /// <summary>
    /// 角色门面 —— 选人界面唯一要打交道的类，和 <see cref="AuthManager"/> 一个套路。
    ///
    /// 分工：
    ///   SpacetimeConnection  连接生命周期，不建任何订阅
    ///   AuthManager          账号：session / session_closed 的订阅和登录态
    ///   CharacterManager     角色：my_character / my_account_profile 的订阅和角色列表  ← 本类
    ///   SelectCharacterUI    只读 <see cref="Characters"/>、只听事件，**不碰 Conn**
    ///
    /// ⚠️ **订阅必须分段建**（服务端 README「角色系统」写死的约定）：
    /// 订阅 SQL join 不到会话，所以角色数据不能在连上时就订 —— 那会儿还没登录，
    /// View 返回空。这里挂在 <see cref="AuthManager.LoginStateChanged"/> 上，
    /// 登录成功才订、登出就退订。
    ///
    /// ⚠️ 表回调**必须把 OnUpdate 一起挂**。同主键的删+插如果发生在同一个事务里
    /// （比如觉醒改了星级），SpacetimeDB 会合并成一次 update，只挂 Insert/Delete 会漏。
    /// 带主键的 View 同理 —— 这个坑账号系统那边已经踩过一次了。
    /// </summary>
    public sealed class CharacterManager
    {
        private static CharacterManager instance;

        public static CharacterManager Instance
        {
            get
            {
                instance ??= new CharacterManager();
                instance.Attach();
                return instance;
            }
        }

        private CharacterManager()
        {
        }

        // ------------------------------------------------------------------
        // 对外状态
        // ------------------------------------------------------------------

        /// <summary>
        /// 角色订阅是否已生效。**为 false 时 <see cref="Characters"/> 不可信**
        /// （可能只是还没同步完，不代表这个账号没有角色）。
        /// </summary>
        public bool IsReady { get; private set; }

        /// <summary>订阅生效时触发一次。界面用它决定「转圈」还是「画格子」。</summary>
        public event Action Ready;

        /// <summary>角色列表变了（增 / 删 / 改）。界面重画就行，不用管具体变了哪个。</summary>
        public event Action CharactersChanged;

        /// <summary>
        /// 这个账号已解锁的角色栏位数。来自 <c>my_account_profile</c> View。
        /// 还没同步到就是 0 —— 界面别拿它当「一个格子都没有」。
        /// </summary>
        public uint CharacterSlots { get; private set; }

        private readonly List<MyCharacterRow> characters = new List<MyCharacterRow>();

        /// <summary>
        /// 当前账号的角色列表（服务端已经过滤掉软删的）。
        /// 排序：最近玩过的在前，没玩过的按创建时间 —— 和选人界面的直觉一致。
        /// </summary>
        public IReadOnlyList<MyCharacterRow> Characters => characters;

        // ------------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------------

        private DbConnection conn;
        private bool attached;
        private bool subscribed;
        private SubscriptionHandle subscription;

        private void Attach()
        {
            if (attached)
            {
                return;
            }
            attached = true;

            SpacetimeConnection.Connected += HandleConnected;
            SpacetimeConnection.Disconnected += HandleConnectionLost;
            SpacetimeConnection.ConnectFailed += HandleConnectFailed;

            // 账号门面先起来：登录态一变就跟着订 / 退订
            AuthManager.Instance.LoginStateChanged += HandleLoginStateChanged;

            if (SpacetimeConnection.IsConnected)
            {
                HandleConnected();
            }
        }

        private void HandleConnected()
        {
            conn = SpacetimeConnection.Conn;

            if (conn == null)
            {
                return;
            }

            // Reducer 回调挂在**连接**上，不跟订阅走：订阅要等登录成功才建，
            // 而 Reducer 的结果是调用方专属的，和订不订阅没关系。
            HookReducers();

            // 连上时不一定已经登录（多数情况下是免密恢复中），所以这里只做准备，
            // 真正订阅要等 LoginStateChanged 说「登录好了」。
            HandleLoginStateChanged();
        }

        /// <summary>断线和连接失败都是同一件事：订阅没了，清空重来。</summary>
        private void HandleConnectionLost(Exception ex)
        {
            // 先把等待中的请求兑掉，否则界面会一直转到超时才恢复
            FailPending("与服务器的连接已断开");

            UnhookReducers();
            Unsubscribe();
            conn = null;
        }

        private void HandleConnectFailed(Exception ex) => HandleConnectionLost(ex);

        /// <summary>登录了就订角色数据，登出了就退订并清空。</summary>
        private void HandleLoginStateChanged()
        {
            if (conn == null)
            {
                return;
            }

            if (AuthManager.Instance.IsLoggedIn)
            {
                Subscribe();
            }
            else
            {
                Unsubscribe();
            }
        }

        /// <summary>
        /// 订阅角色数据。
        ///
        /// **不用带 where** —— <c>my_character</c> / <c>my_account_profile</c> 是
        /// per-subscriber View，服务端已经按订阅者自己的 Identity 过滤过了，
        /// 客户端再加条件既没必要、也伪造不了别人的。
        /// </summary>
        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }
            subscribed = true;

            HookTables();

            subscription = conn.SubscriptionBuilder()
                .OnApplied(HandleSubscriptionApplied)
                .OnError(HandleSubscriptionError)
                .Subscribe(new[]
                {
                    "SELECT * FROM my_character",
                    "SELECT * FROM my_account_profile",
                });
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }
            subscribed = false;

            UnhookTables();

            try
            {
                subscription?.Unsubscribe();
            }
            catch (Exception)
            {
                // 连接已经断了的话退订会抛，这时候本来也不用退了
            }

            subscription = null;

            IsReady = false;
            CharacterSlots = 0;
            characters.Clear();
            CharactersChanged?.Invoke();
        }

        private void HookTables()
        {
            // OnUpdate 一定要挂，理由见类注释
            conn.Db.MyCharacter.OnInsert += HandleCharacterInsert;
            conn.Db.MyCharacter.OnUpdate += HandleCharacterUpdate;
            conn.Db.MyCharacter.OnDelete += HandleCharacterDelete;

            conn.Db.MyAccountProfile.OnInsert += HandleProfileInsert;
            conn.Db.MyAccountProfile.OnUpdate += HandleProfileUpdate;
            conn.Db.MyAccountProfile.OnDelete += HandleProfileDelete;
        }

        private void UnhookTables()
        {
            if (conn == null)
            {
                return;
            }

            conn.Db.MyCharacter.OnInsert -= HandleCharacterInsert;
            conn.Db.MyCharacter.OnUpdate -= HandleCharacterUpdate;
            conn.Db.MyCharacter.OnDelete -= HandleCharacterDelete;

            conn.Db.MyAccountProfile.OnInsert -= HandleProfileInsert;
            conn.Db.MyAccountProfile.OnUpdate -= HandleProfileUpdate;
            conn.Db.MyAccountProfile.OnDelete -= HandleProfileDelete;
        }

        private void HandleSubscriptionApplied(SubscriptionEventContext ctx)
        {
            // 订阅生效时缓存里已经有全量数据了，一次性刷出来
            RefreshFromCache();

            IsReady = true;
            Debug.Log($"[Character] 角色数据已同步：{characters.Count} 个角色，栏位 {CharacterSlots}");

            Ready?.Invoke();
        }

        private void HandleSubscriptionError(ErrorContext ctx, Exception ex)
        {
            Debug.LogError($"[Character] 角色订阅失败：{ex.Message}");

            IsReady = false;
            subscribed = false;
        }

        // ------------------------------------------------------------------
        // 表回调
        // ------------------------------------------------------------------

        private void HandleCharacterInsert(EventContext ctx, MyCharacterRow row) => RefreshCharacters();

        private void HandleCharacterUpdate(EventContext ctx, MyCharacterRow oldRow, MyCharacterRow newRow) =>
            RefreshCharacters();

        private void HandleCharacterDelete(EventContext ctx, MyCharacterRow row) => RefreshCharacters();

        private void HandleProfileInsert(EventContext ctx, MyAccountProfileRow row) => RefreshProfile();

        private void HandleProfileUpdate(EventContext ctx, MyAccountProfileRow oldRow, MyAccountProfileRow newRow) =>
            RefreshProfile();

        private void HandleProfileDelete(EventContext ctx, MyAccountProfileRow row) => RefreshProfile();

        /// <summary>
        /// 整表重读，而不是按单行增删改维护本地列表。
        ///
        /// 角色最多个位数，重读一次的开销可以忽略；而增量维护要处理「同事务删+插合并成
        /// update」「排序键变了要重排」这些情况，出错了表现成「界面上少一个角色」，很难查。
        /// </summary>
        private void RefreshCharacters()
        {
            RefreshFromCache();
            CharactersChanged?.Invoke();
        }

        private void RefreshProfile()
        {
            RefreshFromCache();
            CharactersChanged?.Invoke();
        }

        private void RefreshFromCache()
        {
            characters.Clear();

            if (conn == null)
            {
                CharacterSlots = 0;
                return;
            }

            // 最近玩过的排前面；没玩过的（LastPlayedAt 是 null）按创建时间排在后面
            characters.AddRange(conn.Db.MyCharacter.Iter()
                .OrderByDescending(c => c.LastPlayedAt?.MicrosecondsSinceUnixEpoch ?? long.MinValue)
                .ThenBy(c => c.CreatedAt.MicrosecondsSinceUnixEpoch));

            var profile = conn.Db.MyAccountProfile.Iter().FirstOrDefault();
            CharacterSlots = profile.CharacterSlots;
        }

        // ------------------------------------------------------------------
        // 请求：查重 / 创建
        // ------------------------------------------------------------------

        /// <summary>一次请求等服务端回应的上限。超时只是放弃等待，不代表服务端没执行。</summary>
        private const float RequestTimeoutSeconds = 15f;

        /// <summary>
        /// 一个等待中的请求。查重和创建**各占一个槽** —— 共用一个的话，
        /// 两个结果回来时会互相把对方的兑掉（版本校验和登录当初就是为此分开的）。
        /// </summary>
        private sealed class PendingSlot
        {
            public UniTaskCompletionSource<CharacterResult> Source;

            public bool Busy => Source != null;

            public void Complete(CharacterResult result) => Source?.TrySetResult(result);
        }

        private readonly PendingSlot namePending = new PendingSlot();
        private readonly PendingSlot createPending = new PendingSlot();
        private readonly PendingSlot deletePending = new PendingSlot();
        private bool reducersHooked;

        /// <summary>是否有角色请求正在等服务端回应。界面据此禁用按钮。</summary>
        public bool IsBusy => namePending.Busy || createPending.Busy || deletePending.Busy;

        /// <summary>
        /// 角色名查重 —— 「重复」按钮用。
        ///
        /// ⚠️ **查重通过不等于名字被占住**。从查完到真正创建之间别人随时可能抢走，
        /// 所以 <see cref="CreateCharacterAsync"/> 那边照样会失败，界面必须处理。
        /// 服务端也是这么设计的（<c>CheckCharacterName</c> 一张表都不写）。
        /// </summary>
        public async UniTask<CharacterResult> CheckNameAsync(string name)
        {
            string invalid = CharacterValidation.CheckName(name);
            if (invalid != null)
            {
                return CharacterResult.Fail(invalid);
            }

            return await SendAsync(namePending, () => conn.Reducers.CheckCharacterName(name.Trim()));
        }

        /// <summary>
        /// 创建角色。成功后新角色会通过 <c>my_character</c> View 推下来，
        /// <see cref="CharactersChanged"/> 跟着触发，选人界面自己就重画了 ——
        /// 调用方不用手动刷列表。
        /// </summary>
        public async UniTask<CharacterResult> CreateCharacterAsync(string name, uint jobId)
        {
            string invalid = CharacterValidation.CheckName(name);
            if (invalid != null)
            {
                return CharacterResult.Fail(invalid);
            }

            return await SendAsync(createPending, () => conn.Reducers.CreateCharacter(name.Trim(), jobId));
        }

        /// <summary>
        /// 删除角色。服务端是**软删**：行留着（打 DeletedAt），但名字立刻释放出来，
        /// 而且列表里立刻看不到 —— 对玩家来说就是删掉了，**没有恢复入口**。
        ///
        /// 成功后角色从 <c>my_character</c> View 里消失，<see cref="CharactersChanged"/>
        /// 跟着触发，选人界面自己就重画了。
        /// </summary>
        public async UniTask<CharacterResult> DeleteCharacterAsync(ulong characterId)
        {
            if (characterId == 0)
            {
                return CharacterResult.Fail("请先选择一个角色");
            }

            return await SendAsync(deletePending, () => conn.Reducers.DeleteCharacter(characterId));
        }

        private CharacterResult CheckCanSend()
        {
            if (conn == null || !conn.IsActive)
            {
                return CharacterResult.Fail("还没连上服务器，请稍后再试");
            }

            if (!AuthManager.Instance.IsLoggedIn)
            {
                return CharacterResult.Fail("请先登录");
            }

            // IsReady 为 false 时 Characters 不可信，这时候建角色也判不了栏位，先别放行
            if (!IsReady)
            {
                return CharacterResult.Fail("正在同步角色数据，请稍后再试");
            }

            if (IsBusy)
            {
                return CharacterResult.Fail("正在处理上一次请求，请稍等");
            }

            return CharacterResult.Success();
        }

        /// <summary>
        /// 调 Reducer 并等结果。结果只可能从三个地方来：Reducer 回调、连接断开、超时 ——
        /// 三条路都会把槽兑掉，所以不会卡死在 await 上。
        /// </summary>
        private async UniTask<CharacterResult> SendAsync(PendingSlot slot, Action call)
        {
            var precheck = CheckCanSend();
            if (!precheck.Ok)
            {
                return precheck;
            }

            slot.Source = new UniTaskCompletionSource<CharacterResult>();

            try
            {
                call();
            }
            catch (Exception ex)
            {
                slot.Source = null;
                Debug.LogError($"[Character] 调用 Reducer 失败：{ex}");
                return CharacterResult.Fail("请求发送失败，请检查网络");
            }

            var (winner, answer, timeout) = await UniTask.WhenAny(slot.Source.Task, TimeoutAsync());
            slot.Source = null;

            return winner == 0 ? answer : timeout;
        }

        private static async UniTask<CharacterResult> TimeoutAsync()
        {
            await UniTask.Delay(TimeSpan.FromSeconds(RequestTimeoutSeconds), DelayType.Realtime);
            return CharacterResult.Fail("服务器没有响应，请检查网络后重试");
        }

        private void HookReducers()
        {
            if (reducersHooked)
            {
                return;
            }
            reducersHooked = true;

            conn.Reducers.OnCheckCharacterName += HandleCheckNameResult;
            conn.Reducers.OnCreateCharacter += HandleCreateResult;
            conn.Reducers.OnDeleteCharacter += HandleDeleteResult;
        }

        private void UnhookReducers()
        {
            if (!reducersHooked || conn == null)
            {
                reducersHooked = false;
                return;
            }
            reducersHooked = false;

            conn.Reducers.OnCheckCharacterName -= HandleCheckNameResult;
            conn.Reducers.OnCreateCharacter -= HandleCreateResult;
            conn.Reducers.OnDeleteCharacter -= HandleDeleteResult;
        }

        private void HandleCheckNameResult(ReducerEventContext ctx, string name) =>
            Complete(ctx, namePending, "角色名查重");

        private void HandleCreateResult(ReducerEventContext ctx, string name, uint jobId) =>
            Complete(ctx, createPending, "创建角色");

        private void HandleDeleteResult(ReducerEventContext ctx, ulong characterId) =>
            Complete(ctx, deletePending, "删除角色");

        /// <summary>
        /// 把 Reducer 的执行状态兑给等待中的请求。
        ///
        /// 服务端**不返回数据**，所以「名字能不能用」这类答案就藏在执行状态里：
        /// 提交成功 = 可以，失败 = 不行且 reason 就是那句中文原文。
        /// 2.x 起没有全局 Reducer 回调，这里收到的只会是自己发起的调用，
        /// 但还是核一下 CallerIdentity，免得以后协议变了埋个坑。
        /// </summary>
        private static void Complete(ReducerEventContext ctx, PendingSlot slot, string what)
        {
            if (ctx.Event.CallerIdentity != SpacetimeConnection.LocalIdentity)
            {
                return;
            }

            switch (ctx.Event.Status)
            {
                case Status.Committed:
                    slot.Complete(CharacterResult.Success());
                    break;

                case Status.Failed(var reason):
                    // reason 是服务端 CharacterRules.Reject 抛的中文原文，可直接显示
                    Debug.Log($"[Character] {what}被拒绝：{reason}");
                    slot.Complete(CharacterResult.Fail(reason));
                    break;

                case Status.OutOfEnergy:
                    Debug.LogError($"[Character] {what}失败：服务端能量不足");
                    slot.Complete(CharacterResult.Fail("服务器繁忙，请稍后再试"));
                    break;
            }
        }

        /// <summary>连接断了：把等待中的请求全部兑成失败，别让界面干等到超时。</summary>
        private void FailPending(string message)
        {
            namePending.Complete(CharacterResult.Fail(message));
            createPending.Complete(CharacterResult.Fail(message));
            deletePending.Complete(CharacterResult.Fail(message));
        }
    }
}
