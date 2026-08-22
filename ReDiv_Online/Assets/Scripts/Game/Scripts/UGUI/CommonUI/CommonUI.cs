
using Cysharp.Threading.Tasks;
using ReDiv.Net;
using ReDiv.Net.Bindings;
using UnityEngine;
using XFramework;

/// <summary>
/// 外主界面（标题界面）。
///
/// 除了「点击屏幕开始游戏」，还负责显示两块状态：
///   左下 ServerStatus —— 服务器连接状态，跟着 <see cref="AuthManager.LinkStateChanged"/> 刷
///   左下 LoginInfo    —— 当前登录的账号，跟着 <see cref="AuthManager.LoginStateChanged"/> 刷
///
/// 点屏幕的三种走向：
///   已登录   → 进游戏
///   未登录   → 弹 LoginUI
///   连不上   → 弹重试对话框
/// </summary>
public partial class CommonUI : UIBase
{
    /// <summary>版本号文案节点在 prefab 里的路径（不在 AutoBind 字段里，只能按路径取）。</summary>
    private const string VersionTextPath = "UIMask/ver";

    private static readonly Color ColorOnline = new Color(0.35f, 0.95f, 0.45f);
    private static readonly Color ColorPending = new Color(1f, 0.85f, 0.35f);
    private static readonly Color ColorOffline = new Color(0.95f, 0.40f, 0.40f);

    private bool eventsHooked;
    private bool versionPopupShown;

    public override void Init()
    {
        InitAutoBind();

        // 在这里写其它初始化逻辑。重新生成 UI 绑定时，这个文件不会被覆盖。
        UISystem.Instance.AddUI("CommonUI",this);

        Bind(aVProVideo, OnClickScreen, AudioKeys.CursorClick01);

        HookAuthEvents();
        RefreshVersion();
        RefreshServerStatus();
        RefreshLoginInfo();

        // 版本校验在连上时就跑了，可能比本界面初始化还早（Addressables 初始化更慢），
        // 那种情况下事件已经发过一轮了，所以这里补查一次状态。
        if (AuthManager.Instance.VersionMismatch)
        {
            HandleVersionMismatched(AuthManager.Instance.VersionMessage);
        }
    }

    protected override void OnDestroy()
    {
        UnhookAuthEvents();
        base.OnDestroy();
    }

    // ------------------------------------------------------------------
    // 账号 / 连接状态
    // ------------------------------------------------------------------

    /// <summary>
    /// Init 可能被重复调用（比如以后加了重建界面的流程），事件只挂一次。
    /// AuthManager 的事件是 C# 事件，重复挂会收到重复回调。
    /// </summary>
    private void HookAuthEvents()
    {
        if (eventsHooked)
        {
            return;
        }
        eventsHooked = true;

        var auth = AuthManager.Instance;
        auth.LinkStateChanged += HandleLinkStateChanged;
        auth.LoginStateChanged += RefreshLoginInfo;
        auth.AuthReady += HandleAuthReady;
        auth.SessionClosedByServer += HandleSessionClosedByServer;
        auth.VersionMismatched += HandleVersionMismatched;
    }

    private void UnhookAuthEvents()
    {
        if (!eventsHooked)
        {
            return;
        }
        eventsHooked = false;

        var auth = AuthManager.Instance;
        auth.LinkStateChanged -= HandleLinkStateChanged;
        auth.LoginStateChanged -= RefreshLoginInfo;
        auth.AuthReady -= HandleAuthReady;
        auth.SessionClosedByServer -= HandleSessionClosedByServer;
        auth.VersionMismatched -= HandleVersionMismatched;
    }

    /// <summary>
    /// 客户端 / 服务端版本号不一致。只弹一次，别每次刷状态都糊玩家一脸。
    /// </summary>
    private void HandleVersionMismatched(string message)
    {
        if (versionPopupShown)
        {
            return;
        }
        versionPopupShown = true;

        UIUtility.ShowWindow(message, "版本不一致");
        RefreshLoginInfo();
    }

    private void HandleLinkStateChanged(ServerLinkState state)
    {
        RefreshServerStatus();
        RefreshLoginInfo();
    }

    /// <summary>账号订阅生效：这一刻登录态才可信（服务端可能已经免密恢复了会话）。</summary>
    private void HandleAuthReady()
    {
        RefreshLoginInfo();
    }

    private void HandleSessionClosedByServer(SessionCloseReason reason)
    {
        RefreshLoginInfo();

        // SwitchedAccount 是本机自己换号登录，LoggedOut 是自己点的，都不用提示；
        // 只有被别的设备顶下来才需要告诉玩家发生了什么。
        if (reason == SessionCloseReason.KickedByNewLogin)
        {
            UIUtility.ShowWindow("你的账号在其他设备上登录了，本端已下线。", "已下线");
        }
    }

