using Sirenix.OdinInspector;
using Spine.Unity;
using TMPro;
using UnityEngine;
using XFramework;

/// <summary>
/// 城镇里一个角色的**外层控制器**。预制体
/// <c>Assets/AddressableAssets/Remote/Prefabs/Town/TownCharacterController.prefab</c>，
/// 所有角色共用这一个 —— 里面的 Spine 是运行时按形态塞进来的。
///
/// 分两层的原因：城镇角色不只有 Spine，还要挂名字、以后还有血条 / 称号 / 气泡等等。
/// 那些东西和「用哪个 Spine」无关，所以放外层，Spine 只当一个可替换的子件。
///
/// <code>
/// TownCharacterController          ← 移动（位置）都作用在这一层，名字才会跟着走
/// ├── SkeletonTown                 ← 运行时把 TownSkeletonController 预制体塞进这里
/// └── NameAnchor  [BoneFollower]   ← 跟随头部骨骼（只跟位置，不跟旋转/翻转）
///     └── Name    [TextMeshPro]    ← localPosition.y 就是**头顶偏移**，Inspector 里调
/// </code>
///
/// **职责划分**（别混）：
///   本类               位置（走路 / 传送 / 远端插值）、名字、**撞不撞边界**
///   TownSkeletonController  动画（待机/走路）、朝向（只翻它自己）
///
/// ⚠️ **朝向必须翻在 Spine 那一层，不能翻外层**。翻外层的话名字文字会跟着镜像。
///
/// ⚠️ **名字的 Y 偏移不要写在代码里**。BoneFollower 只写 <c>NameAnchor</c> 自己的
/// transform，碰不到子节点 —— 所以把偏移放在 <c>Name</c> 的 localPosition.y 上，
/// 编辑器里能实时看效果，也不用为了「BoneFollower 没有 offset 参数」去派生它
/// （派生会丢掉它那个骨骼下拉框：<c>BoneFollowerInspector</c> 是
/// <c>[CustomEditor(typeof(BoneFollower))]</c>，没开 editorForChildClasses）。
/// </summary>
public class TownCharacterController : GameBase
{
    [BoxGroup("节点"), LabelText("Spine 挂载点"), Required]
    [SerializeField]
    private Transform skeletonTran;

    [BoxGroup("节点"), LabelText("名字文本")]
    [SerializeField]
    private TextMeshPro nameText;

    [BoxGroup("节点"), LabelText("名字跟骨骼")]
    [Tooltip("挂在 NameAnchor 上。头顶偏移调 Name 子节点的 localPosition.y，不是这里")]
    [SerializeField]
    private BoneFollower nameFollower;

    [BoxGroup("移动"), LabelText("边界碰撞半径"), MinValue(0f)]
    [Tooltip("按脚下这一点做扫掠检测的半径。0 = 退化成一条射线。" +
             "调大角色会离墙更远就停下，调太大可能在窄处卡住")]
    [SerializeField]
    private float blockRadius = 0.05f;

    /// <summary>当前挂着的 Spine。没塞进来 / 配置没配城镇预制体时是 null。</summary>
    public TownSkeletonController Skeleton { get; private set; }

    /// <summary>Spine 的朝向。没有 Spine 时按 1（朝右）。</summary>
    public int Facing => Skeleton != null ? Skeleton.Facing : 1;

    // ------------------------------------------------------------------
    // 组装
    // ------------------------------------------------------------------

    /// <summary>
    /// 把一个 Spine 装进来。从对象池复用时也走这里 —— 它会把上一次的状态清掉。
    ///
    /// <paramref name="skeleton"/> 传 null 是合法的：那个形态还没做城镇预制体时，
    /// 外层照样要显示（至少名字在），只是没有形象。
    /// </summary>
    public void Bind(TownSkeletonController skeleton)
    {
        Skeleton = skeleton;

        if (skeleton != null)
        {
            skeleton.transform.SetParent(skeletonTran, false);
            skeleton.transform.localPosition = Vector3.zero;
            skeleton.Initialize();
        }

        BindNameFollower();
    }

