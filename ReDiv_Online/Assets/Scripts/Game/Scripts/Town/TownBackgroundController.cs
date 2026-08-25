using XFramework;

/// <summary>
/// 城镇背景控制器 —— 挂在**背景预制体根节点**上的组件。
///
/// 一个城镇有三个这样的预制体，对应早 / 中 / 晚三个时段，路径填在配置表
/// <c>Town</c> 的 <c>BgMorning</c> / <c>BgNoon</c> / <c>BgNight</c> 三列上。
///
/// 谁来换：<see cref="MainCommonUI"/>（城镇主界面）。它按 <c>TownManager</c> 给的
/// 「当前城镇 + 当前时段」算出 key，走 <c>UISystem.LoadUIBackground&lt;TownBackgroundController&gt;()</c>
/// 拿实例、<c>HideUIBackground()</c> 回收。**背景自己不订阅任何东西、不认识网络层。**
///
/// 它是 <see cref="UIBackground"/>（也就是 <c>UIBase</c>）的派生类，所以生命周期就是
/// UI 那一套 —— 入场 / 离场表现**重写 <c>Open()</c> / <c>Close()</c>** 即可
/// （云在飘、灯亮起来、粒子、Spine…）。基类不做任何事，也**不要**把某个城镇特有的逻辑写进来。
///
/// ⚠️ 别再往这里加 `Show()` / `Hide()` 之类的自定义钩子 —— 早先有过一对，
/// 在改成 UIBackground 派生之后就和 `Open()` / `Close()` 完全重复了，已经删掉。
///
/// 为什么背景是「一个时段一个预制体」而不是「一个预制体换图」：
/// 早中晚的差别往往不只是底图 —— 夜里多一层灯光和虫鸣、白天多一层人流，
/// 拆成三个预制体让美术各自摆，比在一个预制体里堆三套开关干净。
/// 用户 2026-08-25 明确要求三个。
/// </summary>
public class TownBackgroundController : UIBackground
{
}
