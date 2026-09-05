#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// 根据 export_ngui_atlas_sprites.py 生成的 atlas_manifest.json，
/// 将一张 NGUI 原始图集切割为独立 Sprite PNG，并自动应用 Border / Padding。
/// </summary>
public sealed class NGUIAtlasSpriteImporterWindow : OdinEditorWindow
{
    private const string DefaultOutputFolder = "Assets";

    [Title("NGUI 图集切割与 Sprite 导入")]
    [InfoBox(
        "拖入原始图集 PNG 和对应 atlas_manifest.json。工具按照 JSON 的精确坐标切图、自动命名，并写入 Unity Sprite Border。\n" +
        "Unity 没有独立 Padding 字段；开启“还原透明 Padding”后会扩展透明画布，并把 Padding 合并到 Unity Border。")]

    [BoxGroup("输入")]
    [LabelText("原始 PNG / 图集 PNG")]
    [Required("请拖入原始 PNG 或图集 PNG")]
    [AssetsOnly]
    [PreviewField(90, ObjectFieldAlignment.Left)]
    public Texture2D SourceAtlas;

    [BoxGroup("输入")]
    [LabelText("图集 JSON")]
    [Required("请拖入对应的 atlas_manifest.json")]
    [AssetsOnly]
    public TextAsset AtlasManifestJson;

    [BoxGroup("输出")]
    [InfoBox("可从 Project 面板直接拖入文件夹，也可点击“选择目录”。只允许当前工程 Assets 下的目录。")]
    [HorizontalGroup("输出/FolderRow")]
    [LabelText("输出目录")]
    [AssetsOnly]
    [Required("请拖入或选择输出文件夹")]
    [OnValueChanged(nameof(OnOutputFolderChanged))]
    public DefaultAsset OutputFolder;

    [HorizontalGroup("输出/FolderRow", Width = 120)]
    [Button("选择目录", ButtonSizes.Medium)]
    private void SelectOutputFolder()
    {
        string currentAssetPath = GetSelectedOutputFolderPath();
        string initialAbsolutePath = IsAssetFolderPath(currentAssetPath)
            ? AssetPathToAbsolute(currentAssetPath)
            : Application.dataPath;

        string selectedAbsolutePath = EditorUtility.OpenFolderPanel("选择输出目录", initialAbsolutePath, string.Empty);
        if (string.IsNullOrWhiteSpace(selectedAbsolutePath))
        {
            return;
        }

        string selectedAssetPath = NormalizeAssetPath(FileUtil.GetProjectRelativePath(selectedAbsolutePath));
        if (!IsAssetFolderPath(selectedAssetPath))
        {
            EditorUtility.DisplayDialog("输出目录无效", "请选择当前 Unity 工程 Assets 下的文件夹。", "关闭");
            return;
        }

        AssetDatabase.Refresh();
        DefaultAsset folderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(selectedAssetPath);
        if (folderAsset == null || !AssetDatabase.IsValidFolder(selectedAssetPath))
        {
            EditorUtility.DisplayDialog("输出目录无效", $"Unity 无法识别该文件夹：\n{selectedAssetPath}", "关闭");
            return;
        }

        OutputFolder = folderAsset;
        outputFolderPath = selectedAssetPath;
        GUI.FocusControl(null);
        Repaint();
    }

    [BoxGroup("输出")]
    [ShowInInspector]
    [ReadOnly]
    [LabelText("Assets 路径")]
    private string OutputFolderPathText => GetSelectedOutputFolderPath();

    [SerializeField]
    [HideInInspector]
    private string outputFolderPath = DefaultOutputFolder;

    [BoxGroup("图集 JSON 副本")]
    [InfoBox(
        "复制整张原始图集 PNG，并按 JSON 的全部 Sprite 项导入为 Unity Multiple Sprite。" +
        "Border 写入 Unity 原生字段；Padding 写入每个 SpriteRect.customData。源 PNG 始终不会被修改。")]
    [LabelText("副本文件名")]
    [Tooltip("可不填，默认使用“原图名称_副本.png”。")]
    public string AtlasCopyName;

