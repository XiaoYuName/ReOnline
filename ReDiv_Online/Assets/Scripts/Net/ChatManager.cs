using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ReDiv.Net.Bindings;
using SpacetimeDB;
using UnityEngine;

namespace ReDiv.Net
{
    /// <summary>
    /// 一次发言请求的结果。失败时 <see cref="Message"/> 是可以直接显示给玩家的中文文案 ——
    /// 要么来自 <see cref="ChatValidation"/>，要么就是服务端抛的那句原文。
    ///
    /// 和 <see cref="CharacterResult"/> / <see cref="AuthResult"/> 形状一样但**故意分开**：
    /// 三个门面各自演进，一个叫 CharacterResult 的东西从聊天接口返回读起来别扭。
    /// </summary>
    public readonly struct ChatResult
    {
        public readonly bool Ok;
        public readonly string Message;

        private ChatResult(bool ok, string message)
        {
            Ok = ok;
            Message = message;
        }

        public static ChatResult Success() => new ChatResult(true, string.Empty);

        public static ChatResult Fail(string message) => new ChatResult(false, message);
    }

    /// <summary>
    /// 聊天门面 —— 界面唯一要打交道的类，和另外三个门面一个套路：
    /// **界面只读它、只听事件，不碰 <c>Conn</c>**。
    ///
    /// 分工：
    ///   SpacetimeConnection  连接生命周期，不建任何订阅
    ///   AuthManager          账号
    ///   CharacterManager     角色
    ///   TownManager          城镇 / 世界时间 / 同城镇玩家 / 坐标
    ///   ChatManager          聊天消息  ← 本类
    ///
    /// **订阅跟着当前城镇走。** 附近消息的可见域就是城镇 id，而城镇是运行时才知道的、
    /// 订阅 SQL 是静态字符串 ⇒ 换城镇必须重订。所以这里挂在
    /// <see cref="TownManager.LocationChanged"/> 上，和 TownManager 自己那段
    /// 「同城镇玩家」订阅是同一个模式（**先订新的再退旧的**，避免中间出现数据空窗）。
    ///
    /// **为什么依赖 TownManager 而不是自己去读 <c>character_selection</c>**：
    /// 「我在哪个城镇」这件事已经有权威来源了，再算一遍就是两份实现慢慢对不上。
    /// 门面之间有依赖是既有约定（CharacterManager 就依赖 AuthManager 的登录态）。
    ///
    /// ⚠️ 表回调**必须把 OnUpdate 一起挂**：同主键的删+插如果在同一事务里会被
    /// 合并成一次 update，只挂 Insert/Delete 会漏。聊天消息本身是只插不改的，
    /// 但这条约定在整个工程里是无条件的，别在这里破例（真出问题时表现成「少一条消息」，
    /// 极难往回追）。
    /// </summary>
    public sealed class ChatManager
    {
        private static ChatManager instance;

        public static ChatManager Instance
        {
            get
            {
                instance ??= new ChatManager();
                instance.Attach();
                return instance;
            }
        }

        private ChatManager()
        {
        }

        // ------------------------------------------------------------------
        // 频道
        // ------------------------------------------------------------------

        /// <summary>附近频道。只有同城镇的人看得到。对应服务端 <c>Module.ChatChannelNearby</c>。</summary>
        public const uint ChannelNearby = 1;

        /// <summary>世界频道。所有人在任何地方都看得到。对应服务端 <c>Module.ChatChannelWorld</c>。</summary>
        public const uint ChannelWorld = 2;

        /// <summary>
        /// 世界消息占用的「域 id」（服务端 <c>Module.ChatWorldScopeTownId</c>）。
        /// 城镇 id 是正数，所以 0 永远不会和某个真城镇撞上。
        ///
        /// ⚠️ 世界频道的订阅**还没接**（用户 2026-08-26：先做附近，测通了再做世界）。
        /// 接的时候在 <see cref="SyncTownSubscription"/> 里多订一句
        /// <c>WHERE town_id = 0</c>、并在 <see cref="IsVisible"/> 里放行这个域即可。
        /// </summary>
        public const uint WorldScopeTownId = 0;

