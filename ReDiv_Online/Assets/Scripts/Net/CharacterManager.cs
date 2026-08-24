using System;
using System.Collections.Generic;
using System.Linq;
using ReDiv.Net.Bindings;
using SpacetimeDB.ClientApi;
using UnityEngine;

namespace ReDiv.Net
{
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

            // 连上时不一定已经登录（多数情况下是免密恢复中），所以这里只做准备，
            // 真正订阅要等 LoginStateChanged 说「登录好了」。
            HandleLoginStateChanged();
        }

        /// <summary>断线和连接失败都是同一件事：订阅没了，清空重来。</summary>
        private void HandleConnectionLost(Exception ex)
        {
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
    }
}
