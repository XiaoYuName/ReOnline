using Sirenix.OdinInspector;
using Spine.Unity;
using UnityEngine;
using XFramework;

/// <summary>
/// 城镇角色Spine控制器。挂在**城镇控制预制体**根节点上，路径填在配置表
/// <c>CharacterForm.SkeletonTown</c> 列（按形态分，所以觉醒之后城镇里的形象也会变）。
///
/// 这是**世界空间**的 Spine（<see cref="SkeletonAnimation"/> + MeshRenderer），
/// 不是 Canvas 下的 SkeletonGraphic。
///
/// ⚠️ 它是 <see cref="TownCharacterController"/> 的**子件** —— 被塞进那边的
/// <c>SkeletonTown</c> 节点下。**职责只有动画和朝向**：
///   位置（走路 / 传送 / 远端插值）在**外层**做，否则头顶名字不会跟着走。
/// 移动速度这两个数值留在这里，是因为它们按角色微调（美术摆预制体时就能试），
/// 外层读 <see cref="MoveSpeed"/> / <see cref="RemoteLerpSpeed"/> 来用。
///
/// 朝向**只翻自己**（翻外层的话名字文字会镜像）。
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

    #region Move
    /// <summary>
    /// 城镇里的移动速度（世界单位/秒）。放在预制体上而不是配置表里：
    /// 这是手感数值，按角色微调很正常，美术摆预制体时就能试。
    /// </summary>
    [BoxGroup("Move"), LabelText("移动速度")]
    public float MoveSpeed = 3f;

    /// <summary>
    /// 别人的角色追坐标时的插值速度倍率。服务端每 ~100ms 才推一次坐标，
    /// 直接赋值会一格一格跳，所以要插值。太小会拖影、太大会抖。
    /// </summary>
    [BoxGroup("Move"), LabelText("远端插值速度")]
    public float RemoteLerpSpeed = 12f;
    #endregion

    private SkeletonAnimation skeletonAnimation;

    private SkeletonRenderer skeletonRenderer;

    /// <summary>底层的 <see cref="SkeletonAnimation"/>（放动画）。</summary>
    public SkeletonAnimation Animation
    {
        get
        {
            if (skeletonAnimation == null)
            {
                skeletonAnimation = GetComponent<SkeletonAnimation>();
            }

            return skeletonAnimation;
        }
    }

    /// <summary>
    /// 底层的 <see cref="SkeletonRenderer"/>。外层拿它接名字的 BoneFollower。
    ///
    /// ⚠️ 这个 Spine 版本里 <see cref="SkeletonAnimation"/> **已经不再继承
    /// SkeletonRenderer** 了（源码里那句「Transfer of former base class
    /// SkeletonRenderer parameters」就是拆开的痕迹）—— 两者是同物体上的**两个独立组件**。
    /// 所以别想着把 Animation 直接喂给 BoneFollower，编译期就会报
    /// 「Cannot implicitly convert SkeletonAnimation to SkeletonRenderer」。
    /// </summary>
    public SkeletonRenderer Renderer
    {
        get
        {
            if (skeletonRenderer == null)
            {
                skeletonRenderer = GetComponent<SkeletonRenderer>();
            }

            return skeletonRenderer;
        }
    }

    /// <summary>当前正在播的动画名。用来避免每帧重复 SetAnimation（那会把动画重头播）。</summary>
    private string currentAnimation;

    /// <summary>当前朝向：1 右 / -1 左。</summary>
    public int Facing { get; private set; } = 1;

    /// <summary>初始化。从对象池取出来复用时也要再调一次 —— 它会把状态归零。</summary>
    public void Initialize()
    {
        if (skeletonAnimation == null)
        {
            skeletonAnimation = GetComponent<SkeletonAnimation>();
        }

        currentAnimation = null;
        Facing = 1;
        ApplyFacing();
        PlayIdle();
    }

    // ------------------------------------------------------------------
    // 动画
    // ------------------------------------------------------------------

    public void PlayIdle() => Play(IdleAnimation);

    public void PlayWalk() => Play(WallAnimation);

    public void PlayRun() => Play(RunAnimation);

    /// <summary>
    /// 播一个动画。**同名不重播** —— 每帧无脑 SetAnimation 会让走路动画永远停在第一帧。
    /// </summary>
    private void Play(string animationName)
    {
        if (string.IsNullOrEmpty(animationName) || currentAnimation == animationName)
        {
            return;
        }

        if (skeletonAnimation == null)
        {
            skeletonAnimation = GetComponent<SkeletonAnimation>();
        }

        if (skeletonAnimation == null)
        {
            return;
        }

        currentAnimation = animationName;

        // AnimationState 的 getter 自己会 Initialize，刚从池里取出来没走过帧也能用
        skeletonAnimation.AnimationState.SetAnimation(0, animationName, true);
    }

    /// <summary>按「在不在动」切走路 / 待机。移动逻辑每帧调它，同名不重播所以很便宜。</summary>
    public void SetMoving(bool moving)
    {
        if (moving)
        {
            PlayWalk();
        }
        else
        {
            PlayIdle();
        }
    }

    // ------------------------------------------------------------------
    // 朝向
    // ------------------------------------------------------------------

    public void SetFacing(int facing)
    {
        int normalized = facing >= 0 ? 1 : -1;

        if (Facing == normalized)
        {
            return;
        }

        Facing = normalized;
        ApplyFacing();
    }

    /// <summary>
    /// 朝向靠翻 localScale.x。**取绝对值再乘符号**，不要直接 *= -1 ——
    /// 从对象池里复用出来的实例可能已经是翻过的状态，累乘会翻回去。
    /// </summary>
    private void ApplyFacing()
    {
        Vector3 scale = transform.localScale;
        transform.localScale = new Vector3(Mathf.Abs(scale.x) * Facing, scale.y, scale.z);
    }
}
