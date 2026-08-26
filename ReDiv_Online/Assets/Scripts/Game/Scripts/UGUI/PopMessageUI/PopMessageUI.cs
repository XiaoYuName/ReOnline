using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ReDiv.Net;
using ReDiv.Net.Bindings;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XFramework;

/// <summary>
/// 聊天界面 —— 城镇主界面右下角「世界聊天」按钮打开的那个弹窗。
///
/// 两个页签（**附近** / **世界**）共用一套东西：一个列表 + 一个输入框 + 一个发送按钮。
/// 页签只决定两件事：**列表显示哪个频道**、**发送走哪个频道**。
///
/// 数据全部来自 <see cref="ChatManager"/>，**本界面不碰 Conn**。
/// 服务端契约见 <c>ReDiv_Server/README.md</c> 的「聊天系统」。
///
/// ⚠️ **预制体里两个页签节点的名字和它们的文字是反的**：
/// <c>WordChatButton</c>（听起来是"世界"）上面写的是「附近」，
/// <c>BearbyButton</c>（听起来是"附近"）上面写的是「世界」。
/// 所以频道是**按按钮上的文字判定的，不是按节点名** —— 按节点名接一定接反。
/// 这和形态卡那两个箭头是同一类坑（客户端文档第 5 节坑 6）。
/// </summary>
public partial class PopMessageUI : UIBase
{
    [BoxGroup("颜色配置"),LabelText("默认颜色")]
    public Color NormalColor;
    [BoxGroup("颜色配置"),LabelText("选中颜色")]
    public Color SelectedColor;

    /// <summary>
    /// 列表最多摆多少条。和服务端每个域的保留窗口对齐
    /// （<c>Module.ChatHistoryPerScope</c>，现在是 50）—— 服务端只会推这么多。
    /// </summary>
    private const int MaxMessageRows = 50;

    /// <summary>滚动条离底多近就算「玩家正在看最新消息」。0 = 底。</summary>
    private const float StickToBottomThreshold = 0.05f;

    /// <summary>
    /// 一个页签。<see cref="Channel"/> 是**从按钮文字判出来的**，不是节点名（见类注释）。
    /// </summary>
    private sealed class ChannelTab
    {
        public Button Button;
        public GameObject Selected;
        public TextMeshProUGUI Label;
        public uint Channel;
    }

    private readonly List<ChannelTab> tabs = new List<ChannelTab>();

    /// <summary>
    /// 当前页签的频道。默认**世界** —— 进这个界面的入口按钮上写的就是「世界聊天」，
    /// 打开却停在附近页会很别扭；附近消息在城镇主界面底部本来就一直看得到。
    /// </summary>
    private uint currentChannel = ChatManager.ChannelWorld;

    private readonly List<MessageUI> rows = new List<MessageUI>();

    /// <summary>行预制体，只加载一次（理由和 MainCommonUI 那边一样：<c>Track</c> 不去重）。</summary>
    private GameObject rowPrefab;

    /// <summary>头像缓存，一个 key 只加载一次 —— 一屏 50 条里同一个人可能说了十句话。</summary>
    private readonly Dictionary<string, Sprite> avatarCache = new Dictionary<string, Sprite>();

    private bool hooked;

    // ------------------------------------------------------------------
    // 生命周期
    // ------------------------------------------------------------------

    public override void Init()
    {
        InitAutoBind();

        BuildTabs();

        if (sendButton != null)
        {
            Bind(sendButton, Send, AudioKeys.CursorClick01);
        }

        // 点空白处关闭 —— CloseButton 是铺满整屏的透明按钮，压在 Background 底下，
        // 所以只有点到界面**外面**才会命中它
        if (closeButton != null)
        {
            Bind(closeButton, Close, AudioKeys.CursorClick01);
        }

        if (inputFieldTMP != null)
        {
            // 和城镇主界面那个输入框同一个上限，超了根本打不进去
            inputFieldTMP.characterLimit = ChatValidation.MaxChars;

            // ⚠️ 在 Init 里挂一次，不能在 Open 里 —— onSubmit 是直接 AddListener 的，
            // 每次开界面挂一遍会让一次回车发出去好几条
            inputFieldTMP.onSubmit.AddListener(HandleInputSubmit);
        }
    }

