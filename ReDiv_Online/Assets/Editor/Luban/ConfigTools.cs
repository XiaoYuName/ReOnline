using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using XFramework;
using Debug = UnityEngine.Debug;

public class ConfigTools : OdinEditorWindow
{
    [TitleGroup("配置生成工具", "1 导出 Luban  →  2 AssetKeys  →  3 LubanManager  →  4 UIKeys  →  5 AudioKeys  →  6 服务端配置", TitleAlignments.Left)]
    [HorizontalGroup("配置生成工具/Actions", 0.72f)]
    [Button("一键生成配置", ButtonSizes.Large)]
    [GUIColor(0.4f, 0.85f, 0.5f)]
    [PropertyOrder(-20)]
    private void GenerateAllConfigs()
    {
        try
        {
            // 每一步显式声明自己依赖谁，只有**前置真的失败了**才跳过它。
            //
            // 以前这里是「一步失败就整体 return」，而 UIKeys 排在 Luban 链条后面 ——
            // UIKeys 早就不读 Luban 了（数据源换成了 UIPageConfiguration 资产），
            // 可只要工程里暂时没有 Luban 表类，第 3 步就失败，UIKeys 跟着永远生成不出来。
            // 现在没有依赖关系的步骤互不牵连。
            var steps = new[]
            {
                new GenerateStep("导出 Luban 配置", RunLubanExport),
                new GenerateStep("生成 Addressable AssetKeys", GenerateAssetKeys),
                // 服务端配置：和第 1 步读同一份 Excel，但输出到 SpacetimeDB 模块工程。
                // 依赖第 1 步只是为了共用一次 schema 校验失败的判断 —— Excel 有问题时没必要再导一遍。
                new GenerateStep("导出服务端配置", RunLubanServerExport, "导出 Luban 配置"),
                // 需要第 1 步导出的 Tb*.cs，也引用第 2 步生成的 AssetKeys 常量
                new GenerateStep("生成 LubanManager.Generated.cs", GenerateLubanManager,
                    "导出 Luban 配置", "生成 Addressable AssetKeys"),
                // 读 UIPageConfiguration 资产，和 Luban 无关
                new GenerateStep("生成 UIKeys", GenerateUIKeys),
                // 读 AudioConfiguration 资产，和 Luban 无关
                new GenerateStep("生成 AudioKeys", GenerateAudioKeys),
            };

            var results = new Dictionary<string, bool>(steps.Length, StringComparer.Ordinal);
            var skipped = new List<string>();

            for (int i = 0; i < steps.Length; i++)
            {
                GenerateStep step = steps[i];

                string blockedBy = step.FindFailedPrerequisite(results);
                if (blockedBy != null)
                {
                    skipped.Add($"{step.Title}（前置「{blockedBy}」失败）");
                    results[step.Title] = false;
                    continue;
                }

                EditorUtility.DisplayProgressBar("一键生成配置", step.Title + "...", (float)i / steps.Length);
                results[step.Title] = step.Run();

                // 每步产物都可能是新文件，下一步要能读到。
                EditorUtility.DisplayProgressBar("一键生成配置", "刷新 AssetDatabase...", (i + 0.5f) / steps.Length);
                AssetDatabase.Refresh();
            }

            RefreshExcelInfo();
            RefreshOverview();
            LogSummary(results, skipped);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static void LogSummary(Dictionary<string, bool> results, List<string> skipped)
    {
        var succeeded = results.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
        var failed = results.Where(kv => !kv.Value).Select(kv => kv.Key).ToList();

        var sb = new StringBuilder();
        sb.AppendLine(failed.Count == 0 ? "一键生成配置完成，全部成功。" : "一键生成配置结束，有步骤失败。");
        sb.AppendLine($"成功 {succeeded.Count} 步：{(succeeded.Count == 0 ? "无" : string.Join("、", succeeded))}");

        if (failed.Count > 0)
        {
            sb.AppendLine($"失败 {failed.Count} 步：{string.Join("、", failed)}");
        }

        if (skipped.Count > 0)
        {
            sb.AppendLine($"跳过：{string.Join("、", skipped)}");
        }

        if (failed.Count == 0)
        {
            Debug.Log(sb.ToString());
        }
        else
        {
            // 具体的失败原因各步自己已经 LogError 过了，这里只给总览
            Debug.LogError(sb.ToString());
        }
    }

    /// <summary>
    /// 一键流程里的一步。<see cref="prerequisites"/> 是这一步依赖的其它步骤标题 ——
    /// 只有依赖项真的失败了才跳过它，不相干的步骤失败不影响。
    /// </summary>
    private readonly struct GenerateStep
    {
        public readonly string Title;
        public readonly Func<bool> Run;
        private readonly string[] prerequisites;

        public GenerateStep(string title, Func<bool> run, params string[] prerequisites)
        {
            Title = title;
            Run = run;
            this.prerequisites = prerequisites;
        }

        /// <summary>返回第一个已失败的前置步骤标题；没有则返回 null。</summary>
        public string FindFailedPrerequisite(Dictionary<string, bool> results)
        {
            if (prerequisites == null)
            {
                return null;
            }

            foreach (string prerequisite in prerequisites)
            {
                if (results.TryGetValue(prerequisite, out bool ok) && !ok)
                {
                    return prerequisite;
                }
            }

            return null;
        }
    }

    [HorizontalGroup("配置生成工具/Actions")]
    [Button("刷新 Excel 列表", ButtonSizes.Large)]
    [PropertyOrder(-19)]
    private void RefreshExcelInfoButton()
    {
        RefreshExcelInfo();
    }

    /// <summary>
    /// 空实现，只是给 InfoBox 一个独占整行的位置。
    /// 直接把 InfoBox 挂到按钮上的话，它会被算进按钮所在的 HorizontalGroup，占掉一格把按钮挤歪。
    /// </summary>
    [PropertySpace(SpaceBefore = 6)]
    [TitleGroup("分步生成", "只改了某一环时用它快速重生成，执行的是和一键流程完全相同的逻辑", TitleAlignments.Left)]
    [InfoBox("依赖只有一条：3 需要 1 的导出产物和 2 的常量。1 / 2 / 4 / 5 各自独立，可随时单独执行 —— 4 读 UIPageConfiguration 资产，5 读 AudioConfiguration 资产，都和 Luban 无关。")]
    [OnInspectorGUI]
    [PropertyOrder(-18)]
    private void DrawStepHint()
    {
    }

    [TitleGroup("分步生成")]
    [HorizontalGroup("分步生成/Steps")]
    [Button("1. 导出 Luban 配置", ButtonSizes.Large)]
    [GUIColor(0.72f, 0.82f, 0.95f)]
    [PropertyTooltip("直接调 dotnet Luban.dll（参数见下方「设置」），把 Excel 导出成 Json 和 Tb*.cs。")]
    [PropertyOrder(-17)]
    private void LubanExportStep()
    {
        RunStep("导出 Luban 配置", RunLubanExport);
    }

    [HorizontalGroup("分步生成/Steps")]
    [Button("2. 生成 AssetKeys", ButtonSizes.Large)]
    [GUIColor(0.72f, 0.82f, 0.95f)]
    [PropertyTooltip("扫描 Addressable 资源，生成 AssetKeys 常量类。")]
    [PropertyOrder(-16)]
    private void AssetKeysStep()
    {
        RunStep("生成 AssetKeys", GenerateAssetKeys);
    }

    [HorizontalGroup("分步生成/Steps")]
    [Button("3. 生成 LubanManager", ButtonSizes.Large)]
    [GUIColor(0.72f, 0.82f, 0.95f)]
    [PropertyTooltip("生成 LubanManager.Generated.cs，内部引用 AssetKeys 里的常量。")]
    [PropertyOrder(-15)]
    private void LubanManagerStep()
    {
        RunStep("生成 LubanManager.Generated.cs", GenerateLubanManager);
    }

    [HorizontalGroup("分步生成/Steps")]
    [Button("4. 生成 UIKeys", ButtonSizes.Large)]
    [GUIColor(0.72f, 0.82f, 0.95f)]
    [PropertyTooltip("读取 UIPageConfiguration 资产，生成 UI 界面 ID 常量类 UIKeys。（以前读的是 Luban 导出的 tbuipagedata.json，UI 配置独立出来后不再依赖 Luban。）")]
    [PropertyOrder(-14)]
    private void UIKeysStep()
    {
        RunStep("生成 UIKeys", GenerateUIKeys);
    }

    [HorizontalGroup("分步生成/Steps")]
    [Button("6. 导出服务端配置", ButtonSizes.Large)]
    [GUIColor(0.95f, 0.85f, 0.55f)]
    [PropertyTooltip("把同一份 Excel 按 server 目标导出成 cs-bin 代码 + bin 数据，落进 SpacetimeDB 模块工程。" +
                     "导出完必须 spacetime publish 才生效（数据以嵌入资源编进 wasm）。")]
    [PropertyOrder(-12)]
    private void ServerConfigStep()
    {
        RunStep("导出服务端配置", RunLubanServerExport);
    }

    [HorizontalGroup("分步生成/Steps")]
    [Button("5. 生成 AudioKeys", ButtonSizes.Large)]
    [GUIColor(0.72f, 0.82f, 0.95f)]
    [PropertyTooltip("读取 AudioConfiguration 资源，生成音频 ID 常量类 AudioKeys。")]
    [PropertyOrder(-13)]
    private void AudioKeysStep()
    {
        RunStep("生成 AudioKeys", GenerateAudioKeys);
    }

    /// <summary>
    /// 单步执行的公共外壳：进度条 + 结果日志 + 收尾刷新，和一键流程共用同一份步骤实现。
    /// </summary>
    private void RunStep(string title, Func<bool> step)
    {
        try
        {
            EditorUtility.DisplayProgressBar(title, $"{title}...", 0.5f);

            if (!step())
            {
                Debug.LogError($"{title} 失败。");
                return;
            }

            AssetDatabase.Refresh();
            RefreshOverview();
            Debug.Log($"{title} 完成。");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private bool GenerateAssetKeys()
    {
        return AddressableKeyGeneratorOdinWindow.GenerateWithDefaultSettings();
    }

    private bool GenerateLubanManager()
    {
        return LubanManagerGeneratorWindow.GenerateWithDefaultConfig();
    }

    private bool GenerateUIKeys()
    {
        return UIKeysGenerator.Generate();
    }

    private bool GenerateAudioKeys()
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioConfiguration");

        if (guids.Length == 0)
        {
            Debug.LogError("找不到 AudioConfiguration 资源，无法生成 AudioKeys。");
            return false;
        }

        // 多份配置会各自覆盖同一个 AudioKeys.cs，结果取决于用了哪一份，含糊过去不如直接报错。
        if (guids.Length > 1)
        {
            string paths = string.Join("\n", guids.Select(AssetDatabase.GUIDToAssetPath));
            Debug.LogError($"找到 {guids.Length} 份 AudioConfiguration，无法确定用哪一份生成 AudioKeys：\n{paths}");
            return false;
        }

        string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        // 必须写全名：UnityEngine 里也有个同名的 AudioConfiguration 结构体。
        var config = AssetDatabase.LoadAssetAtPath<XFramework.AudioConfiguration>(assetPath);

        if (config == null)
        {
            Debug.LogError($"加载 AudioConfiguration 失败: {assetPath}");
            return false;
        }

        return config.TryGenerateAudioKeys();
    }

    private void RefreshExcelInfo()
    {
        excelFiles.Clear();

        if (Settings == null)
        {
            return;
        }

        string absoluteXlsxFolder = GetAbsoluteProjectPath(Settings.XlsxFolder);

        if (!Directory.Exists(absoluteXlsxFolder))
        {
            return;
        }

        excelFiles = Directory.GetFiles(absoluteXlsxFolder)
            .Where(IsExcelFile)
            .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .Select(path => new ExcelFileInfo(path))
            .ToList();
    }

    [PropertySpace(SpaceBefore = 10)]
    [TitleGroup("设置")]
    [LabelText("ConfigTools 设置")]
    [InlineEditor(InlineEditorObjectFieldModes.Boxed)]
    [Required("缺少 ConfigToolsSettings 设置资源")]
    [InfoBox("第 1 步的 Luban 参数与输出目录、Excel 目录、第 4 步 UIKeys 的输出路径都在这里改，不用再动 gen_client.bat。")]
    [PropertyOrder(-5)]
    public ConfigToolsSettings Settings;

    [PropertySpace(SpaceBefore = 6)]
    [TitleGroup("输出位置总览")]
    [InfoBox("每一步实际写到哪，从各自的配置资源读出来的 —— 不用再靠翻代码或 bat 去猜。")]
    [TableList(IsReadOnly = true, AlwaysExpanded = true)]
    [HideLabel]
    [PropertyOrder(-4)]
    [SerializeField]
    private List<OutputTarget> outputTargets = new List<OutputTarget>();

    [BoxGroup("Excel 文件")]
    [HorizontalGroup("Excel 文件/Summary")]
    [ShowInInspector]
    [ReadOnly]
    [LabelText("文件数量")]
    [PropertyOrder(1)]
    private int ExcelFileCount => excelFiles.Count;

    [BoxGroup("Excel 文件")]
    [HorizontalGroup("Excel 文件/Summary")]
    [Button("打开目录", ButtonSizes.Medium)]
    [EnableIf(nameof(HasValidXlsxFolder))]
    [PropertyOrder(2)]
    private void OpenXlsxFolder()
    {
        EditorUtility.RevealInFinder(GetAbsoluteProjectPath(Settings.XlsxFolder));
    }

    [BoxGroup("Excel 文件")]
    [TableList(IsReadOnly = true, AlwaysExpanded = true)]
    [HideLabel]
    [PropertyOrder(3)]
    [SerializeField]
    private List<ExcelFileInfo> excelFiles = new List<ExcelFileInfo>();

    protected override void OnEnable()
    {
        base.OnEnable();
        titleContent = new GUIContent("ConfigTools");
        minSize = new Vector2(820, 520);
        Settings = ConfigToolsSettings.LoadOrCreate();
        ConfigToolsSettings.Changed += OnSettingsChanged;
        RefreshExcelInfo();
        RefreshOverview();
    }

    protected override void OnDisable()
    {
        ConfigToolsSettings.Changed -= OnSettingsChanged;
        base.OnDisable();
    }

    private void OnSettingsChanged()
    {
        RefreshExcelInfo();
        RefreshOverview();
    }

    [MenuItem("Tools/XFramework/配置/LuaConfig _F6", false, 200)]
    private static void Init()
    {
        var window = GetWindow<ConfigTools>();
        window.titleContent = new GUIContent("ConfigTools");
        window.minSize = new Vector2(820, 520);
        window.Show();
    }

    /// <summary>
    /// 直接调 dotnet Luban.dll 导出配置，参数全部来自 <see cref="ConfigToolsSettings"/>。
    ///
    /// 以前这里跑的是 gen_client.bat，输出目录写死在 bat 的 -x 参数里，编辑器既看不见也改不了。
    /// 那个 bat 曾经指着旧工程的 Assets/Scripts/XFramework/C#/Luban，于是表 C# 落到一个不存在的
    /// 目录，而第 3 步在另一个目录扫表，LubanManager.Generated.cs 一直是空的。
    /// </summary>
    /// <summary>客户端配置：cs-newtonsoft-json + json，落进 Unity 工程。</summary>
    private bool RunLubanExport()
    {
        return RunLuban("客户端", Settings?.Target, Settings?.CodeTarget, Settings?.DataTarget,
            Settings?.OutputCodeDir, Settings?.OutputDataDir);
    }

    /// <summary>
    /// 服务端配置：cs-bin + bin，落进 SpacetimeDB 模块工程。
    ///
    /// 和客户端**必须用不同的 codeTarget**：服务端是 NativeAOT 裁剪过的 wasm，
    /// cs-newtonsoft-json 那套反射在里面用不了；cs-bin 生成的代码零反射。
    /// 只会导出 group 含 s 的表和字段（分组在 Defines/character.xml 里按字段标）。
    ///
    /// ⚠️ 生成完还要 spacetime publish 才生效 —— 数据是以嵌入资源编进 wasm 的。
    /// </summary>
    private bool RunLubanServerExport()
    {
        return RunLuban("服务端", Settings?.ServerTarget, Settings?.ServerCodeTarget, Settings?.ServerDataTarget,
            Settings?.ServerOutputCodeDir, Settings?.ServerOutputDataDir);
    }

    private bool RunLuban(string label, string target, string codeTarget, string dataTarget,
        string outputCodeDirSetting, string outputDataDirSetting)
    {
        if (Settings == null)
        {
            Debug.LogError("缺少 ConfigToolsSettings 设置资源，无法导出 Luban 配置。");
            return false;
        }

        string dllPath = GetAbsoluteProjectPath(Settings.LubanDllPath);

        if (!File.Exists(dllPath))
        {
            Debug.LogError($"Luban.dll 不存在: {dllPath}");
            return false;
        }

        string confPath = GetAbsoluteProjectPath(Settings.LubanConfPath);

        if (!File.Exists(confPath))
        {
            Debug.LogError($"luban.conf 不存在: {confPath}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(target) ||
            string.IsNullOrWhiteSpace(codeTarget) ||
            string.IsNullOrWhiteSpace(dataTarget))
        {
            Debug.LogError("生成目标 -t / 代码格式 -c / 数据格式 -d 都不能为空。");
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputCodeDirSetting) ||
            string.IsNullOrWhiteSpace(outputDataDirSetting))
        {
            Debug.LogError("表 C# 输出目录和 Json 数据输出目录都不能为空。");
            return false;
        }

        string outputCodeDir = GetAbsoluteProjectPath(outputCodeDirSetting);
        string outputDataDir = GetAbsoluteProjectPath(outputDataDirSetting);

        var arguments = new StringBuilder();
        arguments.Append(Quote(dllPath));
        arguments.Append($" -t {Quote(target.Trim())}");
        arguments.Append($" -c {Quote(codeTarget.Trim())}");
        arguments.Append($" -d {Quote(dataTarget.Trim())}");
        arguments.Append($" --conf {Quote(confPath)}");
        arguments.Append($" -x {Quote($"outputCodeDir={outputCodeDir}")}");
        arguments.Append($" -x {Quote($"outputDataDir={outputDataDir}")}");

        if (Settings.ExtraXArgs != null)
        {
            foreach (string extra in Settings.ExtraXArgs)
            {
                if (!string.IsNullOrWhiteSpace(extra))
                {
                    arguments.Append($" -x {Quote(extra.Trim())}");
                }
            }
        }

        // luban.conf 里的 schemaFiles / dataDir 是相对 conf 自己所在目录解析的，
        // 所以工作目录必须设成 conf 所在目录，不能用工程根目录。
        string workingDirectory = Path.GetDirectoryName(confPath);
        int timeoutMs = Mathf.Max(10, Settings.TimeoutSeconds) * 1000;

        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(Settings.DotnetPath) ? "dotnet" : Settings.DotnetPath.Trim(),
            Arguments = arguments.ToString(),
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            // Luban 的日志里有中文，不指定编码会变成乱码。
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using (Process process = new Process())
        {
            StringBuilder outputBuilder = new StringBuilder();
            StringBuilder errorBuilder = new StringBuilder();

            process.StartInfo = startInfo;
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data != null)
                {
                    outputBuilder.AppendLine(args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data != null)
                {
                    errorBuilder.AppendLine(args.Data);
                }
            };

            try
            {
                if (!process.Start())
                {
                    Debug.LogError("Luban 进程启动失败。");
                    return false;
                }
            }
            catch (Exception e)
            {
                // 最常见的原因是 PATH 里没有 dotnet。
                Debug.LogError($"启动 dotnet 失败（{startInfo.FileName}）: {e.Message}"
                               + "\n可以在设置里把「dotnet 命令」改成 dotnet.exe 的绝对路径。");
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill();
                Debug.LogError($"Luban {label}配置导出超时（{timeoutMs / 1000} 秒）。\n{outputBuilder}\n{errorBuilder}");
                return false;
            }

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Debug.LogError($"Luban {label}配置导出失败，ExitCode: {process.ExitCode}"
                               + $"\n命令: {startInfo.FileName} {startInfo.Arguments}"
                               + $"\n{outputBuilder}\n{errorBuilder}");
                return false;
            }

            string output = outputBuilder.ToString();
            string error = errorBuilder.ToString();

            if (!string.IsNullOrWhiteSpace(output))
            {
                Debug.Log($"Luban {label}配置导出完成。\n表 C# -> {outputCodeDir}\nJson  -> {outputDataDir}\n{output}");
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.LogWarning($"Luban 输出警告:\n{error}");
            }
        }

        return true;
    }

    private static string Quote(string value)
    {
        return "\"" + value + "\"";
    }

    /// <summary>
    /// 汇总五个步骤各自的实际输出位置。每一项都从对应的配置资源现读，
    /// 这里不留第二份真相 —— 之前就是因为「路径写在别处」才踩的坑。
    /// </summary>
    private void RefreshOverview()
    {
        outputTargets.Clear();

        string audioKeysPath = string.Empty;
        string audioConfigPath = string.Empty;
        string[] audioGuids = AssetDatabase.FindAssets("t:AudioConfiguration");

        if (audioGuids.Length == 1)
        {
            audioConfigPath = AssetDatabase.GUIDToAssetPath(audioGuids[0]);
            var audioConfig = AssetDatabase.LoadAssetAtPath<XFramework.AudioConfiguration>(audioConfigPath);

            if (audioConfig != null)
            {
                audioKeysPath = audioConfig.AudioKeysOutputPath;
            }
        }
        else if (audioGuids.Length > 1)
        {
            audioConfigPath = $"找到 {audioGuids.Length} 份 AudioConfiguration，无法确定";
        }

        var assetKeysSettings = AssetDatabase.LoadAssetAtPath<AddressableKeyGeneratorSettings>(
            AddressableKeyGeneratorOdinWindow.DefaultSettingsAssetPath);

        var lubanManagerConfig = AssetDatabase.LoadAssetAtPath<LubanManagerGeneratorConfig>(
            LubanManagerGeneratorWindow.DefaultConfigPath);

        outputTargets.Add(new OutputTarget(
            "1 表 C#",
            Settings == null ? string.Empty : Settings.OutputCodeDir,
            ConfigToolsSettings.DefaultAssetPath));

        outputTargets.Add(new OutputTarget(
            "1 Json 数据",
            Settings == null ? string.Empty : Settings.OutputDataDir,
            ConfigToolsSettings.DefaultAssetPath));

        outputTargets.Add(new OutputTarget(
            "2 AssetKeys",
            assetKeysSettings == null
                ? string.Empty
                : $"{assetKeysSettings.OutputFolder}/{assetKeysSettings.ClassName}.cs",
            AddressableKeyGeneratorOdinWindow.DefaultSettingsAssetPath));

        outputTargets.Add(new OutputTarget(
            "3 LubanManager",
            lubanManagerConfig == null ? string.Empty : lubanManagerConfig.outputPath,
            LubanManagerGeneratorWindow.DefaultConfigPath));

        outputTargets.Add(new OutputTarget(
            "4 UIKeys",
            Settings == null ? string.Empty : Settings.UIKeysOutputPath,
            ConfigToolsSettings.DefaultAssetPath));

        outputTargets.Add(new OutputTarget("5 AudioKeys", audioKeysPath, audioConfigPath));
    }

    private bool HasValidXlsxFolder()
    {
        return Settings != null
               && !string.IsNullOrEmpty(Settings.XlsxFolder)
               && Directory.Exists(GetAbsoluteProjectPath(Settings.XlsxFolder));
    }

    private static string GetProjectRoot()
    {
        return Directory.GetParent(Application.dataPath)?.FullName;
    }

    private static bool IsExcelFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xls", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace("\\", "/");
    }