    /// <summary>
    /// 把 BoneFollower 指到当前 Spine 上。
    ///
    /// ⚠️ **必须在运行时接** —— Spine 是按形态动态塞进来的，预制体里那个
    /// <c>skeletonRenderer</c> 只能是空的（Inspector 里会显示
    /// 「SkeletonRenderer is unassigned」，那是预期的，不是配错了）。
    /// 预制体里也因此把 <c>initializeOnAwake</c> 关了：Awake 时还没有骨架，
    /// 开着只会白报一次警告。
    /// </summary>
    private void BindNameFollower()
    {
        if (nameFollower == null)
        {
            return;
        }

        // ⚠️ 要的是 SkeletonRenderer，**不是** SkeletonAnimation ——
        // 这个 Spine 版本里两者已经拆成同物体上的两个独立组件，不再有继承关系
        SkeletonRenderer renderer = Skeleton != null ? Skeleton.Renderer : null;

        if (renderer == null)
        {
            nameFollower.enabled = false;
            return;
        }

        nameFollower.enabled = true;
        // 属性 setter 会顺带重新 Initialize 并挂 OnRebuild 回调，比直接写字段稳
        nameFollower.SkeletonRenderer = renderer;
    }

    /// <summary>解绑并把 Spine 交还给调用方（由它负责回收进对象池）。</summary>
    public TownSkeletonController Unbind()
    {
        TownSkeletonController skeleton = Skeleton;
        Skeleton = null;

        if (nameFollower != null)
        {
            // 不解开的话 BoneFollower 还握着已经回池的骨架，下一帧会追一个不该追的东西
            nameFollower.enabled = false;
            nameFollower.SkeletonRenderer = null;
        }

        return skeleton;
    }

    /// <summary>设置头顶名字。空串就把文本节点隐藏（自己的角色可能不想显示名字）。</summary>
    public void SetName(string value)
    {
        if (nameText == null)
        {
            return;
        }

        bool show = !string.IsNullOrEmpty(value);
        nameText.gameObject.SetActive(show);

        if (show)
        {
            nameText.text = value;
        }
    }

    // ------------------------------------------------------------------
    // 位置（都作用在外层，名字才会跟着走）
    // ------------------------------------------------------------------

    /// <summary>直接落到某个位置。进城镇第一帧、远端角色第一次出现时用。</summary>
    public void Teleport(Vector2 position)
    {
        Vector3 p = transform.position;
        transform.position = new Vector3(position.x, position.y, p.z);
    }

    /// <summary>
    /// 按摇杆方向走一帧，返回是不是真的动了。自己的角色用这个 ——
    /// 本地立刻响应，不等服务端。
    ///
    /// 速度取自 Spine 那一层（<see cref="TownSkeletonController.MoveSpeed"/>）：
    /// 那是按角色微调的手感数值，美术摆预制体时就能试。没有 Spine 就不动。
    /// </summary>
    public bool MoveByInput(Vector2 direction, float deltaTime)
    {
        if (Skeleton == null)
        {
            return false;
        }

        if (direction.sqrMagnitude < 0.0001f)
        {
            Skeleton.SetMoving(false);
            return false;
        }

        Vector2 step = direction.normalized * (Skeleton.MoveSpeed * deltaTime);

        // ⚠️ **先问边界能不能走再写位置**。判定点就是本节点的位置（角色锚点在脚下），
        // 按轴分离 ⇒ 贴着斜墙走会自然滑动。没配边界时 TownGround 会一律放行。
        // 只有自己走这条路：远端玩家用 MoveTowards，坐标是服务端转发的权威值，不夹
        Vector3 current = transform.position;
        Vector2 moved = TownGround.Move(new Vector2(current.x, current.y), step, blockRadius);
        transform.position = new Vector3(moved.x, moved.y, current.z);

        // 只在水平方向有明显输入时才翻身，否则纯上下走会来回抖
        if (Mathf.Abs(direction.x) > 0.01f)
        {
            Skeleton.SetFacing(direction.x >= 0f ? 1 : -1);
        }

        // ⚠️ 被边界挡住时**照样算"在走"**：玩家推着摇杆顶墙，播走路动画才对
        //（原地踏步），而且位置没变的话调用方的节流本来就不会上报
        Skeleton.SetMoving(true);
        return true;
    }

    /// <summary>朝服务端报来的坐标插值移动。别人的角色用这个。</summary>
    public void MoveTowards(Vector2 target, bool moving, float deltaTime)
    {
        float lerpSpeed = Skeleton != null ? Skeleton.RemoteLerpSpeed : 12f;

        Vector3 current = transform.position;
        Vector3 goal = new Vector3(target.x, target.y, current.z);

        transform.position = Vector3.Lerp(current, goal, Mathf.Clamp01(lerpSpeed * deltaTime));

        Skeleton?.SetMoving(moving);
    }

    public void SetFacing(int facing) => Skeleton?.SetFacing(facing);
}
