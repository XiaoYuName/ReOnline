using Cysharp.Threading.Tasks;
using ReDiv.Net;
using TMPro;
using XFramework;

/// <summary>
/// 角色名输入界面。由创建角色界面的「创建」按钮打开，走完这一步才真的建角色。
/// </summary>
public partial class ReviseCharacterNameUI : UIBase
{
    /// <summary>要创建的角色（职业 id）。由 <see cref="SetJob"/> 传进来。</summary>
    private uint jobId;

    /// <summary>
    /// 已经查重通过的那个名字（原样，未 trim）。null = 还没查 / 查完又改过。
    /// 创建按钮亮不亮就看它和输入框里的字是不是同一个。
    /// </summary>
    private string checkedName;

    /// <summary>长度提示那行字。没登记进 AutoBind，手动 Get。</summary>
    private TMP_Text tips;

    private bool busy;

    public override void Init()
    {
        InitAutoBind();

        tips = Get<TMP_Text>("UIMask/Background/Farme/Tips");

        // 提示文案从校验规则算出来，不用 prefab 里写死的那句
        //（prefab 原文是「$请输入1-10字」，和真实规则对不上）
        if (tips != null)
        {
            tips.text = CharacterValidation.LengthHint;
        }

        // 服务端上限是 16 个显示宽度，全 ASCII 时正好 16 个字符 —— 挡住多输的，
        // 省一次白跑的请求。中文更早就会被长度校验拦下。
        nameField.characterLimit = CharacterValidation.MaxDisplayWidth;

        // 第三个参数是点击音效 ID，不传的话 AudioManager 会拿空 ID 去查表并报错
        Bind(repeatButton, OnClickCheck, AudioKeys.CursorClick01);
        Bind(actionButton, OnClickCreate, AudioKeys.CursorClick01);
        Bind(cancelButton, Close, AudioKeys.CursorClick01);

        // Init 万一被走第二遍（热重载之类），别把监听挂两份
        nameField.onValueChanged.RemoveListener(HandleNameChanged);
        nameField.onValueChanged.AddListener(HandleNameChanged);
    }

    protected override void OnDestroy()
    {
        // 界面预制体没走过 Init 就被销毁时 nameField 是 null，不判会抛 NRE，
        // 而且那个报错和真正的原因看着毫无关系
        if (nameField != null)
        {
            nameField.onValueChanged.RemoveListener(HandleNameChanged);
        }

        base.OnDestroy();
    }

    /// <summary>
    /// 由创建角色界面调用：告诉本界面要建哪个角色，并把上一次的输入清干净。
    ///
    /// 重置放在这里而不是 <c>Open</c> 里，是因为打开本界面的路径只有一条
    /// （<c>CreatCharacterUI</c> 的创建按钮），而 jobId 必须跟着一起给 ——
    /// 两件事放一处就不会出现「开了但没设职业」的中间态。
    /// </summary>
    public void SetJob(uint jobId)
    {
        this.jobId = jobId;

        nameField.text = string.Empty;
        checkedName = null;
        busy = false;

        // SetInteractable 内部会顺带刷新创建按钮（它还受「查重通过没」约束）
        SetInteractable(true);

        nameField.ActivateInputField();
    }

    // ------------------------------------------------------------------
    // 交互
    // ------------------------------------------------------------------

    /// <summary>名字一改，之前那次查重就不算数了。</summary>
    private void HandleNameChanged(string value)
    {
        if (checkedName != null && checkedName != value)
        {
            checkedName = null;
        }

        RefreshCreateButton();
    }

    private void OnClickCheck() => CheckAsync().Forget();

    private void OnClickCreate() => CreateAsync().Forget();

    /// <summary>查重：先本地查格式（省一次往返），再问服务端重名。结果都弹窗告诉玩家。</summary>
    private async UniTask CheckAsync()
    {
        if (busy)
        {
            return;
        }

        string name = nameField.text;

        SetBusy(true);
        var result = await CharacterManager.Instance.CheckNameAsync(name);
        SetBusy(false);

        if (!result.Ok)
        {
            checkedName = null;
            RefreshCreateButton();
            UIUtility.ShowWindow(result.Message, "这个名字不能用");
            nameField.ActivateInputField();
            return;
        }

        // 记的是**查的时候那个名字**，不是现在输入框里的 —— 等待期间玩家可能又改了
        checkedName = name;
        RefreshCreateButton();

        UIUtility.ShowWindow($"「{name.Trim()}」可以使用。", "名字可用");
    }

    /// <summary>创建角色。成功后连创建角色界面一起关掉，回到选人界面。</summary>
    private async UniTask CreateAsync()
    {
        if (busy || !IsChecked)
        {
            return;
        }

        string name = nameField.text;

        SetBusy(true);
        var result = await CharacterManager.Instance.CreateCharacterAsync(name, jobId);
        SetBusy(false);

        if (!result.Ok)
        {
            // 最典型的是「查完到点创建之间名字被别人抢走了」—— 让玩家重新查一次
            checkedName = null;
            RefreshCreateButton();
            UIUtility.ShowWindow(result.Message, "创建失败");
            nameField.ActivateInputField();
            return;
        }

        // 选人界面挂着 CharactersChanged，新角色会自己出现在格子里，这里不用管刷新
        Close();
        UISystem.Instance.CloseUI(UIKeys.CreatCharacterUI);
    }

    // ------------------------------------------------------------------
    // 状态
    // ------------------------------------------------------------------

    /// <summary>输入框里现在这个名字是不是刚查重通过的那个。</summary>
    private bool IsChecked => checkedName != null && checkedName == nameField.text;

    private void RefreshCreateButton()
    {
        actionButton.interactable = !busy && IsChecked;
    }

    private void SetBusy(bool value)
    {
        busy = value;
        SetInteractable(!value);
    }

    private void SetInteractable(bool value)
    {
        nameField.interactable = value;
        repeatButton.interactable = value;
        cancelButton.interactable = value;

        // 创建按钮还额外受「查重通过没」约束，不能跟着一起开
        RefreshCreateButton();
    }
}
