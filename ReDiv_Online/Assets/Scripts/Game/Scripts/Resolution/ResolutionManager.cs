using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;
using XFramework;


public class ResolutionManager : MonoSingleton<ResolutionManager>,IGameInitialized
{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    private const int GWL_STYLE = -16;

    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_SYSMENU = 0x00080000;

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;

    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;

#endif
    
    /// <summary>
    /// 支持的常用分辨率(16:9),超出显示器最大分辨率的会被自动过滤
    /// </summary>
    private static readonly (int Width, int Height, string Label)[] s_commonResolutions =
    {
        (1280, 720, "720P"),
        (1600, 900, "900P"),
        (1920, 1080, "1080P (1K)"),
        (2560, 1440, "1440P (2K)"),
        (3840, 2160, "2160P (4K)"),
    };

    private List<Vector2Int> m_availableResolutions;

    [BoxGroup("屏幕设置"),LabelText("当前屏幕设置")]
    public WindowType SelectedWindowType { get; private set; }
    [BoxGroup("屏幕设置"),LabelText("当前屏幕分辨率索引")]
    public int SelectedWindowResolutionIndex { get; private set; }

    /// <summary>
    /// 当前显示器可用的分辨率列表(由低到高)
    /// </summary>
    public IReadOnlyList<Vector2Int> AvailableResolutions
    {
        get
        {
            if (m_availableResolutions == null)
            {
                BuildAvailableResolutions();
            }
            return m_availableResolutions;
        }
    }

    /// <summary>
    /// 默认分辨率索引(显示器支持的最高一档)
    /// </summary>
    public int DefaultResolutionIndex => AvailableResolutions.Count - 1;

    /// <summary>
    /// 初始化脚本函数
    /// </summary>
    /// <returns></returns>
    public async UniTask Initialized()
    {
        var windowType = PlayerPrefs.GetInt("WindowType", (int)0);
        SelectedWindowType = (WindowType)windowType;
        SelectedWindowResolutionIndex = ClampResolutionIndex(
            PlayerPrefs.GetInt("WindowResolutionIndex", DefaultResolutionIndex));
        Debug.Log($"保存本地的窗口模式: {SelectedWindowType} 保存到本地的分辨率 : {GetResolutionLabel(SelectedWindowResolutionIndex)}");

        ChangeWindowMode(SelectedWindowType, SelectedWindowResolutionIndex);
        await UniTask.CompletedTask;
    }

    /// <summary>
    /// 获取指定索引分辨率的显示文本
    /// </summary>
    public string GetResolutionLabel(int index)
    {
        var resolution = AvailableResolutions[ClampResolutionIndex(index)];
        foreach (var common in s_commonResolutions)
        {
            if (common.Width == resolution.x && common.Height == resolution.y)
            {
                return $"{resolution.x} x {resolution.y}  {common.Label}";
            }
        }
        return $"{resolution.x} x {resolution.y}";
    }

    /// <summary>
    /// 把索引限制在可用分辨率范围内,避免显示器更换后本地存档越界
    /// </summary>
    public int ClampResolutionIndex(int index)
    {
        return Mathf.Clamp(index, 0, AvailableResolutions.Count - 1);
    }

    private void BuildAvailableResolutions()
    {
        int maxWidth = 0;
        int maxHeight = 0;
        foreach (var resolution in Screen.resolutions)
        {
            maxWidth = Mathf.Max(maxWidth, resolution.width);
            maxHeight = Mathf.Max(maxHeight, resolution.height);
        }

        // 编辑器/异常情况下 Screen.resolutions 可能为空,退回当前分辨率
        if (maxWidth <= 0 || maxHeight <= 0)
        {
            maxWidth = Screen.currentResolution.width;
            maxHeight = Screen.currentResolution.height;
        }

        m_availableResolutions = new List<Vector2Int>();
        foreach (var common in s_commonResolutions)
        {
            if (common.Width <= maxWidth && common.Height <= maxHeight)
            {
                m_availableResolutions.Add(new Vector2Int(common.Width, common.Height));
            }
        }

        // 显示器分辨率低于最小档时,至少保留一项
        if (m_availableResolutions.Count == 0)
        {
            m_availableResolutions.Add(new Vector2Int(maxWidth, maxHeight));
        }
    }

    /// <summary>
    /// 释放脚本函数
    /// </summary>
    public async UniTask Release()
    {
        await UniTask.CompletedTask;
    }

    public void ChangeWindowMode(WindowType mode, int resolutionIndex)
    {
        resolutionIndex = ClampResolutionIndex(resolutionIndex);
        SelectedWindowType = mode;
        SelectedWindowResolutionIndex = resolutionIndex;
        PlayerPrefs.SetInt("WindowType", (int)mode);
        PlayerPrefs.SetInt("WindowResolutionIndex", resolutionIndex);
        Debug.Log($"保存本地的窗口模式: {SelectedWindowType} 保存到本地的分辨率 : {GetResolutionLabel(resolutionIndex)}");
        var resolution = AvailableResolutions[resolutionIndex];
        ChangeWindowMode(SelectedWindowType, resolution.x, resolution.y);
    }

    private void ChangeWindowMode(WindowType mode, int width, int height)
    {
        switch (mode)
        {
            case WindowType.Fullscreen:
                Screen.SetResolution(width, height, true);
                break;

            case WindowType.Borderless:
                StartCoroutine(SetBorderless(width, height));
                break;

            case WindowType.Windowed:
                StartCoroutine(SetWindowed(width, height));
                break;
        }
    }

    private IEnumerator SetBorderless(int width, int height)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        IntPtr hwnd = GetActiveWindow();
        GetWindowPosition(hwnd, out int currentX, out int currentY);
#endif

        Screen.SetResolution(width, height, false);

        yield return null;
        yield return null;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        hwnd = GetActiveWindow();

        int style = GetWindowLong(hwnd, GWL_STYLE);

        style &= ~WS_CAPTION;
        style &= ~WS_THICKFRAME;
        style &= ~WS_MINIMIZEBOX;
        style &= ~WS_MAXIMIZEBOX;
        style &= ~WS_SYSMENU;

        SetWindowLong(hwnd, GWL_STYLE, style);

        SetWindowPos(
            hwnd,
            HWND_TOP,
            currentX,
            currentY,
            width,
            height,
            SWP_SHOWWINDOW | SWP_FRAMECHANGED);
#endif
    }

    private IEnumerator SetWindowed(int width, int height)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        IntPtr hwnd = GetActiveWindow();
        GetWindowPosition(hwnd, out int currentX, out int currentY);
#endif

        Screen.SetResolution(width, height, false);

        yield return null;
        yield return null;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        hwnd = GetActiveWindow();

        int style = GetWindowLong(hwnd, GWL_STYLE);

        style |= WS_CAPTION;
        style |= WS_SYSMENU;
        style |= WS_MINIMIZEBOX;

        // 禁用拖拽边框缩放
        style &= ~WS_THICKFRAME;
        style &= ~WS_MAXIMIZEBOX;

        SetWindowLong(hwnd, GWL_STYLE, style);

        SetWindowPos(
            hwnd,
            HWND_TOP,
            currentX,
            currentY,
            width,
            height,
            SWP_SHOWWINDOW | SWP_FRAMECHANGED);
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    private void GetWindowPosition(IntPtr hwnd, out int x, out int y)
    {
        x = 0;
        y = 0;

        if (GetWindowRect(hwnd, out RECT rect))
        {
            x = rect.Left;
            y = rect.Top;
        }
    }
#endif
}