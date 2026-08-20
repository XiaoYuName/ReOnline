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
    private const string GenClientBatRelativePath = "ExcelTool/LubanTools/DataTables/gen_client.bat";
    private const string DefaultXlsxFolderRelativePath = "ExcelTool/LubanTools/DataTables/Datas";
    private const int GenClientTimeoutMs = 300000;

    [TitleGroup("配置生成工具", "1 导出 Luban  →  2 AssetKeys  →  3 LubanManager.Generated.cs  →  4 UIKeys  →  5 AudioKeys", TitleAlignments.Left)]
    [HorizontalGroup("配置生成工具/Actions", 0.72f)]
    [Button("一键生成配置", ButtonSizes.Large)]
    [GUIColor(0.4f, 0.85f, 0.5f)]
    [PropertyOrder(-20)]
    private void GenerateAllConfigs()
    {
        try
        {
            // 顺序不能调：UIKeys 读的是 gen_client.bat 导出的 tbuipagedata.json，
            // LubanManager.Generated.cs 又引用 AssetKeys 里的常量。
            var steps = new (string Title, Func<bool> Run)[]
            {
                ("执行 gen_client.bat...", RunGenClientBat),
                ("生成 Addressable AssetKeys...", GenerateAssetKeys),
                ("生成 LubanManager.Generated.cs...", GenerateLubanManager),
                ("生成 UIKeys...", GenerateUIKeys),
                ("生成 AudioKeys...", GenerateAudioKeys),
            };

            for (int i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                EditorUtility.DisplayProgressBar("一键生成配置", step.Title, (float)i / steps.Length);

                if (!step.Run())
                {
                    Debug.LogError($"一键生成配置中断：{step.Title.TrimEnd('.')} 失败。");
                    return;
                }

                // 每步产物都可能是新文件，下一步要能读到。
                EditorUtility.DisplayProgressBar("一键生成配置", "刷新 AssetDatabase...", (i + 0.5f) / steps.Length);
                AssetDatabase.Refresh();
            }

            RefreshExcelInfo();
            Debug.Log("一键生成配置完成：gen_client.bat、AssetKeys、LubanManager.Generated.cs、UIKeys、AudioKeys 全部生成成功。");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
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
    [InfoBox("步骤间有依赖：4 读的是 1 导出的 Json，3 引用 2 生成的常量；5 只依赖 AudioConfiguration 资源，可随时单独执行。")]
    [OnInspectorGUI]
    [PropertyOrder(-18)]
    private void DrawStepHint()
    {
    }

    [TitleGroup("分步生成")]
    [HorizontalGroup("分步生成/Steps")]
    [Button("1. 导出 Luban 配置", ButtonSizes.Large)]
    [GUIColor(0.72f, 0.82f, 0.95f)]
    [PropertyTooltip("执行 ExcelTool/LubanTools/DataTables/gen_client.bat，把 Excel 导出成 Json 和 Tb*.cs。")]
    [PropertyOrder(-17)]
    private void GenClientBatStep()
    {
        RunStep("导出 Luban 配置", RunGenClientBat);
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
    [PropertyTooltip("读取 tbuipagedata.json，生成 UI 界面 ID 常量类 UIKeys。")]
    [PropertyOrder(-14)]
    private void UIKeysStep()
    {
        RunStep("生成 UIKeys", GenerateUIKeys);
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
        string absoluteXlsxFolder = GetAbsoluteProjectPath(xlsxFolder);

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
    [BoxGroup("Excel 文件")]
    [FolderPath(RequireExistingPath = true)]
    [LabelText("Excel 目录")]
    [OnValueChanged(nameof(OnXlsxFolderChanged))]
    [InfoBox("当前 Excel 目录不存在。", InfoMessageType.Warning, nameof(IsXlsxFolderInvalid))]
    [PropertyOrder(0)]
    [SerializeField]
    private string xlsxFolder;

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
        EditorUtility.RevealInFinder(GetAbsoluteProjectPath(xlsxFolder));
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
        ReadPath();
        RefreshExcelInfo();
    }

    [MenuItem("Tools/LuaConfig _F6")]
    private static void Init()
    {
        var window = GetWindow<ConfigTools>();
        window.titleContent = new GUIContent("ConfigTools");
        window.minSize = new Vector2(820, 520);
        window.Show();
    }

    private void OnXlsxFolderChanged()
    {
        xlsxFolder = ToProjectRelativePath(xlsxFolder);
        SavePath();
        RefreshExcelInfo();
    }

    private bool RunGenClientBat()
    {
        string projectRoot = GetProjectRoot();
        if (string.IsNullOrEmpty(projectRoot))
        {
            Debug.LogError("无法获取 Unity 项目根目录。");
            return false;
        }

        string batPath = Path.Combine(projectRoot, GenClientBatRelativePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
        if (!File.Exists(batPath))
        {
            Debug.LogError($"gen_client.bat 不存在: {batPath}");
            return false;
        }

        string workingDirectory = Path.GetDirectoryName(batPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{batPath}\"\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
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

            if (!process.Start())
            {
                Debug.LogError("gen_client.bat 进程启动失败。");
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // gen_client.bat 末尾有 pause，写入回车避免后台执行卡住。
            process.StandardInput.WriteLine();
            process.StandardInput.Close();

            if (!process.WaitForExit(GenClientTimeoutMs))
            {
                process.Kill();
                Debug.LogError($"gen_client.bat 执行超时（{GenClientTimeoutMs / 1000} 秒）。\n{outputBuilder}\n{errorBuilder}");
                return false;
            }

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Debug.LogError($"gen_client.bat 执行失败，ExitCode: {process.ExitCode}\n{outputBuilder}\n{errorBuilder}");
                return false;
            }

            string output = outputBuilder.ToString();
            string error = errorBuilder.ToString();

            if (!string.IsNullOrWhiteSpace(output))
            {
                Debug.Log($"gen_client.bat 执行完成:\n{output}");
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.LogWarning($"gen_client.bat 输出警告:\n{error}");
            }
        }

        return true;
    }

    private bool IsXlsxFolderInvalid()
    {
        return !HasValidXlsxFolder();
    }

    private bool HasValidXlsxFolder()
    {
        return !string.IsNullOrEmpty(xlsxFolder) && Directory.Exists(GetAbsoluteProjectPath(xlsxFolder));
    }

    private void SavePath()
    {
        EditorPrefs.SetString("xlsxFolder_" + PlayerSettings.applicationIdentifier, xlsxFolder);
    }

    private void ReadPath()
    {
        xlsxFolder = EditorPrefs.GetString("xlsxFolder_" + PlayerSettings.applicationIdentifier);
        if (string.IsNullOrEmpty(xlsxFolder))
        {
            xlsxFolder = DefaultXlsxFolderRelativePath;
        }

        xlsxFolder = ToProjectRelativePath(xlsxFolder);
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
