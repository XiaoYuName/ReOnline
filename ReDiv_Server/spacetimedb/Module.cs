using SpacetimeDB;

/// <summary>
/// ReDiv 服务端模块（空壳）。
///
/// 目前只有生命周期钩子，没有任何业务表 —— 玩法定型后再往里加。
///
/// SpacetimeDB 2.8 的写法约定：
///   - 表 / Reducer / View / 定时表都挂在这个 partial class 下，
///     这样定时表声明里的 nameof(...) 能解析到对应 Reducer。
///   - 表用 [SpacetimeDB.Table(Accessor = "Xxx", Public = true)] 标在 partial struct 上；
///     Accessor 决定 ctx.Db.Xxx 的名字（2.0 起不再是 Name =）。
///   - 索引必须写全 [SpacetimeDB.Index.BTree]，裸 Index 会和 System.Index 撞名。
///   - 写入只能走 Reducer；客户端读取走订阅（公开表）或 View（私有表）。
///   - Reducer 内禁止 DateTime.Now / new Random() / 网络 IO / static 可变状态，
///     时间和随机只能取 ctx.Timestamp 和 ctx.Rng（事务重放时才能一致）。
/// </summary>
public static partial class Module
{
    /// <summary>
    /// 首次 publish 或 <c>--delete-data</c> 清库后执行一次。
    /// 这里的 <c>ctx.Sender</c> 是模块 owner（发布者），是唯一能拿到 owner 身份的地方；
    /// 之后要做 GM 鉴权的话，需要在这里把它写进表里存下来。
    /// </summary>
    [SpacetimeDB.Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        Log.Info($"[Init] ReDiv module initialized. owner={ctx.Sender}");
    }

    /// <summary>
    /// 每条连接建立时执行。抛异常会拒绝这条连接。
    /// 同一个 Identity 可能有多条连接（多端登录），要区分就用 ctx.ConnectionId。
    /// </summary>
    [SpacetimeDB.Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext ctx)
    {
        Log.Debug($"[Connect] {ctx.Sender} / {ctx.ConnectionId}");
    }

    /// <summary>连接断开时执行。抛异常只会记日志，不会阻止断开。</summary>
    [SpacetimeDB.Reducer(ReducerKind.ClientDisconnected)]
    public static void ClientDisconnected(ReducerContext ctx)
    {
        Log.Debug($"[Disconnect] {ctx.Sender} / {ctx.ConnectionId}");
    }

    /// <summary>
    /// 连通性自检：客户端连上后调一次，服务端日志里应能看到。
    /// 有了真实业务后可以删掉。
    /// </summary>
    [SpacetimeDB.Reducer]
    public static void Ping(ReducerContext ctx)
    {
        Log.Info($"[Ping] from {ctx.Sender} at {ctx.Timestamp}");
    }
}
