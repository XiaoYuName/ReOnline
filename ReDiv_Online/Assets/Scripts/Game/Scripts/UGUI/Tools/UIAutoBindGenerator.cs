using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using XFramework;
#endif

[DisallowMultipleComponent]
[AddComponentMenu("XFramework/UI 自动绑定生成器")]
[InfoBox("挂到 UI 预制体根节点上。维护下方绑定列表后，点击“生成 UI 脚本”会生成两个脚本：手写入口脚本和自动绑定脚本。之后重新生成只会改自动绑定脚本。")]
public class UIAutoBindGenerator : MonoBehaviour
{
    private const string DefaultOutputFolder = "Assets/Scripts/Game/Scripts/UGUI";

    [TitleGroup("脚本生成配置")]
    [LabelText("脚本输出父目录")]
    [InfoBox("必须是 Assets 下的相对路径。生成时会在这个目录下自动创建“脚本名”子文件夹。")]
    [SerializeField,FolderPath]
    private string outputFolder = DefaultOutputFolder;

    [TitleGroup("脚本生成配置")]
    [LabelText("覆盖自动绑定脚本")]
    [SerializeField]
    private bool overwriteExisting = true;

    [TitleGroup("脚本生成配置")]
    [LabelText("生成后自动挂载脚本")]
    [InfoBox("如果根节点已有其它 UIBase 组件，只会给出警告，不会自动替换。")]
    [SerializeField]
    private bool attachGeneratedComponent = true;

    [TitleGroup("绑定列表")]
    [LabelText("需要自动绑定的子物体")]
    [InfoBox("绑定方式支持“单个”和“子级列表”。子级列表会按 Hierarchy 顺序收集目标下面的所有同类型组件（包含未激活节点，不包含目标自身）。")]
    [ListDrawerSettings(ShowFoldout = false, DraggableItems = true, ShowPaging = false)]
    [SerializeField]
    private List<UIAutoBindItem> bindItems = new List<UIAutoBindItem>();

    public string OutputFolder
    {
        get => outputFolder;
        set => outputFolder = value;
    }

    public bool OverwriteExisting
    {
        get => overwriteExisting;
        set => overwriteExisting = value;
    }

    public bool AttachGeneratedComponent
    {
        get => attachGeneratedComponent;
        set => attachGeneratedComponent = value;
    }

    public List<UIAutoBindItem> BindItems => bindItems;

#if UNITY_EDITOR
    private const string PendingAttachObjectKey = "UIAutoBindGenerator.PendingAttachObject";
    private const string PendingAttachClassKey = "UIAutoBindGenerator.PendingAttachClass";

    private static readonly string[] PreferredComponentNames =
    {
        "CustomButton",
        "SelectedButton",
        "AGVButton",
        "Button",
        "TMP_Dropdown",
        "Dropdown",
        "Slider",
        "Toggle",
        "ScrollRect",
        "TMP_InputField",
        "InputField",
        "TextMeshProUGUI",
        "TMP_Text",
        "LocalizeStringEvent",
        "Image",
        "RawImage",
        "RectTransform",
        "Transform"
    };

    [TitleGroup("操作")]
    [ButtonGroup("操作/列表操作")]
    [Button("添加空绑定")]
    private void AddEmptyBindItem()
    {
        Undo.RecordObject(this, "Add UI auto bind item");
        bindItems.Add(new UIAutoBindItem());
        MarkDirty(this);
    }

    [ButtonGroup("操作/列表操作")]
    [Button("添加当前选择")]
    private void AddSelection()
    {
        Undo.RecordObject(this, "Add selected UI bind items");
        foreach (GameObject selectedObject in Selection.gameObjects)
        {
            if (selectedObject == null || !IsChildOrSelf(transform, selectedObject.transform))
            {
                continue;
            }

            AddOrUpdateItem(this, selectedObject, PickDefaultType(selectedObject));
        }

        RefreshItems(this, false);
        MarkDirty(this);
    }

    [ButtonGroup("操作/列表操作")]
    [Button("扫描全部子物体")]
    private void ScanChildren()
    {
        Undo.RecordObject(this, "Scan UI bind items");
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child == transform)
            {
                continue;
            }

            Type type = PickDefaultType(child.gameObject);
            if (type == null)
            {
                continue;
            }