    [BoxGroup("图集 JSON 副本")]
    [Button("复制图集并按 JSON 设置 Multiple Sprites", ButtonSizes.Large)]
    [GUIColor(1f, 0.65f, 0.2f)]
    private void CopyAtlasAndApplyJson()
    {
        if (!TryParseManifest(true) ||
            !TryResolveOutputFolder(out string outputAssetFolder, out string outputAbsoluteFolder))
        {
            return;
        }

        string sourceAssetPath = AssetDatabase.GetAssetPath(SourceAtlas);
        string sourceAbsolutePath = AssetPathToAbsolute(sourceAssetPath);
        if (!File.Exists(sourceAbsolutePath) ||
            !string.Equals(Path.GetExtension(sourceAbsolutePath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("图集副本", $"源文件必须是工程内的 PNG：\n{sourceAssetPath}", "关闭");
            return;
        }

        try
        {
            ValidateAtlasSize(SourceAtlas);
            foreach (SpriteEntryData sprite in manifest.sprites)
            {
                ValidateSprite(sprite, SourceAtlas.width, SourceAtlas.height);
            }

            string fileName = BuildAtlasCopyFileName(AtlasCopyName, SourceAtlas.name);
            string outputAssetPath = CombineAssetPath(outputAssetFolder, fileName);
            if (string.Equals(outputAssetPath, sourceAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                fileName = Path.GetFileNameWithoutExtension(fileName) + "_副本.png";
                outputAssetPath = CombineAssetPath(outputAssetFolder, fileName);
            }

            string outputAbsolutePath = AssetPathToAbsolute(outputAssetPath);
            if (File.Exists(outputAbsolutePath) && !OverwriteExisting)
            {
                EditorUtility.DisplayDialog("图集副本", $"目标文件已存在且未开启覆盖：\n{outputAssetPath}", "关闭");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "创建 Multiple Sprite 图集副本",
                    $"原图：{sourceAssetPath}\n副本：{outputAssetPath}\n" +
                    $"Sprite：{manifest.sprites.Count:N0}\n\n原 PNG 不会被修改，是否继续？",
                    "创建副本",
                    "取消"))
            {
                return;
            }

            Directory.CreateDirectory(outputAbsoluteFolder);
            File.Copy(sourceAbsolutePath, outputAbsolutePath, true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ApplyAtlasMultipleImportSettings(outputAssetPath, sourceAssetPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            UnityEngine.Object outputObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(outputAssetPath);
            if (outputObject != null)
            {
                Selection.activeObject = outputObject;
                EditorGUIUtility.PingObject(outputObject);
            }

            Debug.Log(
                $"[NGUI Atlas Importer] Multiple Sprite 图集副本完成：{sourceAssetPath} -> {outputAssetPath}，" +
                $"Sprite={manifest.sprites.Count:N0}");
            EditorUtility.DisplayDialog(
                "图集副本完成",
                $"已复制原图并写入 {manifest.sprites.Count:N0} 个 Multiple Sprites：\n" +
                $"{outputAssetPath}\n\n源 PNG 未修改。",
                "确定");
        }
        catch (Exception exception)
        {
            Debug.LogError($"[NGUI Atlas Importer] 图集副本失败：{exception}");
            EditorUtility.DisplayDialog("图集副本失败", exception.Message, "关闭");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [BoxGroup("导入设置")]
    [LabelText("还原透明 Padding（独立 PNG）")]
    [Tooltip("仅影响“切割并自动导入”。图集副本保持原图像素不变，Padding 会保存到 SpriteRect.customData。")]
    public bool RestoreTransparentPadding = true;

    [BoxGroup("导入设置")]
    [LabelText("覆盖已有 PNG")]
    public bool OverwriteExisting = true;

    [BoxGroup("导入设置")]
    [LabelText("Pixels Per Unit")]
    [MinValue(0.01f)]
    public float PixelsPerUnit = 100f;

    [BoxGroup("导入设置")]
    [LabelText("过滤模式")]
    public FilterMode FilterMode = FilterMode.Bilinear;

    [BoxGroup("导入设置")]
    [LabelText("导入压缩")]
    public TextureImporterCompression Compression = TextureImporterCompression.Uncompressed;

    [BoxGroup("导入设置")]
    [LabelText("Alpha Is Transparency")]
    public bool AlphaIsTransparency = true;

    [BoxGroup("预览")]
    [ShowInInspector]
    [ReadOnly]
    [LabelText("图集尺寸")]
    private string AtlasSizeText => manifest == null || manifest.texture_size == null
        ? "未解析"
        : $"{manifest.texture_size.width} x {manifest.texture_size.height}";

    [BoxGroup("预览")]
    [ShowInInspector]
    [ReadOnly]
    [LabelText("Sprite 数量")]
    private int SpriteCount => manifest?.sprites?.Count ?? 0;

    [BoxGroup("预览")]
    [ShowInInspector]
    [ReadOnly]
    [LabelText("包含 Border")]
    private int BorderCount => manifest?.sprites?.Count(item => item.border != null && item.border.HasAnyValue()) ?? 0;

    [BoxGroup("预览")]
    [ShowInInspector]
    [ReadOnly]
    [LabelText("包含 Padding")]
    private int PaddingCount => manifest?.sprites?.Count(item => item.padding != null && item.padding.HasAnyValue()) ?? 0;

    [BoxGroup("预览")]
    [TableList(IsReadOnly = true, AlwaysExpanded = false, NumberOfItemsPerPage = 20)]
    [LabelText("前 200 项")]
    [SerializeField]
    private List<SpritePreviewItem> previewItems = new List<SpritePreviewItem>();

    private AtlasManifestData manifest;

    [MenuItem("Tools/ReDiv/NGUI 图集切割与 Sprite 导入")]
    private static void Open()
    {
        NGUIAtlasSpriteImporterWindow window = GetWindow<NGUIAtlasSpriteImporterWindow>();
        window.titleContent = new GUIContent("NGUI 图集切割");
        window.minSize = new Vector2(820, 620);
        window.Show();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        titleContent = new GUIContent("NGUI 图集切割");
        minSize = new Vector2(820, 620);
        RestoreOutputFolderReference();
    }

    [BoxGroup("操作")]
    [HorizontalGroup("操作/Buttons")]
    [Button("1. 解析并预览", ButtonSizes.Large)]
    [GUIColor(0.35f, 0.7f, 1f)]
    private void ParseAndPreview()
    {
        if (!TryParseManifest(true))
        {
            return;
        }

        previewItems = manifest.sprites
            .Take(200)
            .Select(CreatePreviewItem)
            .ToList();

        Debug.Log(
            $"[NGUI Atlas Importer] 解析完成：{manifest.source_texture_name}，" +
            $"Sprite {SpriteCount}，Border {BorderCount}，Padding {PaddingCount}");
    }

    [HorizontalGroup("操作/Buttons")]
    [Button("2. 切割并自动导入", ButtonSizes.Large)]
    [GUIColor(0.3f, 0.85f, 0.45f)]
    private void SliceAndImport()
    {
        if (!TryParseManifest(true) || !TryResolveOutputFolder(out string outputAssetFolder, out string outputAbsoluteFolder))
        {
            return;
        }

        string atlasAssetPath = AssetDatabase.GetAssetPath(SourceAtlas);
        string atlasAbsolutePath = AssetPathToAbsolute(atlasAssetPath);
        if (!File.Exists(atlasAbsolutePath))
        {
            EditorUtility.DisplayDialog("NGUI 图集切割", $"找不到原图文件：\n{atlasAbsolutePath}", "关闭");
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "开始切割",
                $"图集：{SourceAtlas.name}\nSprite：{manifest.sprites.Count:N0}\n" +
                $"输出：{outputAssetFolder}\n还原 Padding：{RestoreTransparentPadding}\n\n是否继续？",
                "开始",
                "取消"))
        {
            return;
        }

        Texture2D readableAtlas = null;
        var workItems = new List<ImportWorkItem>(manifest.sprites.Count);
        var errors = new List<string>();
        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int written = 0;
        int reused = 0;
        bool cancelled = false;

        try
        {
            readableAtlas = LoadReadablePng(atlasAbsolutePath);
            ValidateAtlasSize(readableAtlas);
            Color32[] atlasPixels = readableAtlas.GetPixels32();
            Directory.CreateDirectory(outputAbsoluteFolder);

            for (int index = 0; index < manifest.sprites.Count; index++)
            {
                SpriteEntryData sprite = manifest.sprites[index];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "切割 NGUI 图集",
                        sprite.name,
                        manifest.sprites.Count == 0 ? 1f : (float)index / manifest.sprites.Count))
                {
                    cancelled = true;
                    break;
                }

                try
                {
                    ValidateSprite(sprite, readableAtlas.width, readableAtlas.height);
                    string fileName = BuildUniqueFileName(sprite, usedFileNames);
                    string outputAssetPath = CombineAssetPath(outputAssetFolder, fileName);
                    string outputAbsolutePath = AssetPathToAbsolute(outputAssetPath);
                    ImportWorkItem item = CreateWorkItem(sprite, outputAssetPath, fileName);

                    bool dimensionsMatch = File.Exists(outputAbsolutePath) &&
                                           TryReadPngDimensions(outputAbsolutePath, out int existingWidth, out int existingHeight) &&
                                           existingWidth == item.outputWidth && existingHeight == item.outputHeight;

                    if (OverwriteExisting || !File.Exists(outputAbsolutePath) || !dimensionsMatch)
                    {
                        byte[] png = SliceSpriteToPng(readableAtlas, atlasPixels, sprite, item);
                        File.WriteAllBytes(outputAbsolutePath, png);
                        written++;
                    }
                    else
                    {
                        reused++;
                    }

                    workItems.Add(item);
                }
                catch (Exception exception)
                {
                    errors.Add($"{sprite.name}: {exception.Message}");
                }
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            if (!cancelled)
            {
                ApplyTextureImportSettings(workItems, errors, ref cancelled);
            }
        }
        catch (Exception exception)
        {
            errors.Add(exception.ToString());
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (readableAtlas != null)
            {
                DestroyImmediate(readableAtlas);
            }
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        string result =
            $"写入 {written:N0}，复用 {reused:N0}，应用设置 {workItems.Count:N0}，错误 {errors.Count:N0}" +
            (cancelled ? "（用户取消）" : string.Empty);

        Debug.Log($"[NGUI Atlas Importer] {result}\n输出目录：{outputAssetFolder}");
        if (errors.Count > 0)
        {
            Debug.LogError("[NGUI Atlas Importer] 错误：\n" + string.Join("\n", errors.Take(100)));
        }

        UnityEngine.Object outputFolderObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(outputAssetFolder);
        if (outputFolderObject != null)
        {
            Selection.activeObject = outputFolderObject;
            EditorGUIUtility.PingObject(outputFolderObject);
        }

        EditorUtility.DisplayDialog("NGUI 图集切割完成", result + "\n\n详细信息请查看 Console。", "确定");
    }

    private bool TryParseManifest(bool showDialog)
    {
        if (SourceAtlas == null || AtlasManifestJson == null)
        {
            if (showDialog)
            {
                EditorUtility.DisplayDialog("NGUI 图集切割", "请拖入原始图集 PNG 和对应的 atlas_manifest.json。", "关闭");
            }

            return false;
        }

        try
        {
            manifest = JsonUtility.FromJson<AtlasManifestData>(AtlasManifestJson.text);
            if (manifest == null || manifest.sprites == null || manifest.sprites.Count == 0)
            {
                throw new InvalidDataException("JSON 中没有 sprites 数据。");
            }

            if (manifest.sprite_count != 0 && manifest.sprite_count != manifest.sprites.Count)
            {
                throw new InvalidDataException(
                    $"sprite_count={manifest.sprite_count}，实际 sprites={manifest.sprites.Count}，数量不一致。");
            }

            return true;
        }
        catch (Exception exception)
        {
            manifest = null;
            previewItems.Clear();
            if (showDialog)
            {
                EditorUtility.DisplayDialog("JSON 解析失败", exception.Message, "关闭");
            }

            Debug.LogError($"[NGUI Atlas Importer] JSON 解析失败：{exception}");
            return false;
        }
    }

    private static string BuildAtlasCopyFileName(string requestedName, string sourceName)
    {
        string stem = string.IsNullOrWhiteSpace(requestedName)
            ? sourceName + "_副本"
            : Path.GetFileNameWithoutExtension(requestedName.Trim());

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            stem = stem.Replace(invalid, '_');
        }

        stem = stem.Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = sourceName + "_副本";
        }

        return stem + ".png";
    }

    private void ApplyAtlasMultipleImportSettings(string outputAssetPath, string sourceAssetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(outputAssetPath) as TextureImporter;
        if (importer == null)
        {
            throw new InvalidOperationException($"找不到 TextureImporter：{outputAssetPath}");
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = Mathf.Max(0.01f, PixelsPerUnit);
        importer.alphaIsTransparency = AlphaIsTransparency;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.sRGBTexture = true;
        importer.textureCompression = Compression;
        importer.maxTextureSize = Mathf.Clamp(
            Mathf.NextPowerOfTwo(Mathf.Max(SourceAtlas.width, SourceAtlas.height)),
            32,
            16384);
        var atlasMetadata = new AtlasMultipleImportMetadata
        {
            sourceAtlas = sourceAssetPath,
            sourceManifest = AssetDatabase.GetAssetPath(AtlasManifestJson),
            spriteCount = manifest.sprites.Count,
            paddingStorage = "SpriteRect.customData"
        };
        importer.userData = JsonUtility.ToJson(atlasMetadata);
        importer.SaveAndReimport();

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
        if (dataProvider == null)
        {
            throw new InvalidOperationException("无法取得 Unity Sprite Editor Data Provider。请确认已安装 2D Sprite 包。");
        }

        dataProvider.InitSpriteEditorDataProvider();
        Dictionary<string, GUID> existingIds = dataProvider.GetSpriteRects()
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.name))
            .GroupBy(item => item.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().spriteID, StringComparer.OrdinalIgnoreCase);

        int textureHeight = manifest.texture_size?.height ?? SourceAtlas.height;
        var spriteRects = new List<SpriteRect>(manifest.sprites.Count);
        int clampedBorderCount = 0;
        for (int index = 0; index < manifest.sprites.Count; index++)
        {
            SpriteEntryData sprite = manifest.sprites[index];
            EditorUtility.DisplayProgressBar(
                "设置 Multiple Sprites",
                sprite.name,
                manifest.sprites.Count == 0 ? 1f : (float)index / manifest.sprites.Count);

            RectData sourceRect = sprite.ngui_rect_top_left;
            EdgeData border = sprite.border ?? new EdgeData();
            EdgeData padding = sprite.padding ?? new EdgeData();
            Vector4 unityBorder = CreateUnityCompatibleBorder(
                border,
                sourceRect.width,
                sourceRect.height,
                out EdgeData appliedBorder,
                out bool borderWasClamped);
            if (borderWasClamped)
            {
                clampedBorderCount++;
            }

            GUID spriteId = existingIds.TryGetValue(sprite.name, out GUID existingId)
                ? existingId
                : GUID.Generate();
            spriteRects.Add(new SpriteRect
            {
                name = sprite.name,
                rect = new Rect(
                    sourceRect.x,
                    textureHeight - sourceRect.y - sourceRect.height,
                    sourceRect.width,
                    sourceRect.height),
                alignment = SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                border = unityBorder,
                spriteID = spriteId,
                customData = JsonUtility.ToJson(new AtlasSpriteCustomMetadata
                {
                    padding = CloneEdge(padding),
                    originalBorder = CloneEdge(border),
                    appliedUnityBorder = appliedBorder,
                    originalRectTopLeft = CloneRect(sourceRect),
                    logicalSize = new SizeData
                    {
                        width = sourceRect.width + padding.left + padding.right,
                        height = sourceRect.height + padding.top + padding.bottom
                    }
                })
            });
        }

        dataProvider.SetSpriteRects(spriteRects.ToArray());
        ISpriteNameFileIdDataProvider nameProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        if (nameProvider != null)
        {
            nameProvider.SetNameFileIdPairs(
                spriteRects.Select(item => new SpriteNameFileIdPair(item.name, item.spriteID)).ToArray());
        }

        dataProvider.Apply();
        atlasMetadata.clampedBorderCount = clampedBorderCount;
        importer.userData = JsonUtility.ToJson(atlasMetadata);
        importer.SaveAndReimport();
        if (clampedBorderCount > 0)
        {
            Debug.LogWarning(
                $"[NGUI Atlas Importer] {clampedBorderCount:N0} 个 NGUI Border 含负数或超出 Sprite Rect，" +
                "已钳制/按比例压缩为 Unity 可接受的 Border；原始值保存在 SpriteRect.customData。");
        }
    }

    private static Vector4 CreateUnityCompatibleBorder(
        EdgeData source,
        int width,
        int height,
        out EdgeData applied,
        out bool wasClamped)
    {
        ScaleBorderPair(source.left, source.right, width, out int left, out int right);
        ScaleBorderPair(source.bottom, source.top, height, out int bottom, out int top);
        applied = new EdgeData { left = left, right = right, top = top, bottom = bottom };
        wasClamped = left != source.left || right != source.right || top != source.top || bottom != source.bottom;
        return new Vector4(left, bottom, right, top);
    }

    private static void ScaleBorderPair(int first, int second, int limit, out int appliedFirst, out int appliedSecond)
    {
        first = Mathf.Max(0, first);
        second = Mathf.Max(0, second);
        int total = first + second;
        if (total <= limit)
        {
            appliedFirst = first;
            appliedSecond = second;
            return;
        }

        float scale = total <= 0 ? 0f : (float)limit / total;
        appliedFirst = Mathf.Clamp(Mathf.RoundToInt(first * scale), 0, limit);
        appliedSecond = Mathf.Clamp(limit - appliedFirst, 0, limit);
    }

    private bool TryResolveOutputFolder(out string assetFolder, out string absoluteFolder)
    {
        assetFolder = GetSelectedOutputFolderPath();
        absoluteFolder = string.Empty;

        if (!IsAssetFolderPath(assetFolder) || !AssetDatabase.IsValidFolder(assetFolder))
        {
            EditorUtility.DisplayDialog("输出目录无效", "请拖入或选择当前工程 Assets 下的有效文件夹。", "关闭");
            return false;
        }

        absoluteFolder = AssetPathToAbsolute(assetFolder);
        string assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string resolved = Path.GetFullPath(absoluteFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("输出目录无效", "解析后的输出目录超出了当前工程 Assets。", "关闭");
            return false;
        }

        return true;
    }

    private void OnOutputFolderChanged()
    {
        if (OutputFolder == null)
        {
            outputFolderPath = string.Empty;
            return;
        }

        string assetPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(OutputFolder));
        if (!IsAssetFolderPath(assetPath) || !AssetDatabase.IsValidFolder(assetPath))
        {
            OutputFolder = null;
            outputFolderPath = string.Empty;
            EditorUtility.DisplayDialog("输出目录无效", "拖入的对象不是当前工程 Assets 下的文件夹。", "关闭");
            return;
        }

        outputFolderPath = assetPath;
    }

    private void RestoreOutputFolderReference()
    {
        string candidate = NormalizeAssetPath(outputFolderPath);
        if (!IsAssetFolderPath(candidate) || !AssetDatabase.IsValidFolder(candidate))
        {
            candidate = DefaultOutputFolder;
        }

        OutputFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(candidate);
        outputFolderPath = OutputFolder == null ? string.Empty : candidate;
    }

    private string GetSelectedOutputFolderPath()
    {
        if (OutputFolder != null)
        {
            string objectPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(OutputFolder));
            if (IsAssetFolderPath(objectPath) && AssetDatabase.IsValidFolder(objectPath))
            {
                outputFolderPath = objectPath;
                return objectPath;
            }
        }

        return NormalizeAssetPath(outputFolderPath);
    }

