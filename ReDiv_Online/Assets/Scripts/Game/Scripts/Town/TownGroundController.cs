using Sirenix.OdinInspector;
using UnityEngine;
using XFramework;

/// <summary>
/// 一张城镇背景上的**地面数据** —— 挂在**内层背景预制体**根节点上
/// （`Assets/AddressableAssets/Remote/Prefabs/Town/<城镇>/Town_xxxxx.prefab`）。
///
/// 它管两样和这张图绑死的东西：
/// <code>
/// Town_50020                  ← SpriteRenderer（背景图）+ 本组件
/// ├── StartPoints             ← **出生点**：玩家进这个城镇站的位置
/// └── GroundCollider          ← **可行走边界**：EdgeCollider2D，围出能走的地面
/// </code>
///
/// 为什么这两样在**内层**而不是共用的外层控制器上：出生点和边界是**跟着地图美术走**的，
/// 一张图一套。外层是所有城镇共用的壳子，放在那儿就变成「所有城镇同一个落点」了
/// （2026-08-25 一开始就是那样，随后改成了现在这样）。
///
/// ⚠️ 代价是**一个城镇三个时段就有三份**（早/中/晚各一张背景预制体）。
/// 三份的出生点和边界应该是一致的 —— 改了一张记得另外两张一起改，
/// 不然玩家跨时段会发现「白天能走的地方晚上走不了」。
///
/// 边界的判定逻辑在 <see cref="TownGround"/>（纯查询、不开物理模拟），
/// 调用方是 <c>TownCharacterController.MoveByInput</c>，**只夹自己不夹别人**。
/// </summary>
public class TownGroundController : GameBase
{
    [BoxGroup("节点"), LabelText("出生点"), Required]
    [Tooltip("玩家进这个城镇时站的位置。就是个空节点，摆到地面上想让人出现的地方")]
    [SerializeField]
    private Transform startPoint;

    /// <summary>运行时收集到的边界碰撞体。只用来做自检和画 Gizmo，判定走的是物理查询。</summary>
    private Collider2D[] groundColliders;

    /// <summary>
    /// 出生点世界坐标。没配就退回本节点自己的位置 —— 那会表现成
    /// 「所有人挤在背景中心」，所以 <see cref="Awake"/> 里会明说一次。
    /// </summary>
    public Vector2 SpawnPosition
    {
        get
        {
            Vector3 p = startPoint != null ? startPoint.position : transform.position;
            return new Vector2(p.x, p.y);
        }
    }

    private void Awake()
    {
        if (startPoint == null)
        {
            Debug.LogWarning($"[TownGround] {name} 没配出生点节点，落点退回背景根节点的位置");
        }

        CheckColliders();
    }

    /// <summary>
    /// 自检边界：**一个都没有**、或者**层放错了**都只会表现成「能一直走出画面」，
    /// 从现象根本看不出是配漏了 —— 所以这里直接报出来。
    /// </summary>
    private void CheckColliders()
    {
        groundColliders = GetComponentsInChildren<Collider2D>(true);

        if (groundColliders.Length == 0)
        {
            Debug.LogWarning($"[TownGround] {name} 没有任何 Collider2D，这张图没有可行走边界");
            return;
        }

        int layer = LayerMask.NameToLayer(TownGround.LayerName);

        foreach (Collider2D collider in groundColliders)
        {
            if (collider.gameObject.layer != layer)
            {
                Debug.LogError($"[TownGround] {name}/{collider.name} 不在「{TownGround.LayerName}」层上" +
                               $"（现在是「{LayerMask.LayerToName(collider.gameObject.layer)}」），" +
                               $"这块边界不会生效");
            }
        }
    }

#if UNITY_EDITOR
    /// <summary>把出生点画出来 —— 边界本身 Unity 自己会画（选中碰撞体就能看到）。</summary>
    private void OnDrawGizmos()
    {
        Vector3 center = startPoint != null ? startPoint.position : transform.position;

        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireSphere(center, 0.2f);
        Gizmos.DrawLine(center + Vector3.down * 0.4f, center + Vector3.up * 0.4f);
        Gizmos.DrawLine(center + Vector3.left * 0.4f, center + Vector3.right * 0.4f);
    }
#endif
}
