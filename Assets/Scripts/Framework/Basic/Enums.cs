using System;
using Sirenix.OdinInspector;

namespace XFramework
{
    /// <summary>
    /// 回调参数的回调时机
    /// </summary>
    public enum ActionBehaviour
    {
        /// <summary>
        /// 一开始调用
        /// </summary>
        Star,
        /// <summary>
        /// 中途回调
        /// </summary>
        Mid,
        /// <summary>
        /// 结束时回调执行
        /// </summary>
        End,
    }

    /// <summary>
    /// UICanvas层。UISystem 下每个Canvas层是一个根节点,层之间靠这个枚举区分。
    /// </summary>
    public enum UICanvasLayer
    {
        /// <summary>
        /// 最底层
        /// </summary>
        UIBackground = 0,
        /// <summary>
        /// 中层
        /// </summary>
        UIPanel = 1,
    }

    /// <summary>
    /// UICanvas层下的子渲染层级。界面挂到哪个子层由配置表的 UIParent 决定。
    /// </summary>
    public enum UIParentLayer
    {
        /// <summary>
        /// 最底层
        /// </summary>
        UIPanel = 0,
        /// <summary>
        /// 中层
        /// </summary>
        UIDialogue = 1,
        /// <summary>
        /// 弹窗层
        /// </summary>
        UIPop = 2,
        /// <summary>
        /// 顶层
        /// </summary>
        UITop = 3,
    }

    /// <summary>
    /// 转场渐变遮罩的遮挡范围。对应 Project Settings 里两个专门的 Sorting Layer。
    /// </summary>
    public enum FadeLayer
    {
        /// <summary>
        /// SceneFade:只遮住场景,UI照常显示(小场景之间切换用)
        /// </summary>
        Scene = 0,
        /// <summary>
        /// UIFade:最顶层,连UI一起遮掉(进出小游戏、读档这种整体转场用)
        /// </summary>
        All = 1,
    }
}
