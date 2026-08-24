using Sirenix.OdinInspector;
using Spine.Unity;
using UnityEngine;
using XFramework;

/// <summary>
/// 挂在**每个角色形态的 UISpine 预制体**上（`Character/&lt;JobId&gt;/.../Prefab/Skeleton/xxx_UI.prefab`）。
///
/// 动画名不进配置表，就编在这个预制体里 —— 美术在 Inspector 里用 <see cref="IdleName"/>
/// 的下拉（`[SpineAnimation]` 会列出这套骨骼真有的动画）挑一个，挑错挑不出不存在的名字。
/// 选人界面只管把预制体实例化到格子里，然后调 <see cref="PlayIdle"/>。
///
/// ⚠️ Spine 4.3 把渲染和动画拆成了两个组件：<see cref="SkeletonGraphic"/> 只负责
/// 在 Canvas 下渲染，**动画在同一个物体上的 <see cref="SkeletonAnimation"/> 里**
/// （SkeletonGraphic 自己没有 AnimationState，它通过 <c>Animation</c> 指向前者）。
/// 所以播动画要驱动 SkeletonAnimation。两个都没有就只打一条警告、不抛异常 ——
/// 一个角色的骨骼配漏了，不该让整个选人界面打不开。
/// </summary>
public class CharacterGraphicUI : UIBase
{
    private SkeletonGraphic _skeletonGraphic;
    private SkeletonAnimation _skeletonAnimation;

    [SpineAnimation, LabelText("待机动画")]
    public string IdleName;

    /// <summary>
    /// 初始化方法,一般不需要手动调用
    /// </summary>
    public override void Init()
    {
        _skeletonGraphic = GetComponent<SkeletonGraphic>();
        _skeletonAnimation = GetComponent<SkeletonAnimation>();
    }

    /// <summary>
    /// 播待机动画（循环）。<see cref="Init"/> 之后调。
    ///
    /// <paramref name="animationName"/> 不传就用预制体上配的 <see cref="IdleName"/>。
    /// </summary>
    public void PlayIdle(string animationName = null)
    {
        string clip = string.IsNullOrEmpty(animationName) ? IdleName : animationName;

        if (string.IsNullOrEmpty(clip))
        {
            Debug.LogWarning($"[CharacterGraphic] {name} 没有配待机动画名（IdleName），骨骼会停在初始姿势", this);
            return;
        }

        if (_skeletonAnimation == null)
        {
            Debug.LogWarning($"[CharacterGraphic] {name} 上没有 SkeletonAnimation 组件，播不了动画", this);
            return;
        }

        if (_skeletonGraphic == null)
        {
            // 动画能播，但 Canvas 下没有渲染器就什么都看不见 —— 这种「不报错但空白」最难查，
            // 所以单独提一句
            Debug.LogWarning($"[CharacterGraphic] {name} 上没有 SkeletonGraphic，在 UI 里不会显示", this);
        }

        // AnimationState 的 getter 自己会 Initialize，预制体刚实例化还没走过一帧也能直接用
        _skeletonAnimation.AnimationState?.SetAnimation(0, clip, true);
    }
}
