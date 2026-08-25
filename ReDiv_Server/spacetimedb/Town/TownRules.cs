using System;
using System.Collections.Generic;
using System.Linq;
using ReDiv.Server.Config;

namespace ReDiv.Server.Town
{
    /// <summary>
    /// 城镇与时段的纯计算。**不碰数据库**，只读配置 —— 所以能单独拿测试向量钉住。
    /// </summary>
    internal static class TownRules
    {
        /// <summary>时段固定三段（早/中/晚）。城镇表是三列背景，段数不能随便加。</summary>
        public const int BandCount = 3;

        /// <summary>
        /// 按 UTC 时间戳算当前是哪个时段。
        ///
        /// 规则：把 UTC 换成服务器本地时，取小时数，落在「StartHour ≤ hour 的那些段里
        /// StartHour 最大的那一段」；比所有 StartHour 都小的话说明还在**跨午夜**那段里，
        /// 也就是 StartHour 最大的那一段（默认配置下是「夜晚」，18 点到次日 5 点）。
        /// </summary>
        public static uint CurrentBandId(long utcMicroseconds, int utcOffsetHours)
        {
            List<TimeBand> bands = SortedBands();

            if (bands.Count == 0)
            {
                throw Reject("时间段配置是空的，跑一次 gen_server.bat 再 publish");
            }

            int hour = LocalHour(utcMicroseconds, utcOffsetHours);

            // 倒着找第一个 StartHour ≤ hour 的
            for (int i = bands.Count - 1; i >= 0; i--)
            {
                if (bands[i].StartHour <= hour)
                {
                    return (uint)bands[i].BandId;
                }
            }

            // 比最早那段还早 ⇒ 还在跨午夜的那一段里
            return (uint)bands[bands.Count - 1].BandId;
        }

        /// <summary>
        /// UTC 微秒 → 服务器本地小时（0~23）。
        ///
        /// 用整数运算而不是 DateTime：Reducer 里禁止外部时钟，而且这样避免了
        /// 任何和时区数据库 / 本地化相关的东西（模块跑在 InvariantGlobalization 下）。
        /// 负数取模在 C# 里会给负值，所以要 +24 再取一次。
        /// </summary>
        public static int LocalHour(long utcMicroseconds, int utcOffsetHours)
        {
            const long MicrosPerHour = 3600L * 1_000_000L;

            long localMicros = utcMicroseconds + utcOffsetHours * MicrosPerHour;
            long hours = localMicros / MicrosPerHour;

            // C# 的 / 对负数是向零取整，会让午夜前后差一小时 —— 先把负的补齐
            if (localMicros < 0 && localMicros % MicrosPerHour != 0)
            {
                hours--;
            }

            return (int)(((hours % 24) + 24) % 24);
        }

        /// <summary>时段配置，按 StartHour 从小到大。</summary>
        public static List<TimeBand> SortedBands() =>
            ServerConfig.Tables.TbTimeBand.DataList.OrderBy(b => b.StartHour).ToList();

        /// <summary>新角色的初始城镇。配置里 <c>IsStartTown</c> 为 True 的那一个。</summary>
        public static uint StartTownId()
        {
            List<Config.Town> towns = ServerConfig.Tables.TbTown.DataList
                .Where(t => t.IsStartTown)
                .ToList();

            if (towns.Count != 1)
            {
                throw Reject($"城镇配置里 IsStartTown 为 True 的有 {towns.Count} 个，必须恰好 1 个");
            }

            return (uint)towns[0].TownId;
        }

        /// <summary>城镇存不存在。</summary>
        public static bool TownExists(uint townId) =>
            ServerConfig.Tables.TbTown.GetOrDefault((int)townId) != null;

        /// <summary>
        /// 可预期的业务失败统一从这里抛，和账号 / 角色系统一致：
        /// Reducer 抛异常 ⇒ 事务回滚 + 消息经 Status.Failed 回给调用方，可直接显示给玩家。
        /// </summary>
        public static Exception Reject(string message) => new Exception(message);
    }
}
