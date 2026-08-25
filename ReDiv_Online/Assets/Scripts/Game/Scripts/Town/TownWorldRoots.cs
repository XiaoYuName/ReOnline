using UnityEngine;

/// <summary>
/// 城镇里那几个**世界空间**父节点的查找入口。
///
/// 场景结构（都在原点、缩放 1，所以挂上去的东西 local 坐标就是世界坐标）：
/// <code>
/// GameManager/Games/SkeletonCharacters   ← 角色（外层控制器 + 按形态的 Spine）
/// GameManager/Games/Backgrounds          ← 背景（外层控制器 + 按时段的 SpriteRenderer）
/// </code>
///
/// 靠 **Tag** 找而不是按路径找：节点在层级里挪位置（`Games` 这一层就是 2026-08-25 新加的）
/// 不该让代码跟着改。代价是 Tag 必须在 Tags &amp; Layers 里登记过，没登记
/// <c>FindGameObjectWithTag</c> 会**抛异常**而不是返回 null —— 所以统一从这里查，
/// 别在各处再写一遍 try/catch。
/// </summary>
public static class TownWorldRoots
{
    /// <summary>角色挂载根节点的 Tag。</summary>
    public const string CharactersTag = "SkeletonCharacters";

    /// <summary>背景挂载根节点的 Tag。</summary>
    public const string BackgroundsTag = "Backgrounds";

    /// <summary>
    /// 按 Tag 找世界空间根节点。找不到就报错并返回 null ——
    /// 静默失败的话表现成「城镇里什么都没有」，很难往回查。
    /// </summary>
    public static GameObject Find(string tag)
    {
        try
        {
            GameObject found = GameObject.FindGameObjectWithTag(tag);

            if (found == null)
            {
                Debug.LogError($"[TownWorldRoots] 场景里找不到 Tag 为 {tag} 的节点");
            }

            return found;
        }
        catch (UnityException)
        {
            // Tag 没在 Project Settings > Tags & Layers 里登记时会走到这里
            Debug.LogError($"[TownWorldRoots] Tag「{tag}」没在 Tags & Layers 里登记");
            return null;
        }
    }
}
