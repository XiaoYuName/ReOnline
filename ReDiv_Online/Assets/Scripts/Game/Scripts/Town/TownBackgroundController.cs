using Sirenix.OdinInspector;
using UnityEngine;
using XFramework;

/// <summary>
/// 城镇背景的**外层控制器** —— 挂在
/// <c>Assets/AddressableAssets/Remote/Prefabs/Town/TownBackgroundController.prefab</c> 根节点上，
/// **所有城镇共用这一个**（和 <see cref="TownCharacterController"/> 一个套路）。
///
/// <code>
/// TownBackgroundController        ← 挂在 Games/Backgrounds 下（世界空间，原点 + 缩放 1）
/// ├── Background                  ← 运行时按「城镇 + 时段」把背景预制体塞进这里
/// └── StartPoint                  ← 出生点：玩家进城镇就站这儿
/// </code>
///
/// **背景是世界空间的 SpriteRenderer，不再是 UI**（2026-08-25 改的）。原来它挂在
/// `UIBackground` Canvas（ScreenSpaceCamera + CanvasScaler）下，那是 canvas 像素空间、
/// 还会按分辨率缩放，而角色 / 出生点是世界空间的 —— 两套坐标系的比例一旦随分辨率变，
/// 同一个坐标在不同人屏幕上就落在美术的不同位置（联机里这是硬伤：他站喷泉边、我看他站台阶上）。
/// 现在美术**钉在世界坐标里**，适配交给相机做。
///
/// **职责划分**（别混）：
///   本类                     出生点、把背景塞进来 / 摘出去
///   内层背景预制体           就一个 SpriteRenderer（材质 + Sorting Layer + 拉成 16:9 的 scale）
///   <see cref="MainCommonUI"/>  决定「该显示哪张」并负责取用回收
///
/// ⚠️ **内层的 scale 是美术在预制体里烘好的**（方图压扁存，靠 x≈1.7778 拉开），
/// 本类**不去动它** —— 想改画面大小就在那个预制体上改，代码里别偷偷覆盖。
///
/// ⚠️ **它不认识网络层**：不订阅任何东西、不碰 `Conn`、不自己算时段。
/// </summary>
public class TownBackgroundController : GameBase
{
    /// <summary>外层预制体的 Addressable key。所有城镇共用，所以是常量而不是配置列。</summary>
    public const string PrefabKey =
        "Assets/AddressableAssets/Remote/Prefabs/Town/TownBackgroundController.prefab";

    [BoxGroup("节点"), LabelText("背景挂载点"), Required]
    [Tooltip("运行时把按时段取的背景预制体塞到这里")]
    [SerializeField]
    private Transform backgroundTran;

    [BoxGroup("节点"), LabelText("出生点"), Required]
    [Tooltip("玩家进这个城镇时站的位置。就是个空节点，摆到背景上想让人出现的地方即可")]
    [SerializeField]
    private Transform startPoint;

    /// <summary>当前挂着的背景实例。还没塞进来 / 配置没配这个时段时是 null。</summary>
    public GameObject Background { get; private set; }

    /// <summary>
    /// 出生点世界坐标。没配出生点节点就退回本节点自己的位置（通常是原点）——
    /// 那会表现成「所有人挤在画面正中」，所以 <see cref="Awake"/> 里会明说一次。
    /// </summary>
    public Vector2 SpawnPosition
    {
        get
        {
            Vector3 p = startPoint != null ? startPoint.position : transform.position;
            return new Vector2(p.x, p.y);
        }
    }

    /// <summary>
    /// 把一张背景装进来。传 null 是合法的（这个城镇的这个时段还没配图），
    /// 那样只是没有画面 —— 出生点和角色照常工作。
    /// </summary>
    public void Bind(GameObject background)
    {
        Background = background;

        if (background == null)
        {
            return;
        }

        background.transform.SetParent(backgroundTran, false);
        background.transform.localPosition = Vector3.zero;
    }

    /// <summary>解绑并把背景交还给调用方（由它负责回收）。</summary>
    public GameObject Unbind()
    {
        GameObject background = Background;
        Background = null;
        return background;
    }

    private void Awake()
    {
        if (startPoint == null)
        {
            Debug.LogWarning($"[TownBackground] {name} 没配出生点节点，落点退回控制器自己的位置");
        }
    }

#if UNITY_EDITOR
    /// <summary>把出生点画出来 —— 摆位置时不用进 Play 也能看见。</summary>
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
