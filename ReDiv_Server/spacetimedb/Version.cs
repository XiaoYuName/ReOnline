using SpacetimeDB;

/// <summary>
/// 版本号与版本校验。
///
/// 为什么要有这个：客户端和服务端各自独立发布，很容易出现「客户端还是旧的、服务端已经改了
/// 表结构或 Reducer 语义」。那种情况下报出来的错五花八门（订阅失败、字段对不上、
/// Reducer 参数不匹配），排查起来很费时间。开局先对一次版本号，不匹配直接说清楚。
/// </summary>
public static partial class Module
{
    /// <summary>
    /// 服务端版本号。
    ///
    /// ⚠️ 必须和客户端 <c>ProjectSettings/ProjectSettings.asset</c> 里的
    /// <c>bundleVersion</c>（Unity 里是 Player Settings 的 Version，客户端代码读
    /// <c>Application.version</c>）**保持一致**，两边一起改。
    /// 改完记得 <c>spacetime publish</c>，否则线上还是旧值。
    ///
    /// 判定是**字符串全等**，不做 major/minor 兼容性判断 —— 玩法还没定型，
    /// 与其现在猜哪些改动算兼容，不如一律要求一致，等真的有发布流程了再放宽。
    /// </summary>
    public const string ServerVersion = "0.0.1";

    /// <summary>
    /// 版本校验。客户端连上后立刻调一次，参数传自己的 <c>Application.version</c>。
    ///
    /// 不匹配时**抛异常**：事务回滚（本来也没写任何数据），
    /// 调用方在 Reducer 回调的 <c>ctx.Event.Status</c> 里拿到 <c>Status.Failed(reason)</c>，
    /// reason 就是下面那句带两边版本号的中文，客户端直接弹出来给玩家看。
    ///
    /// 这是**提示性**校验，不是安全边界：它拦不住改过的客户端。真要按版本卡死请求，
    /// 得把版本号存进按连接的表里，然后在每个业务 Reducer 里核对。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void CheckVersion(ReducerContext ctx, string clientVersion)
    {
        if (clientVersion == ServerVersion)
        {
            Log.Debug($"[Version] 版本一致 {ServerVersion} identity={ctx.Sender}");
            return;
        }

        string shown = string.IsNullOrWhiteSpace(clientVersion) ? "未知" : clientVersion;
        Log.Warn($"[Version] 版本不一致：服务器 {ServerVersion}，客户端 {shown}，identity={ctx.Sender}");

        throw new Exception($"版本不一致。\n服务器版本：{ServerVersion}\n客户端版本：{shown}\n请更新客户端后再试。");
    }
}
