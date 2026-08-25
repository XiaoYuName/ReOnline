using UnityEngine;

/// <summary>
/// 城镇背景控制器 —— 挂在**背景预制体根节点**上的组件。
///
/// 一个城镇有三个这样的预制体，对应早 / 中 / 晚三个时段，路径填在配置表
/// <c>Town</c> 的 <c>BgMorning</c> / <c>BgNoon</c> / <c>BgNight</c> 三列上。
/// 由 <see cref="TownBackgroundLoader"/> 按「当前城镇 + 当前时段」实例化到
/// <c>UIBackground</c> 层下，切时段时销毁旧的、生成新的。
///
/// 基类本身只定义生命周期钩子，什么都不做 —— 具体的表现（云在飘、灯亮起来、
/// 粒子、Spine…）由各背景预制体自己派生实现。**不要**把某个城镇特有的逻辑写到基类里。
///
/// 为什么背景是「一个时段一个预制体」而不是「一个预制体换图」：
/// 早中晚的差别往往不只是底图 —— 夜里多一层灯光和虫鸣、白天多一层人流，
/// 拆成三个预制体让美术各自摆，比在一个预制体里堆三套开关干净。
/// 用户 2026-08-25 明确要求三个。
/// </summary>
public class TownBackgroundController : MonoBehaviour
{
    /// <summary>这个背景是哪个城镇的哪个时段 —— 只用来在 Inspector 里认人，逻辑不读它。</summary>
    [SerializeField]
    [Tooltip("仅备注用：这个背景对应哪个城镇的哪个时段。逻辑不读这个字段。")]
    private string note;

    /// <summary>刚被实例化、准备显示时调用。做入场表现（淡入、开始播放动画）就重写这里。</summary>
    public virtual void Show()
    {
    }

    /// <summary>
    /// 即将被替换掉时调用。
    ///
    /// ⚠️ 调用方紧接着就会 <c>SetActive(false)</c> + <c>Destroy</c>，
    /// 所以这里**不能**做需要跨帧的淡出（Unity 的 Destroy 延到帧末，看着像生效了，
    /// 但下一帧物体就没了）。真要做淡出得先改 Loader 的替换时序。
    /// </summary>
    public virtual void Hide()
    {
    }
}
