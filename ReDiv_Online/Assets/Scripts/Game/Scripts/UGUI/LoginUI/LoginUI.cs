using Cysharp.Threading.Tasks;
using ReDiv.Net;
using TMPro;
using XFramework;

/// <summary>
/// 登录 / 注册界面。
///
/// 只做三件事：收输入、调 <see cref="AuthManager"/>、显示结果。
/// 不碰连接、不碰 Reducer、不碰表 —— 那些都在 AuthManager 里。
///
/// 失败文案直接用 <see cref="AuthResult.Message"/>：本地格式错误是
/// <c>AuthValidation</c> 给的，服务端拒绝是 Reducer 抛的中文原文，两边都能直接显示。
/// </summary>
public partial class LoginUI : UIBase
{
    /// <summary>请求进行中时顶栏显示的文案，结束后还原成 prefab 里的原文。</summary>
    private const string BusyTitle = "请稍候…";

    private string defaultTitle;
    private bool busy;

    public override void Init()
    {
        InitAutoBind();

        defaultTitle = title != null ? title.text : string.Empty;

        // prefab 里 PassField 的 ContentType 是 Standard（密码会明文显示在屏幕上），
        // 在这里压成 Password。放代码里而不是改 prefab：这是逻辑要求，不该靠美术记得勾。
        passField.contentType = TMP_InputField.ContentType.Password;
        passField.characterLimit = AuthValidation.PasswordMaxLength;
        passField.ForceLabelUpdate();

        // 服务端的用户名上限是 16，这里挡住多输的字符，省一次白跑的请求
        nameField.characterLimit = AuthValidation.UsernameMaxLength;

        Bind(loginButoon, OnClickLogin, AudioKeys.CursorClick01);
        Bind(registerBtn, OnClickRegister, AudioKeys.CursorClick01);

        // 密码框里按回车 = 点登录
        passField.onSubmit.RemoveAllListeners();
        passField.onSubmit.AddListener(_ => OnClickLogin());
    }

    public override void Open()
    {
        base.Open();

        busy = false;
        SetInteractable(true);
        RestoreTitle();

        // 打开时把焦点放到用户名框，玩家可以直接开始打字
        if (string.IsNullOrEmpty(nameField.text))
        {
            nameField.ActivateInputField();
        }
        else
        {
            passField.ActivateInputField();
        }
    }

    private void OnClickLogin()
    {
        SubmitAsync(isRegister: false).Forget();
    }

    private void OnClickRegister()
    {
        SubmitAsync(isRegister: true).Forget();
    }

    /// <summary>
    /// 提交登录或注册。
    ///
    /// 注册成功后服务端会**直接建会话**（注册即登录），所以两条路成功后的处理是一样的：
    /// 关掉本界面回到外主界面，CommonUI 会自己刷新成已登录。
    /// </summary>
    private async UniTask SubmitAsync(bool isRegister)
    {
        if (busy)
        {
            return;
        }

        string username = nameField.text;
        string password = passField.text;
        string what = isRegister ? "注册" : "登录";

        busy = true;
        SetInteractable(false);
        if (title != null)
        {
            title.text = BusyTitle;
        }

        var result = isRegister
            ? await AuthManager.Instance.RegisterAsync(username, password)
            : await AuthManager.Instance.LoginAsync(username, password);

        busy = false;
        SetInteractable(true);
        RestoreTitle();

        if (!result.Ok)
        {
            UIUtility.ShowWindow(result.Message, $"{what}失败");
            passField.ActivateInputField();
            return;
        }

        // 成功了就别把密码留在输入框里
        passField.text = string.Empty;
        Close();
    }

    private void SetInteractable(bool value)
    {
        loginButoon.interactable = value;
        registerBtn.interactable = value;
        nameField.interactable = value;
        passField.interactable = value;
    }

    private void RestoreTitle()
    {
        if (title != null)
        {
            title.text = defaultTitle;
        }
    }
}