    public override void Open()
    {
        base.Open();

        HookEvents();
        SelectChannel(currentChannel);
    }

    public override void Close()
    {
        UnhookEvents();
        ClearRows();

        base.Close();
    }

    protected override void OnDestroy()
    {
        UnhookEvents();
        ClearRows();

        base.OnDestroy();
    }

    /// <summary>门面的事件是 C# 事件，重复挂会收到重复回调 —— 用标志位挡住。</summary>
    private void HookEvents()
    {
        if (hooked)
        {
            return;
        }
        hooked = true;

        var chat = ChatManager.Instance;
        chat.MessagesChanged += RefreshMessages;
        // 聊天订阅是跟着城镇换的，所以 Ready 会反复触发，不是只在开局响一下
        chat.Ready += RefreshMessages;
    }

    private void UnhookEvents()
    {
        if (!hooked)
        {
            return;
        }
        hooked = false;

        var chat = ChatManager.Instance;
        chat.MessagesChanged -= RefreshMessages;
        chat.Ready -= RefreshMessages;
    }

    // ------------------------------------------------------------------
    // 页签
    // ------------------------------------------------------------------

    /// <summary>
    /// 把两个页签装起来。**频道按按钮文字判**（见类注释：节点名和文字是反的）。
    ///
    /// 两个节点在预制体里只有 <c>Image</c> 没有 <c>Button</c>（美术就那么搭的），
    /// <c>Button</c> 是后补上去的、<c>transition</c> 设成 None —— 选中态已经有
    /// <c>Selected</c> 那张图在表达了，再来一层 ColorTint 会和它打架。
    /// </summary>
    private void BuildTabs()
    {
        tabs.Clear();

        AddTab(wordChatButton);
        AddTab(bearbyButton);

        // 自检：两个页签必须刚好覆盖两个频道。判错了（比如美术把文字改了）
        // 表现是「两个页签点起来一模一样」，不报出来极难查
        bool hasNearby = false;
        bool hasWorld = false;

        foreach (ChannelTab tab in tabs)
        {
            hasNearby |= tab.Channel == ChatManager.ChannelNearby;
            hasWorld |= tab.Channel == ChatManager.ChannelWorld;
        }

        if (!hasNearby || !hasWorld)
        {
            Debug.LogError("[PopMessage] 两个页签没有覆盖「附近」和「世界」两个频道 —— " +
                           "频道是按按钮上的文字判的，检查一下预制体里那两个 Text 是不是被改了", this);
        }
    }

    private void AddTab(Image root)
    {
        if (root == null)
        {
            return;
        }

        Transform node = root.transform;
        var label = node.Find("Text")?.GetComponent<TextMeshProUGUI>();

        if (label == null)
        {
            Debug.LogError($"[PopMessage] 页签 {node.name} 下面没有 Text 节点", this);
            return;
        }

        var button = root.GetComponent<Button>();

        if (button == null)
        {
            Debug.LogError($"[PopMessage] 页签 {node.name} 上没有 Button 组件（要补一个，transition 设 None）", this);
            return;
        }

        var tab = new ChannelTab
        {
            Button = button,
            Selected = node.Find("Selected")?.gameObject,
            Label = label,
            // ⚠️ 按**文字**判频道，不按节点名
            Channel = label.text != null && label.text.Contains("世界")
                ? ChatManager.ChannelWorld
                : ChatManager.ChannelNearby,
        };

        uint channel = tab.Channel;
        Bind(button, () => SelectChannel(channel), AudioKeys.CursorClick01);

        tabs.Add(tab);
    }

