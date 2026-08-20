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
    /// Windows 一键出包窗口：编辑配置 + 一键打包。
    /// </summary>
    public class PlayerBuildWindow : OdinEditorWindow
    {
        private const string LastResultKey = "XFramework.PlayerBuildWindow.LastResult";

        [TitleGroup("Windows 一键出包", "配置 → 校验 → 打包，输出 StandaloneWindows64", TitleAlignments.Left)]
        [BoxGroup("Windows 一键出包/操作", ShowLabel = false)]
        [HorizontalGroup("Windows 一键出包/操作/Buttons")]
        [Button("一键打包", ButtonSizes.Gigantic)]
        [GUIColor(0.35f, 0.85f, 0.45f)]
        [PropertyOrder(-20)]
        private void BuildPlayer()
        {
            if (config == null)
            {
                EditorUtility.DisplayDialog("一键出包", "出包配置为空。", "关闭");
                return;
            }

            List<string> errors = config.Validate();
            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("配置校验失败", string.Join("\n", errors.Select(error => "· " + error)), "关闭");
                return;
            }

            string confirmMessage =
                $"产品：{config.ProductName}\n" +
                $"版本：{config.Version} ({config.VersionCode})\n" +
                $"渠道：{config.Channel}\n" +
                $"包名：{config.BundleIdentifier}\n" +
                $"场景：{config.GetScenePaths().Length} 个\n" +
                $"宏定义：{FormatDefineSymbols()}\n" +
                $"输出：{config.GetOutputPath()}";

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

        [HorizontalGroup("Windows 一键出包/操作/Buttons", 0.22f)]
        [VerticalGroup("Windows 一键出包/操作/Buttons/Side")]
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
                    ? $"配置可用。\n输出：{config.GetOutputPath()}"
                    : string.Join("\n", errors.Select(error => "· " + error)),
                "确定");
        }

        [VerticalGroup("Windows 一键出包/操作/Buttons/Side")]
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

        [VerticalGroup("Windows 一键出包/操作/Buttons/Side")]
        [Button("生成配置表", ButtonSizes.Medium)]
        [PropertyTooltip("打开 Luban 配置生成工具。生成配置表会重新生成 .cs 并触发域重载，所以不放进出包流程，需要时先在这里生成完再打包。")]
        [PropertyOrder(-17)]
        private void OpenConfigTools()
        {
            GetWindow<ConfigTools>().Show();
        }

        [VerticalGroup("Windows 一键出包/操作/Buttons/Side")]
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

        [BoxGroup("Windows 一键出包/概览")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("输出路径")]
        [PropertyOrder(-10)]
        private string OutputPath => config == null ? string.Empty : config.GetOutputPath();

        [BoxGroup("Windows 一键出包/概览")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("场景数量")]
        [PropertyOrder(-9)]
        private int SceneCount => config == null ? 0 : config.GetScenePaths().Length;

        [BoxGroup("Windows 一键出包/概览")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("宏定义")]
        [PropertyOrder(-8)]
        private string DefineSymbols => FormatDefineSymbols();

        [BoxGroup("Windows 一键出包/概览")]
        [ShowInInspector]
        [ReadOnly]
        [LabelText("上次结果")]
        [PropertyOrder(-7)]
        [SerializeField]
        private string lastResult;

        [BoxGroup("Windows 一键出包/配置", ShowLabel = false)]
        [InlineEditor(InlineEditorObjectFieldModes.Hidden, Expanded = true)]
        [HideLabel]
        [Required]
        [PropertyOrder(0)]
        [SerializeField]
        private PlayerBuildConfig config;

        [MenuItem("Tools/XFramework/打包/Windows 一键出包", false, 101)]
        private static void Open()
        {
            PlayerBuildWindow window = GetWindow<PlayerBuildWindow>();
            window.titleContent = new GUIContent("Windows 一键出包");
            window.minSize = new Vector2(760, 720);
            window.Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            titleContent = new GUIContent("Windows 一键出包");
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
