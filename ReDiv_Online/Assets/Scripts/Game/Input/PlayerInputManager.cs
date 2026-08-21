using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using XFramework;

public class PlayerInputManager : MonoSingleton<PlayerInputManager>,IGameInitialized
{
    private PlayerInputActions input;
    
    public event Action OnSpace; 
    
    public event Action OnEsc;

    /// <summary>
    /// 没有任何监听者消费掉这次Esc时才触发的兜底事件,含义同 <see cref="OnRightClickUnconsumed"/>。
    /// </summary>
    public event Action OnEscUnconsumed;

    public event Action OnClick;
    public event Action OnLeftMouseDown;
    public event Action OnLeftMouseUp;
    public event Action OnRightClick;

    /// <summary>
    /// 没有任何监听者消费掉这次右键时才触发的兜底事件。
    /// 右键/Esc 是共用的取消类输入:开着界面时语义是"关界面",没界面时才轮到别的用途
    /// (小场景返回大地图)。两边都直接监听 OnRightClick 的话,一次右键会被用两遍,
    /// 所以处理掉这次输入的监听者要调 <see cref="ConsumeCancelInput"/> 声明一下。
    /// </summary>
    public event Action OnRightClickUnconsumed;

    public event Action OnMiddleClick;

    public bool IsMouseLeftDown => input.Game.Click.IsPressed();

    /// <summary>方向：左（A / ←）</summary>
    public event Action OnLeft;

    /// <summary>方向：右（D / →）</summary>
    public event Action OnRight;

    /// <summary>方向：上（W / ↑）</summary>
    public event Action OnUp;

    /// <summary>方向：下（S / ↓，暂无使用，预留）</summary>
    public event Action OnDown;

    /// <summary>
    /// 初始化脚本函数
    /// </summary>
    /// <returns></returns>
    public async UniTask Initialized()
    {
        input = new ();
        input.Game.Enable();
        input.Game.Space.performed += OnSpaceInvoke;
        input.Game.Esc.performed += OnEscInvoke;
        input.Game.Click.performed += OnClickInvoke;
        input.Game.Click.started += OnLeftMouseDownInvoke;
        input.Game.Click.canceled += OnLeftMouseUpInvoke;
        input.Game.RightClick.performed += OnRightClickInvoke;
        input.Game.MiddleClick.performed += OnMiddleClickInvoke;
        input.Game.Left.performed += OnLeftInvoke;
        input.Game.Right.performed += OnRightInvoke;
        input.Game.Up.performed += OnUpInvoke;
        input.Game.Down.performed += OnDownInvoke;

        // 右键/Esc 关栈顶UI。以前是 UISystem 自己订阅这里的事件,但那样框架层就依赖了业务层的输入系统,
        // 所以反过来由输入层调 UISystem —— 回调是在真正按键时才执行的,不存在两者的初始化先后问题。
        OnRightClick += CloseTopUIOnCancelInput;
        OnEsc += CloseTopUIOnCancelInput;

        await UniTask.CompletedTask;
    }

    /// <summary>
    /// 取消类输入优先用来关界面。真关掉了就把这次输入消费掉,
    /// 免得同一次输入接着被兜底逻辑(小场景返回大地图)再用一遍。
    /// </summary>
    private void CloseTopUIOnCancelInput()
    {
        if (UISystem.IsInitialized && UISystem.Instance.TryCloseStackUI())
        {
            ConsumeCancelInput();
        }
    }

    void OnSpaceInvoke(InputAction.CallbackContext context)
    {
        OnSpace?.Invoke();
    }

    void OnClickInvoke(InputAction.CallbackContext context)
    {
        OnClick?.Invoke();
    }
    void OnLeftMouseDownInvoke(InputAction.CallbackContext context)
    {
        OnLeftMouseDown?.Invoke();
    }
    void OnLeftMouseUpInvoke(InputAction.CallbackContext context)
    {
        OnLeftMouseUp?.Invoke();
    }
    void OnRightClickInvoke(InputAction.CallbackContext context)
    {
        InvokeCancelInput(OnRightClick, OnRightClickUnconsumed);
    }

    private bool isCancelInputConsumed;

    /// <summary>
    /// 派发一次取消类输入(右键/Esc):先给正常监听者,没人消费掉就再抛兜底事件。
    /// 两个输入不会在同一次派发里嵌套,所以共用一个消费标记。
    /// </summary>
    void InvokeCancelInput(Action onInput, Action onUnconsumed)
    {
        isCancelInputConsumed = false;
        onInput?.Invoke();
        if (!isCancelInputConsumed)
        {
            onUnconsumed?.Invoke();
        }
    }

    /// <summary>
    /// 在 <see cref="OnRightClick"/> / <see cref="OnEsc"/> 的回调里调用,声明这次输入已经被自己处理掉了,
    /// 本次输入不再触发对应的 Unconsumed 兜底事件。
    /// </summary>
    public void ConsumeCancelInput()
    {
        isCancelInputConsumed = true;
    }
    void OnMiddleClickInvoke(InputAction.CallbackContext context)
    {
        OnMiddleClick?.Invoke();
    }
    void OnLeftInvoke(InputAction.CallbackContext context)
    {
        OnLeft?.Invoke();
    }
    void OnRightInvoke(InputAction.CallbackContext context)
    {
        OnRight?.Invoke();
    }
    void OnUpInvoke(InputAction.CallbackContext context)
    {
        OnUp?.Invoke();
    }
    void OnDownInvoke(InputAction.CallbackContext context)
    {
        OnDown?.Invoke();
    }
    void OnEscInvoke(InputAction.CallbackContext context)
    {
        InvokeCancelInput(OnEsc, OnEscUnconsumed);
    }

    /// <summary>
    /// 释放脚本函数
    /// </summary>
    public async UniTask Release()
    {
        input.Game.Space.performed -= OnSpaceInvoke;
        input.Game.Esc.performed -= OnEscInvoke;
        input.Game.Click.performed -= OnClickInvoke;
        input.Game.Click.started -= OnLeftMouseDownInvoke;
        input.Game.Click.canceled -= OnLeftMouseUpInvoke;
        input.Game.RightClick.performed -= OnRightClickInvoke;
        input.Game.MiddleClick.performed -= OnMiddleClickInvoke;
        input.Game.Left.performed -= OnLeftInvoke;
        input.Game.Right.performed -= OnRightInvoke;
        input.Game.Up.performed -= OnUpInvoke;
        input.Game.Down.performed -= OnDownInvoke;
        input.Dispose();
        input = null;
        await UniTask.CompletedTask;
    }
}