    /// <summary>
    /// 切页签。**幂等** —— 重复点同一个不会有副作用（Open 里也会调一次来铺初始状态）。
    /// </summary>
    private void SelectChannel(uint channel)
    {
        currentChannel = channel;

        foreach (ChannelTab tab in tabs)
        {
            bool on = tab.Channel == channel;

            if (tab.Selected != null)
            {
                tab.Selected.SetActive(on);
            }

            if (tab.Label != null)
            {
                tab.Label.color = on ? SelectedColor : NormalColor;
            }
        }

        RefreshMessages();
    }

    // ------------------------------------------------------------------
    // 列表
    // ------------------------------------------------------------------

    /// <summary>
    /// 按当前页签的频道重画列表。挂在 <c>MessagesChanged</c> / <c>Ready</c> 上，
    /// 也在切页签时调 —— **必须能重复调**。
    ///
    /// 和城镇主界面那个聊天框一样：**复用已有的行，只补 / 收差额**，不是每次全拆重建。
    /// </summary>
    private void RefreshMessages()
    {
        RectTransform content = scrollView != null ? scrollView.content : null;

        if (content == null)
        {
            return;
        }

        // 先筛出当前频道的消息（门面给的是"可见的全部"，两个频道混在一起）
        var visible = new List<ChatMessage>();

        foreach (ChatMessage row in ChatManager.Instance.Messages)
        {
            if (row.Channel == currentChannel)
            {
                visible.Add(row);
            }
        }

        // 只显示最后 MaxMessageRows 条（列表是升序，最新的在最后）
        int start = Mathf.Max(0, visible.Count - MaxMessageRows);
        int count = visible.Count - start;

        // 重画前先记住玩家是不是正贴着底看最新消息 —— 他要是滚上去翻历史，
        // 新消息不该把他拽回底部
        bool stickToBottom = IsScrolledToBottom();

        while (rows.Count > count)
        {
            int last = rows.Count - 1;
            MessageUI row = rows[last];
            rows.RemoveAt(last);

            if (row != null)
            {
                // 先 SetActive(false) 再 Destroy —— Destroy 延迟到帧末，
                // 不关掉的话这一帧里旧行还占着布局位置
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
        }

        while (rows.Count < count)
        {
            MessageUI row = CreateRow(content);

            if (row == null)
            {
                // 预制体加载不出来，报错已经打过了，别在这循环里刷屏
                return;
            }

            rows.Add(row);
        }

        for (int i = 0; i < count; i++)
        {
            ChatMessage message = visible[start + i];
            rows[i].SetMessage(message.SenderName, message.Content, AvatarOf(message));
        }

        if (stickToBottom)
        {
            ScrollToBottom();
        }
    }

    private MessageUI CreateRow(RectTransform content)
    {
        if (rowPrefab == null)
        {
            rowPrefab = LoadAsset<GameObject>(AssetKeys.MessageUIPath);
        }

        if (rowPrefab == null)
        {
            Debug.LogError($"[PopMessage] 消息行预制体加载不出来：{AssetKeys.MessageUIPath}", this);
            return null;
        }

        var go = Instantiate(rowPrefab, content, false);
        var row = go.GetComponent<MessageUI>();

        if (row == null)
        {
            Debug.LogError("[PopMessage] MessageUI 预制体上没有 MessageUI 组件", this);
            Destroy(go);
            return null;
        }

        row.Init();

        return row;
    }

    private void ClearRows()
    {
        foreach (MessageUI row in rows)
        {
            if (row != null)
            {
                Destroy(row.gameObject);
            }
        }

        rows.Clear();

        // ⚠️ 紧接着 base.Close() 会调 Release() 把 AA 引用还掉，
        // 缓存里那些引用就悬空了，必须一起清
        rowPrefab = null;
        avatarCache.Clear();
    }

    /// <summary>
    /// 取一条消息的发送者头像。
    ///
    /// 头像挂在**形态**上不在角色上（觉醒之后会变），所以按
    /// <c>(SenderJobId, SenderFormId)</c> 查配置表 <c>CharacterForm</c> 的 <c>IconKey</c>。
    /// 这两个字段是服务端在发送那一刻快照进消息行的 —— **不能去 join 在线玩家**：
    /// 世界频道里说话的人可能在另一个城镇，本地根本没订阅到他。
    ///
    /// 取不到返回 null（没配头像 / 老消息没有这两个字段），调用方会把头像藏起来。
    /// </summary>
    private Sprite AvatarOf(ChatMessage message)
    {
        if (message.SenderJobId == 0)
        {
            return null;
        }

        // 联合主键的表用 Get(jobId, formId)，取不到返回 null
        XFramework.CharacterForm form = LubanManager.Instance.TbCharacterForm?
            .Get((int)message.SenderJobId, (int)message.SenderFormId);

        string key = form?.IconKey;

        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (avatarCache.TryGetValue(key, out Sprite cached))
        {
            return cached;
        }

        Sprite sprite = LoadAsset<Sprite>(key);
        avatarCache[key] = sprite;

        return sprite;
    }

    /// <summary>
    /// 玩家是不是正贴着底看最新消息。内容还没长到超过视口时也算「在底部」——
    /// 那种情况下 <c>verticalNormalizedPosition</c> 的值没意义（ScrollRect 会夹住它）。
    /// </summary>
    private bool IsScrolledToBottom()
    {
        if (scrollView == null)
        {
            return false;
        }

        RectTransform content = scrollView.content;
        RectTransform viewport = scrollView.viewport != null
            ? scrollView.viewport
            : scrollView.transform as RectTransform;

        if (content == null || viewport == null)
        {
            return true;
        }

        if (content.rect.height <= viewport.rect.height)
        {
            return true;
        }

        return scrollView.verticalNormalizedPosition <= StickToBottomThreshold;
    }

    /// <summary>
    /// 滚到底。⚠️ **必须先强制重算一次布局** —— VerticalLayoutGroup + ContentSizeFitter
    /// 算高度排在下一次布局阶段，不重算的话这里用的是上一帧的高度，
    /// 刚加进来那条还没算进去，滚动会停在倒数第二条上。
    /// </summary>
    private void ScrollToBottom()
    {
        if (scrollView == null || scrollView.content == null)
        {
            return;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollView.content);
        scrollView.verticalNormalizedPosition = 0f;
    }

    // ------------------------------------------------------------------
    // 发送
    // ------------------------------------------------------------------

    private void HandleInputSubmit(string _) => Send();

    /// <summary>
    /// 按当前页签的频道把输入框里的内容发出去。
    ///
    /// 和城镇主界面那个发送口同一套约定：**不做本地乐观显示**（成功后服务端会把消息
    /// 推回来，本地先塞一条会重复）、**等回应期间就清空输入框**（不清玩家会以为没发出去
    /// 而重复点），失败时把原文填回去让他改一改重发。
    /// </summary>
    private void Send()
    {
        if (inputFieldTMP == null)
        {
            return;
        }

        string draft = inputFieldTMP.text;

        if (string.IsNullOrWhiteSpace(draft))
        {
            // 空输入不值得弹窗打断（回车连按很常见），静默忽略
            return;
        }

        inputFieldTMP.text = string.Empty;

        SendAsync(draft, currentChannel).Forget();
    }

    private async UniTaskVoid SendAsync(string draft, uint channel)
    {
        var chat = ChatManager.Instance;

        ChatResult result = channel == ChatManager.ChannelWorld
            ? await chat.SendWorldAsync(draft)
            : await chat.SendNearbyAsync(draft);

        if (result.Ok)
        {
            return;
        }

        // 界面可能在等回应的这段时间里被关掉了，那就别再动它
        if (!isOpen)
        {
            return;
        }

        if (inputFieldTMP != null && string.IsNullOrEmpty(inputFieldTMP.text))
        {
            inputFieldTMP.text = draft;
        }

        UIUtility.ShowWindow(result.Message, "发送失败");
    }
}
