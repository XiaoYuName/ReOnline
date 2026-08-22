using System;
using System.IO;
using System.Reflection;
using Luban;
using ReDiv.Server.Config;
using SpacetimeDB;

namespace ReDiv.Server
{
    /// <summary>
    /// 服务端配置表的唯一入口：<c>ServerConfig.Tables.TbCharacterJob</c> 这样用。
    ///
    /// 数据来自 Excel → Luban（<c>gen_server.bat</c>，只导出 group 含 s 的表和字段）
    /// → <c>Configs/*.bytes</c> → 以**嵌入资源**编进 wasm。模块跑在数据库进程内、
    /// 没有文件系统，所以嵌入资源是唯一的路（实测 wasi-wasm + NativeAOT + 裁剪下可用）。
    ///
    /// 关于 static：Reducer 里禁止**可变** static 状态，因为事务可能被重放、模块实例可能被重建。
    /// 这里是**只读**的配置，来源是编译进产物的字节，任何时刻任何实例读到的都完全一样，
    /// 所以是确定性的，可以放心用 static 缓存 —— 否则每个 Reducer 都要重新解析一遍配置。
    ///
    /// ⚠️ 改了配置要走两步：<c>gen_server.bat</c> 重新导出，再 <c>spacetime publish</c>。
    /// 只改 Excel 不发布，线上还是旧配置。
    /// </summary>
    public static class ServerConfig
    {
        private static Tables tables;

        /// <summary>配置表集合。第一次访问时解析嵌入资源，之后复用。</summary>
        public static Tables Tables => tables ??= Load();

        private static Tables Load()
        {
            var loaded = new Tables(LoadTable);
            Log.Info("[Config] 服务端配置表已加载");
            return loaded;
        }

        /// <summary>
        /// Luban 的表加载回调：给一个表名，返回它的字节流。
        ///
        /// 资源名是「默认命名空间 + 相对路径（目录分隔符换成点）」，即
        /// <c>StdbModule.Configs.tbcharacterjob.bytes</c>。这里不硬编码前缀，
        /// 而是按后缀匹配，免得以后改了程序集名或目录就悄悄失效。
        /// </summary>
        private static ByteBuf LoadTable(string tableName)
        {
            string suffix = "." + tableName + ".bytes";
            Assembly assembly = typeof(ServerConfig).Assembly;

            foreach (string resourceName in assembly.GetManifestResourceNames())
            {
                if (!resourceName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using Stream stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    break;
                }

                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return new ByteBuf(buffer.ToArray());
            }

            // 缺表就直接抛：让它在第一次访问配置时炸掉，比后面读出一堆默认值好查得多。
            // 常见原因是改了表却忘了跑 gen_server.bat。
            throw new Exception($"[Config] 找不到配置数据 {tableName}.bytes，" +
                                "先跑 ExcelTool/LubanTools/DataTables/gen_server.bat 再 publish");
        }
    }
}