    private static bool IsAssetFolderPath(string assetPath)
    {
        string normalized = NormalizeAssetPath(assetPath);
        return normalized == "Assets" || normalized.StartsWith("Assets/", StringComparison.Ordinal);
    }

    private void ValidateAtlasSize(Texture2D atlas)
    {
        if (manifest.texture_size == null)
        {
            return;
        }

        if (atlas.width != manifest.texture_size.width || atlas.height != manifest.texture_size.height)
        {
            throw new InvalidDataException(
                $"原图尺寸 {atlas.width}x{atlas.height} 与 JSON 记录的 " +
                $"{manifest.texture_size.width}x{manifest.texture_size.height} 不一致。请检查 PNG 和 JSON 是否配套。");
        }
    }

    private static void ValidateSprite(SpriteEntryData sprite, int atlasWidth, int atlasHeight)
    {
        if (sprite == null || sprite.ngui_rect_top_left == null)
        {
            throw new InvalidDataException("缺少 ngui_rect_top_left。");
        }

        RectData rect = sprite.ngui_rect_top_left;
        if (rect.width <= 0 || rect.height <= 0 || rect.x < 0 || rect.y < 0 ||
            rect.x + rect.width > atlasWidth || rect.y + rect.height > atlasHeight)
        {
            throw new InvalidDataException(
                $"矩形越界：({rect.x}, {rect.y}, {rect.width}, {rect.height}) / {atlasWidth}x{atlasHeight}");
        }

        EdgeData padding = sprite.padding ?? new EdgeData();
        if (padding.HasNegativeValue())
        {
            throw new InvalidDataException("Padding 包含负数。");
        }
    }

