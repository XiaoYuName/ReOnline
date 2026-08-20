#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Addressable Key 常量生成器。入口在 ConfigTools 窗口，这里不再注册 MenuItem。
/// 生成参数存在 AddressableKeyGeneratorWindow.asset 里，要改直接在 Project 里选中它改。
/// </summary>
public class AddressableKeyGeneratorOdinWindow : OdinEditorWindow
{
    private const string DefaultSettingsAssetPath =
        "Assets/Editor/AddressableKeyGeneratorWindow/AddressableKeyGeneratorWindow.asset";

    [Title("Addressable Key 常量生成器")]

    [BoxGroup("配置资产")]
    [LabelText("生成器配置")]
    [InlineEditor(InlineEditorObjectFieldModes.Boxed)]
    [Required("缺少生成器配置资产")]
    public AddressableKeyGeneratorSettings Settings;

    [BoxGroup("预览")]
    [LabelText("找到的资源数量")]
    [ReadOnly]
    public int AssetCount;

    [BoxGroup("预览")]
    [LabelText("输出路径")]
    [ReadOnly]
    [ShowInInspector]
    private string OutputFilePath
    {
        get
        {
            if (Settings == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(Settings.OutputFolder) ||
                string.IsNullOrWhiteSpace(Settings.ClassName))
            {
                return string.Empty;
            }

            return $"{Settings.OutputFolder}/{Settings.ClassName}.cs";
        }
    }

    [BoxGroup("预览")]
    [LabelText("资源预览")]
    [TableList]
    [ReadOnly]
    public List<AddressableKeyPreviewItem> PreviewItems = new List<AddressableKeyPreviewItem>();

    protected override void OnEnable()
    {
        base.OnEnable();
        LoadOrCreateSettings();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        SaveSettings();
    }

    [BoxGroup("配置资产/操作")]
    [Button("重新加载配置资产", ButtonSizes.Medium)]
    private void LoadOrCreateSettings()
    {
        Settings = AssetDatabase.LoadAssetAtPath<AddressableKeyGeneratorSettings>(DefaultSettingsAssetPath);

        if (Settings != null)
        {
            return;
        }

        string directory = Path.GetDirectoryName(DefaultSettingsAssetPath);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Settings = CreateInstance<AddressableKeyGeneratorSettings>();
        AssetDatabase.CreateAsset(Settings, DefaultSettingsAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"已创建 Addressable Key 生成器配置: {DefaultSettingsAssetPath}");
    }

    [BoxGroup("配置资产/操作")]
    [Button("保存配置资产", ButtonSizes.Medium)]
    private void SaveSettings()
    {
        if (Settings == null)
        {
            return;
        }

        EditorUtility.SetDirty(Settings);
        AssetDatabase.SaveAssets();
    }

    [BoxGroup("操作")]
    [Button("刷新预览", ButtonSizes.Large)]
    [GUIColor(0.3f, 0.7f, 1f)]
    private void RefreshPreview()
    {
        if (!CheckSettings())
        {
            return;
        }

        var assetPaths = FindAssetPaths();

        PreviewItems = assetPaths
            .Select(path => new AddressableKeyPreviewItem
            {
                ConstName = BuildConstName(path),
                AssetPath = path,
                Extension = Path.GetExtension(path)
            })
            .ToList();

        AssetCount = PreviewItems.Count;

        SaveSettings();

        Debug.Log($"刷新完成，找到资源数量: {AssetCount}");
    }

    [BoxGroup("操作")]
    [Button("生成常量类", ButtonSizes.Large)]
    [GUIColor(0.3f, 0.8f, 0.4f)]
    public bool Generate()
    {
        if (!CheckSettings())
        {
            return false;
        }

        var assetPaths = FindAssetPaths();

        if (assetPaths.Count == 0)
        {
            Debug.LogWarning($"没有找到可生成的资源: {Settings.TargetFolder}");
            return false;
        }

        string outputPath = $"{Settings.OutputFolder}/{Settings.ClassName}.cs";

        if (File.Exists(outputPath) && !Settings.OverwriteFile)
        {
            Debug.LogError($"文件已存在，且未开启覆盖: {outputPath}");
            return false;
        }

        string scriptContent = BuildScript(assetPaths);

        string directory = Path.GetDirectoryName(outputPath);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, scriptContent, Encoding.UTF8);
        AssetDatabase.Refresh();

