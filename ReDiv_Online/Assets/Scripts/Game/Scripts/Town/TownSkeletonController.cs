using Sirenix.OdinInspector;
using Spine.Unity;
using UnityEngine;
using XFramework;

/// <summary>
/// 城镇角色Spine控制器。挂在**城镇控制预制体**根节点上，路径填在配置表
/// <c>CharacterForm.SkeletonTown</c> 列（按形态分，所以觉醒之后城镇里的形象也会变）。
///
/// 这是**世界空间**的 Spine（<see cref="SkeletonAnimation"/> + MeshRenderer），
/// 不是 Canvas 下的 SkeletonGraphic —— 所以移动是改 <c>transform.position</c>，
/// 朝向是翻 <c>localScale.x</c>。实例统一挂在场景的 <c>SkeletonCharacters</c> 节点下。
///
/// 自己和别的玩家用的是**同一个预制体、同一个控制器**，区别只在谁来驱动：
///   自己    —— <see cref="MainCommonUI"/> 每帧按摇杆算位置，然后上报服务端
///   别人    —— 按服务端推下来的坐标插值追过去（<see cref="MoveTowards"/>）
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
    // 位置与朝向
    // ------------------------------------------------------------------

    /// <summary>直接落到某个位置（进城镇的第一帧、或者远端角色第一次出现时用）。</summary>
    public void Teleport(Vector2 position)
    {
        Vector3 p = transform.position;
        transform.position = new Vector3(position.x, position.y, p.z);
    }

    /// <summary>
    /// 按方向走一帧，返回是不是真的动了。自己的角色用这个 ——
    /// 本地立刻响应摇杆，不等服务端。
    /// </summary>
    public bool MoveByInput(Vector2 direction, float deltaTime)
    {
        if (direction.sqrMagnitude < 0.0001f)
        {
            SetMoving(false);
            return false;
        }

        Vector2 step = direction.normalized * (MoveSpeed * deltaTime);
        transform.position += new Vector3(step.x, step.y, 0f);

        // 只在水平方向有明显输入时才翻身，否则纯上下走会来回抖
        if (Mathf.Abs(direction.x) > 0.01f)
        {
            SetFacing(direction.x >= 0f ? 1 : -1);
        }

        SetMoving(true);
        return true;
    }

    /// <summary>
    /// 朝服务端报来的坐标插值移动。别人的角色用这个。
    /// </summary>
    public void MoveTowards(Vector2 target, bool moving, float deltaTime)
    {
        Vector3 current = transform.position;
        Vector3 goal = new Vector3(target.x, target.y, current.z);

        transform.position = Vector3.Lerp(current, goal, Mathf.Clamp01(RemoteLerpSpeed * deltaTime));

        SetMoving(moving);
    }

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