    private static string GetAbsoluteProjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string normalizedPath = NormalizePath(path);
        if (Path.IsPathRooted(normalizedPath))
        {
            return normalizedPath;
        }

        string projectRoot = GetProjectRoot();
        return string.IsNullOrEmpty(projectRoot)
            ? normalizedPath
            : NormalizePath(Path.Combine(projectRoot, normalizedPath));
    }

    private static string ToProjectRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string normalizedPath = NormalizePath(path);
        string projectRoot = NormalizePath(GetProjectRoot());
        if (string.IsNullOrEmpty(projectRoot) || !Path.IsPathRooted(normalizedPath))
        {
            return normalizedPath;
        }

        string absolutePath = NormalizePath(Path.GetFullPath(normalizedPath));
        string absoluteRoot = NormalizePath(Path.GetFullPath(projectRoot)).TrimEnd('/');

        if (absolutePath.Equals(absoluteRoot, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        string rootPrefix = absoluteRoot + "/";
        return absolutePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            ? absolutePath.Substring(rootPrefix.Length)
            : absolutePath;
    }

    [Serializable]
    private class OutputTarget
    {
        [TableColumnWidth(130, false)]
        [ReadOnly]
        [LabelText("步骤")]
        public string Step;

        [ReadOnly]
        [LabelText("输出位置")]
        public string Output;

        [ReadOnly]
        [LabelText("在哪改")]
        public string ConfigAsset;

        public OutputTarget(string step, string output, string configAsset)
        {
            Step = step;
            Output = string.IsNullOrWhiteSpace(output) ? "(读不到)" : output;
            ConfigAsset = string.IsNullOrWhiteSpace(configAsset) ? "(找不到配置资源)" : configAsset;
        }
    }

    [Serializable]
    private class ExcelFileInfo
    {
        [TableColumnWidth(220)]
        [ReadOnly]
        [LabelText("表名")]
        public string Name;

        [ReadOnly]
        [LabelText("路径")]
        public string Path;

        [HideInInspector]
        private readonly string absolutePath;

        public ExcelFileInfo(string path)
        {
            absolutePath = NormalizePath(path);
            Path = ToProjectRelativePath(path);
            Name = System.IO.Path.GetFileNameWithoutExtension(path);
        }

        [TableColumnWidth(90, false)]
        [Button("打开", ButtonSizes.Small)]
        private void Open()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = absolutePath,
                UseShellExecute = true
            });
        }
    }
}