        AssetCount = assetPaths.Count;

        PreviewItems = assetPaths
            .Select(path => new AddressableKeyPreviewItem
            {
                ConstName = BuildConstName(path),
                AssetPath = path,
                Extension = Path.GetExtension(path)
            })
            .ToList();

        SaveSettings();

        Debug.Log($"生成成功: {outputPath}\n共生成 {assetPaths.Count} 条资源常量");
        return true;
    }

    public static bool GenerateWithDefaultSettings()
    {
        var generator = CreateInstance<AddressableKeyGeneratorOdinWindow>();
        try
        {
            generator.LoadOrCreateSettings();
            return generator.Generate();
        }
        finally
        {
            DestroyImmediate(generator);
        }
    }

    private bool CheckSettings()
    {
        if (Settings == null)
        {
            Debug.LogError("缺少 AddressableKeyGeneratorSettings 配置资产。");
            return false;
        }

        if (!IsValidAssetFolder(Settings.TargetFolder))
        {
            Debug.LogError($"资源文件夹无效: {Settings.TargetFolder}");
            return false;
        }

        if (!IsValidAssetFolder(Settings.OutputFolder))
        {
            Debug.LogError($"输出文件夹无效: {Settings.OutputFolder}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(Settings.ClassName))
        {
            Debug.LogError("类名不能为空");
            return false;
        }

        return true;
    }

    private List<string> FindAssetPaths()
    {
        List<string> result = new List<string>();

        if (Settings == null || !IsValidAssetFolder(Settings.TargetFolder))
        {
            return result;
        }

        string filter = Settings.OnlyPrefab ? "t:Prefab" : string.Empty;
        string[] guids = AssetDatabase.FindAssets(filter, new[] { Settings.TargetFolder });

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);

            if (string.IsNullOrWhiteSpace(assetPath))
            {
                continue;
            }

            assetPath = NormalizePath(assetPath);

            if (ShouldIgnoreAsset(assetPath))
            {
                continue;
            }

            result.Add(assetPath);
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private bool ShouldIgnoreAsset(string assetPath)
    {
        // 1. 排除文件夹，解决空文件夹被统计的问题
        if (AssetDatabase.IsValidFolder(assetPath))
        {
            return true;
        }

        // 2. 不包含子文件夹时，只取当前目录
        if (!Settings.IncludeSubFolders)
        {
            string directory = NormalizePath(Path.GetDirectoryName(assetPath));

            if (directory != NormalizePath(Settings.TargetFolder))
            {
                return true;
            }
        }

        // 3. OnlyPrefab 开启时，只允许 prefab
        if (Settings.OnlyPrefab &&
            !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string extension = Path.GetExtension(assetPath).ToLowerInvariant();

        // 4. 包含扩展名列表不为空时，只生成这些扩展名
        if (Settings.IncludeExtensions != null && Settings.IncludeExtensions.Count > 0)
        {
            bool included = Settings.IncludeExtensions.Any(item =>
                NormalizeExtension(item) == extension);

            if (!included)
            {
                return true;
            }
        }

        // 5. 排除扩展名
        if (Settings.IgnoreExtensions != null && Settings.IgnoreExtensions.Count > 0)
        {
            bool ignored = Settings.IgnoreExtensions.Any(item =>
                NormalizeExtension(item) == extension);

            if (ignored)
            {
                return true;
            }
        }

        // 6. 排除指定文件夹
        if (Settings.IgnoreFolders != null && Settings.IgnoreFolders.Count > 0)
        {
            foreach (string folder in Settings.IgnoreFolders)
            {
                if (string.IsNullOrWhiteSpace(folder))
                {
                    continue;
                }

                string normalizedFolder = NormalizePath(folder);

                if (assetPath == normalizedFolder ||
                    assetPath.StartsWith(normalizedFolder + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // 7. 排除路径关键字
        if (Settings.IgnorePathKeywords != null && Settings.IgnorePathKeywords.Count > 0)
        {
            foreach (string keyword in Settings.IgnorePathKeywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (assetPath.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private string BuildScript(List<string> assetPaths)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("// ------------------------------------------------------------------------------");
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("//     此文件由 AddressableKeyGeneratorOdinWindow 自动生成。");
        sb.AppendLine("//     请不要手动修改，重新生成会覆盖内容。");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("// ------------------------------------------------------------------------------");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(Settings.NamespaceName))
        {
            sb.AppendLine($"namespace {Settings.NamespaceName}");
            sb.AppendLine("{");
        }

        string indent = string.IsNullOrWhiteSpace(Settings.NamespaceName) ? "" : "    ";
        string memberIndent = string.IsNullOrWhiteSpace(Settings.NamespaceName) ? "    " : "        ";

        sb.AppendLine($"{indent}public static class {Settings.ClassName}");
        sb.AppendLine($"{indent}{{");

        HashSet<string> usedConstNames = new HashSet<string>();

        foreach (string assetPath in assetPaths)
        {
            string constName = BuildConstName(assetPath);
            constName = MakeUniqueName(constName, usedConstNames);

            string value = EscapeString(assetPath);

            sb.AppendLine($"{memberIndent}public const string {constName} = \"{value}\";");
        }

        sb.AppendLine($"{indent}}}");

        if (!string.IsNullOrWhiteSpace(Settings.NamespaceName))
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private string BuildConstName(string assetPath)
    {
        string constName = Settings.UseFullPathAsConstName
            ? PathToConstName(assetPath)
            : FileNameToConstName(assetPath);

        if (!string.IsNullOrWhiteSpace(Settings.ConstSuffix))
        {
            constName += Settings.ConstSuffix;
        }

        return constName;
    }

    private string FileNameToConstName(string assetPath)
    {
        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        return ToPascalCase(fileName);
    }

    private string PathToConstName(string assetPath)
    {
        string path = Path.ChangeExtension(assetPath, null);
        path = NormalizePath(path);

        if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring("Assets/".Length);
        }

        string[] parts = path.Split('/');

        StringBuilder sb = new StringBuilder();

        foreach (string part in parts)
        {
            sb.Append(ToPascalCase(part));
        }

        return sb.ToString();
    }

    private static string ToPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unnamed";
        }

        StringBuilder sb = new StringBuilder();
        bool nextUpper = true;

        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (sb.Length == 0 && char.IsDigit(c))
                {
                    sb.Append('_');
                }

                sb.Append(nextUpper ? char.ToUpperInvariant(c) : c);
                nextUpper = false;
            }
            else
            {
                nextUpper = true;
            }
        }

        return sb.Length == 0 ? "Unnamed" : sb.ToString();
    }

    private static string MakeUniqueName(string baseName, HashSet<string> usedNames)
    {
        if (!usedNames.Contains(baseName))
        {
            usedNames.Add(baseName);
            return baseName;
        }

        int index = 2;

        while (true)
        {
            string newName = baseName + index;

            if (!usedNames.Contains(newName))
            {
                usedNames.Add(newName);
                return newName;
            }

            index++;
        }
    }

    private static bool IsValidAssetFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        path = NormalizePath(path);

        if (!path.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return AssetDatabase.IsValidFolder(path);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Replace("\\", "/").TrimEnd('/');
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        extension = extension.Trim().ToLowerInvariant();

        if (!extension.StartsWith("."))
        {
            extension = "." + extension;
        }

        return extension;
    }

    private static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }
}

[Serializable]
public class AddressableKeyPreviewItem
{
    [TableColumnWidth(220)]
    [LabelText("常量名")]
    public string ConstName;

    [LabelText("资源路径")]
    public string AssetPath;

    [TableColumnWidth(80)]
    [LabelText("扩展名")]
    public string Extension;
}

#endif
