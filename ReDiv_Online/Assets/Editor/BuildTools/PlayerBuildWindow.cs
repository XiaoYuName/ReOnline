using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// 一键出包窗口：编辑配置 + 一键打包。平台在配置的「基础信息」页里选（Windows64 / Android）。
    /// </summary>
    public class PlayerBuildWindow : OdinEditorWindow
    {
        private const string LastResultKey = "XFramework.PlayerBuildWindow.LastResult";
        private const string WindowTitle = "一键出包";

        [TitleGroup(WindowTitle, "配置 → 校验 → 打包，支持 StandaloneWindows64 与 Android", TitleAlignments.Left)]
        [BoxGroup(WindowTitle + "/操作", ShowLabel = false)]
        [HorizontalGroup(WindowTitle + "/操作/Buttons")]
        [Button("@\"一键打包（\" + PlatformName + \"）\"", ButtonSizes.Gigantic)]
        [GUIColor(0.35f, 0.85f, 0.45f)]
        [PropertyOrder(-20)]
        private void BuildPlayer()
        {
            if (config == null)
            {
                EditorUtility.DisplayDialog(WindowTitle, "出包配置为空。", "关闭");
                return;
            }

            List<string> errors = config.Validate();
            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("配置校验失败", string.Join("\n", errors.Select(error => "· " + error)), "关闭");
                return;
            }

            string platformSwitchNote = EditorUserBuildSettings.activeBuildTarget == config.BuildTarget
                ? string.Empty
                : $"\n\n⚠️ 编辑器当前平台是 {EditorUserBuildSettings.activeBuildTarget}，" +
                  $"会先切到 {config.BuildTarget}。切平台要按新平台重新导入全部资源，第一次可能要几分钟。";

            string confirmMessage =
                $"平台：{config.PlatformDisplayName}\n" +
                $"产品：{config.ProductName}\n" +
                $"版本：{config.Version} ({config.VersionCode})\n" +
                $"渠道：{config.Channel}\n" +
                $"包名：{config.BundleIdentifier}\n" +
                $"场景：{config.GetScenePaths().Length} 个\n" +
                $"宏定义：{FormatDefineSymbols()}\n" +
                $"输出：{config.GetOutputPath()}" +
                platformSwitchNote;

            if (!EditorUtility.DisplayDialog("确认开始出包？", confirmMessage, "开始打包", "取消"))
            {
                return;
            }

            PlayerBuildReport report = PlayerBuilder.Build(config);
            lastResult = report.Success
                ? $"成功  {report.OutputPath}  {report.TotalSize / 1024f / 1024f:F1} MB  {report.Duration.TotalMinutes:F1} 分钟"
                : $"失败  {report.Message.Replace("\n", " ")}";
            EditorPrefs.SetString(LastResultKey, lastResult);

            EditorUtility.DisplayDialog(
                report.Success ? "出包完成" : "出包失败",
                report.Steps.Count > 0
                    ? report.Message + "\n\n" + string.Join("\n", report.Steps.Select(step => "· " + step))
                    : report.Message,
                report.Success ? "确定" : "关闭");
        }

        [HorizontalGroup(WindowTitle + "/操作/Buttons", 0.22f)]
        [VerticalGroup(WindowTitle + "/操作/Buttons/Side")]
        [Button("校验配置", ButtonSizes.Medium)]
        [GUIColor(0.45f, 0.7f, 1f)]
        [PropertyOrder(-19)]
        private void ValidateConfig()
        {
            if (config == null)
            {
                EditorUtility.DisplayDialog("校验配置", "出包配置为空。", "关闭");
                return;
            }

            List<string> errors = config.Validate();
            EditorUtility.DisplayDialog(
                "校验配置",
                errors.Count == 0
                    ? $"配置可用。\n平台：{config.PlatformDisplayName}\n输出：{config.GetOutputPath()}"
                    : string.Join("\n", errors.Select(error => "· " + error)),
                "确定");
        }

        [VerticalGroup(WindowTitle + "/操作/Buttons/Side")]
        [Button("@\"切到 \" + PlatformName + \" 平台\"", ButtonSizes.Medium)]
        [GUIColor(0.95f, 0.75f, 0.35f)]
        [EnableIf(nameof(NeedsPlatformSwitch))]
        [PropertyTooltip("单独把编辑器切到目标平台。出包时也会自动切，这个按钮是为了先把重新导入的时间花掉。")]
        [PropertyOrder(-18.5f)]
        private void SwitchPlatform()
        {
            if (config == null || !NeedsPlatformSwitch)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "切换平台",
                    $"把编辑器从 {EditorUserBuildSettings.activeBuildTarget} 切到 {config.BuildTarget}？\n" +
                    "要按新平台重新导入全部资源，第一次可能要几分钟。",
                    "切换",
                    "取消"))
            {
                return;
            }

            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(config.BuildTargetGroup, config.BuildTarget);
            EditorUtility.DisplayDialog(
                "切换平台",
                switched ? $"已切到 {EditorUserBuildSettings.activeBuildTarget}。" : "切换失败，详见 Console。",
                "确定");
        }

        [VerticalGroup(WindowTitle + "/操作/Buttons/Side")]
        [Button("打开输出目录", ButtonSizes.Medium)]
        [PropertyOrder(-18)]
        private void OpenOutputFolder()
        {
            if (config == null)
            {
                return;
            }

            string directory = PlayerBuildConfig.GetAbsolutePath(config.OutputRoot);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            EditorUtility.RevealInFinder(directory);
        }

        [VerticalGroup(WindowTitle + "/操作/Buttons/Side")]
        [Button("生成配置表", ButtonSizes.Medium)]
        [PropertyTooltip("打开 Luban 配置生成工具。生成配置表会重新生成 .cs 并触发域重载，所以不放进出包流程，需要时先在这里生成完再打包。")]
        [PropertyOrder(-17)]
        private void OpenConfigTools()
        {
            GetWindow<ConfigTools>().Show();
        }

        [VerticalGroup(WindowTitle + "/操作/Buttons/Side")]
        [Button("定位配置资产", ButtonSizes.Medium)]
        [PropertyOrder(-16)]
        private void PingConfig()
        {
            if (config == null)
            {
                LoadConfig();
            }

            EditorGUIUtility.PingObject(config);
            Selection.activeObject = config;
        }

        [BoxGroup(WindowTitle + "/概览")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("目标平台")]
        [PropertyOrder(-11)]
        private string PlatformSummary => config == null
            ? string.Empty
            : $"{config.PlatformDisplayName}（编辑器当前：{EditorUserBuildSettings.activeBuildTarget}）";

        [BoxGroup(WindowTitle + "/概览")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("输出路径")]
        [PropertyOrder(-10)]
        private string OutputPath => config == null ? string.Empty : config.GetOutputPath();

        [BoxGroup(WindowTitle + "/概览")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("场景数量")]
        [PropertyOrder(-9)]
        private int SceneCount => config == null ? 0 : config.GetScenePaths().Length;

        [BoxGroup(WindowTitle + "/概览")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("宏定义")]
        [PropertyOrder(-8)]
        private string DefineSymbols => FormatDefineSymbols();

        [BoxGroup(WindowTitle + "/概览")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("上次结果")]
        [PropertyOrder(-7)]
        [SerializeField]
        private string lastResult;

        [BoxGroup(WindowTitle + "/配置", ShowLabel = false)]
        [InlineEditor(InlineEditorObjectFieldModes.Hidden, Expanded = true)]
        [HideLabel]
        [Required]
        [PropertyOrder(0)]
        [SerializeField]
        private PlayerBuildConfig config;

        /// <summary>按钮文字里用的平台名（Odin 表达式要访问，所以是内部属性）。</summary>
        private string PlatformName => config == null ? "?" : config.PlatformDisplayName;

        private bool NeedsPlatformSwitch => config != null && EditorUserBuildSettings.activeBuildTarget != config.BuildTarget;

        [MenuItem("Tools/XFramework/打包/一键出包", false, 101)]
        private static void Open()
        {
            PlayerBuildWindow window = GetWindow<PlayerBuildWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(760, 720);
            window.Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(760, 720);
            lastResult = EditorPrefs.GetString(LastResultKey, "无");
            LoadConfig();
        }

        private void LoadConfig()
        {
            config = PlayerBuildConfig.LoadOrCreate();
        }

        private string FormatDefineSymbols()
        {
            if (config == null)
            {
                return string.Empty;
            }

            List<string> symbols = config.GetDefineSymbols();
            return symbols.Count == 0 ? "无" : string.Join(";", symbols);
        }
    }
}