            AddOrUpdateItem(this, child.gameObject, type);
        }

        RefreshItems(this, false);
        MarkDirty(this);
    }

    [ButtonGroup("操作/维护操作")]
    [Button("刷新字段名和路径")]
    private void RefreshNamesAndPaths()
    {
        Undo.RecordObject(this, "Refresh UI auto bind items");
        RefreshItems(this, true);
        MarkDirty(this);
    }

    [ButtonGroup("操作/维护操作")]
    [Button("清空绑定列表")]
    private void ClearBindItems()
    {
        Undo.RecordObject(this, "Clear UI auto bind items");
        bindItems.Clear();
        MarkDirty(this);
    }

    [TitleGroup("操作")]
    [Button("生成 UI 脚本", ButtonSizes.Large)]
    [GUIColor(0.35f, 0.75f, 1f)]
    private void GenerateUIScript()
    {
        GenerateScript(this);
    }

    internal static void AddOrUpdateItem(UIAutoBindGenerator generator, GameObject targetObject, Type componentType)
    {
        UIAutoBindItem item = generator.BindItems.FirstOrDefault(bindItem => bindItem.Target == targetObject);
        if (item == null)
        {
            item = new UIAutoBindItem { Target = targetObject };
            generator.BindItems.Add(item);
        }

        if (string.IsNullOrEmpty(item.ComponentTypeName))
        {
            item.ComponentTypeName = GetTypeStorageName(componentType);
        }

        if (string.IsNullOrEmpty(item.FieldName))
        {
            item.FieldName = MakeFieldName(targetObject.name);
        }

        item.Path = GetRelativePath(generator.transform, targetObject.transform);
    }

    internal static void MarkDirty(UIAutoBindGenerator generator)
    {
        if (generator == null)
        {
            return;
        }

        EditorUtility.SetDirty(generator);

        // SetDirty 对场景中的 Prefab 实例和 Prefab Stage 不够可靠，
        // 显式记录覆盖并标记场景，避免域重载、打包或切换资源后列表回退。
        if (PrefabUtility.IsPartOfPrefabInstance(generator))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(generator);
        }

        if (generator.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }
    }

    internal static void RefreshItems(UIAutoBindGenerator generator, bool fillEmptyFieldNames)
    {
        HashSet<string> usedNames = new HashSet<string>();
        foreach (UIAutoBindItem item in generator.BindItems)
        {
            if (item.Target == null)
            {
                continue;
            }

            if (string.IsNullOrEmpty(item.ComponentTypeName))
            {
                item.ComponentTypeName = GetTypeStorageName(PickDefaultType(
                    item.Target,
                    item.BindMode == UIAutoBindMode.ChildrenList));
            }

            if (fillEmptyFieldNames || string.IsNullOrWhiteSpace(item.FieldName))
            {
                item.FieldName = MakeFieldName(item.Target.name);
            }

            item.FieldName = MakeUniqueFieldName(MakeFieldName(item.FieldName), usedNames);
            item.Path = GetRelativePath(generator.transform, item.Target.transform);
        }
    }

    private static void GenerateScript(UIAutoBindGenerator generator)
    {
        RefreshItems(generator, false);

        string prefabName = GetPrefabName(generator.gameObject);
        string className = MakeClassName(prefabName);
        if (string.IsNullOrEmpty(className))
        {
            Debug.LogError("无法根据预制体/根节点名生成合法的 C# 类名。", generator);
            return;
        }

        if (className != prefabName)
        {
            Debug.LogWarning($"预制体/根节点名 '{prefabName}' 不是合法 C# 标识符，已使用 '{className}' 作为类名。", generator);
        }

        List<GeneratedBindItem> generatedItems = BuildGeneratedItems(generator);
        if (generatedItems == null)
        {
            return;
        }

        string outputFolder = NormalizeAssetPath(generator.OutputFolder);
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            outputFolder = DefaultOutputFolder;
        }

        if (outputFolder != "Assets" && !outputFolder.StartsWith("Assets/", StringComparison.Ordinal))
        {
            Debug.LogError("输出父目录必须是 Assets 下的相对路径，例如 Assets/Scripts/Game/Scripts/UGUI/UGUI。", generator);
            return;
        }

        string scriptFolderAssetPath = $"{outputFolder}/{className}";
        string scriptFolderFullPath = AssetPathToFullPath(scriptFolderAssetPath);
        Directory.CreateDirectory(scriptFolderFullPath);

        string manualAssetPath = $"{scriptFolderAssetPath}/{className}.cs";
        string autoBindAssetPath = $"{scriptFolderAssetPath}/{className}.AutoBind.cs";
        string manualFullPath = AssetPathToFullPath(manualAssetPath);
        string autoBindFullPath = AssetPathToFullPath(autoBindAssetPath);

        if (File.Exists(autoBindFullPath) && !generator.OverwriteExisting)
        {
            Debug.LogError($"自动绑定脚本已存在，且当前未开启覆盖：{autoBindAssetPath}", generator);
            return;
        }

        if (generator.AttachGeneratedComponent)
        {
            SavePendingAttach(generator.gameObject, className);
        }

        bool shouldWriteManualScript = !File.Exists(manualFullPath) || IsAutoGeneratedFile(manualFullPath);
        if (shouldWriteManualScript)
        {
            File.WriteAllText(manualFullPath, BuildManualScriptContent(className), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(manualAssetPath);
        }
        else
        {
            Debug.Log($"手写脚本已存在，已保留：{manualAssetPath}。请确认 Init() 中会调用 InitAutoBind();", generator);
        }

        File.WriteAllText(autoBindFullPath, BuildAutoBindScriptContent(className, generatedItems), new UTF8Encoding(false));
        AssetDatabase.ImportAsset(autoBindAssetPath);
        AssetDatabase.Refresh();

        Debug.Log($"UI 脚本已生成：{manualAssetPath} / {autoBindAssetPath}", generator);

        if (generator.AttachGeneratedComponent)
        {
            Type generatedType = FindUIBaseType(className);
            if (generatedType != null)
            {
                AttachGeneratedUIComponent(generator.gameObject, generatedType);
                ClearPendingAttach();
            }
        }
    }

    private static List<GeneratedBindItem> BuildGeneratedItems(UIAutoBindGenerator generator)
    {
        List<GeneratedBindItem> generatedItems = new List<GeneratedBindItem>();
        HashSet<string> usedFieldNames = new HashSet<string>();

        foreach (UIAutoBindItem item in generator.BindItems)
        {
            if (item.Target == null)
            {
                Debug.LogError("生成失败：绑定列表中存在空目标。", generator);
                return null;
            }

            if (!IsChildOrSelf(generator.transform, item.Target.transform))
            {
                Debug.LogError($"生成失败：'{item.Target.name}' 不在根节点 '{generator.name}' 下。", item.Target);
                return null;
            }

            Type type = ResolveType(item.ComponentTypeName);
            if (type == null)
            {
                Debug.LogError($"生成失败：'{item.Target.name}' 没有选择有效组件。", item.Target);
                return null;
            }

            if (item.BindMode == UIAutoBindMode.Single && type != typeof(GameObject) && item.Target.GetComponent(type) == null)
            {
                Debug.LogError($"生成失败：'{item.Target.name}' 上没有组件 '{GetTypeDisplayName(type)}'。", item.Target);
                return null;
            }

            if (item.BindMode == UIAutoBindMode.ChildrenList && !HasBindableChild(item.Target, type))
            {
                Debug.LogError($"生成失败：'{item.Target.name}' 的子级中没有组件 '{GetTypeDisplayName(type)}'。", item.Target);
                return null;
            }

            string fieldName = MakeUniqueFieldName(MakeFieldName(item.FieldName), usedFieldNames);
            string relativePath = GetRelativePath(generator.transform, item.Target.transform);
            bool autoInit = item.AutoInit && typeof(UIBase).IsAssignableFrom(type);
            generatedItems.Add(new GeneratedBindItem(fieldName, type, relativePath, item.BindMode, autoInit));
        }

        return generatedItems;
    }

    private static string BuildManualScriptContent(string className)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("using XFramework;");
        builder.AppendLine();
        builder.AppendLine($"public partial class {className} : UIBase");
        builder.AppendLine("{");
        builder.AppendLine("    public override void Init()");
        builder.AppendLine("    {");
        builder.AppendLine("        InitAutoBind();");
        builder.AppendLine();
        builder.AppendLine("        // 在这里写其它初始化逻辑。重新生成 UI 绑定时，这个文件不会被覆盖。");
        builder.AppendLine("    }");
        builder.AppendLine("}");

        return builder.ToString();
    }

    private static string BuildAutoBindScriptContent(string className, List<GeneratedBindItem> items)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Linq;");
        builder.AppendLine("using UnityEngine;");
        builder.AppendLine();
        builder.AppendLine($"public partial class {className}");
        builder.AppendLine("{");

        foreach (GeneratedBindItem item in items)
        {
            string fieldType = GetTypeNameForCode(item.Type);
            if (item.BindMode == UIAutoBindMode.ChildrenList)
            {
                fieldType = $"List<{fieldType}>";
            }

            builder.AppendLine($"    private {fieldType} {item.FieldName};");
        }

        if (items.Count > 0)
        {
            builder.AppendLine();
        }

        builder.AppendLine("    private void InitAutoBind()");
        builder.AppendLine("    {");
        foreach (GeneratedBindItem item in items)
        {
            builder.AppendLine($"        {item.FieldName} = {BuildAssignment(item)};");
        }

        List<GeneratedBindItem> autoInitItems = items.Where(item => item.AutoInit).ToList();
        if (autoInitItems.Count > 0)
        {
            builder.AppendLine();
        }

        foreach (GeneratedBindItem item in autoInitItems)
        {
            if (item.BindMode == UIAutoBindMode.ChildrenList)
            {
                builder.AppendLine($"        foreach ({GetTypeNameForCode(item.Type)} element in {item.FieldName})");
                builder.AppendLine("        {");
                builder.AppendLine("            InitBoundUI(element);");
                builder.AppendLine("        }");
            }
            else
            {
                builder.AppendLine($"        InitBoundUI({item.FieldName});");
            }
        }

        if (items.Count > 0)
        {
            builder.AppendLine();
        }

        builder.AppendLine("    }");

        if (autoInitItems.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("    private static void InitBoundUI(XFramework.UIBase target)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (target != null)");
            builder.AppendLine("        {");
            builder.AppendLine("            target.Init();");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
        }

        builder.AppendLine("}");

        return builder.ToString();
    }

    private static string BuildAssignment(GeneratedBindItem item)
    {
        string escapedPath = EscapeString(item.Path);

        if (item.BindMode == UIAutoBindMode.ChildrenList)
        {
            string root = string.IsNullOrEmpty(item.Path) ? "transform" : $"Get<Transform>(\"{escapedPath}\")";
            if (item.Type == typeof(GameObject))
            {
                return $"{root}.GetComponentsInChildren<Transform>(true)" +
                       $".Where(item => item != {root}).Select(item => item.gameObject).ToList()";
            }

            string listTypeName = GetTypeNameForCode(item.Type);
            return $"{root}.GetComponentsInChildren<{listTypeName}>(true)" +
                   $".Where(item => item.transform != {root}).ToList()";
        }

        if (item.Type == typeof(GameObject))
        {
            return string.IsNullOrEmpty(item.Path) ? "gameObject" : $"Get(\"{escapedPath}\")";
        }

        if (item.Type == typeof(Transform))
        {
            return string.IsNullOrEmpty(item.Path) ? "transform" : $"Get<Transform>(\"{escapedPath}\")";
        }

        string typeName = GetTypeNameForCode(item.Type);
        return string.IsNullOrEmpty(item.Path) ? $"GetComponent<{typeName}>()" : $"Get<{typeName}>(\"{escapedPath}\")";
    }

    internal static Type PickDefaultType(GameObject targetObject, bool searchChildren = false)
    {
        if (targetObject == null)
        {
            return null;
        }

        List<Type> types = GetBindableTypes(targetObject, searchChildren);
        foreach (string preferredName in PreferredComponentNames)
        {
            Type type = types.FirstOrDefault(candidate => candidate.Name == preferredName);
            if (type != null)
            {
                return type;
            }
        }

        return types.FirstOrDefault(type => type != typeof(GameObject));
    }

    internal static List<Type> GetBindableTypes(GameObject targetObject, bool searchChildren = false)
    {
        List<Type> types = new List<Type>();
        if (targetObject == null)
        {
            return types;
        }

        types.Add(typeof(GameObject));
        Component[] components = searchChildren
            ? targetObject.GetComponentsInChildren<Component>(true)
            : targetObject.GetComponents<Component>();

        foreach (Component component in components)
        {
            if (component == null || component is UIAutoBindGenerator ||
                searchChildren && component.gameObject == targetObject)
            {
                continue;
            }

            Type type = component.GetType();
            if (!types.Contains(type))
            {
                types.Add(type);
            }
        }

        return types;
    }

    private static bool HasBindableChild(GameObject targetObject, Type type)
    {
        if (targetObject == null || type == null)
        {
            return false;
        }

        foreach (Transform child in targetObject.GetComponentsInChildren<Transform>(true))
        {
            if (child == targetObject.transform)
            {
                continue;
            }

            if (type == typeof(GameObject) || child.GetComponent(type) != null)
            {
                return true;
            }
        }

        return false;
    }

    internal static string GetRelativePath(Transform root, Transform child)
    {
        if (root == null || child == null)
        {
            return string.Empty;
        }

        if (root == child)
        {
            return string.Empty;
        }

        Stack<string> pathParts = new Stack<string>();
        Transform current = child;
        while (current != null && current != root)
        {
            pathParts.Push(current.name);
            current = current.parent;
        }

        return current == root ? string.Join("/", pathParts.ToArray()) : string.Empty;
    }

    private static bool IsChildOrSelf(Transform root, Transform child)
    {
        if (root == null || child == null)
        {
            return false;
        }

        Transform current = child;
        while (current != null)
        {
            if (current == root)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static string GetPrefabName(GameObject root)
    {
        string assetPath = AssetDatabase.GetAssetPath(root);
        if (!string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(assetPath);
        }

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null && prefabStage.prefabContentsRoot == root)
        {
            return Path.GetFileNameWithoutExtension(prefabStage.assetPath);
        }

        string prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
        return !string.IsNullOrEmpty(prefabAssetPath) ? Path.GetFileNameWithoutExtension(prefabAssetPath) : root.name;
    }

    private static string MakeClassName(string source)
    {
        string className = MakeIdentifier(source, true);
        return string.IsNullOrEmpty(className) ? "GeneratedUI" : className;
    }

    internal static string MakeFieldName(string source)
    {
        string identifier = MakeIdentifier(source, false);
        return string.IsNullOrEmpty(identifier) ? "bindItem" : identifier;
    }

    private static string MakeIdentifier(string source, bool pascalCase)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        string[] parts = Regex.Split(source.Trim(), "[^A-Za-z0-9]+")
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (parts.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (!pascalCase && i == 0)
            {
                builder.Append(char.ToLowerInvariant(part[0]));
                if (part.Length > 1)
                {
                    builder.Append(part.Substring(1));
                }
            }
            else
            {
                builder.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                {
                    builder.Append(part.Substring(1));
                }
            }
        }

        string identifier = builder.ToString();
        if (identifier.Length == 0)
        {
            return string.Empty;
        }

        if (char.IsDigit(identifier[0]))
        {
            identifier = "_" + identifier;
        }

        return IsCSharpKeyword(identifier) ? "@" + identifier : identifier;
    }

    private static string MakeUniqueFieldName(string fieldName, HashSet<string> usedNames)
    {
        string baseName = string.IsNullOrWhiteSpace(fieldName) ? "bindItem" : fieldName;
        string uniqueName = baseName;
        int index = 2;
        while (usedNames.Contains(uniqueName))
        {
            uniqueName = $"{baseName}{index}";
            index++;
        }

        usedNames.Add(uniqueName);
        return uniqueName;
    }

    private static bool IsCSharpKeyword(string identifier)
    {
        switch (identifier)
        {
            case "abstract":
            case "as":
            case "base":
            case "bool":
            case "break":
            case "byte":
            case "case":
            case "catch":
            case "char":
            case "checked":
            case "class":
            case "const":
            case "continue":
            case "decimal":
            case "default":
            case "delegate":
            case "do":
            case "double":
            case "else":
            case "enum":
            case "event":
            case "explicit":
            case "extern":
            case "false":
            case "finally":
            case "fixed":
            case "float":
            case "for":
            case "foreach":
            case "goto":
            case "if":
            case "implicit":
            case "in":
            case "int":
            case "interface":
            case "internal":
            case "is":
            case "lock":
            case "long":
            case "namespace":
            case "new":
            case "null":
            case "object":
            case "operator":
            case "out":
            case "override":
            case "params":
            case "private":
            case "protected":
            case "public":
            case "readonly":
            case "ref":
            case "return":
            case "sbyte":
            case "sealed":
            case "short":
            case "sizeof":
            case "stackalloc":
            case "static":
            case "string":
            case "struct":
            case "switch":
            case "this":
            case "throw":
            case "true":
            case "try":
            case "typeof":
            case "uint":
            case "ulong":
            case "unchecked":
            case "unsafe":
            case "ushort":
            case "using":
            case "virtual":
            case "void":
            case "volatile":
            case "while":
                return true;
            default:
                return false;
        }
    }

    internal static Type ResolveType(string storageName)
    {
        if (string.IsNullOrEmpty(storageName))
        {
            return null;
        }

        Type directType = Type.GetType(storageName);
        if (directType != null)
        {
            return directType;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(storageName);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    internal static string GetTypeStorageName(Type type)
    {
        return type == null ? string.Empty : type.AssemblyQualifiedName;
    }

    internal static string GetTypeDisplayName(Type type)
    {
        if (type == null)
        {
            return "未选择";
        }

        return string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
    }

    private static string GetTypeNameForCode(Type type)
    {
        if (type == typeof(GameObject))
        {
            return "GameObject";
        }

        if (type == typeof(Transform))
        {
            return "Transform";
        }

        return string.IsNullOrEmpty(type.Namespace) ? type.Name : type.FullName.Replace("+", ".");
    }

    private static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static bool IsAutoGeneratedFile(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return false;
        }

        using (StreamReader reader = new StreamReader(fullPath, Encoding.UTF8, true))
        {
            string firstLine = reader.ReadLine();
            return firstLine != null && firstLine.Contains("<auto-generated");
        }
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return string.IsNullOrWhiteSpace(assetPath) ? string.Empty : assetPath.Replace("\\", "/").TrimEnd('/');
    }

    private static string FullPathToAssetPath(string fullPath)
    {
        string normalizedFullPath = NormalizeAssetPath(fullPath);
        string normalizedDataPath = NormalizeAssetPath(Application.dataPath);
        if (!normalizedFullPath.Equals(normalizedDataPath, StringComparison.OrdinalIgnoreCase) &&
            !normalizedFullPath.StartsWith(normalizedDataPath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return "Assets" + normalizedFullPath.Substring(normalizedDataPath.Length);
    }

    private static string AssetPathToFullPath(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, NormalizeAssetPath(assetPath));
    }

    private static Type FindUIBaseType(string className)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type != null).ToArray();
            }

            foreach (Type type in types)
            {
                if (type != null && type.Name == className && typeof(UIBase).IsAssignableFrom(type))
                {
                    return type;
                }
            }
        }

        return null;
    }

    private static void AttachGeneratedUIComponent(GameObject root, Type componentType)
    {
        if (root == null || componentType == null || !typeof(UIBase).IsAssignableFrom(componentType))
        {
            return;
        }

        if (root.GetComponent(componentType) != null)
        {
            return;
        }

        UIBase existingUIBase = root.GetComponent<UIBase>();
        if (existingUIBase != null && existingUIBase.GetType() != componentType)
        {
            Debug.LogWarning($"根节点已经存在 UIBase 组件 '{existingUIBase.GetType().Name}'，不会自动添加生成组件 '{componentType.Name}'。", root);
            return;
        }

        Undo.AddComponent(root, componentType);
        EditorUtility.SetDirty(root);
        PrefabUtility.RecordPrefabInstancePropertyModifications(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"已将生成组件 '{componentType.Name}' 挂载到 '{root.name}'。", root);
    }

    private static void SavePendingAttach(GameObject root, string className)
    {
        GlobalObjectId globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(root);
        EditorPrefs.SetString(PendingAttachObjectKey, globalObjectId.ToString());
        EditorPrefs.SetString(PendingAttachClassKey, className);
    }

    private static void ClearPendingAttach()
    {
        EditorPrefs.DeleteKey(PendingAttachObjectKey);
        EditorPrefs.DeleteKey(PendingAttachClassKey);
    }

    [DidReloadScripts]
    private static void AttachAfterScriptReload()
    {
        string objectId = EditorPrefs.GetString(PendingAttachObjectKey, string.Empty);
        string className = EditorPrefs.GetString(PendingAttachClassKey, string.Empty);
        if (string.IsNullOrEmpty(objectId) || string.IsNullOrEmpty(className))
        {
            return;
        }

        ClearPendingAttach();

        if (!GlobalObjectId.TryParse(objectId, out GlobalObjectId globalObjectId))
        {
            return;
        }

        UnityEngine.Object targetObject = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalObjectId);
        GameObject root = targetObject as GameObject;
        Type generatedType = FindUIBaseType(className);
        if (root != null && generatedType != null)
        {
            AttachGeneratedUIComponent(root, generatedType);
        }
    }

    private readonly struct GeneratedBindItem
    {
        public readonly string FieldName;
        public readonly Type Type;
        public readonly string Path;
        public readonly UIAutoBindMode BindMode;
        public readonly bool AutoInit;

        public GeneratedBindItem(string fieldName, Type type, string path, UIAutoBindMode bindMode, bool autoInit)
        {
            FieldName = fieldName;
            Type = type;
            Path = path;
            BindMode = bindMode;
            AutoInit = autoInit;
        }
    }
