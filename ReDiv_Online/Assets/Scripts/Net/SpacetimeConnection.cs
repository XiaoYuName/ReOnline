using System;
using ReDiv.Net.Bindings;
using UnityEngine;
using Identity = SpacetimeDB.Identity;

namespace ReDiv.Net
{
    /// <summary>服务器连接状态。UI 直接拿它显示「服务器已启动 / 连接中 / 连不上」。</summary>
    public enum ServerLinkState
    {
        /// <summary>还没连，或者已经断开。</summary>
        Disconnected = 0,

        /// <summary>正在连。</summary>
        Connecting = 1,

        /// <summary>连上了，identity 已拿到。</summary>
        Connected = 2,

        /// <summary>连接失败（服务器没起、地址不对、网络不通）。</summary>
        Failed = 3,
    }

    /// <summary>
    /// SpacetimeDB 连接管理器。挂在一个常驻 GameObject 上即可。
    ///
    /// 只负责连接生命周期，不含任何玩法逻辑，**也不建立任何订阅** ——
    /// 订阅由各个系统自己管（账号相关的在 <see cref="AuthManager"/> 里），
    /// 官方的建议是按生命周期分组订阅，攒在这里迟早变成一坨谁都不敢删的查询。
    ///
    /// 关于 <c>SpacetimeDBNetworkManager</c>：
    /// SDK 靠它在 Unity 的 Update 里驱动 <c>FrameTick()</c>（把后台线程解析好的消息应用到
    /// 客户端缓存），WebGL 平台还靠它跑消息解析协程。所以它是必需的，不是可选的。
    /// 本脚本会在 Awake 里自动补一个，省得每个场景手动挂 —— 它是单例，重复挂会抛异常，
    /// 因此这里先查了再加。
    ///
    /// 另：FrameTick 会写 <c>Conn.Db</c>，绝不能放到后台线程去调。
    /// </summary>
    [DisallowMultipleComponent]
    public class SpacetimeConnection : MonoBehaviour
    {
        [Header("服务器")]
        [Tooltip("SpacetimeDB 地址。本机开发用 http://127.0.0.1:2383；" +
                 "局域网其它设备（真机调试）要填 http://192.168.10.226:2383")]
        [SerializeField] private string serverUri = "http://127.0.0.1:2383";

        [Tooltip("数据库名，与 ReDiv_Server/spacetime.json 里的 database 一致")]
        [SerializeField] private string databaseName = "rediv";

        [Header("行为")]
        [Tooltip("关掉可降低延迟：服务端不等事务落盘就推送。代价是服务器崩溃时" +
                 "客户端可能已经看到了最终丢失的数据。2.0 起默认开启。")]
        [SerializeField] private bool confirmedReads = true;

        [Tooltip("跨场景保留连接")]
        [SerializeField] private bool dontDestroyOnLoad = true;

        [Tooltip("启动时自动连接")]
        [SerializeField] private bool connectOnStart = true;

        // ------------------------------------------------------------------
        // 对外状态
        // ------------------------------------------------------------------

        public static SpacetimeConnection Instance { get; private set; }

        /// <summary>底层连接。未连接时为 null。</summary>
        public static DbConnection Conn { get; private set; }

        /// <summary>本次连接被服务器识别的身份。连上之前是 default。</summary>
        public static Identity LocalIdentity { get; private set; }

        public static bool IsConnected => Conn != null && Conn.IsActive;

        /// <summary>当前连接状态。</summary>
        public static ServerLinkState LinkState { get; private set; } = ServerLinkState.Disconnected;

        /// <summary>连接状态变化时触发。UI 用这个刷服务器状态显示。</summary>
        public static event Action<ServerLinkState> LinkStateChanged;

        /// <summary>连接建立、拿到 Identity 之后触发。</summary>
        public static event Action Connected;

        /// <summary>连接断开时触发。参数为异常，正常断开时为 null。</summary>
        public static event Action<Exception> Disconnected;

        /// <summary>连接失败时触发。</summary>
        public static event Action<Exception> ConnectFailed;