    private ImportWorkItem CreateWorkItem(SpriteEntryData sprite, string assetPath, string fileName)
    {
        RectData rect = sprite.ngui_rect_top_left;
        EdgeData border = sprite.border ?? new EdgeData();
        EdgeData padding = sprite.padding ?? new EdgeData();

        int outputWidth = rect.width + (RestoreTransparentPadding ? padding.left + padding.right : 0);
        int outputHeight = rect.height + (RestoreTransparentPadding ? padding.top + padding.bottom : 0);
        var requestedBorder = new EdgeData
        {
            left = border.left + (RestoreTransparentPadding ? padding.left : 0),
            right = border.right + (RestoreTransparentPadding ? padding.right : 0),
            top = border.top + (RestoreTransparentPadding ? padding.top : 0),
            bottom = border.bottom + (RestoreTransparentPadding ? padding.bottom : 0)
        };
        CreateUnityCompatibleBorder(
            requestedBorder,
            outputWidth,
            outputHeight,
            out EdgeData appliedBorder,
            out _);

        return new ImportWorkItem
        {
            sprite = sprite,
            assetPath = assetPath,
            fileName = fileName,
            outputWidth = outputWidth,
            outputHeight = outputHeight,
            appliedBorder = appliedBorder
        };
    }

    private byte[] SliceSpriteToPng(
        Texture2D atlas,
        Color32[] atlasPixels,
        SpriteEntryData sprite,
        ImportWorkItem item)
    {
        RectData rect = sprite.ngui_rect_top_left;
        EdgeData padding = sprite.padding ?? new EdgeData();
        int destinationX = RestoreTransparentPadding ? padding.left : 0;
        int destinationY = RestoreTransparentPadding ? padding.bottom : 0;
        int sourceBottomY = atlas.height - rect.y - rect.height;

        var outputPixels = new Color32[item.outputWidth * item.outputHeight];
        for (int y = 0; y < rect.height; y++)
        {
            int sourceIndex = (sourceBottomY + y) * atlas.width + rect.x;
            int destinationIndex = (destinationY + y) * item.outputWidth + destinationX;
            Array.Copy(atlasPixels, sourceIndex, outputPixels, destinationIndex, rect.width);
        }

        var output = new Texture2D(item.outputWidth, item.outputHeight, TextureFormat.RGBA32, false, false);
        try
        {
            output.SetPixels32(outputPixels);
            output.Apply(false, false);
            return output.EncodeToPNG();
        }
        finally
        {
            DestroyImmediate(output);
        }
    }