#endif
}

[Serializable]
[InlineProperty]
[HideLabel]
public class UIAutoBindItem
{
    [HorizontalGroup("BindingRow", Width = 0.34f)]
    [LabelText("目标")]
    [LabelWidth(38)]
#if UNITY_EDITOR
    [OnValueChanged(nameof(OnTargetChanged))]
#endif
    public GameObject Target;

    [HorizontalGroup("BindingRow", Width = 0.20f)]
    [LabelText("方式")]
    [LabelWidth(38)]
#if UNITY_EDITOR
    [OnValueChanged(nameof(OnBindModeChanged))]
#endif
    public UIAutoBindMode BindMode;

    [HorizontalGroup("BindingRow", Width = 0.46f)]
    [LabelText("组件")]
    [LabelWidth(38)]
#if UNITY_EDITOR
    [ValueDropdown(nameof(GetComponentTypeOptions))]
#endif
    public string ComponentTypeName;

    [HorizontalGroup("PathRow", Width = 0.32f)]
    [LabelText("字段名")]
    [LabelWidth(50)]
    public string FieldName;

    [HorizontalGroup("PathRow", Width = 0.68f)]
    [LabelText("相对路径")]
    [LabelWidth(62)]
    [ReadOnly]
    public string Path;

    [LabelText("生成时自动调用 Init()")]
    [LabelWidth(150)]
#if UNITY_EDITOR
    [ShowIf(nameof(IsUIBaseComponent))]
#endif
    public bool AutoInit = true;

#if UNITY_EDITOR
    private bool IsUIBaseComponent()
    {
        Type type = UIAutoBindGenerator.ResolveType(ComponentTypeName);
        return type != null && typeof(UIBase).IsAssignableFrom(type);
    }

