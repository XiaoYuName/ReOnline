using UnityEngine;

/// <summary>
/// 一张**副本区域背景** —— 挂在区域背景预制体的根节点上
/// （现在只有 <c>Assets/AddressableAssets/Remote/Prefabs/Dungeon/bg_500170/bg_500170_Preview.prefab</c>，
/// 路径填在配置表 <c>DungeonArea.BackgroundKey</c> 那一列）。
///
/// 预制体上是一个 <c>RawImage</c>（1024×1024，VariantCard 材质，来自国服资源还原）。
/// **贴图是压扁的方图**（和城镇背景一个套路），所以挂进来之后要**拉满整屏**才是正确比例
/// —— 那一步在 <see cref="PopDungeonUI"/> 里做（`Stretch` 那几行）。
///
/// ⚠️ **它挂在 <c>PopDungeonUI</c> 自己的层级里**（`UIMask` 下的第一个子节点，
/// 压在 `Contents` 和 `Background` 下面），**不走框架的 `UIBackground` 背景层**
/// （用户 2026-08-27 订正的 —— 一开始走的是那一层，结果副本界面开着时城镇整个露在外面：
/// 背景层的 Canvas 在城镇角色下面，只盖住了城镇背景）。
///
/// 所以本类**不是** <c>UIBackground</c> 的派生类，就是个普通组件。它的作用有两个：
/// <list type="bullet">
///   <item>给 <c>PopDungeonUI</c> 一个**有类型的引用**存着
///         （<c>PopDungeonUI.AreaBackground</c>，用户明确要求的），方便以后在背景上做逻辑；</item>
///   <item>以后区域背景真要自己做点事（视差、时段变色、播动画、叠特效）就写在这里，
///         **别写到 <c>PopDungeonUI</c> 里去**。</item>
/// </list>
///
/// 现在它一行逻辑都没有 —— 这是有意的，占个位。
/// </summary>
public class DungeonAreaBackground : MonoBehaviour
{
}
