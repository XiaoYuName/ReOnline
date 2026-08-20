#if UNITY_EDITOR


using UnityEngine;
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEditor;

[CreateAssetMenu(fileName = "AddressableKeyGeneratorWindow.asset", menuName = "Configs/Project/AddressableKeyGeneratorWindow")]
public class AddressableKeyGeneratorSettings : ScriptableObject
{
    [Title("路径设置")]

    [LabelText("资源文件夹")]
    [FolderPath(RequireExistingPath = true)]
    [ValidateInput(nameof(IsValidAssetFolder), "必须选择 Assets 目录下的文件夹")]
    public string TargetFolder = "Assets/AddressableAssets/Remote";

    [LabelText("输出文件夹")]
    [FolderPath(RequireExistingPath = true)]
    [ValidateInput(nameof(IsValidAssetFolder), "必须选择 Assets 目录下的文件夹")]
    public string OutputFolder = "Assets/Scripts/Game/Scripts/AddressableKeys";

    [Title("生成设置")]

    [LabelText("命名空间")]
    public string NamespaceName = "XFramework";

    [LabelText("类名")]
    public string ClassName = "AssetKeys";

    [LabelText("常量后缀")]
    public string ConstSuffix = "Path";

    [LabelText("包含子文件夹")]
    public bool IncludeSubFolders = true;

    [LabelText("只生成 Prefab")]
    [Tooltip("开启后，只生成 .prefab。关闭后，会根据包含/排除列表筛选资源。")]
    public bool OnlyPrefab = true;

    [LabelText("覆盖同名文件")]
    public bool OverwriteFile = true;

    [LabelText("使用完整路径生成常量名")]
    [InfoBox("关闭时只使用资源文件名生成常量名，例如 SceneCharacterPath。开启时会根据路径生成更长的名字，减少重名。")]
    public bool UseFullPathAsConstName = false;

    [Title("过滤设置")]

    [LabelText("只包含扩展名")]
    [Tooltip("为空表示不限制。例如：.prefab、.png、.asset。OnlyPrefab 开启时优先使用 OnlyPrefab。")]
    [ListDrawerSettings(ShowFoldout = true, DraggableItems = true)]
    public List<string> IncludeExtensions = new List<string>();

    [LabelText("排除扩展名")]
    [Tooltip("例如：.cs、.asmdef、.meta、.json。")]
    [ListDrawerSettings(ShowFoldout = true, DraggableItems = true)]
    public List<string> IgnoreExtensions = new List<string>
    {
        ".cs",
        ".asmdef",
        ".meta",
        ".json",
        ".txt",
        ".md"
    };

    [LabelText("排除文件夹")]
    [FolderPath(RequireExistingPath = true)]
    [ListDrawerSettings(ShowFoldout = true, DraggableItems = true)]
    public List<string> IgnoreFolders = new List<string>();

    [LabelText("排除路径关键字")]
    [Tooltip("路径中包含这些关键字时会被排除。例如：/Editor/、/Test/、_Backup。")]
    [ListDrawerSettings(ShowFoldout = true, DraggableItems = true)]
    public List<string> IgnorePathKeywords = new List<string>
    {
        "/Editor/",
        "/Tests/",
        "/Test/",
        "_Backup"
    };

    private static bool IsValidAssetFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        path = path.Replace("\\", "/").TrimEnd('/');

        if (!path.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return AssetDatabase.IsValidFolder(path);
    }
}
#endif