    private void OnTargetChanged()
    {
        RefreshDefaultComponentType();

        if (Target != null && string.IsNullOrWhiteSpace(FieldName))
        {
            FieldName = UIAutoBindGenerator.MakeFieldName(Target.name);
        }
    }

    private void OnBindModeChanged()
    {
        RefreshDefaultComponentType();
    }

    private void RefreshDefaultComponentType()
    {
        bool searchChildren = BindMode == UIAutoBindMode.ChildrenList;
        ComponentTypeName = UIAutoBindGenerator.GetTypeStorageName(
            UIAutoBindGenerator.PickDefaultType(Target, searchChildren));
    }

    private IEnumerable<ValueDropdownItem<string>> GetComponentTypeOptions()
    {
        if (Target == null)
        {
            yield return new ValueDropdownItem<string>("未选择目标物体", string.Empty);
            yield break;
        }

        bool searchChildren = BindMode == UIAutoBindMode.ChildrenList;
        foreach (Type type in UIAutoBindGenerator.GetBindableTypes(Target, searchChildren))
        {
            yield return new ValueDropdownItem<string>(UIAutoBindGenerator.GetTypeDisplayName(type),
                UIAutoBindGenerator.GetTypeStorageName(type));
        }
    }
#endif
}

public enum UIAutoBindMode
{
    [LabelText("单个")]
    Single,

    [LabelText("子级列表")]
    ChildrenList
}