        // ------------------------------------------------------------------
        // 对外状态
        // ------------------------------------------------------------------

        /// <summary>
        /// 聊天订阅是否已生效。**为 false 时 <see cref="Messages"/> 不可信**
        /// （可能只是还没同步完，不代表这个城镇没人说过话）。
        /// </summary>
        public bool IsReady { get; private set; }

        /// <summary>订阅生效时触发。**每次换城镇都会再触发一次**（那是一次新的订阅）。</summary>
        public event Action Ready;

        /// <summary>
        /// 当前可见的消息，按 <c>(SentAt, MessageId)</c> **升序** —— 最新的在最后，
        /// 界面从上往下摆就是发言顺序。
        ///
        /// 条数上限由服务端的保留窗口决定（每个域 50 条），所以这个列表不会无限长。
        /// </summary>
        public IReadOnlyList<ChatMessage> Messages => messages;

        /// <summary>消息列表变了（有新消息 / 换城镇 / 旧消息被裁掉）。界面重画就行。</summary>
        public event Action MessagesChanged;

        /// <summary>
        /// **刚刚**有人说了一句话。参数就是那条消息 —— 城镇里的说话气泡靠它触发。
        ///
        /// 和 <see cref="MessagesChanged"/> 的区别是「哪些是新的」：那个只说
        /// 「列表变了，重画吧」，聊天框重画一遍就完事；气泡必须知道**是谁刚说的**，
        /// 光看列表变化推不出来。
        ///
        /// ⚠️ **订阅刚生效时补下来的历史不算新消息，不会触发这个事件。**
        /// 那 50 条历史也是走 `OnInsert` 下来的，不挡住的话一进城镇每个人头上
        /// 会同时炸出一串气泡。判断方式见 <see cref="HandleMessageInsert"/>。
        /// </summary>
        public event Action<ChatMessage> MessageArrived;

        /// <summary>有发言请求正在等服务端回应。界面据此禁用发送按钮，防连点。</summary>
        public bool IsBusy => sendPending.Busy;

        private readonly List<ChatMessage> messages = new List<ChatMessage>();

        // ------------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------------

        private DbConnection conn;
        private bool attached;
        private bool tablesHooked;
        private bool reducersHooked;

        private SubscriptionHandle townSubscription;

        /// <summary>townSubscription 现在订的是哪个城镇。0 = 没订。</summary>
        private uint subscribedTownId;

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

            // 城镇门面先起来：所在城镇一变就跟着换订阅
            var town = TownManager.Instance;
            town.LocationChanged += SyncTownSubscription;
            town.Ready += SyncTownSubscription;

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

            // 表回调和 Reducer 回调都挂在**连接**上，不跟订阅走：
            // 订阅要等进城镇才建，而 Reducer 的结果是调用方专属的，和订不订阅没关系
            HookTables();
            HookReducers();

            // 连上时不一定已经在城镇里（多数情况下还在登录 / 选人）。
            // 真的进了城镇 TownManager 会喊 LocationChanged，那时候才订
            SyncTownSubscription();
        }

        /// <summary>断线和连接失败都是同一件事：订阅没了，清空重来。</summary>
        private void HandleConnectionLost(Exception ex)
        {
            // 先把等待中的请求兑掉，否则界面上的发送按钮会一直灰到超时
            sendPending.Complete(ChatResult.Fail("与服务器的连接已断开"));

            UnhookReducers();
            UnhookTables();

            TryUnsubscribe(townSubscription);
            townSubscription = null;
            subscribedTownId = 0;

            IsReady = false;
            ClearMessages();

            conn = null;
        }

        private void HandleConnectFailed(Exception ex) => HandleConnectionLost(ex);

        // ------------------------------------------------------------------
        // 订阅：跟着当前城镇走
        // ------------------------------------------------------------------

