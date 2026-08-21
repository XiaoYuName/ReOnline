using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// SpacetimeDB 服务端控制台。
    ///
    /// 常用操作一站式：发布模块、重新生成客户端绑定、看日志、跑 SQL、调 Reducer、启停 Docker 容器。
    ///
    /// 所有命令都在服务端工程目录下执行，服务器地址与数据库名由该目录的 spacetime.json 决定，
    /// 所以这里<b>不</b>额外传 --server / --database，避免和配置文件打架。
    /// 窗口里的「服务器地址 / 数据库名」只用于展示和拼日志提示。
    /// </summary>
    public class SpacetimeServerWindow : OdinEditorWindow
    {
        private const int DisplayLineLimit = 300;

        [TitleGroup("SpacetimeDB 服务端")]
        [BoxGroup("SpacetimeDB 服务端/配置", ShowLabel = false)]
        [InlineEditor(InlineEditorObjectFieldModes.Boxed)]
        [LabelText("服务端配置")]
        [Required]
        [SerializeField]
        private SpacetimeServerConfig config;

        [SerializeField]
        [HideInInspector]
        private List<string> output = new List<string>();

        private readonly SpacetimeCliRunner _runner = new SpacetimeCliRunner();

        // ------------------------------------------------------------------
        // 状态
        // ------------------------------------------------------------------

        [BoxGroup("SpacetimeDB 服务端/状态")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("服务端工程")]
        private string ServerProjectState
        {
            get
            {
                if (config == null)
                {
                    return "配置为空";
                }
                string path = config.ResolveServerProjectPath();
                return config.ServerProjectExists() ? path : $"找不到 spacetime.json：{path}";
            }
        }

        [BoxGroup("SpacetimeDB 服务端/状态")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("CLI")]
        private string CliState
        {
            get
            {
                if (config == null)
                {
                    return "配置为空";
                }
                string exe = config.ResolveSpacetimeExe();
                return exe == "spacetime" ? "spacetime（依赖 PATH）" : exe;
            }
        }

        [BoxGroup("SpacetimeDB 服务端/状态")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("目标")]
        private string TargetState =>
            config == null ? string.Empty : $"{config.DatabaseName} @ {config.ServerUrl}";

        [BoxGroup("SpacetimeDB 服务端/状态")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("当前任务")]
        private string CurrentTask =>
            _runner.IsRunning ? _runner.CurrentLabel : "空闲";

        // ------------------------------------------------------------------
        // 发布
        // ------------------------------------------------------------------

        [BoxGroup("SpacetimeDB 服务端/发布")]
        [InfoBox("首次发布会自动下载 535MB 的 WASI SDK 到 ~/.wasi-sdk/，耗时较长，之后不再下载。")]
        [HorizontalGroup("SpacetimeDB 服务端/发布/行1")]
        [Button("发布 + 生成绑定", ButtonSizes.Large)]
        [GUIColor(0.35f, 0.85f, 0.45f)]
        [DisableIf(nameof(IsBusy))]
        private void PublishAndGenerate()
        {
            if (!Ready())
            {
                return;
            }

            RunCommands(
                new[]
                {
                    Cli("发布模块", "publish --yes"),
                    Cli("生成客户端绑定", "generate --yes"),
                },
                afterGenerate: true);
        }

        [HorizontalGroup("SpacetimeDB 服务端/发布/行1")]
        [Button("仅发布", ButtonSizes.Large)]
        [GUIColor(0.45f, 0.7f, 1f)]
        [DisableIf(nameof(IsBusy))]
        private void PublishOnly()
        {
            if (!Ready())
            {
                return;
            }

            RunCommands(new[] { Cli("发布模块", "publish --yes") }, afterGenerate: false);
        }

        [HorizontalGroup("SpacetimeDB 服务端/发布/行1")]
        [Button("仅生成绑定", ButtonSizes.Large)]
        [GUIColor(0.45f, 0.7f, 1f)]
        [DisableIf(nameof(IsBusy))]
        private void GenerateOnly()
        {
            if (!Ready())
            {
                return;
            }

            RunCommands(new[] { Cli("生成客户端绑定", "generate --yes") }, afterGenerate: true);
        }

        [BoxGroup("SpacetimeDB 服务端/发布")]
        [HorizontalGroup("SpacetimeDB 服务端/发布/行2")]
        [Button("清库重发（销毁所有数据）", ButtonSizes.Large)]
        [GUIColor(0.95f, 0.35f, 0.35f)]
        [DisableIf(nameof(IsBusy))]
        private void PublishWithWipe()
        {
            if (!Ready())
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "清库重发",
                    $"这会删除数据库 [{config.DatabaseName}] 的全部数据并重新发布，不可撤销。\n\n"
                    + "只有在表结构改动导致无法自动迁移时才需要这么做。",
                    "确认清库", "取消"))
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "再确认一次",
                    $"真的要清空 [{config.DatabaseName}] 吗？所有玩家数据都会消失。",
                    "就是要清", "算了"))
            {
                return;
            }

            RunCommands(
                new[]
                {
                    Cli("清库重发", "publish --delete-data=always --yes"),
                    Cli("生成客户端绑定", "generate --yes"),
                },
                afterGenerate: true);
        }

        // ------------------------------------------------------------------
        // 日志与查询
        // ------------------------------------------------------------------

        [BoxGroup("SpacetimeDB 服务端/日志与查询")]
        [HorizontalGroup("SpacetimeDB 服务端/日志与查询/行1")]
        [Button("查看最近日志", ButtonSizes.Medium)]
        [DisableIf(nameof(IsBusy))]
        private void ViewLogs()
        {
            if (!Ready())
            {
                return;
            }

            RunCommands(
                new[] { Cli($"最近 {config.LogTailLines} 行日志", $"logs {config.DatabaseName} -n {config.LogTailLines}") },
                afterGenerate: false);
        }

        [HorizontalGroup("SpacetimeDB 服务端/日志与查询/行1")]
        [Button("实时日志", ButtonSizes.Medium)]
        [GUIColor(0.45f, 0.7f, 1f)]
        [DisableIf(nameof(IsBusy))]
        private void FollowLogs()
        {
            if (!Ready())
            {
                return;
            }

            AppendLine("[工具] 实时日志已开始，用「停止」按钮结束。");
            RunCommands(
                new[] { Cli("实时日志", $"logs {config.DatabaseName} --follow") },
                afterGenerate: false);
        }

        [HorizontalGroup("SpacetimeDB 服务端/日志与查询/行1")]
        [Button("停止", ButtonSizes.Medium)]
        [GUIColor(1f, 0.72f, 0.35f)]
        [EnableIf(nameof(IsBusy))]
        private void StopCurrent()
        {
            _runner.Stop();
            AppendLine("[工具] 已请求终止当前命令。");
        }

        [BoxGroup("SpacetimeDB 服务端/日志与查询")]
        [LabelText("SQL")]
        [InfoBox("只读查询用。系统表可以查：SELECT * FROM st_table")]
        [SerializeField]
        private string sqlQuery = "SELECT * FROM st_table";

        [BoxGroup("SpacetimeDB 服务端/日志与查询")]
        [Button("执行 SQL", ButtonSizes.Medium)]
        [DisableIf(nameof(IsBusy))]
        private void RunSql()
        {
            if (!Ready())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(sqlQuery))
            {
                AppendLine("[工具] SQL 为空。");
                return;
            }

            string escaped = sqlQuery.Replace("\"", "\\\"");
            RunCommands(
                new[] { Cli("执行 SQL", $"sql {config.DatabaseName} \"{escaped}\"") },
                afterGenerate: false);
        }

        [BoxGroup("SpacetimeDB 服务端/日志与查询")]
        [LabelText("Reducer 名")]
        [InfoBox("用规范名（snake_case）。C# 里写 Ping，这里要填 ping —— 生成的客户端绑定才是 PascalCase。")]
        [SerializeField]
        private string reducerName = "ping";

        [BoxGroup("SpacetimeDB 服务端/日志与查询")]
        [LabelText("Reducer 参数")]
        [InfoBox("按 CLI 的写法，每个参数一段、用空格分隔，字符串要带引号，例如：'\"Alice\"' 123")]
        [SerializeField]
        private string reducerArgs = string.Empty;

        [BoxGroup("SpacetimeDB 服务端/日志与查询")]
        [Button("调用 Reducer", ButtonSizes.Medium)]
        [DisableIf(nameof(IsBusy))]
        private void CallReducer()
        {
            if (!Ready())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(reducerName))
            {
                AppendLine("[工具] Reducer 名为空。");
                return;
            }

            string args = $"call {config.DatabaseName} {reducerName}";
            if (!string.IsNullOrWhiteSpace(reducerArgs))
            {
                args += " " + reducerArgs;
            }

            RunCommands(new[] { Cli($"调用 {reducerName}", args) }, afterGenerate: false);
        }

        [BoxGroup("SpacetimeDB 服务端/日志与查询")]
        [Button("查看已发布的 Schema", ButtonSizes.Medium)]
        [DisableIf(nameof(IsBusy))]
        private void DescribeSchema()
        {
            if (!Ready())
            {
                return;
            }

            RunCommands(
                new[] { Cli("查看 Schema", $"describe {config.DatabaseName} --json") },
                afterGenerate: false);
        }

        // ------------------------------------------------------------------
        // Docker
        // ------------------------------------------------------------------

        [BoxGroup("SpacetimeDB 服务端/Docker")]
        [ShowIf("@config != null && config.EnableDockerControls")]
        [HorizontalGroup("SpacetimeDB 服务端/Docker/行1")]
        [Button("启动容器", ButtonSizes.Medium)]
        [GUIColor(0.35f, 0.85f, 0.45f)]
        [DisableIf(nameof(IsBusy))]
        private void DockerStart() => RunDocker("启动容器", $"start {config.DockerContainerName}");

        [ShowIf("@config != null && config.EnableDockerControls")]
        [HorizontalGroup("SpacetimeDB 服务端/Docker/行1")]
        [Button("停止容器", ButtonSizes.Medium)]
        [GUIColor(1f, 0.72f, 0.35f)]
        [DisableIf(nameof(IsBusy))]
        private void DockerStop()
        {
            if (!EditorUtility.DisplayDialog(
                    "停止容器",
                    $"停止容器 [{config.DockerContainerName}] 后，所有客户端会断线。数据不会丢（已持久化到 volume）。",
                    "停止", "取消"))
            {
                return;
            }

            RunDocker("停止容器", $"stop {config.DockerContainerName}");
        }

        [ShowIf("@config != null && config.EnableDockerControls")]
        [HorizontalGroup("SpacetimeDB 服务端/Docker/行1")]
        [Button("重启容器", ButtonSizes.Medium)]
        [DisableIf(nameof(IsBusy))]
        private void DockerRestart() => RunDocker("重启容器", $"restart {config.DockerContainerName}");

        [ShowIf("@config != null && config.EnableDockerControls")]
        [HorizontalGroup("SpacetimeDB 服务端/Docker/行1")]
        [Button("容器状态", ButtonSizes.Medium)]
        [DisableIf(nameof(IsBusy))]
        private void DockerStatus() =>
            RunDocker("容器状态",
                $"ps -a --filter name={config.DockerContainerName} " +
                "--format \"{{.Names}} | {{.Status}} | {{.Ports}}\"");

        // ------------------------------------------------------------------
        // 输出
        // ------------------------------------------------------------------

        [BoxGroup("SpacetimeDB 服务端/输出")]
        [ShowInInspector]
        [ReadOnly]
        [HideLabel]
        [MultiLineProperty(22)]
        private string OutputText
        {
            get
            {
                if (output == null || output.Count == 0)
                {
                    return "（无输出）";
                }

                // Unity 的 TextArea 在超长字符串下会渲染异常，只显示尾部若干行
                int skip = Mathf.Max(0, output.Count - DisplayLineLimit);
                IEnumerable<string> tail = output.Skip(skip);
                string text = string.Join("\n", tail);
                return skip > 0 ? $"（省略前 {skip} 行）\n{text}" : text;
            }
        }

        [BoxGroup("SpacetimeDB 服务端/输出")]
        [HorizontalGroup("SpacetimeDB 服务端/输出/行1")]
        [Button("清空输出", ButtonSizes.Medium)]
        private void ClearOutput()
        {
            output.Clear();
            Repaint();
        }

        [HorizontalGroup("SpacetimeDB 服务端/输出/行1")]
        [Button("复制到剪贴板", ButtonSizes.Medium)]
        private void CopyOutput()
        {
            EditorGUIUtility.systemCopyBuffer = string.Join("\n", output);
            AppendLine("[工具] 已复制全部输出到剪贴板。");
        }

        [HorizontalGroup("SpacetimeDB 服务端/输出/行1")]
        [Button("打开服务端工程目录", ButtonSizes.Medium)]
        private void RevealServerFolder()
        {
            if (config == null)
            {
                return;
            }

            string path = config.ResolveServerProjectPath();
            if (Directory.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
            }
            else
            {
                AppendLine($"[工具] 目录不存在：{path}");
            }
        }

        // ------------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------------

        private bool IsBusy => _runner.IsRunning;

        private SpacetimeCommand Cli(string label, string arguments) =>
            new SpacetimeCommand(label, config.ResolveSpacetimeExe(), arguments, config.ResolveServerProjectPath());

        private void RunDocker(string label, string arguments)
        {
            if (config == null)
            {
                return;
            }

            RunCommands(
                new[] { new SpacetimeCommand(label, "docker", arguments, config.ResolveServerProjectPath()) },
                afterGenerate: false);
        }

        /// <summary>校验配置齐全，不齐就把原因写进输出区。</summary>
        private bool Ready()
        {
            if (config == null)
            {
                AppendLine("[工具] 服务端配置为空，先在窗口顶部指定配置资源。");
                return false;
            }

            if (!config.ServerProjectExists())
            {
                AppendLine($"[工具] 服务端工程不存在或缺少 spacetime.json：{config.ResolveServerProjectPath()}");
                return false;
            }

            return true;
        }

        private void RunCommands(IEnumerable<SpacetimeCommand> commands, bool afterGenerate)
        {
            _runner.Run(commands, AppendLine, success =>
            {
                if (success && afterGenerate)
                {
                    AfterGenerate();
                }
                Repaint();
            });
        }

        /// <summary>
        /// 生成绑定之后的收尾：刷新资源库，再按配置请求一次重编译。
        /// 不刷新的话 Unity 看不到新写入的 .cs 文件。
        /// </summary>
        private void AfterGenerate()
        {
            if (config == null)
            {
                return;
            }

            if (config.RefreshAfterGenerate)
            {
                AppendLine("[工具] 刷新资源库…");
                AssetDatabase.Refresh();
            }

            if (config.RecompileAfterGenerate)
            {
                AppendLine("[工具] 请求重编译，编译结果去 Console 看。");
                CompilationPipeline.RequestScriptCompilation();
            }
        }

        private void AppendLine(string line)
        {
            if (line == null)
            {
                return;
            }

            output.Add(line);

            int limit = config != null ? config.OutputBufferLines : 1000;
            int overflow = output.Count - limit;
            if (overflow > 0)
            {
                output.RemoveRange(0, overflow);
            }

            Repaint();
        }

        // ------------------------------------------------------------------
        // 窗口
        // ------------------------------------------------------------------

        [MenuItem("Tools/XFramework/服务端/SpacetimeDB 控制台", false, 150)]
        private static void Open()
        {
            SpacetimeServerWindow window = GetWindow<SpacetimeServerWindow>();
            window.titleContent = new GUIContent("SpacetimeDB");
            window.minSize = new Vector2(760, 640);
            window.Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            titleContent = new GUIContent("SpacetimeDB");
            minSize = new Vector2(760, 640);
            output ??= new List<string>();
            if (config == null)
            {
                config = SpacetimeServerConfig.LoadOrCreate();
            }
        }

        private void OnDisable()
        {
            // 窗口关掉时别把 logs --follow 留在后台
            _runner.Stop();
        }
    }
}
