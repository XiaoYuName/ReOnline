using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class ReDivVariantCardMaterialRestorerWindow : EditorWindow
{
    private const string ShaderAssetPath = "Assets/Shader/ReDiv/VariantCard.shader";
    private const string DefaultOutputAssetPath = "Assets/Shader/ReDiv/Restored";
    private const long OriginalVariantCardShaderPathId = 8273635072764025099L;
    private const string AnimationTextureRelativePath = "场景与背景/背景/bg/animationtexture_bundle";
    private const string AnimationTextureMapFileName = "animationtexture_asset_map.json";
    private const string Default105831Source =
        @"D:\AssetsStudio\Rediv\CN_分类完成\角色\佩可莉姆_105801\立绘\bg\still_unit_bundleroot\still_unit_105831";

    [SerializeField] private string sourceDirectory = string.Empty;
    [SerializeField] private string animationTextureDirectory = string.Empty;
    [SerializeField] private string animationTextureMapPath = string.Empty;
    [SerializeField] private string outputAssetDirectory = DefaultOutputAssetPath;
    [SerializeField] private Shader variantCardShader;
    [SerializeField] private bool createPreviewPrefab = true;

    private Vector2 scrollPosition;
    private List<MaterialAnalysis> analyzedMaterials = new();
    private string analysisMessage = string.Empty;
    private MessageType analysisMessageType = MessageType.Info;

    [MenuItem("Tools/ReDiv/VariantCard/一键还原材质")]
    public static void Open()
    {
        GetWindow<ReDivVariantCardMaterialRestorerWindow>("VariantCard 还原");
    }

    private void OnEnable()
    {
        if (variantCardShader == null)
            variantCardShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderAssetPath);

        if (string.IsNullOrWhiteSpace(sourceDirectory) && Directory.Exists(Default105831Source))
            sourceDirectory = Default105831Source;

        AutoDetectSharedData(overwriteExisting: false);
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.HelpBox(
            "本窗口用于 still_unit_xxxxxx 或单个 bg_xxxxxx 的 VariantCard 材质还原。bg_10001 这类多背景战斗场景组请使用 Tools/ReDiv/战斗背景/还原 SpriteRenderer 场景。源文件不会被修改。",
            MessageType.Info);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("输入", EditorStyles.boldLabel);
        DrawExternalPathField("VariantCard 素材目录", ref sourceDirectory, selectFolder: true);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(EditorGUIUtility.labelWidth);
        if (GUILayout.Button("自动定位通用纹理和 PathID 映射"))
        {
            AutoDetectSharedData(overwriteExisting: true);
            Analyze();
        }
        EditorGUILayout.EndHorizontal();

        DrawExternalPathField("通用效果纹理目录", ref animationTextureDirectory, selectFolder: true);
        DrawExternalPathField("PathID 映射 JSON", ref animationTextureMapPath, selectFolder: false);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("输出", EditorStyles.boldLabel);
        DrawAssetsFolderField("输出目录", ref outputAssetDirectory);
        variantCardShader = (Shader)EditorGUILayout.ObjectField("VariantCard Shader", variantCardShader, typeof(Shader), false);
        createPreviewPrefab = EditorGUILayout.Toggle("生成 RawImage 预览 Prefab", createPreviewPrefab);

        EditorGUILayout.Space(8f);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("解析并检查", GUILayout.Height(30f)))
            Analyze();
        if (GUILayout.Button("一键还原", GUILayout.Height(30f)))
            Restore();
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(analysisMessage))
            EditorGUILayout.HelpBox(analysisMessage, analysisMessageType);

        DrawAnalysis();
        EditorGUILayout.EndScrollView();
    }

    private void DrawAnalysis()
    {
        if (analyzedMaterials.Count == 0)
            return;

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("解析结果", EditorStyles.boldLabel);
        if (analyzedMaterials.Count > 1)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Material 数", analyzedMaterials.Count);
                EditorGUILayout.IntField("纹理槽总数", analyzedMaterials.Sum(item => item.Textures.Count));
                EditorGUILayout.IntField("未匹配纹理槽", analyzedMaterials.Sum(item => item.Textures.Count(texture => !texture.IsResolved)));
                EditorGUILayout.IntField("找到 Front", analyzedMaterials.Count(item => !string.IsNullOrEmpty(item.ForegroundPath)));
            }

            foreach (MaterialAnalysis analysis in analyzedMaterials)
            {
                int missing = analysis.Textures.Count(texture => !texture.IsResolved);
                string front = string.IsNullOrEmpty(analysis.ForegroundPath)
                    ? "无 Front（可选）"
                    : Path.GetFileName(analysis.ForegroundPath);
                string detail = missing == 0
                    ? $"{analysis.Snapshot.Name}  |  纹理 {analysis.Textures.Count}/{analysis.Textures.Count}  |  {front}"
                    : $"{analysis.Snapshot.Name}  |  缺少 {missing}/{analysis.Textures.Count} 个纹理  |  {front}";
                EditorGUILayout.HelpBox(detail, missing == 0 ? MessageType.Info : MessageType.Error);
            }
            return;
        }

        MaterialAnalysis single = analyzedMaterials[0];
        MaterialSnapshot analyzedMaterial = single.Snapshot;
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextField("Material", analyzedMaterial.Name);
            EditorGUILayout.IntField("纹理槽", analyzedMaterial.Textures.Count);
            EditorGUILayout.IntField("Float", analyzedMaterial.Floats.Count);
            EditorGUILayout.IntField("Color", analyzedMaterial.Colors.Count);
            EditorGUILayout.IntField("Render Queue", analyzedMaterial.CustomRenderQueue);
            EditorGUILayout.TextField("有效 Keywords", string.Join(", ", analyzedMaterial.ValidKeywords));
            EditorGUILayout.LongField("原 Shader PathID", analyzedMaterial.ShaderPathId);
        }

        foreach (TextureResolution texture in single.Textures)
        {
            MessageType type = texture.IsResolved ? MessageType.Info : MessageType.Error;
            string detail = texture.IsResolved
                ? $"{texture.PropertyName}  →  {Path.GetFileName(texture.SourcePath)}\nScale {texture.Scale}  Offset {texture.Offset}"
                : $"{texture.PropertyName}  →  未找到（PathID {texture.PathId}）";
            EditorGUILayout.HelpBox(detail, type);
        }

        if (!string.IsNullOrEmpty(single.ForegroundPath))
            EditorGUILayout.HelpBox($"Front 叠加层  →  {Path.GetFileName(single.ForegroundPath)}", MessageType.Info);
    }

    private void Analyze()
    {
        analyzedMaterials.Clear();

        try
        {
            ValidateSourceDirectory();
            AutoDetectSharedData(overwriteExisting: false);

            Dictionary<long, string> textureMap = LoadTextureMap(animationTextureMapPath);
            foreach (string materialBinary in FindVariantCardMaterialBinaries(sourceDirectory))
            {
                MaterialSnapshot snapshot = MaterialBinaryReader.Read(materialBinary);
                analyzedMaterials.Add(new MaterialAnalysis(
                    materialBinary,
                    snapshot,
                    ResolveTextures(snapshot, sourceDirectory, animationTextureDirectory, textureMap),
                    ResolveForegroundTexture(snapshot.Name, sourceDirectory)));
            }
            analyzedMaterials = analyzedMaterials
                .OrderBy(item => item.AssetId, StringComparer.Ordinal)
                .ThenBy(item => item.Snapshot.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int missing = analyzedMaterials.Sum(item => item.Textures.Count(texture => !texture.IsResolved));
            int textureCount = analyzedMaterials.Sum(item => item.Textures.Count);
            analysisMessage = missing == 0
                ? analyzedMaterials.Count == 1
                    ? $"解析成功：{analyzedMaterials[0].Snapshot.Name}，全部 {textureCount} 个纹理槽均已精确匹配。"
                    : $"场景组解析成功：{analyzedMaterials.Count} 个 VariantCard 材质，全部 {textureCount} 个纹理槽均已精确匹配。"
                : $"参数解析成功，但有 {missing} 个纹理槽未匹配。请检查通用纹理目录和 PathID 映射 JSON。";
            analysisMessageType = missing == 0 ? MessageType.Info : MessageType.Warning;
        }
        catch (Exception exception)
        {
            analysisMessage = exception.Message;
            analysisMessageType = MessageType.Error;
            Debug.LogException(exception);
        }

        Repaint();
    }

    private void Restore()
    {
        Analyze();
        if (analyzedMaterials.Count == 0
            || analyzedMaterials.Any(item => item.Textures.Any(texture => !texture.IsResolved)))
            return;
        if (analyzedMaterials.Count > 1)
        {
            analysisMessage =
                $"检测到 {analyzedMaterials.Count} 个战斗背景材质。请改用 Tools/ReDiv/战斗背景/还原 SpriteRenderer 场景。";
            analysisMessageType = MessageType.Warning;
            return;
        }

        try
        {
            ValidateOutputAssetDirectory();
            if (variantCardShader == null)
                throw new InvalidOperationException($"未指定 VariantCard Shader。默认位置：{ShaderAssetPath}");

            string sourceName = new DirectoryInfo(sourceDirectory).Name;
            string assetRoot = CombineAssetPath(outputAssetDirectory, SanitizeFileName(sourceName));
            RestoredEntry restored = RestoreSingleMaterial(assetRoot, analyzedMaterials[0]);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = restored.Material;
            analysisMessage = createPreviewPrefab
                ? $"还原完成：{restored.MaterialAssetPath}\n预览：{restored.PrefabAssetPath}"
                : $"还原完成：{restored.MaterialAssetPath}";
            analysisMessageType = MessageType.Info;
            Debug.Log($"[ReDiv VariantCard] {analysisMessage}");
        }
        catch (Exception exception)
        {
            analysisMessage = exception.Message;
            analysisMessageType = MessageType.Error;
            Debug.LogException(exception);
        }
    }

    private RestoredEntry RestoreSingleMaterial(string assetRoot, MaterialAnalysis analysis)
    {
        string textureAssetRoot = CombineAssetPath(assetRoot, "Textures");
        EnsureAssetFolder(textureAssetRoot);

        var cache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Texture2D> importedTextures = ImportMaterialTextures(
            analysis,
            textureAssetRoot,
            textureAssetRoot,
            cache);
        Texture2D foreground = ImportExternalTexture(
            analysis.ForegroundPath,
            textureAssetRoot,
            "_FrontOverlay",
            cache);

        string materialAssetPath = CombineAssetPath(assetRoot, analysis.Snapshot.Name + ".mat");
        Material material = CreateOrLoadMaterial(materialAssetPath, variantCardShader);
        ApplySnapshot(material, analysis.Snapshot, importedTextures);

        string prefabAssetPath = string.Empty;
        if (createPreviewPrefab)
        {
            string identifier = analysis.Identifier;
            prefabAssetPath = CombineAssetPath(assetRoot, identifier + "_Preview.prefab");
            CreatePreviewPrefab(
                prefabAssetPath,
                identifier,
                material,
                importedTextures["_MainTex"],
                foreground,
                ReadOffsetY(sourceDirectory));
        }

        return new RestoredEntry(
            analysis,
            material,
            importedTextures["_MainTex"],
            foreground,
            materialAssetPath,
            prefabAssetPath);
    }

    internal static BattleSpriteRestoreResult RestoreBattleSpriteAssets(
        string sourceDirectory,
        string animationTextureDirectory,
        string animationTextureMapPath,
        string outputAssetDirectory,
        Shader shader,
        float pixelsPerUnit)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"战斗背景场景组目录不存在：{sourceDirectory}");
        if (string.IsNullOrWhiteSpace(animationTextureDirectory) || !Directory.Exists(animationTextureDirectory))
            throw new DirectoryNotFoundException($"通用效果纹理目录不存在：{animationTextureDirectory}");
        if (shader == null)
            throw new InvalidOperationException($"未指定 VariantCard Shader。默认位置：{ShaderAssetPath}");
        if (pixelsPerUnit <= 0f)
            throw new ArgumentOutOfRangeException(nameof(pixelsPerUnit), "Pixels Per Unit 必须大于 0。");

        outputAssetDirectory = NormalizeAssetPath(outputAssetDirectory);
        if (outputAssetDirectory != "Assets"
            && !outputAssetDirectory.StartsWith("Assets/", StringComparison.Ordinal))
            throw new InvalidOperationException("输出目录必须位于当前 Unity 工程的 Assets 下。");

        Dictionary<long, string> textureMap = LoadTextureMap(animationTextureMapPath);
        List<MaterialAnalysis> analyses = FindVariantCardMaterialBinaries(sourceDirectory)
            .Select(path =>
            {
                MaterialSnapshot snapshot = MaterialBinaryReader.Read(path);
                return new MaterialAnalysis(
                    path,
                    snapshot,
                    ResolveTextures(snapshot, sourceDirectory, animationTextureDirectory, textureMap),
                    ResolveForegroundTexture(snapshot.Name, sourceDirectory));
            })
            .OrderBy(item => item.AssetId, StringComparer.Ordinal)
            .ThenBy(item => item.Snapshot.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<string> missing = analyses
            .SelectMany(analysis => analysis.Textures
                .Where(texture => !texture.IsResolved)
                .Select(texture => $"{analysis.Identifier}:{texture.PropertyName} (PathID {texture.PathId})"))
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"有 {missing.Count} 个纹理槽未匹配，不能生成场景：\n{string.Join("\n", missing)}");

        string groupName = new DirectoryInfo(sourceDirectory).Name;
        string assetRoot = CombineAssetPath(outputAssetDirectory, SanitizeFileName(groupName));
        string materialRoot = CombineAssetPath(assetRoot, "Materials");
        string commonTextureRoot = CombineAssetPath(assetRoot, "Textures", "Common");
        string sceneTextureRoot = CombineAssetPath(assetRoot, "Textures", "Scenes");
        EnsureAssetFolder(materialRoot);
        EnsureAssetFolder(commonTextureRoot);
        EnsureAssetFolder(sceneTextureRoot);

        var cache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        var assets = new List<BattleSpriteAsset>();
        foreach (MaterialAnalysis analysis in analyses)
        {
            var importedTextures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            foreach (TextureResolution resolution in analysis.Textures)
            {
                if (string.IsNullOrEmpty(resolution.SourcePath))
                    continue;

                bool isCommon = IsSameOrChildPath(resolution.SourcePath, animationTextureDirectory);
                bool isMainSprite = resolution.PropertyName == "_MainTex" && !isCommon;
                importedTextures[resolution.PropertyName] = ImportExternalTexture(
                    resolution.SourcePath,
                    isCommon ? commonTextureRoot : sceneTextureRoot,
                    resolution.PropertyName,
                    cache,
                    isMainSprite,
                    pixelsPerUnit);
            }

            Texture2D foregroundTexture = ImportExternalTexture(
                analysis.ForegroundPath,
                sceneTextureRoot,
                "_FrontOverlay",
                cache,
                importAsSprite: true,
                pixelsPerUnit);

            string materialAssetPath = CombineAssetPath(materialRoot, analysis.Snapshot.Name + ".mat");
            Material material = CreateOrLoadMaterial(materialAssetPath, shader);
            ApplySnapshot(material, analysis.Snapshot, importedTextures);

            if (!importedTextures.TryGetValue("_MainTex", out Texture2D mainTexture))
                throw new InvalidOperationException($"{analysis.Identifier} 没有可用的 _MainTex。");

            string mainTextureAssetPath = AssetDatabase.GetAssetPath(mainTexture);
            Sprite mainSprite = AssetDatabase.LoadAssetAtPath<Sprite>(mainTextureAssetPath);
            if (mainSprite == null)
                throw new InvalidOperationException($"主背景没有按 Sprite 导入：{mainTextureAssetPath}");

            Sprite foregroundSprite = foregroundTexture == null
                ? null
                : AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(foregroundTexture));
            assets.Add(new BattleSpriteAsset(
                analysis.AssetId,
                analysis.Identifier,
                material,
                mainSprite,
                foregroundSprite));
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return new BattleSpriteRestoreResult(assetRoot, assets);
    }

    private List<RestoredEntry> RestoreMaterialGroup(string assetRoot)
    {
        string materialRoot = CombineAssetPath(assetRoot, "Materials");
        string prefabRoot = CombineAssetPath(assetRoot, "Prefabs");
        string commonTextureRoot = CombineAssetPath(assetRoot, "Textures", "Common");
        string sceneTextureRoot = CombineAssetPath(assetRoot, "Textures", "Scenes");
        EnsureAssetFolder(materialRoot);
        EnsureAssetFolder(commonTextureRoot);
        EnsureAssetFolder(sceneTextureRoot);
        if (createPreviewPrefab)
            EnsureAssetFolder(prefabRoot);

        var cache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        var restored = new List<RestoredEntry>();
        foreach (MaterialAnalysis analysis in analyzedMaterials)
        {
            Dictionary<string, Texture2D> importedTextures = ImportMaterialTextures(
                analysis,
                sceneTextureRoot,
                commonTextureRoot,
                cache);
            Texture2D foreground = ImportExternalTexture(
                analysis.ForegroundPath,
                sceneTextureRoot,
                "_FrontOverlay",
                cache);

            string materialAssetPath = CombineAssetPath(materialRoot, analysis.Snapshot.Name + ".mat");
            Material material = CreateOrLoadMaterial(materialAssetPath, variantCardShader);
            ApplySnapshot(material, analysis.Snapshot, importedTextures);

            string prefabAssetPath = string.Empty;
            if (createPreviewPrefab)
            {
                prefabAssetPath = CombineAssetPath(prefabRoot, analysis.Identifier + "_Preview.prefab");
                CreatePreviewPrefab(
                    prefabAssetPath,
                    analysis.Identifier,
                    material,
                    importedTextures["_MainTex"],
                    foreground,
                    ReadOffsetY(sourceDirectory));
            }

            restored.Add(new RestoredEntry(
                analysis,
                material,
                importedTextures["_MainTex"],
                foreground,
                materialAssetPath,
                prefabAssetPath));
        }
        return restored;
    }

    private Dictionary<string, Texture2D> ImportMaterialTextures(
        MaterialAnalysis analysis,
        string localTextureRoot,
        string commonTextureRoot,
        IDictionary<string, Texture2D> cache)
    {
        var imported = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        foreach (TextureResolution resolution in analysis.Textures)
        {
            if (string.IsNullOrEmpty(resolution.SourcePath))
                continue;

            bool isCommon = IsSameOrChildPath(resolution.SourcePath, animationTextureDirectory);
            Texture2D texture = ImportExternalTexture(
                resolution.SourcePath,
                isCommon ? commonTextureRoot : localTextureRoot,
                resolution.PropertyName,
                cache);
            imported[resolution.PropertyName] = texture;
        }
        return imported;
    }

    private static Texture2D ImportExternalTexture(
        string sourcePath,
        string targetAssetRoot,
        string propertyName,
        IDictionary<string, Texture2D> cache,
        bool importAsSprite = false,
        float pixelsPerUnit = 100f)
    {
        if (string.IsNullOrEmpty(sourcePath))
            return null;

        string sourceKey = Path.GetFullPath(sourcePath);
        if (cache.TryGetValue(sourceKey, out Texture2D cached))
            return cached;

        string targetAssetPath = CombineAssetPath(targetAssetRoot, Path.GetFileName(sourcePath));
        EnsureAssetFolder(targetAssetRoot);
        CopyExternalFileToAsset(sourcePath, targetAssetPath);
        ConfigureTextureImporter(targetAssetPath, propertyName, importAsSprite, pixelsPerUnit);

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(targetAssetPath);
        if (texture == null)
            throw new InvalidOperationException($"纹理导入失败：{targetAssetPath}");
        cache[sourceKey] = texture;
        return texture;
    }

    private void AutoDetectSharedData(bool overwriteExisting)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
            return;

        DirectoryInfo current = new DirectoryInfo(sourceDirectory);
        while (current != null && !string.Equals(current.Name, "CN_分类完成", StringComparison.OrdinalIgnoreCase))
            current = current.Parent;

        if (current == null)
            return;

        string commonDirectory = Path.Combine(current.FullName, AnimationTextureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        string mapPath = Path.Combine(commonDirectory, AnimationTextureMapFileName);

        if ((overwriteExisting || string.IsNullOrWhiteSpace(animationTextureDirectory)) && Directory.Exists(commonDirectory))
            animationTextureDirectory = commonDirectory;
        if ((overwriteExisting || string.IsNullOrWhiteSpace(animationTextureMapPath)) && File.Exists(mapPath))
            animationTextureMapPath = mapPath;
    }

    private static List<TextureResolution> ResolveTextures(
        MaterialSnapshot snapshot,
        string localDirectory,
        string commonDirectory,
        IReadOnlyDictionary<long, string> textureMap)
    {
        var result = new List<TextureResolution>();
        foreach (TextureEnvironment source in snapshot.Textures)
        {
            string path = null;
            if (source.PathId == 0)
            {
                result.Add(new TextureResolution(source, null));
                continue;
            }

            if (source.PropertyName is "_MainTex" or "_MaskTex")
                path = ResolveLocalTexture(snapshot.Name, source.PropertyName, localDirectory);

            if (string.IsNullOrEmpty(path) && source.FileId == 0)
                path = ResolveLocalTexture(snapshot.Name, source.PropertyName, localDirectory);

            if (string.IsNullOrEmpty(path) && textureMap.TryGetValue(source.PathId, out string textureName))
                path = FindFileIgnoringCase(commonDirectory, textureName + ".png");

            result.Add(new TextureResolution(source, path));
        }

        return result;
    }

    private static string ResolveLocalTexture(string materialName, string propertyName, string localDirectory)
    {
        if (propertyName is not ("_MainTex" or "_MaskTex"))
            return null;

        string materialStem = materialName.EndsWith("_mat", StringComparison.OrdinalIgnoreCase)
            ? materialName.Substring(0, materialName.Length - "_mat".Length)
            : materialName;
        string directoryStem = new DirectoryInfo(localDirectory).Name;

        var stems = new List<string> { materialStem, directoryStem };
        if (materialStem.StartsWith("bg_bg_", StringComparison.OrdinalIgnoreCase))
            stems.Add(materialStem.Substring("bg_".Length));

        var expectedFileNames = new List<string>();
        foreach (string stem in stems.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (propertyName == "_MainTex")
            {
                expectedFileNames.Add(stem + ".png");
                continue;
            }

            expectedFileNames.Add(stem + "_mask.png");
            if (stem.StartsWith("bg_", StringComparison.OrdinalIgnoreCase))
                expectedFileNames.Add("bg_mask_" + stem.Substring("bg_".Length) + ".png");
        }

        foreach (string expectedFileName in expectedFileNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string exactMatch = FindFileIgnoringCase(localDirectory, expectedFileName);
            if (!string.IsNullOrEmpty(exactMatch))
                return exactMatch;
        }

        string[] pngFiles = Directory.EnumerateFiles(localDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string[] semanticMatches = propertyName == "_MaskTex"
            ? pngFiles.Where(path => Path.GetFileNameWithoutExtension(path).Contains("mask", StringComparison.OrdinalIgnoreCase)).ToArray()
            : pngFiles.Where(path =>
                    !Path.GetFileNameWithoutExtension(path).Contains("mask", StringComparison.OrdinalIgnoreCase)
                    && !Path.GetFileNameWithoutExtension(path).Contains("effect", StringComparison.OrdinalIgnoreCase))
                .ToArray();

        return semanticMatches.Length == 1 ? semanticMatches[0] : null;
    }

    private static Dictionary<long, string> LoadTextureMap(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("缺少 animationtexture_asset_map.json。请点击自动定位，或手动拖入 PathID 映射 JSON。", path);

        AnimationTextureAssetMap map = JsonUtility.FromJson<AnimationTextureAssetMap>(File.ReadAllText(path));
        if (map?.AssetEntries == null || map.AssetEntries.Count == 0)
            throw new InvalidDataException($"PathID 映射 JSON 没有 AssetEntries：{path}");

        return map.AssetEntries
            .Where(item => item != null && !string.IsNullOrEmpty(item.Name))
            .GroupBy(item => item.PathID)
            .ToDictionary(group => group.Key, group => group.First().Name);
    }

    private static Material CreateOrLoadMaterial(string assetPath, Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (material == null)
        {
            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(assetPath) };
            AssetDatabase.CreateAsset(material, assetPath);
        }
        else
        {
            material.shader = shader;
        }

        return material;
    }

    private static void ApplySnapshot(
        Material material,
        MaterialSnapshot snapshot,
        IReadOnlyDictionary<string, Texture2D> importedTextures)
    {
        foreach (TextureEnvironment texture in snapshot.Textures)
        {
            if (!material.HasProperty(texture.PropertyName))
                continue;

            importedTextures.TryGetValue(texture.PropertyName, out Texture2D importedTexture);
            material.SetTexture(texture.PropertyName, importedTexture);
            material.SetTextureScale(texture.PropertyName, texture.Scale);
            material.SetTextureOffset(texture.PropertyName, texture.Offset);
        }

        foreach ((string propertyName, int value) in snapshot.Ints)
        {
            if (material.HasProperty(propertyName))
                material.SetInteger(propertyName, value);
        }

        foreach ((string propertyName, float value) in snapshot.Floats)
        {
            if (material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }

        foreach ((string propertyName, Color value) in snapshot.Colors)
        {
            if (material.HasProperty(propertyName))
                material.SetColor(propertyName, value);
        }

        material.shaderKeywords = snapshot.ValidKeywords.ToArray();
        material.globalIlluminationFlags = (MaterialGlobalIlluminationFlags)snapshot.LightmapFlags;
        material.enableInstancing = snapshot.EnableInstancingVariants;
        material.doubleSidedGI = snapshot.DoubleSidedGi;
        material.renderQueue = snapshot.CustomRenderQueue;

        foreach ((string tag, string value) in snapshot.StringTags)
            material.SetOverrideTag(tag, value);
        foreach (string pass in snapshot.DisabledShaderPasses)
            material.SetShaderPassEnabled(pass, false);

        EditorUtility.SetDirty(material);
    }

    private static void CreatePreviewPrefab(
        string prefabPath,
        string identifier,
        Material material,
        Texture2D mainTexture,
        Texture2D foregroundTexture,
        float offsetY)
    {
        var root = new GameObject(
            identifier + "_Preview",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage),
            typeof(AspectRatioFitter));

        try
        {
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(mainTexture.width, mainTexture.height);
            rect.anchoredPosition = new Vector2(0f, offsetY);

            RawImage image = root.GetComponent<RawImage>();
            image.texture = mainTexture;
            image.material = material;
            image.color = Color.white;
            image.raycastTarget = false;

            AspectRatioFitter fitter = root.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = mainTexture.width / (float)mainTexture.height;

            AddForegroundOverlay(root.transform, foregroundTexture);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            DestroyImmediate(root);
        }
    }

    private static string CreateGroupPreviewPrefab(
        string assetRoot,
        string sourceName,
        IReadOnlyList<RestoredEntry> restored)
    {
        string prefabPath = CombineAssetPath(assetRoot, SanitizeFileName(sourceName) + "_GroupPreview.prefab");
        float totalWidth = restored.Sum(item => item.MainTexture.width);
        float maxHeight = restored.Max(item => item.MainTexture.height);
        var root = new GameObject(SanitizeFileName(sourceName) + "_GroupPreview", typeof(RectTransform));

        try
        {
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(totalWidth, maxHeight);

            float cursor = -totalWidth * 0.5f;
            foreach (RestoredEntry entry in restored)
            {
                var panel = new GameObject(
                    entry.Analysis.Identifier,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RawImage));
                RectTransform panelRect = panel.GetComponent<RectTransform>();
                panelRect.SetParent(rootRect, false);
                panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.sizeDelta = new Vector2(entry.MainTexture.width, entry.MainTexture.height);
                panelRect.anchoredPosition = new Vector2(cursor + entry.MainTexture.width * 0.5f, 0f);

                RawImage image = panel.GetComponent<RawImage>();
                image.texture = entry.MainTexture;
                image.material = entry.Material;
                image.color = Color.white;
                image.raycastTarget = false;
                AddForegroundOverlay(panel.transform, entry.ForegroundTexture);

                cursor += entry.MainTexture.width;
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return prefabPath;
        }
        finally
        {
            DestroyImmediate(root);
        }
    }

    private static void AddForegroundOverlay(Transform parent, Texture2D foregroundTexture)
    {
        if (foregroundTexture == null)
            return;

        var overlay = new GameObject(
            "Front",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage));
        RectTransform rect = overlay.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        RawImage image = overlay.GetComponent<RawImage>();
        image.texture = foregroundTexture;
        image.color = Color.white;
        image.raycastTarget = false;
    }

    private static float ReadOffsetY(string directory)
    {
        string path = Directory.EnumerateFiles(directory, "offset_*.json", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (string.IsNullOrEmpty(path))
            return 0f;

        OffsetData data = JsonUtility.FromJson<OffsetData>(File.ReadAllText(path));
        return data?.OffsetY ?? 0f;
    }

    private static void ConfigureTextureImporter(
        string assetPath,
        string propertyName,
        bool importAsSprite = false,
        float pixelsPerUnit = 100f)
    {
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
            throw new InvalidOperationException($"无法取得 TextureImporter：{assetPath}");

        bool isStillTexture = propertyName is "_MainTex" or "_MaskTex" or "_FrontOverlay";
        importer.textureType = importAsSprite ? TextureImporterType.Sprite : TextureImporterType.Default;
        if (importAsSprite)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.spritePivot = new Vector2(0.5f, 0.5f);
            var textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(textureSettings);
        }
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.anisoLevel = 0;
        importer.wrapMode = isStillTexture ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    private static void CopyExternalFileToAsset(string sourcePath, string targetAssetPath)
    {
        string targetAbsolutePath = AssetPathToAbsolutePath(targetAssetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetAbsolutePath) ?? throw new InvalidOperationException(targetAssetPath));
        File.Copy(sourcePath, targetAbsolutePath, overwrite: true);
    }

    private static List<string> FindVariantCardMaterialBinaries(string directory)
    {
        string[] matches = Directory.GetFiles(directory, "*_mat.bin", SearchOption.TopDirectoryOnly);
        if (matches.Length == 0)
            throw new InvalidOperationException($"目录内没有 *_mat.bin：{directory}");

        var variantCardMatches = new List<string>();
        var detectedShaders = new List<string>();
        foreach (string path in matches)
        {
            try
            {
                MaterialSnapshot snapshot = MaterialBinaryReader.Read(path);
                detectedShaders.Add($"{Path.GetFileName(path)} → {snapshot.ShaderFileId}:{snapshot.ShaderPathId}");
                if (snapshot.ShaderPathId == OriginalVariantCardShaderPathId)
                    variantCardMatches.Add(path);
            }
            catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
            {
                detectedShaders.Add($"{Path.GetFileName(path)} → 无法按当前 Unity Material 布局解析");
            }
        }

        if (variantCardMatches.Count > 0)
            return variantCardMatches.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

        throw new InvalidOperationException(
            $"目录内没有找到原版 VariantCardShader（PathID {OriginalVariantCardShaderPathId}）。这个背景可能使用其他 Shader。\n{string.Join("\n", detectedShaders)}");
    }

    private static string ResolveForegroundTexture(string materialName, string directory)
    {
        string identifier = GetMaterialIdentifier(materialName);
        var candidates = new List<string> { identifier + "_front.png" };
        if (identifier.StartsWith("bg_bg_", StringComparison.OrdinalIgnoreCase))
            identifier = "bg_" + identifier.Substring("bg_bg_".Length);
        if (identifier.StartsWith("bg_", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(identifier + "_front.png");
            candidates.Add("bg_front_" + identifier.Substring("bg_".Length) + ".png");
        }

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string match = FindFileIgnoringCase(directory, candidate);
            if (!string.IsNullOrEmpty(match))
                return match;
        }
        return null;
    }

    private static string GetMaterialIdentifier(string materialName)
    {
        return materialName.EndsWith("_mat", StringComparison.OrdinalIgnoreCase)
            ? materialName.Substring(0, materialName.Length - "_mat".Length)
            : materialName;
    }

    private static string ExtractAssetId(string materialName)
    {
        MatchCollection matches = Regex.Matches(materialName ?? string.Empty, @"\d+");
        return matches.Count == 0 ? materialName ?? string.Empty : matches[^1].Value;
    }

    private static bool IsSameOrChildPath(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
            return false;

        string fullPath = Path.GetFullPath(path);
        string fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindFileIgnoringCase(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;

        return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private void ValidateSourceDirectory()
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"立绘素材目录不存在：{sourceDirectory}");
    }

    private void ValidateOutputAssetDirectory()
    {
        outputAssetDirectory = NormalizeAssetPath(outputAssetDirectory);
        if (outputAssetDirectory != "Assets" && !outputAssetDirectory.StartsWith("Assets/", StringComparison.Ordinal))
            throw new InvalidOperationException("输出目录必须位于当前 Unity 工程的 Assets 下。");
        EnsureAssetFolder(outputAssetDirectory);
    }

    private static void EnsureAssetFolder(string assetPath)
    {
        assetPath = NormalizeAssetPath(assetPath);
        if (AssetDatabase.IsValidFolder(assetPath))
            return;

        string parent = NormalizeAssetPath(Path.GetDirectoryName(assetPath));
        string name = Path.GetFileName(assetPath);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            throw new InvalidOperationException($"无效的 Assets 目录：{assetPath}");

        EnsureAssetFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static string AssetPathToAbsolutePath(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                             ?? throw new InvalidOperationException("无法取得 Unity 工程根目录。");
        return Path.GetFullPath(Path.Combine(projectRoot, NormalizeAssetPath(assetPath)));
    }

    private static string AbsolutePathToAssetPath(string absolutePath)
    {
        string fullPath = Path.GetFullPath(absolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!fullPath.Equals(assetsRoot, StringComparison.OrdinalIgnoreCase)
            && !fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;

        return "Assets" + fullPath.Substring(assetsRoot.Length).Replace('\\', '/');
    }

    private static string CombineAssetPath(params string[] parts)
    {
        return string.Join("/", parts.Select(part => part.Trim('/', '\\')));
    }

    private static string NormalizeAssetPath(string path)
    {
        return (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }

    private static void DrawExternalPathField(string label, ref string value, bool selectFolder)
    {
        EditorGUILayout.BeginHorizontal();
        Rect dropRect = EditorGUILayout.GetControlRect();
        value = EditorGUI.TextField(dropRect, label, value ?? string.Empty);
        if (GUILayout.Button("选择", GUILayout.Width(56f)))
        {
            string selected = selectFolder
                ? EditorUtility.OpenFolderPanel(label, Directory.Exists(value) ? value : string.Empty, string.Empty)
                : EditorUtility.OpenFilePanel(label, File.Exists(value) ? Path.GetDirectoryName(value) : string.Empty, "json");
            if (!string.IsNullOrEmpty(selected))
                value = selected;
        }
        EditorGUILayout.EndHorizontal();

        HandlePathDrop(dropRect, ref value, selectFolder);
    }

    private static void DrawAssetsFolderField(string label, ref string value)
    {
        EditorGUILayout.BeginHorizontal();
        Rect dropRect = EditorGUILayout.GetControlRect();
        value = EditorGUI.TextField(dropRect, label, value ?? string.Empty);
        if (GUILayout.Button("选择", GUILayout.Width(56f)))
        {
            string start = AssetPathToAbsolutePath(string.IsNullOrWhiteSpace(value) ? "Assets" : value);
            string selected = EditorUtility.OpenFolderPanel(label, start, string.Empty);
            if (!string.IsNullOrEmpty(selected))
            {
                string assetPath = AbsolutePathToAssetPath(selected);
                if (!string.IsNullOrEmpty(assetPath))
                    value = assetPath;
                else
                    EditorUtility.DisplayDialog("目录无效", "输出目录必须位于当前工程的 Assets 下。", "关闭");
            }
        }
        EditorGUILayout.EndHorizontal();

        Event current = Event.current;
        if (!dropRect.Contains(current.mousePosition) || (current.type != EventType.DragUpdated && current.type != EventType.DragPerform))
            return;

        string droppedPath = DragAndDrop.paths.FirstOrDefault();
        string droppedAssetPath = string.IsNullOrEmpty(droppedPath) ? null : AbsolutePathToAssetPath(droppedPath);
        DragAndDrop.visualMode = !string.IsNullOrEmpty(droppedAssetPath) && Directory.Exists(droppedPath)
            ? DragAndDropVisualMode.Copy
            : DragAndDropVisualMode.Rejected;

        if (current.type == EventType.DragPerform && DragAndDrop.visualMode == DragAndDropVisualMode.Copy)
        {
            DragAndDrop.AcceptDrag();
            value = droppedAssetPath;
        }
        current.Use();
    }

    private static void HandlePathDrop(Rect rect, ref string value, bool requireDirectory)
    {
        Event current = Event.current;
        if (!rect.Contains(current.mousePosition) || (current.type != EventType.DragUpdated && current.type != EventType.DragPerform))
            return;

        string path = DragAndDrop.paths.FirstOrDefault();
        bool valid = !string.IsNullOrEmpty(path) && (requireDirectory ? Directory.Exists(path) : File.Exists(path));
        DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
        if (current.type == EventType.DragPerform && valid)
        {
            DragAndDrop.AcceptDrag();
            value = path;
        }
        current.Use();
    }

    internal sealed class BattleSpriteRestoreResult
    {
        public BattleSpriteRestoreResult(string assetRoot, IReadOnlyList<BattleSpriteAsset> assets)
        {
            AssetRoot = assetRoot;
            Assets = assets;
        }

        public string AssetRoot { get; }
        public IReadOnlyList<BattleSpriteAsset> Assets { get; }
    }

    internal sealed class BattleSpriteAsset
    {
        public BattleSpriteAsset(
            string backgroundId,
            string identifier,
            Material material,
            Sprite mainSprite,
            Sprite foregroundSprite)
        {
            BackgroundId = backgroundId;
            Identifier = identifier;
            Material = material;
            MainSprite = mainSprite;
            ForegroundSprite = foregroundSprite;
        }

        public string BackgroundId { get; }
        public string Identifier { get; }
        public Material Material { get; }
        public Sprite MainSprite { get; }
        public Sprite ForegroundSprite { get; }
    }

    [Serializable]
    private sealed class AnimationTextureAssetMap
    {
        public List<AnimationTextureEntry> AssetEntries;
    }

    [Serializable]
    private sealed class AnimationTextureEntry
    {
        public string Name;
        public long PathID;
    }

    [Serializable]
    private sealed class OffsetData
    {
        public float OffsetY;
    }

    private sealed class TextureResolution
    {
        public TextureResolution(TextureEnvironment source, string sourcePath)
        {
            PropertyName = source.PropertyName;
            PathId = source.PathId;
            Scale = source.Scale;
            Offset = source.Offset;
            SourcePath = sourcePath;
        }

        public string PropertyName { get; }
        public long PathId { get; }
        public Vector2 Scale { get; }
        public Vector2 Offset { get; }
        public string SourcePath { get; }
        public bool IsResolved => PathId == 0 || !string.IsNullOrEmpty(SourcePath);
    }

    private sealed class MaterialAnalysis
    {
        public MaterialAnalysis(
            string binaryPath,
            MaterialSnapshot snapshot,
            List<TextureResolution> textures,
            string foregroundPath)
        {
            BinaryPath = binaryPath;
            Snapshot = snapshot;
            Textures = textures;
            ForegroundPath = foregroundPath;
            Identifier = GetMaterialIdentifier(snapshot.Name);
            AssetId = ExtractAssetId(snapshot.Name);
        }

        public string BinaryPath { get; }
        public MaterialSnapshot Snapshot { get; }
        public List<TextureResolution> Textures { get; }
        public string ForegroundPath { get; }
        public string Identifier { get; }
        public string AssetId { get; }
    }

    private sealed class RestoredEntry
    {
        public RestoredEntry(
            MaterialAnalysis analysis,
            Material material,
            Texture2D mainTexture,
            Texture2D foregroundTexture,
            string materialAssetPath,
            string prefabAssetPath)
        {
            Analysis = analysis;
            Material = material;
            MainTexture = mainTexture;
            ForegroundTexture = foregroundTexture;
            MaterialAssetPath = materialAssetPath;
            PrefabAssetPath = prefabAssetPath;
        }

        public MaterialAnalysis Analysis { get; }
        public Material Material { get; }
        public Texture2D MainTexture { get; }
        public Texture2D ForegroundTexture { get; }
        public string MaterialAssetPath { get; }
        public string PrefabAssetPath { get; }
    }

    private sealed class MaterialSnapshot
    {
        public string Name;
        public int ShaderFileId;
        public long ShaderPathId;
        public readonly List<string> ValidKeywords = new();
        public readonly List<string> InvalidKeywords = new();
        public uint LightmapFlags;
        public bool EnableInstancingVariants;
        public bool DoubleSidedGi;
        public int CustomRenderQueue;
        public readonly Dictionary<string, string> StringTags = new();
        public readonly List<string> DisabledShaderPasses = new();
        public readonly List<TextureEnvironment> Textures = new();
        public readonly Dictionary<string, int> Ints = new();
        public readonly Dictionary<string, float> Floats = new();
        public readonly Dictionary<string, Color> Colors = new();
    }

    private sealed class TextureEnvironment
    {
        public string PropertyName;
        public int FileId;
        public long PathId;
        public Vector2 Scale;
        public Vector2 Offset;
    }

    private static class MaterialBinaryReader
    {
        public static MaterialSnapshot Read(string path)
        {
            var reader = new CheckedBinaryReader(File.ReadAllBytes(path));
            var result = new MaterialSnapshot { Name = reader.ReadAlignedString() };

            result.ShaderFileId = reader.ReadInt32();
            result.ShaderPathId = reader.ReadInt64();
            result.ValidKeywords.AddRange(reader.ReadAlignedStringArray());
            result.InvalidKeywords.AddRange(reader.ReadAlignedStringArray());
            result.LightmapFlags = reader.ReadUInt32();
            result.EnableInstancingVariants = reader.ReadBoolean();
            result.DoubleSidedGi = reader.ReadBoolean();
            reader.Align4();
            result.CustomRenderQueue = reader.ReadInt32();

            int tagCount = reader.ReadSafeCount("stringTagMap");
            for (int index = 0; index < tagCount; index++)
                result.StringTags.Add(reader.ReadAlignedString(), reader.ReadAlignedString());

            result.DisabledShaderPasses.AddRange(reader.ReadAlignedStringArray());

            int textureCount = reader.ReadSafeCount("m_TexEnvs");
            for (int index = 0; index < textureCount; index++)
            {
                result.Textures.Add(new TextureEnvironment
                {
                    PropertyName = reader.ReadAlignedString(),
                    FileId = reader.ReadInt32(),
                    PathId = reader.ReadInt64(),
                    Scale = reader.ReadVector2(),
                    Offset = reader.ReadVector2()
                });
            }

            int intCount = reader.ReadSafeCount("m_Ints");
            for (int index = 0; index < intCount; index++)
                result.Ints.Add(reader.ReadAlignedString(), reader.ReadInt32());

            int floatCount = reader.ReadSafeCount("m_Floats");
            for (int index = 0; index < floatCount; index++)
                result.Floats.Add(reader.ReadAlignedString(), reader.ReadSingle());

            int colorCount = reader.ReadSafeCount("m_Colors");
            for (int index = 0; index < colorCount; index++)
            {
                string propertyName = reader.ReadAlignedString();
                result.Colors.Add(propertyName, new Color(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle()));
            }

            if (reader.Remaining >= sizeof(int))
            {
                int textureStackCount = reader.ReadSafeCount("m_BuildTextureStacks");
                if (textureStackCount != 0)
                    throw new InvalidDataException("当前 Material 含非空 m_BuildTextureStacks，暂不支持这种罕见格式。");
            }

            if (reader.Remaining != 0)
                throw new InvalidDataException($"Material 二进制解析后仍有 {reader.Remaining} 字节，文件布局与当前国服版本不一致：{path}");

            return result;
        }
    }

    private sealed class CheckedBinaryReader
    {
        private readonly byte[] data;
        private int position;

        public CheckedBinaryReader(byte[] data)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public int Remaining => data.Length - position;

        public int ReadSafeCount(string fieldName)
        {
            int count = ReadInt32();
            if (count < 0 || count > 4096)
                throw new InvalidDataException($"{fieldName} 数量异常：{count}（偏移 {position - 4}）");
            return count;
        }

        public string[] ReadAlignedStringArray()
        {
            int count = ReadSafeCount("string[]");
            var result = new string[count];
            for (int index = 0; index < count; index++)
                result[index] = ReadAlignedString();
            return result;
        }

        public string ReadAlignedString()
        {
            int length = ReadInt32();
            if (length < 0 || length > Remaining)
                throw new InvalidDataException($"字符串长度异常：{length}（偏移 {position - 4}）");

            EnsureAvailable(length);
            string value = Encoding.UTF8.GetString(data, position, length);
            position += length;
            Align4();
            return value;
        }

        public bool ReadBoolean()
        {
            EnsureAvailable(1);
            return data[position++] != 0;
        }

        public int ReadInt32()
        {
            EnsureAvailable(sizeof(int));
            int value = BitConverter.ToInt32(data, position);
            position += sizeof(int);
            return value;
        }

        public uint ReadUInt32()
        {
            EnsureAvailable(sizeof(uint));
            uint value = BitConverter.ToUInt32(data, position);
            position += sizeof(uint);
            return value;
        }

        public long ReadInt64()
        {
            EnsureAvailable(sizeof(long));
            long value = BitConverter.ToInt64(data, position);
            position += sizeof(long);
            return value;
        }

        public float ReadSingle()
        {
            EnsureAvailable(sizeof(float));
            float value = BitConverter.ToSingle(data, position);
            position += sizeof(float);
            return value;
        }

        public Vector2 ReadVector2()
        {
            return new Vector2(ReadSingle(), ReadSingle());
        }

        public void Align4()
        {
            position = (position + 3) & ~3;
            if (position > data.Length)
                throw new EndOfStreamException("Material 二进制在 4 字节对齐时越界。");
        }

        private void EnsureAvailable(int length)
        {
            if (length < 0 || position > data.Length - length)
                throw new EndOfStreamException($"Material 二进制读取越界：偏移 {position}，需要 {length} 字节，总长 {data.Length}。");
        }
    }
}