    private void ApplyTextureImportSettings(
        List<ImportWorkItem> workItems,
        List<string> errors,
        ref bool cancelled)
    {
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int index = 0; index < workItems.Count; index++)
            {
                ImportWorkItem item = workItems[index];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "应用 Sprite Border / Padding",
                        item.sprite.name,
                        workItems.Count == 0 ? 1f : (float)index / workItems.Count))
                {
                    cancelled = true;
                    break;
                }

                try
                {
                    TextureImporter importer = AssetImporter.GetAtPath(item.assetPath) as TextureImporter;
                    if (importer == null)
                    {
                        errors.Add($"找不到 TextureImporter：{item.assetPath}");
                        continue;
                    }

                    importer.textureType = TextureImporterType.Sprite;
                    importer.textureShape = TextureImporterShape.Texture2D;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = Mathf.Max(0.01f, PixelsPerUnit);
                    importer.spriteBorder = new Vector4(
                        item.appliedBorder.left,
                        item.appliedBorder.bottom,
                        item.appliedBorder.right,
                        item.appliedBorder.top);
                    importer.alphaIsTransparency = AlphaIsTransparency;
                    importer.mipmapEnabled = false;
                    importer.isReadable = false;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.sRGBTexture = true;
                    importer.textureCompression = Compression;
                    importer.maxTextureSize = Mathf.Clamp(
                        Mathf.NextPowerOfTwo(Mathf.Max(item.outputWidth, item.outputHeight)),
                        32,
                        16384);
                    importer.userData = JsonUtility.ToJson(CreateMetadata(item));

                    EditorUtility.SetDirty(importer);
                    AssetDatabase.WriteImportSettingsIfDirty(item.assetPath);
                }
                catch (Exception exception)
                {
                    errors.Add($"{item.sprite.name}: {exception.Message}");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }

    private ImportedSpriteMetadata CreateMetadata(ImportWorkItem item)
    {
        return new ImportedSpriteMetadata
        {
            spriteName = item.sprite.name,
            sourceAtlas = AssetDatabase.GetAssetPath(SourceAtlas),
            sourceManifest = AssetDatabase.GetAssetPath(AtlasManifestJson),
            transparentPaddingRestored = RestoreTransparentPadding,
            originalRectTopLeft = CloneRect(item.sprite.ngui_rect_top_left),
            originalBorder = CloneEdge(item.sprite.border ?? new EdgeData()),
            originalPadding = CloneEdge(item.sprite.padding ?? new EdgeData()),
            appliedUnityBorder = CloneEdge(item.appliedBorder),
            outputSize = new SizeData { width = item.outputWidth, height = item.outputHeight }
        };
    }

    private SpritePreviewItem CreatePreviewItem(SpriteEntryData sprite)
    {
        EdgeData border = sprite.border ?? new EdgeData();
        EdgeData padding = sprite.padding ?? new EdgeData();
        RectData rect = sprite.ngui_rect_top_left ?? new RectData();
        int width = rect.width + (RestoreTransparentPadding ? padding.left + padding.right : 0);
        int height = rect.height + (RestoreTransparentPadding ? padding.top + padding.bottom : 0);

        return new SpritePreviewItem
        {
            Name = sprite.name,
            Rect = $"{rect.x},{rect.y}  {rect.width}x{rect.height}",
            Border = $"L{border.left} B{border.bottom} R{border.right} T{border.top}",
            Padding = $"L{padding.left} B{padding.bottom} R{padding.right} T{padding.top}",
            OutputSize = $"{width}x{height}"
        };
    }

    private static Texture2D LoadReadablePng(string absolutePath)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(absolutePath), false))
        {
            DestroyImmediate(texture);
            throw new InvalidDataException($"Unity 无法解码 PNG：{absolutePath}");
        }

        return texture;
    }

    private static string BuildUniqueFileName(SpriteEntryData sprite, HashSet<string> used)
    {
        string original = string.IsNullOrWhiteSpace(sprite.file) ? sprite.name + ".png" : sprite.file;
        string stem = Path.GetFileNameWithoutExtension(original);
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            stem = stem.Replace(invalid, '_');
        }

        stem = stem.Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "unnamed_sprite";
        }

        string fileName = stem + ".png";
        if (!used.Add(fileName))
        {
            throw new InvalidDataException($"存在重复的 Sprite 文件名：{fileName}");
        }

        return fileName;
    }

    private static bool TryReadPngDimensions(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            using (FileStream stream = File.OpenRead(path))
            {
                var header = new byte[24];
                if (stream.Read(header, 0, header.Length) != header.Length ||
                    header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47)
                {
                    return false;
                }

                width = ReadBigEndianInt32(header, 16);
                height = ReadBigEndianInt32(header, 20);
                return width > 0 && height > 0;
            }
        }
        catch
        {
            return false;
        }
    }

    private static int ReadBigEndianInt32(byte[] data, int offset)
    {
        return (data[offset] << 24) |
               (data[offset + 1] << 16) |
               (data[offset + 2] << 8) |
               data[offset + 3];
    }

    private static string AssetPathToAbsolute(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.GetFullPath(Path.Combine(projectRoot, NormalizeAssetPath(assetPath)));
    }

    private static string NormalizeAssetPath(string path)
    {
        return (path ?? string.Empty).Replace('\\', '/').Trim().TrimEnd('/');
    }

    private static string CombineAssetPath(string folder, string fileName)
    {
        return NormalizeAssetPath(folder) + "/" + fileName.Replace('\\', '/').TrimStart('/');
    }

    private static EdgeData CloneEdge(EdgeData source)
    {
        return new EdgeData
        {
            left = source.left,
            right = source.right,
            top = source.top,
            bottom = source.bottom
        };
    }

    private static RectData CloneRect(RectData source)
    {
        return new RectData
        {
            x = source.x,
            y = source.y,
            width = source.width,
            height = source.height
        };
    }

    [Serializable]
    private sealed class AtlasManifestData
    {
        public string tool;
        public string tool_version;
        public string source_bundle;
        public string source_texture_name;
        public string classified_atlas;
        public int sprite_count;
        public SizeData texture_size;
        public List<SpriteEntryData> sprites;
    }

    [Serializable]
    private sealed class SpriteEntryData
    {
        public string name;
        public string file;
        public RectData ngui_rect_top_left;
        public EdgeData border;
        public EdgeData padding;
    }

    [Serializable]
    private sealed class RectData
    {
        public int x;
        public int y;
        public int width;
        public int height;
    }

    [Serializable]
    private sealed class EdgeData
    {
        public int left;
        public int right;
        public int top;
        public int bottom;

        public bool HasAnyValue()
        {
            return left != 0 || right != 0 || top != 0 || bottom != 0;
        }

        public bool HasNegativeValue()
        {
            return left < 0 || right < 0 || top < 0 || bottom < 0;
        }
    }

    [Serializable]
    private sealed class SizeData
    {
        public int width;
        public int height;
    }

    [Serializable]
    private sealed class ImportWorkItem
    {
        public SpriteEntryData sprite;
        public string assetPath;
        public string fileName;
        public int outputWidth;
        public int outputHeight;
        public EdgeData appliedBorder;
    }

    [Serializable]
    private sealed class ImportedSpriteMetadata
    {
        public string schema = "Rediv.NGUI.SpriteMetadata/v1";
        public string spriteName;
        public string sourceAtlas;
        public string sourceManifest;
        public bool transparentPaddingRestored;
        public RectData originalRectTopLeft;
        public EdgeData originalBorder;
        public EdgeData originalPadding;
        public EdgeData appliedUnityBorder;
        public SizeData outputSize;
    }

    [Serializable]
    private sealed class AtlasMultipleImportMetadata
    {
        public string schema = "Rediv.NGUI.AtlasMultipleMetadata/v1";
        public string sourceAtlas;
        public string sourceManifest;
        public int spriteCount;
        public string paddingStorage;
        public int clampedBorderCount;
    }

    [Serializable]
    private sealed class AtlasSpriteCustomMetadata
    {
        public string schema = "Rediv.NGUI.AtlasSpriteCustomData/v1";
        public EdgeData padding;
        public EdgeData originalBorder;
        public EdgeData appliedUnityBorder;
        public RectData originalRectTopLeft;
        public SizeData logicalSize;
    }

    [Serializable]
    private sealed class SpritePreviewItem
    {
        [TableColumnWidth(220)]
        [ReadOnly]
        [LabelText("名称")]
        public string Name;

        [TableColumnWidth(160)]
        [ReadOnly]
        [LabelText("原图矩形")]
        public string Rect;

        [TableColumnWidth(170)]
        [ReadOnly]
        [LabelText("Border")]
        public string Border;

        [TableColumnWidth(170)]
        [ReadOnly]
        [LabelText("Padding")]
        public string Padding;

        [TableColumnWidth(100)]
        [ReadOnly]
        [LabelText("输出尺寸")]
        public string OutputSize;
    }
}

#endif
