using UnityEngine;

/// <summary>
/// 城镇**可行走边界**的碰撞查询。纯几何、无状态，所以是 static。
///
/// 边界是美术在每张背景预制体里画的 <c>EdgeCollider2D</c>（节点名 <c>GroundCollider</c>），
/// 围出一圈能走的地面。**这里只做查询，不开物理模拟** ——
/// 角色身上没有 <c>Rigidbody2D</c> / <c>Collider2D</c>，移动照旧是直接写
/// <c>transform.position</c>，只是写之前先用 <see cref="Physics2D.CircleCast"/> 问一句
/// 「这一步会不会穿过边界」。
///
/// 为什么不用「点在多边形内」判定：<c>EdgeCollider2D</c> 是一条**线**、没有面积，
/// <c>OverlapPoint</c> 永远返回 false。扫掠检测（cast）对开口的折线一样有效 ——
/// 美术要画一段拦不住两头的栏杆也照样管用。
///
/// 为什么不用物理（给角色加 Rigidbody2D 让引擎推）：那要把移动改成 <c>MovePosition</c>、
/// 受 FixedUpdate 节奏影响，还得管远端玩家的刚体互相推挤。我们只需要「这一步能不能走」，
/// 一次 cast 就够了，现有的手感和 100ms 上报节流一点都不用动。
///
/// ⚠️ **边界只夹自己，不夹别人**：远端玩家的坐标是服务端转发的权威值，
/// 夹回来只会让他在我屏幕上和他自己看到的位置不一致。
/// </summary>
public static class TownGround
{
    /// <summary>
    /// 地面边界碰撞体所在的**物理层**。美术画完边界要把那个节点放到这一层，
    /// 放错了表现成「边界完全不起作用、能一直走出画面」——
    /// <c>TownGroundController</c> 在 Awake 里会把这种情况报出来。
    ///
    /// ⚠️ 别和 **Sorting Layer** 的 `Ground` 搞混，那是渲染排序、和物理层没关系。
    /// </summary>
    public const string LayerName = "Ground";

    /// <summary>-1 = 还没算过。层名查不到时置 0（= 不拦任何东西）。</summary>
    private static int mask = -1;

    /// <summary>边界层的 LayerMask。工程里没有这个层时是 0，此时**退化成不做边界**。</summary>
    public static int Mask
    {
        get
        {
            if (mask == -1)
            {
                int layer = LayerMask.NameToLayer(LayerName);

                if (layer < 0)
                {
                    Debug.LogError($"[TownGround] 工程里没有名为「{LayerName}」的层，边界不会生效");
                    mask = 0;
                }
                else
                {
                    mask = 1 << layer;
                }
            }

            return mask;
        }
    }

    /// <summary>
    /// 撞墙后留的缝。不留的话下一帧的 cast 起点正好压在碰撞体上，
    /// 「起点重叠」的命中会让角色**贴着墙走不动**（连离开墙的方向都被判成撞）。
    /// </summary>
    private const float SkinWidth = 0.01f;

    /// <summary>
    /// 从 <paramref name="from"/> 走 <paramref name="delta"/>，返回**实际能走到**的位置。
    ///
    /// 做法是标准的「撞上就沿表面滑」（collide & slide）：
    /// <list type="number">
    ///   <item>朝位移方向扫一次，没撞就整步走完；</item>
    ///   <item>撞了就**走到贴墙为止**（留一点缝），把剩下的位移**投影到表面切线上**，
    ///         再扫一次 —— 这样斜着撞墙会顺着墙滑过去，而不是停住。</item>
    /// </list>
    ///
    /// ⚠️ **别退回「按轴分离」那种写法**（先试 X 再试 Y）。它对横平竖直的墙没问题，
    /// 但边界是美术手画的折线、大部分段都是斜的 —— 实测过：贴着微微上升的地面底边往右走，
    /// X 方向直接撞上斜面，人在 x=3.23 就卡死了，怎么推都过不去。
    ///
    /// 最多迭代两次（撞角落时第二次多半又撞上，那就停下），每帧最多两次 cast ——
    /// 只有自己这一个角色在调，可以忽略。
    /// </summary>
    public static Vector2 Move(Vector2 from, Vector2 delta, float radius)
    {
        // 没有边界层 ⇒ 一律放行。**配漏了要退化成「能走」，不能变成「一步都走不了」**
        if (Mask == 0)
        {
            return from + delta;
        }

        Vector2 position = from;
        Vector2 remaining = delta;

        for (int i = 0; i < 2 && remaining.sqrMagnitude > 1e-10f; i++)
        {
            float distance = remaining.magnitude;
            Vector2 direction = remaining / distance;

            RaycastHit2D hit = Cast(position, direction, distance, radius);

            if (hit.collider == null)
            {
                position += remaining;
                break;
            }

            // 起点已经压在碰撞体里了（出生点贴太近、或者美术把边界挪到了人脚下）：
            // 沿法线往外挪一丁点，**这一帧的位移一点都不放行**，下一次循环再试。
            // ⚠️ 别图省事写成「朝离开墙的方向就整步放行」—— 那等于免检，
            // 实测角色会直接穿过边界飞到 x=53（起点只要接触就算 fraction=0，条件太容易满足）。
            if (hit.fraction <= 0f)
            {
                position += hit.normal * SkinWidth;
                continue;
            }

            // 走到贴墙为止（留一点缝，免得下一帧起点压在碰撞体上）。
            // ⚠️ **必须用 fraction 不能用 hit.distance** —— 对 CircleCast 来说
            // `distance` 是「起点到接触点」的距离，比圆心真正能走的距离多出大约一个半径；
            // 拿它当位移用等于每次贴墙都把角色往墙里塞一个半径，塞进去之后
            // （queriesStartInColliders 默认 true）所有方向的扫掠都返回 0，人就彻底卡死。
            // 实测踩过：贴着地面底边走两步就再也动不了了。
            float advance = Mathf.Max(0f, hit.fraction * distance - SkinWidth);
            position += direction * advance;

            // 剩下的位移贴着表面滑：把「扎进墙里」的那个分量减掉
            Vector2 rest = remaining - direction * advance;
            remaining = rest - hit.normal * Vector2.Dot(rest, hit.normal);
        }

        return position;
    }

    /// <summary>这一步会不会撞上边界。<paramref name="radius"/> 给 0 就退化成射线。</summary>
    public static bool Blocked(Vector2 from, Vector2 delta, float radius)
    {
        float distance = delta.magnitude;

        if (distance <= 0f || Mask == 0)
        {
            return false;
        }

        return Cast(from, delta / distance, distance, radius).collider != null;
    }

    /// <summary>
    /// 一次扫掠。<paramref name="radius"/> 给 0 就退化成射线 ——
    /// 半径不为 0 时用圆扫，能避免高速下正好从折线的顶点缝里穿过去。
    /// </summary>
    private static RaycastHit2D Cast(Vector2 from, Vector2 direction, float distance, float radius) =>
        radius > 0f
            ? Physics2D.CircleCast(from, radius, direction, distance, Mask)
            : Physics2D.Raycast(from, direction, distance, Mask);
}
