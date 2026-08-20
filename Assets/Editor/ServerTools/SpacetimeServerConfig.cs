using System;
using System.IO;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// SpacetimeDB 服务端工具的配置。
    ///
    /// 服务端工程在 Unity 工程<b>外面</b>（默认 ../ReDiv_Server），所以路径统一存成
    /// 「相对 Unity 工程根目录」的形式，用 <see cref="ResolveServerProjectPath"/> 解析成绝对路径。
    /// 不用 Odin 的 FolderPath 特性，因为那个是相对 Assets/ 的，指不到工程外面。
    /// </summary>
    [CreateAssetMenu(fileName = "SpacetimeServerConfig", menuName = "Configs/Project/SpacetimeServerConfig")]
    public class SpacetimeServerConfig : SerializedScriptableObject
    {
        public const string ConfigPath = "Assets/Editor/ServerTools/SpacetimeServerConfig.asset";

        // ------------------------------------------------------------------
        // 路径
        // ------------------------------------------------------------------

        [TitleGroup("SpacetimeDB 服务端配置")]
        [BoxGroup("SpacetimeDB 服务端配置/路径")]
        [LabelText("服务端工程路径")]
        [InfoBox("相对 Unity 工程根目录（Assets 的上一级）。默认 ../ReDiv_Server。")]
        public string ServerProjectPath = "../ReDiv_Server";

        [BoxGroup("SpacetimeDB 服务端配置/路径")]
        [LabelText("spacetime CLI 路径")]
        [InfoBox("留空则先试 PATH 里的 spacetime，再试 %LOCALAPPDATA%\\SpacetimeDB\\spacetime.exe。")]
        public string SpacetimeExePath = string.Empty;

        [BoxGroup("SpacetimeDB 服务端配置/路径")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("解析后的服务端路径")]
        private string ResolvedServerPath => ResolveServerProjectPath();

        [BoxGroup("SpacetimeDB 服务端配置/路径")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("解析后的 CLI 路径")]
        private string ResolvedCliPath => ResolveSpacetimeExe();

        // ------------------------------------------------------------------
        // 数据库
        // ------------------------------------------------------------------

        [BoxGroup("SpacetimeDB 服务端配置/数据库")]
        [LabelText("数据库名")]
        [InfoBox("要和 ReDiv_Server/spacetime.json 里的 database 一致。")]
        public string DatabaseName = "rediv";

        [BoxGroup("SpacetimeDB 服务端配置/数据库")]
        [LabelText("服务器地址")]
        public string ServerUrl = "http://127.0.0.1:2383";

        // ------------------------------------------------------------------
        // Docker
        // ------------------------------------------------------------------

        [BoxGroup("SpacetimeDB 服务端配置/Docker")]
        [LabelText("启用 Docker 控制")]
        [InfoBox("本机 SpacetimeDB 跑在 Docker 容器里时勾上，可在窗口里启停容器。")]
        public bool EnableDockerControls = true;

        [BoxGroup("SpacetimeDB 服务端配置/Docker")]
        [LabelText("容器名")]
        [EnableIf(nameof(EnableDockerControls))]
        public string DockerContainerName = "spacetimedb";

        // ------------------------------------------------------------------
        // 行为
        // ------------------------------------------------------------------

        [BoxGroup("SpacetimeDB 服务端配置/行为")]
        [LabelText("生成绑定后刷新资源库")]
        public bool RefreshAfterGenerate = true;

        [BoxGroup("SpacetimeDB 服务端配置/行为")]
        [LabelText("生成绑定后请求重编译")]
        [InfoBox("schema 变了可能让现有客户端代码编不过，建议开着。")]
        public bool RecompileAfterGenerate = true;

        [BoxGroup("SpacetimeDB 服务端配置/行为")]
        [LabelText("日志默认行数")]
        [PropertyRange(20, 1000)]
        public int LogTailLines = 100;

        [BoxGroup("SpacetimeDB 服务端配置/行为")]
        [LabelText("输出保留行数")]
        [PropertyRange(200, 5000)]
        [InfoBox("窗口输出区最多保留多少行，超出丢弃最旧的。")]
        public int OutputBufferLines = 1000;

        // ------------------------------------------------------------------
        // 解析
        // ------------------------------------------------------------------

        /// <summary>Unity 工程根目录（Assets 的上一级）。</summary>
        public static string UnityProjectRoot =>
            Directory.GetParent(Application.dataPath)!.FullName;

        /// <summary>把 <see cref="ServerProjectPath"/> 解析成绝对路径。</summary>
        public string ResolveServerProjectPath()
        {
            if (string.IsNullOrWhiteSpace(ServerProjectPath))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(ServerProjectPath))
            {
                return Path.GetFullPath(ServerProjectPath);
            }

            return Path.GetFullPath(Path.Combine(UnityProjectRoot, ServerProjectPath));
        }

        /// <summary>
        /// 找 spacetime 可执行文件。顺序：配置值 → PATH → %LOCALAPPDATA%。
        /// 找不到返回 "spacetime"，让进程启动时自己报错，错误信息更直白。
        /// </summary>
        public string ResolveSpacetimeExe()
        {
            if (!string.IsNullOrWhiteSpace(SpacetimeExePath))
            {
                return SpacetimeExePath;
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                string candidate = Path.Combine(localAppData, "SpacetimeDB", "spacetime.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "spacetime";
        }

        /// <summary>服务端工程是否存在（用 spacetime.json 判断，而不是只看目录在不在）。</summary>
        public bool ServerProjectExists()
        {
            string root = ResolveServerProjectPath();
            return !string.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, "spacetime.json"));
        }

        // ------------------------------------------------------------------
        // 加载
        // ------------------------------------------------------------------

        /// <summary>取配置资源，不存在则创建一个默认的。</summary>
        public static SpacetimeServerConfig LoadOrCreate()
        {
            SpacetimeServerConfig config = AssetDatabase.LoadAssetAtPath<SpacetimeServerConfig>(ConfigPath);
            if (config != null)
            {
                return config;
            }

            string dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            config = CreateInstance<SpacetimeServerConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            return config;
        }
    }
}
