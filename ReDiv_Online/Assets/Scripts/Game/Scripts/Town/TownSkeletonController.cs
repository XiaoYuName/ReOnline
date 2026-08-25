using Sirenix.OdinInspector;
using Spine.Unity;
using UnityEngine;
using XFramework;

/// <summary>
/// 城镇角色Spine控制器
/// </summary>
public class TownSkeletonController : GameBase
{
    #region Animation
    [BoxGroup("Animation"),LabelText("待机动画"),SpineAnimation]
    public string IdleAnimation = "Idle";
    [BoxGroup("Animation"),LabelText("走路动画"),SpineAnimation]
    public string WallAnimation = "Move";
    [BoxGroup("Animation"),LabelText("奔跑动画"),SpineAnimation]
    public string RunAnimation = "Run";
    #endregion
    
    private SkeletonGraphic  skeletonGraphic;
    private SkeletonAnimation skeletonAnimation;

    public void Initialize()
    {
        skeletonAnimation = GetComponent<SkeletonAnimation>();
        skeletonGraphic = GetComponent<SkeletonGraphic>();
        skeletonAnimation.AnimationState.SetAnimation(0,IdleAnimation, true);
    }

}