        // ------------------------------------------------------------------
        // Unity 生命周期
        // ------------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (dontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }

            EnsureNetworkManager();
        }

        private void Start()
        {
            if (connectOnStart)
            {
                Connect();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            Disconnect();
        }

        // ------------------------------------------------------------------
        // 连接
        // ------------------------------------------------------------------

        public void Connect()
        {
            if (Conn != null)
            {
                Debug.LogWarning("[Stdb] 已有连接，忽略这次 Connect");
                return;
            }

            SetLinkState(ServerLinkState.Connecting);

            var builder = DbConnection.Builder()
                .WithUri(serverUri)
                .WithDatabaseName(databaseName)
                .WithConfirmedReads(confirmedReads)
                .OnConnect(HandleConnect)
                .OnConnectError(HandleConnectError)
                .OnDisconnect(HandleDisconnect);

            // 复用上次的身份。没有 token 时服务端会签发一个新身份，
            // 在 OnConnect 里存下来，下次启动就还是同一个玩家。
            var saved = SpacetimeDB.AuthToken.Token;
            if (!string.IsNullOrEmpty(saved))
            {
                builder = builder.WithToken(saved);
            }

            Debug.Log($"[Stdb] 正在连接 {serverUri} / {databaseName}");
            Conn = builder.Build();
        }

        public void Disconnect()
        {
            if (Conn == null)
            {
                return;
            }

            if (Conn.IsActive)
            {
                Conn.Disconnect();
            }
            Conn = null;
            SetLinkState(ServerLinkState.Disconnected);
        }

        // ------------------------------------------------------------------
        // 回调
        // ------------------------------------------------------------------

        private void HandleConnect(DbConnection conn, Identity identity, string token)
        {
            LocalIdentity = identity;

            // WebGL 的浏览器 WebSocket 不能设 Authorization 头，带 token 重连时服务端
            // 可能回一个短期 token；直接覆盖会把长期身份弄丢，所以只在首次落库。
#if UNITY_WEBGL && !UNITY_EDITOR
            if (string.IsNullOrEmpty(SpacetimeDB.AuthToken.Token))
            {
                SpacetimeDB.AuthToken.SaveToken(token);
            }
#else
            SpacetimeDB.AuthToken.SaveToken(token);
#endif

            Debug.Log($"[Stdb] 已连接，identity={identity}");

            // 先切状态再发 Connected：订阅者（AuthManager）在回调里会检查 LinkState
            SetLinkState(ServerLinkState.Connected);
            Connected?.Invoke();
        }

        private void HandleConnectError(Exception ex)
        {
            Debug.LogError($"[Stdb] 连接失败：{ex.Message}");
            Conn = null;
            SetLinkState(ServerLinkState.Failed);
            ConnectFailed?.Invoke(ex);
        }

        private void HandleDisconnect(DbConnection conn, Exception ex)
        {
            if (ex != null)
            {
                Debug.LogError($"[Stdb] 连接断开：{ex.Message}");
            }
            else
            {
                Debug.Log("[Stdb] 连接已关闭");
            }

            Conn = null;
            SetLinkState(ServerLinkState.Disconnected);
            Disconnected?.Invoke(ex);
        }

        // ------------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------------

        private static void SetLinkState(ServerLinkState state)
        {
            if (LinkState == state)
            {
                return;
            }

            LinkState = state;
            LinkStateChanged?.Invoke(state);
        }

        /// <summary>
        /// 确保场景里有且只有一个 SpacetimeDBNetworkManager。
        /// 必须在 Build() 之前完成：SDK 在 Build 时读它的静态实例来注册连接。
        /// </summary>
        private void EnsureNetworkManager()
        {
            var existing = FindAnyObjectByType<SpacetimeDB.SpacetimeDBNetworkManager>(
                FindObjectsInactive.Include);

            if (existing == null)
            {
                gameObject.AddComponent<SpacetimeDB.SpacetimeDBNetworkManager>();
            }
        }
    }
}