        /// <summary>
        /// 让聊天订阅跟上当前城镇。**幂等** —— 城镇没变就什么都不做
        /// （它挂在 LocationChanged / Ready 上，这两个会连着触发）。
        ///
        /// ⚠️ 官方建议「更新订阅时先订新的再退旧的」，避免中间出现一段没有数据的空窗。
        /// 这里就是这个顺序，和 <see cref="TownManager"/> 那段同城镇玩家订阅一致。
        /// </summary>
        private void SyncTownSubscription()
        {
            if (conn == null || !conn.IsActive)
            {
                return;
            }

            uint townId = TownManager.Instance.CurrentTownId;

            if (townId == subscribedTownId)
            {
                return;
            }

            SubscriptionHandle previous = townSubscription;

            if (townId == 0)
            {
                // 离开城镇了（回选人界面 / 断线）：先清本地列表，再退订
                townSubscription = null;
                subscribedTownId = 0;
                IsReady = false;
                ClearMessages();
                TryUnsubscribe(previous);
                return;
            }

            subscribedTownId = townId;
            IsReady = false;

            townSubscription = conn.SubscriptionBuilder()
                .OnApplied(HandleSubscriptionApplied)
                .OnError((_, ex) => Debug.LogError($"[Chat] 聊天订阅失败：{ex.Message}"))
                .Subscribe(new[]
                {
                    // 附近消息：域就是城镇 id。世界消息以后在这里多加一句
                    // "SELECT * FROM chat_message WHERE town_id = 0"
                    $"SELECT * FROM chat_message WHERE town_id = {townId}",
                });

            // 先订新的、再退旧的
            TryUnsubscribe(previous);
        }

        private void HandleSubscriptionApplied(SubscriptionEventContext ctx)
        {
            // 订阅生效时缓存里已经有这个域的全部消息了，一次性刷出来
            RefreshFromCache();

            IsReady = true;
            Debug.Log($"[Chat] 城镇 {subscribedTownId} 的聊天已同步：{messages.Count} 条");

            Ready?.Invoke();
        }

        private static void TryUnsubscribe(SubscriptionHandle handle)
        {
            if (handle == null)
            {
                return;
            }

            try
            {
                handle.Unsubscribe();
            }
            catch (Exception)
            {
                // 连接已经断了的话退订会抛，这时候本来也不用退了
            }
        }

        // ------------------------------------------------------------------
        // 表回调
        // ------------------------------------------------------------------

        private void HookTables()
        {
            if (tablesHooked)
            {
                return;
            }
            tablesHooked = true;

            // OnUpdate 一定要挂，理由见类注释
            conn.Db.ChatMessage.OnInsert += HandleMessageInsert;
            conn.Db.ChatMessage.OnUpdate += HandleMessageUpdate;
            conn.Db.ChatMessage.OnDelete += HandleMessageDelete;
        }

        private void UnhookTables()
        {
            if (!tablesHooked || conn == null)
            {
                tablesHooked = false;
                return;
            }
            tablesHooked = false;

            conn.Db.ChatMessage.OnInsert -= HandleMessageInsert;
            conn.Db.ChatMessage.OnUpdate -= HandleMessageUpdate;
            conn.Db.ChatMessage.OnDelete -= HandleMessageDelete;
        }

        /// <summary>
        /// 新增一行。除了刷列表，还要判断这算不算「刚刚有人说话」（触发气泡）。
        ///
        /// ⚠️ **订阅刚生效时，那一批历史消息也是走这个回调下来的。**
        /// 区分办法是看事件来源：<c>Event.Reducer</c> 表示这一行是某次真实的
        /// Reducer 调用产生的（有人真的按了发送），<c>Event.SubscribeApplied</c>
        /// 才是订阅回填。不判这一下的话，一进城镇满屏气泡。
        ///
        /// 不用「拿消息时间和本地时钟比」那种办法：`SentAt` 是**服务端**时间，
        /// 和玩家本地时钟差多少不知道，那个判断本身就不可靠。
        /// </summary>
        private void HandleMessageInsert(EventContext ctx, ChatMessage row)
        {
            // 先刷列表再发气泡事件：这样界面重画和气泡看到的是同一份数据
            RefreshMessages();

            if (ctx.Event is not Event<Reducer>.Reducer)
            {
                return;
            }

            if (!IsVisible(row))
            {
                return;
            }

            MessageArrived?.Invoke(row);
        }

