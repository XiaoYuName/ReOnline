using Sirenix.OdinInspector;
using UnityEngine;
using XFramework;

/// <summary>
/// 城镇里一个触发器的**场上代表**。挂在运行时 new 出来的一个空节点上：
///
/// <code>
/// Trigger_1  [本组件]              ← 摆在 (PosX, PosY)，挂在 Games/Backgrounds 下
/// └── (IconPrefab 的实例)          ← 地面标记，**可空**（没配就只有一个看不见的判定区）
/// </code>
///
/// 传送点还有一个**出口点**（`ArriveOffset`）：别人从对端传送过来时站在这儿。
/// 运行时它只是两个数（不建节点）；摆位窗口里是一个可拖的子节点（见 <see cref="OnDrawGizmos"/>）。
///
/// **为什么不做成预制体**：它自己没有任何美术，只是「判定区 + 可选的地面标记挂载点」。
/// 真正的美术在 <c>IconPrefab</c> 那一列里，一个触发器一张图。
///
/// **为什么运行时也要有这个节点**（判定本身是 <see cref="TownTriggers"/> 的纯几何，
/// 不需要 GameObject）：
///   ① 配了 <c>IconPrefab</c> 时得有个地方挂；
///   ② Scene 视图里能看到判定区（<see cref="OnDrawGizmos"/>）—— 「踩了没反应」
///      这类问题第一件事就是看人有没有真的走进那个框；
///   ③ 摆位窗口的预览对象用的是同一个组件，所以编辑器里所见即游戏里所得。
///
/// ⚠️ **挂在 <c>Games/Backgrounds</c> 根节点下，不是背景那张图的子节点** ——
/// 触发器和时段无关（一个城镇一套，早/中/晚共用），塞进背景里换时段就会被一起拆掉。
/// 那个根节点是原点 + 缩放 1，所以 localPosition 就是世界坐标。
/// </summary>
public class TownTriggerController : GameBase
{
    [BoxGroup("配置（运行时由 TownTrigger 表填）"), LabelText("触发器ID"), ReadOnly]
    [SerializeField]
    private int triggerId;

    [BoxGroup("配置（运行时由 TownTrigger 表填）"), LabelText("类型"), ReadOnly]
    [Tooltip("1=传送到别的城镇 2=打开副本界面")]
    [SerializeField]
    private int kind;

    [BoxGroup("配置（运行时由 TownTrigger 表填）"), LabelText("目标ID"), ReadOnly]
    [SerializeField]
    private int targetId;

    [BoxGroup("配置（运行时由 TownTrigger 表填）"), LabelText("提示文字"), ReadOnly]
    [SerializeField]
    private string triggerName;

    [BoxGroup("配置（运行时由 TownTrigger 表填）"), LabelText("矩形大小")]
    [Tooltip("世界单位。摆位窗口拖的就是这两个值")]
    [SerializeField]
    private Vector2 size = Vector2.one;

    [BoxGroup("配置（运行时由 TownTrigger 表填）"), LabelText("出口点偏移")]
    [Tooltip("别人从对端传送过来时站在「本触发器中心 + 这个偏移」上。只有传送点用得到")]
    [SerializeField]
    private Vector2 arriveOffset;

    /// <summary>
    /// 摆位窗口里那个**可拖的出口点子节点**的名字。运行时不会有这个节点
    /// （出口点只是两个数），只有编辑器预览才建。
    /// </summary>
    public const string ExitNodeName = "出口点";

    /// <summary>配置行。运行时由 <see cref="Bind"/> 塞进来，摆位窗口的预览对象也一样。</summary>
    public TownTrigger Row { get; private set; }

    public int TriggerId => triggerId;

    public int Kind => kind;

    public int TargetId => targetId;

    /// <summary>矩形大小（世界单位）。摆位窗口会直接改它。</summary>
    public Vector2 Size
    {
        get => size;
        set => size = new Vector2(Mathf.Max(0f, value.x), Mathf.Max(0f, value.y));
    }

