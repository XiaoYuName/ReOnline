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
/// └── Background                  ← 运行时按「城镇 + 时段」把背景预制体塞进这里
///     └── Town_50020              ← SpriteRenderer + TownGroundController
///         ├── StartPoints         ← **出生点**（跟着地图美术走，所以在内层）
///         └── GroundCollider      ← **可行走边界**（同上）
/// </code>
///
/// **背景是世界空间的 SpriteRenderer，不再是 UI**（2026-08-25 改的）。原来它挂在
/// `UIBackground` Canvas（ScreenSpaceCamera + CanvasScaler）下，那是 canvas 像素空间、
/// 还会按分辨率缩放，而角色 / 出生点是世界空间的 —— 两套坐标系的比例一旦随分辨率变，
/// 同一个坐标在不同人屏幕上就落在美术的不同位置（联机里这是硬伤：他站喷泉边、我看他站台阶上）。
/// 现在美术**钉在世界坐标里**，适配交给相机做。
///
/// **职责划分**（别混）：
///   本类                        把背景塞进来 / 摘出去，出生点**转发**给内层
///   <see cref="TownGroundController"/>  内层：出生点 + 可行走边界（跟着地图美术走，一张图一套）
///   <see cref="MainCommonUI"/>          决定「该显示哪张」并负责取用回收
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

    /// <summary>当前挂着的背景实例。还没塞进来 / 配置没配这个时段时是 null。</summary>
    public GameObject Background { get; private set; }

    /// <summary>
    /// 当前背景上的地面数据（出生点 + 边界）。背景预制体上没挂
    /// <see cref="TownGroundController"/> 时是 null。
    /// </summary>
    public TownGroundController Ground { get; private set; }

    /// <summary>
    /// 出生点世界坐标 —— **来自当前那张背景**（<see cref="TownGroundController"/>）。
    /// 背景还没塞进来、或者那张图没挂地面组件时退回本节点自己的位置（通常是原点）：
    /// **配漏了要退化，不能让人进不了城镇**。
    /// </summary>
    public Vector2 SpawnPosition =>
        Ground != null ? Ground.SpawnPosition : new Vector2(transform.position.x, transform.position.y);

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

        // 出生点和边界都在这张图上，换一张图就换一套
        Ground = background.GetComponent<TownGroundController>();

        if (Ground == null)
        {
            Debug.LogWarning($"[TownBackground] {background.name} 上没有 TownGroundController，" +
                             $"这张图没有出生点也没有可行走边界");
        }
    }

    /// <summary>解绑并把背景交还给调用方（由它负责回收）。</summary>
    public GameObject Unbind()
    {
        GameObject background = Background;
        Background = null;
        Ground = null;
        return background;
    }
}