        private void HandleMessageUpdate(EventContext ctx, ChatMessage oldRow, ChatMessage newRow) =>
            RefreshMessages();

        private void HandleMessageDelete(EventContext ctx, ChatMessage row) => RefreshMessages();

        private void RefreshMessages()
        {
            RefreshFromCache();
            MessagesChanged?.Invoke();
        }

        /// <summary>
        /// 整表重读，不做增量维护。
        ///
        /// 一个域最多 50 条（服务端的保留窗口），重读一次的开销可以忽略；
        /// 而增量维护要处理「服务端裁剪掉旧消息」「换城镇时新旧两个域的行同时在缓存里」
        /// 这些情况，出错了表现成「聊天框少一条 / 混进别的城镇的消息」，很难查。
        /// 角色列表和城镇状态那两个门面也是这么做的。
        ///
        /// ⚠️ **必须按 <see cref="IsVisible"/> 过滤**，不能直接把 Iter() 全收下：
        /// 换城镇的那一小段时间里「先订新的、再退旧的」会让**两个城镇的消息同时在缓存里**，
        /// 不过滤的话会把上一个城镇的对话混进来。
        /// </summary>
        private void RefreshFromCache()
        {
            messages.Clear();

            if (conn == null)
            {
                return;
            }

            foreach (var row in conn.Db.ChatMessage.Iter())
            {
                if (IsVisible(row))
                {
                    messages.Add(row);
                }
            }

            // 按 (SentAt, MessageId) 升序 —— 最新的在最后。
            // ⚠️ **不能只按 MessageId 排**：官方规则明确说自增 id 不保证连续、也不保证
            // 单调（ReDiv_Server/CLAUDE.md 的 Critical Rules 第 4 条）。
            // MessageId 只当平局裁判，保证时间戳相同时顺序稳定
            messages.Sort(CompareByTime);
        }

        private static int CompareByTime(ChatMessage a, ChatMessage b)
        {
            int byTime = a.SentAt.MicrosecondsSinceUnixEpoch.CompareTo(b.SentAt.MicrosecondsSinceUnixEpoch);
            return byTime != 0 ? byTime : a.MessageId.CompareTo(b.MessageId);
        }

        /// <summary>
        /// 这条消息该不该显示。现在只有附近（当前城镇）这一个域；
        /// 世界频道接上之后这里再放行 <see cref="WorldScopeTownId"/>。
        /// </summary>
        private bool IsVisible(ChatMessage row) => row.TownId == subscribedTownId && subscribedTownId != 0;

        private void ClearMessages()
        {
            if (messages.Count == 0)
            {
                return;
            }

            messages.Clear();
            MessagesChanged?.Invoke();
        }

        // ------------------------------------------------------------------
        // 发言
        // ------------------------------------------------------------------

        /// <summary>一次请求等服务端回应的上限。超时只是放弃等待，不代表服务端没执行。</summary>
        private const float RequestTimeoutSeconds = 10f;

        /// <summary>
        /// 一个等待中的请求。发言只有一个槽 —— 界面在 <see cref="IsBusy"/> 时禁用按钮，
        /// 所以同时最多一条在飞。（角色那边查重 / 创建 / 删除各占一个槽，
        /// 是因为它们可以并发；这里没有这个需要。）
        /// </summary>
        private sealed class PendingSlot
        {
            public UniTaskCompletionSource<ChatResult> Source;

            public bool Busy => Source != null;

            public void Complete(ChatResult result) => Source?.TrySetResult(result);
        }

        private readonly PendingSlot sendPending = new PendingSlot();

