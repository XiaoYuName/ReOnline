#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// LubanManager.Generated.cs 生成器。入口在 ConfigTools 窗口，这里不再注册 MenuItem。
    /// 生成参数存在 LubanManagerGeneratorConfig.asset 里，要改直接在 Project 里选中它改。
    /// </summary>
    public class LubanManagerGeneratorWindow : OdinEditorWindow
    {
        public const string DefaultConfigPath = "Assets/Editor/Luban/LubanManagerGeneratorConfig.asset";

        [TitleGroup("配置文件")]
        [LabelText("配置资源")]
        [Required("请指定或创建 LubanManagerGeneratorConfig 配置资源")]
        [AssetSelector(Paths = "Assets")]
        [OnValueChanged(nameof(OnConfigChanged))]
        public LubanManagerGeneratorConfig config;

        [HorizontalGroup("配置文件/Buttons")]
        [Button("加载默认配置", ButtonSizes.Medium)]
        private void LoadDefaultConfigButton()
        {
            LoadOrCreateDefaultConfig();
        }

        [HorizontalGroup("配置文件/Buttons")]
        [Button("创建新配置", ButtonSizes.Medium)]
        private void CreateConfigButton()
        {
            CreateConfig();
        }

        [HorizontalGroup("配置文件/Buttons")]
        [Button("选中配置资源", ButtonSizes.Medium)]
        private void PingConfig()
        {
            if (config == null)
            {
                Debug.LogWarning("当前没有配置资源。");
                return;
            }

            EditorGUIUtility.PingObject(config);
            Selection.activeObject = config;
        }

        [TitleGroup("当前配置预览")]
        [ShowInInspector]
        [InlineEditor(InlineEditorObjectFieldModes.Hidden)]
        [HideLabel]
        [ShowIf(nameof(HasConfig))]
        private LubanManagerGeneratorConfig ConfigPreview => config;

        [TitleGroup("扫描结果")]
        [ShowInInspector]
        [ReadOnly]
        [TableList(IsReadOnly = true, AlwaysExpanded = true)]
        private List<TableInfo> scannedTables = new List<TableInfo>();

        [TitleGroup("代码预览")]
        [ShowInInspector]
        [ReadOnly]
        [MultiLineProperty(18)]
        [HideLabel]
        private string previewCode = "";

        private bool HasConfig()
        {
            return config != null;
        }

        private void OnConfigChanged()
        {
            scannedTables.Clear();
            previewCode = "";
        }

        private void LoadOrCreateDefaultConfig()
        {
            config = AssetDatabase.LoadAssetAtPath<LubanManagerGeneratorConfig>(DefaultConfigPath);

            if (config == null)
            {
                CreateConfigAtPath(DefaultConfigPath);
            }
        }

        private void CreateConfig()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "创建 LubanManagerGeneratorConfig",
                "LubanManagerGeneratorConfig",
                "asset",
                "请选择配置保存路径",
                "Assets/Editor/Luban"
            );

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            CreateConfigAtPath(path);
        }

        private void CreateConfigAtPath(string path)
        {
            string directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var asset = CreateInstance<LubanManagerGeneratorConfig>();

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            config = asset;

            EditorGUIUtility.PingObject(config);
            Selection.activeObject = config;

            Debug.Log($"LubanManagerGeneratorConfig 创建完成: {path}");
        }

        [TitleGroup("操作")]
        [Button("扫描 Luban 表", ButtonSizes.Large)]
        [GUIColor(0.4f, 0.8f, 1f)]
        private bool ScanTables()
        {
            if (!CheckConfig())
            {
                return false;
            }

            scannedTables.Clear();

            string lubanCodeDirectory = config.lubanCodeDirectory;

            if (string.IsNullOrWhiteSpace(lubanCodeDirectory))
            {
                Debug.LogError("Luban C#代码目录不能为空。");
                return false;
            }

            if (!Directory.Exists(lubanCodeDirectory))
            {
                Debug.LogError($"Luban C#代码目录不存在: {lubanCodeDirectory}");
                return false;
            }

            string[] files = Directory.GetFiles(lubanCodeDirectory, "Tb*.cs", SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string className = Path.GetFileNameWithoutExtension(file);

                if (string.IsNullOrWhiteSpace(className))
                {
                    continue;
                }

                if (!className.StartsWith("Tb", StringComparison.Ordinal))
                {
                    continue;
                }

                string content = File.ReadAllText(file);

                if (!IsValidLubanTableClass(content, className))
                {
                    continue;
                }

                scannedTables.Add(new TableInfo
                {
                    TableClassName = className,
                    FieldName = ToPrivateFieldName(className),
                    PropertyName = className,
                    AssetKeyName = ToAssetKeyName(className),
                    FilePath = file.Replace("\\", "/")
                });
            }

            scannedTables.Sort((a, b) => string.Compare(a.TableClassName, b.TableClassName, StringComparison.Ordinal));

            previewCode = GenerateCode(scannedTables);

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            Debug.Log($"扫描完成，共找到 {scannedTables.Count} 张 Luban 表。");
            return true;
        }
        
        private bool IsValidLubanTableClass(string content, string className)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(className))
            {
                return false;
            }

            // 必须存在这个类声明
            if (!Regex.IsMatch(content, $@"\bclass\s+{Regex.Escape(className)}\b"))
            {
                return false;
            }

            // 排除 Luban Bean 类型：
            // 例如：public sealed partial class TbLocalzationKeyData : Luban.BeanBase
            if (Regex.IsMatch(content, $@"\bclass\s+{Regex.Escape(className)}\b[\s\S]*?:\s*Luban\.BeanBase"))
            {
                return false;
            }

            // 真正的 Luban 表管理类构造函数通常是 JArray
            // 例如：public TbGameSceneData(JArray _buf)
            bool hasJArrayConstructor = Regex.IsMatch(
                content,
                $@"\b{Regex.Escape(className)}\s*\(\s*JArray\s+_buf\s*\)"
            );

            if (!hasJArrayConstructor)
            {
                return false;
            }

            // 真正的表管理类通常会有 DataList
            bool hasDataList = content.Contains("DataList");

            // 真正的表管理类通常会有 Dictionary 或 IReadOnlyDictionary
            bool hasDictionary = content.Contains("Dictionary<") || content.Contains("IReadOnlyDictionary<");

            // 二者至少满足一个，避免误判普通 Bean
            if (!hasDataList && !hasDictionary)
            {
                return false;
            }

            return true;
        }

        [TitleGroup("操作")]
        [Button("预览生成代码", ButtonSizes.Large)]
        [GUIColor(0.8f, 0.9f, 1f)]
        private void PreviewCode()
        {
            if (!CheckConfig())
            {
                return;
            }

            if (scannedTables == null || scannedTables.Count == 0)
            {
                ScanTables();
                return;
            }

            previewCode = GenerateCode(scannedTables);
        }

        [TitleGroup("操作")]
        [Button("生成 LubanManager.Generated.cs", ButtonSizes.Large)]
        [GUIColor(0.3f, 1f, 0.5f)]
        public bool Generate()
        {
            if (!CheckConfig())
            {
                return false;
            }

            if (config.autoScanBeforeGenerate || scannedTables == null || scannedTables.Count == 0)
            {
                if (!ScanTables())
                {
                    return false;
                }
            }

            if (scannedTables == null || scannedTables.Count == 0)
            {
                // 常见原因就两个：还没导出过 Luban（目录里没有 Tb*.cs），
                // 或者目录里只有 Bean 类（继承 Luban.BeanBase，不是表，会被 IsValidLubanTableClass 排掉）。
                // 这里不生成空文件：已有的 LubanManager.Generated.cs 会被清空，比报错难查得多。
                Debug.LogError($"没有扫描到任何 Luban 表，生成失败。\n"
                               + $"扫描目录: {config.lubanCodeDirectory}\n"
                               + "先跑「1. 导出 Luban 配置」；如果导出过了还是 0 张，"
                               + "说明目录里只有 Bean 类（Luban.BeanBase）而没有表类，检查 __tables__.xlsx 里是否登记了表。\n"
                               + "注意 UIKeys / AudioKeys 不依赖这一步，可以单独生成。");
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.outputPath))
            {
                Debug.LogError("生成文件输出路径不能为空。");
                return false;
            }

            string code = GenerateCode(scannedTables);

            string directory = Path.GetDirectoryName(config.outputPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Encoding encoding = config.utf8WithoutBom ? new UTF8Encoding(false) : Encoding.UTF8;
            File.WriteAllText(config.outputPath, code, encoding);

            previewCode = code;

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            if (config.refreshAssetDatabase)
            {
                AssetDatabase.Refresh();
            }

            Debug.Log($"LubanManager.Generated.cs 生成成功，共生成 {scannedTables.Count} 张表。\n路径: {config.outputPath}");
            return true;
        }

        public static bool GenerateWithDefaultConfig()
        {
            var generator = CreateInstance<LubanManagerGeneratorWindow>();
            try
            {
                generator.LoadOrCreateDefaultConfig();
                return generator.Generate();
            }
            finally
            {
                DestroyImmediate(generator);
            }
        }

        [TitleGroup("操作")]
        [HorizontalGroup("操作/SubButtons")]
        [Button("打开生成文件", ButtonSizes.Medium)]
        private void OpenGeneratedFile()
        {
            if (!CheckConfig())
            {
                return;
            }

            if (File.Exists(config.outputPath))
            {
                UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(config.outputPath, 1);
            }
            else
            {
                Debug.LogWarning($"生成文件不存在: {config.outputPath}");
            }
        }

        [TitleGroup("操作")]
        [HorizontalGroup("操作/SubButtons")]
        [Button("打开 Luban 代码目录", ButtonSizes.Medium)]
        private void OpenLubanCodeDirectory()
        {
            if (!CheckConfig())
            {
                return;
            }

            if (Directory.Exists(config.lubanCodeDirectory))
            {
                EditorUtility.RevealInFinder(config.lubanCodeDirectory);
            }
            else
            {
                Debug.LogWarning($"目录不存在: {config.lubanCodeDirectory}");
            }
        }

        [TitleGroup("操作")]
        [HorizontalGroup("操作/SubButtons")]
        [Button("清空预览", ButtonSizes.Medium)]
        private void ClearPreview()
        {
            previewCode = "";
        }

        private bool CheckConfig()
        {
            if (config != null)
            {
                return true;
            }

            Debug.LogError("请先创建或指定 LubanManagerGeneratorConfig 配置。");
            return false;
        }

        private string GenerateCode(List<TableInfo> tables)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// 这个文件由 LubanManagerGeneratorWindow 自动生成，请勿手动修改。");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();

            bool shouldAddXFrameworkUsing = !config.useNamespace || config.namespaceName != "XFramework";

            if (shouldAddXFrameworkUsing)
            {
                sb.AppendLine("using XFramework;");
                sb.AppendLine();
            }

            if (config.useNamespace)
            {
                sb.AppendLine($"namespace {config.namespaceName}");
                sb.AppendLine("{");
            }

            string indent = config.useNamespace ? "    " : "";

            sb.AppendLine($"{indent}public partial class LubanManager");
            sb.AppendLine($"{indent}{{");

            foreach (TableInfo table in tables)
            {
                sb.AppendLine($"{indent}    private {table.TableClassName} {table.FieldName};");
                sb.AppendLine();

                sb.AppendLine($"{indent}    public {table.TableClassName} {table.PropertyName}");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        get");
                sb.AppendLine($"{indent}        {{");
                sb.AppendLine($"{indent}            return LoadTable(");
                sb.AppendLine($"{indent}                ref {table.FieldName},");
                sb.AppendLine($"{indent}                {config.assetKeysClassName}.{table.AssetKeyName},");
                sb.AppendLine($"{indent}                json => new {table.TableClassName}(json)");
                sb.AppendLine($"{indent}            );");
                sb.AppendLine($"{indent}        }}");
                sb.AppendLine($"{indent}    }}");
                sb.AppendLine();
            }

            sb.AppendLine($"{indent}    partial void ClearGeneratedCache()");
            sb.AppendLine($"{indent}    {{");

            foreach (TableInfo table in tables)
            {
                sb.AppendLine($"{indent}        {table.FieldName} = null;");

                if (config.freeAssetOnClearCache)
                {
                    sb.AppendLine($"{indent}        AssetsManager.Instance.FreeAsset({config.assetKeysClassName}.{table.AssetKeyName});");
                }
            }

            sb.AppendLine($"{indent}    }}");
            sb.AppendLine($"{indent}}}");

            if (config.useNamespace)
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private string ToPrivateFieldName(string tableClassName)
        {
            if (string.IsNullOrWhiteSpace(tableClassName))
            {
                return "_unknown";
            }

            return "_" + char.ToLowerInvariant(tableClassName[0]) + tableClassName.Substring(1);
        }

        private string ToAssetKeyName(string tableClassName)
        {
            switch (config.assetKeyNameMode)
            {
                case LubanManagerGeneratorConfig.AssetKeyNameMode.KeepTableClassName:
                    // TbGameSceneData -> TbGameSceneDataPath
                    return tableClassName + "Path";

                case LubanManagerGeneratorConfig.AssetKeyNameMode.LowerCase:
                    // TbGameSceneData -> tbgamescenedataPath
                    return tableClassName.ToLowerInvariant() + "Path";

                case LubanManagerGeneratorConfig.AssetKeyNameMode.FirstUpperRestLower:
                    // TbGameSceneData -> TbgamescenedataPath
                    return char.ToUpperInvariant(tableClassName[0]) +
                           tableClassName.Substring(1).ToLowerInvariant() +
                           "Path";

                case LubanManagerGeneratorConfig.AssetKeyNameMode.RemoveTbPrefix:
                    // TbGameSceneData -> GameSceneDataPath
                    if (tableClassName.StartsWith("Tb", StringComparison.Ordinal) && tableClassName.Length > 2)
                    {
                        return tableClassName.Substring(2) + "Path";
                    }

                    return tableClassName + "Path";

                case LubanManagerGeneratorConfig.AssetKeyNameMode.CustomPrefixSuffix:
                    return $"{config.assetKeyPrefix}{tableClassName}{config.assetKeySuffix}";

                default:
                    // 默认使用小写，和 Luban 生成的 Json 文件名保持一致
                    return tableClassName.ToLowerInvariant() + "Path";
            }
        }

        [Serializable]
        public class TableInfo
        {
            [LabelText("表类名")]
            [ReadOnly]
            public string TableClassName;

            [LabelText("字段名")]
            [ReadOnly]
            public string FieldName;

            [LabelText("属性名")]
            [ReadOnly]
            public string PropertyName;

            [LabelText("AssetKey")]
            [ReadOnly]
            public string AssetKeyName;

            [LabelText("文件路径")]
            [ReadOnly]
            public string FilePath;
        }
    }
}

#endif