    /// <summary>
    /// 出口点相对本触发器中心的偏移 —— **别人从对端传送过来时站这儿**。
    /// 只有传送点（<c>Kind=1</c>）用得到。
    /// </summary>
    public Vector2 ArriveOffset
    {
        get => arriveOffset;
        set => arriveOffset = value;
    }

    /// <summary>
    /// 把一行配置装进来，并把节点摆到那个世界坐标上。
    ///
    /// 位置作用在**本节点**（和城镇角色一样，位置永远在外层）——
    /// 地面标记是子节点，跟着走。
    /// </summary>
    public void Bind(TownTrigger row)
    {
        Row = row;

        if (row == null)
        {
            return;
        }

        Configure(row.TriggerId, row.Kind, row.TargetId, row.Name, new Vector2(row.Width, row.Height),
            new Vector2(row.ArriveOffsetX, row.ArriveOffsetY));

        Vector3 p = transform.position;
        transform.position = new Vector3(row.PosX, row.PosY, p.z);
    }

    /// <summary>
    /// 只填「画框和标签要用的那几个值」，不改位置。
    ///
    /// **给摆位窗口用的**：那边是直接读 Luban 导出的 json（还没写回 Excel 的改动
    /// 不在配置对象里），所以拿不到 <c>TownTrigger</c> 行 —— 但框和颜色照样要对。
    /// </summary>
    public void Configure(int triggerId, int kind, int targetId, string name, Vector2 size,
                          Vector2 arriveOffset)
    {
        this.triggerId = triggerId;
        this.kind = kind;
        this.targetId = targetId;
        triggerName = name;
        Size = size;
        ArriveOffset = arriveOffset;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 把判定区画出来。传送和副本用不同颜色 —— 摆位时一眼能分清，
    /// 而且「踩了没反应」时能看出人是不是真的走进了框。
    /// </summary>
    private void OnDrawGizmos()
    {
        Color fill = kind == TownTriggers.KindDungeon
            ? new Color(1f, 0.75f, 0.2f, 0.18f)   // 副本：橙
            : new Color(0.3f, 0.7f, 1f, 0.18f);   // 传送：蓝

        Color line = new Color(fill.r, fill.g, fill.b, 0.95f);

        Vector3 center = transform.position;
        Vector3 box = new Vector3(size.x, size.y, 0.01f);

        Gizmos.color = fill;
        Gizmos.DrawCube(center, box);

        Gizmos.color = line;
        Gizmos.DrawWireCube(center, box);

        // 中心十字：矩形很扁的时候光靠边框看不出中心在哪
        Gizmos.DrawLine(center + Vector3.left * 0.15f, center + Vector3.right * 0.15f);
        Gizmos.DrawLine(center + Vector3.down * 0.15f, center + Vector3.up * 0.15f);

        string label = string.IsNullOrEmpty(triggerName) ? $"#{triggerId}" : $"#{triggerId} {triggerName}";
        UnityEditor.Handles.Label(center + Vector3.up * (size.y * 0.5f + 0.15f), label);

        DrawExitGizmo(center);
    }

    /// <summary>
    /// 画**出口点**（别人从对端传送过来站的位置）。只有传送点画。
    ///
    /// 优先用一个叫 <see cref="ExitNodeName"/> 的子节点 —— 摆位窗口就是靠它让人**拖**出口点的
    /// （拖的是 transform，所以得以它为准）。没有那个子节点就用
    /// <see cref="arriveOffset"/> 这两个数，也就是运行时的情况。
    /// </summary>
    private void DrawExitGizmo(Vector3 center)
    {
        if (kind != TownTriggers.KindChangeTown)
        {
            return;
        }

        Transform exitNode = transform.Find(ExitNodeName);
        Vector3 exit = exitNode != null
            ? exitNode.position
            : center + new Vector3(arriveOffset.x, arriveOffset.y, 0f);

        Gizmos.color = new Color(0.4f, 1f, 0.55f, 0.95f);
        Gizmos.DrawWireSphere(exit, 0.18f);
        Gizmos.DrawLine(center, exit);

        UnityEditor.Handles.Label(exit + Vector3.down * 0.28f, "出口");
    }
#endif
}