        /// <summary>
        /// 发一条**附近消息**：只有和自己在同一个城镇的人收得到。
        ///
        /// 「发到哪个城镇」由服务端从选角行读，**客户端不传** ——
        /// 传了的话改过的客户端就能往任意城镇喊话。
        ///
        /// 成功后消息会通过订阅推回来（**包括自己发的那条**），
        /// <see cref="MessagesChanged"/> 跟着触发，界面自己就重画了 ——
        /// ⚠️ **不要在本地先塞一条乐观显示的消息**：那条和推回来的那条会重复，
        /// 而且被服务端拒掉时还得再去把它抠出来。
        /// </summary>
        public async UniTask<ChatResult> SendNearbyAsync(string content)
        {
            string text = ChatValidation.Normalize(content, out string invalid);

            if (text == null)
            {
                return ChatResult.Fail(invalid);
            }

            var precheck = CheckCanSend();

            if (!precheck.Ok)
            {
                return precheck;
            }

            sendPending.Source = new UniTaskCompletionSource<ChatResult>();

            try
            {
                conn.Reducers.SendNearbyMessage(text);
            }
            catch (Exception ex)
            {
                sendPending.Source = null;
                Debug.LogError($"[Chat] 调用 Reducer 失败：{ex}");
                return ChatResult.Fail("消息发送失败，请检查网络");
            }

            var (winner, answer, timeout) = await UniTask.WhenAny(sendPending.Source.Task, TimeoutAsync());
            sendPending.Source = null;

            return winner == 0 ? answer : timeout;
        }

        private ChatResult CheckCanSend()
        {
            if (conn == null || !conn.IsActive)
            {
                return ChatResult.Fail("还没连上服务器，请稍后再试");
            }

            // 服务端的鉴权就是「本连接有没有选角行」，本地先照着判一次，
            // 少一次注定失败的往返
            if (TownManager.Instance.CurrentTownId == 0)
            {
                return ChatResult.Fail("请先进入城镇再发言");
            }

            if (IsBusy)
            {
                return ChatResult.Fail("正在发送上一条消息，请稍等");
            }

            return ChatResult.Success();
        }

        private static async UniTask<ChatResult> TimeoutAsync()
        {
            await UniTask.Delay(TimeSpan.FromSeconds(RequestTimeoutSeconds), DelayType.Realtime);
            return ChatResult.Fail("服务器没有响应，请检查网络后重试");
        }

        private void HookReducers()
        {
            if (reducersHooked)
            {
                return;
            }
            reducersHooked = true;

            conn.Reducers.OnSendNearbyMessage += HandleSendResult;
        }

        private void UnhookReducers()
        {
            if (!reducersHooked || conn == null)
            {
                reducersHooked = false;
                return;
            }
            reducersHooked = false;

            conn.Reducers.OnSendNearbyMessage -= HandleSendResult;
        }

        /// <summary>
        /// 把 Reducer 的执行状态兑给等待中的请求。
        ///
        /// 2.x 起没有全局 Reducer 回调，这里收到的只会是自己发起的调用，
        /// 但还是核一下 CallerIdentity，免得以后协议变了埋个坑
        /// （和角色门面同一个写法）。
        /// </summary>
        private void HandleSendResult(ReducerEventContext ctx, string content)
        {
            if (ctx.Event.CallerIdentity != SpacetimeConnection.LocalIdentity)
            {
                return;
            }

            switch (ctx.Event.Status)
            {
                case Status.Committed:
                    sendPending.Complete(ChatResult.Success());
                    break;

                case Status.Failed(var reason):
                    // reason 是服务端 ChatRules.Reject 抛的中文原文，可直接显示
                    Debug.Log($"[Chat] 发言被拒：{reason}");
                    sendPending.Complete(ChatResult.Fail(reason));
                    break;

                case Status.OutOfEnergy:
                    Debug.LogError("[Chat] 发言失败：服务端能量不足");
                    sendPending.Complete(ChatResult.Fail("服务器繁忙，请稍后再试"));
                    break;
            }
        }
    }
}