    /// <summary>
    /// 刷右下角的版本号，值取 <c>Application.version</c>（= Player Settings 里的 Version）。
    ///
    /// 为什么要在运行时刷：prefab 里这行文案原来是写死的「版本：0.0.1」，而
    /// bundleVersion 当时是 1.0 —— 界面上显示的版本和服务端校验用的版本压根不是一个数。
    /// 读同一个源头就不会再漂。
    ///
    /// 这个节点不在 AutoBind 生成的字段里（生成器只认特定命名），所以按路径取。
    /// 以后重命名节点记得跟着改这里。
    /// </summary>
    private void RefreshVersion()
    {
        var versionTex = Get<TMPro.TextMeshProUGUI>(VersionTextPath);
        if (versionTex != null)
        {
            versionTex.text = $"版本：{Application.version}";
        }
    }

    private void RefreshServerStatus()
    {
        if (serverStatusTex == null)
        {
            return;
        }

        switch (AuthManager.Instance.LinkState)
        {
            case ServerLinkState.Connected:
                serverStatusTex.text = "服务器已启动";
                serverStatusTex.color = ColorOnline;
                break;

            case ServerLinkState.Connecting:
                serverStatusTex.text = "连接服务器中…";
                serverStatusTex.color = ColorPending;
                break;

            case ServerLinkState.Failed:
                serverStatusTex.text = "服务器未启动";
                serverStatusTex.color = ColorOffline;
                break;

            default:
                serverStatusTex.text = "服务器已断开";
                serverStatusTex.color = ColorOffline;
                break;
        }
    }

    private void RefreshLoginInfo()
    {
        var auth = AuthManager.Instance;

        if (loginTex != null)
        {
            loginTex.text = auth.IsLoggedIn ? auth.Username : "未登录";
        }

        if (clickTex == null)
        {
            return;
        }

        if (auth.LinkState != ServerLinkState.Connected)
        {
            clickTex.text = "点击屏幕重连服务器";
        }
        else if (auth.VersionMismatch && AuthManager.BlockRequestsOnVersionMismatch)
        {
            clickTex.text = "版本不一致，请更新客户端";
        }
        else if (!auth.IsAuthReady)
        {
            clickTex.text = "正在同步账号数据…";
        }
        else
        {
            clickTex.text = auth.IsLoggedIn ? "点击屏幕开始游戏" : "点击屏幕登录";
        }
    }

    // ------------------------------------------------------------------
    // 交互
    // ------------------------------------------------------------------

    private void OnClickScreen()
    {
        var auth = AuthManager.Instance;

        switch (auth.LinkState)
        {
            case ServerLinkState.Connecting:
                UIUtility.ShowWindow("正在连接服务器，请稍候。", "请稍候");
                return;

            case ServerLinkState.Failed:
            case ServerLinkState.Disconnected:
                UIUtility.ShowDialogue(
                    "连不上服务器。确认服务端已启动后可以重试。",
                    "无法连接",
                    "重试",
                    "取消",
                    () => auth.RetryConnect());
                return;
        }

        if (auth.VersionMismatch && AuthManager.BlockRequestsOnVersionMismatch)
        {
            UIUtility.ShowWindow(auth.VersionMessage, "版本不一致");
            return;
        }

        if (!auth.IsAuthReady)
        {
            UIUtility.ShowWindow("正在同步账号数据，请稍候。", "请稍候");
            return;
        }

        if (!auth.IsLoggedIn)
        {
            UISystem.Instance.OpenUI<LoginUI>(UIKeys.LoginUI);
            return;
        }

        StartGame();
    }

    private void OpenSettings()
    {

    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
        Application.Quit();
    }

    /// <summary>
    /// 登出。prefab 里还没有登出按钮，加了之后 Bind 到这里就行。
    /// 服务端会同时解除免密绑定，所以下次连上不会被自动登录回去。
    /// </summary>
    private void Logout()
    {
        LogoutAsync().Forget();
    }

    private async UniTask LogoutAsync()
    {
        var result = await AuthManager.Instance.LogoutAsync();
        if (!result.Ok)
        {
            UIUtility.ShowWindow(result.Message, "登出失败");
        }
    }

    private void StartGame()
    {
        StartGameAsync().Forget();
    }

    private async UniTask StartGameAsync()
    {
        await UIUtility.FadeInAsync(1,FadeLayer.All);
        Close();
        await UIUtility.FadeOutAsync(1, FadeLayer.All);

    }

}
