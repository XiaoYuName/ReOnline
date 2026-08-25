using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    private MaterialSnapshot analyzedMaterial;
    private List<TextureResolution> analyzedTextures = new();
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
            "拖入 still_unit_xxxxxx 或 bg_xxxxxx 文件夹即可。工具会识别原版 VariantCard 材质，解析 *_mat.bin，并恢复纹理槽、Scale/Offset、Float、Color、Keyword 和 RenderQueue；源文件不会被修改。",
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
        if (analyzedMaterial == null)
            return;

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("解析结果", EditorStyles.boldLabel);
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

        foreach (TextureResolution texture in analyzedTextures)
        {
            MessageType type = texture.IsResolved ? MessageType.Info : MessageType.Error;
            string detail = texture.IsResolved
                ? $"{texture.PropertyName}  →  {Path.GetFileName(texture.SourcePath)}\nScale {texture.Scale}  Offset {texture.Offset}"
                : $"{texture.PropertyName}  →  未找到（PathID {texture.PathId}）";
            EditorGUILayout.HelpBox(detail, type);
        }
    }

    private void Analyze()
    {
        analyzedMaterial = null;
        analyzedTextures.Clear();

        try
        {
            ValidateSourceDirectory();
            AutoDetectSharedData(overwriteExisting: false);

            string materialBinary = FindVariantCardMaterialBinary(sourceDirectory);
            analyzedMaterial = MaterialBinaryReader.Read(materialBinary);
            Dictionary<long, string> textureMap = LoadTextureMap(animationTextureMapPath);
            analyzedTextures = ResolveTextures(analyzedMaterial, sourceDirectory, animationTextureDirectory, textureMap);

            int missing = analyzedTextures.Count(item => !item.IsResolved);
            analysisMessage = missing == 0
                ? $"解析成功：{analyzedMaterial.Name}，全部 {analyzedTextures.Count} 个纹理槽均已精确匹配。"
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
        if (analyzedMaterial == null || analyzedTextures.Any(item => !item.IsResolved))
            return;

        try
        {
            ValidateOutputAssetDirectory();
            if (variantCardShader == null)
                throw new InvalidOperationException($"未指定 VariantCard Shader。默认位置：{ShaderAssetPath}");

            string sourceName = new DirectoryInfo(sourceDirectory).Name;
            string assetRoot = CombineAssetPath(outputAssetDirectory, SanitizeFileName(sourceName));
            string textureAssetRoot = CombineAssetPath(assetRoot, "Textures");
            EnsureAssetFolder(textureAssetRoot);

            var importedTextures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            foreach (TextureResolution resolution in analyzedTextures)
            {
                if (string.IsNullOrEmpty(resolution.SourcePath))
                    continue;

                string fileName = Path.GetFileName(resolution.SourcePath);
                string targetAssetPath = CombineAssetPath(textureAssetRoot, fileName);
                CopyExternalFileToAsset(resolution.SourcePath, targetAssetPath);
                ConfigureTextureImporter(targetAssetPath, resolution.PropertyName);

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(targetAssetPath);
                if (texture == null)
                    throw new InvalidOperationException($"纹理导入失败：{targetAssetPath}");
                importedTextures[resolution.PropertyName] = texture;
            }

            string materialAssetPath = CombineAssetPath(assetRoot, analyzedMaterial.Name + ".mat");
            Material material = CreateOrLoadMaterial(materialAssetPath, variantCardShader);
            ApplySnapshot(material, analyzedMaterial, importedTextures);

            string prefabAssetPath = string.Empty;
            if (createPreviewPrefab)
                prefabAssetPath = CreatePreviewPrefab(assetRoot, material, importedTextures["_MainTex"]);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = material;

            analysisMessage = createPreviewPrefab
                ? $"还原完成：{materialAssetPath}\n预览：{prefabAssetPath}"
                : $"还原完成：{materialAssetPath}";
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

            if (source.FileId == 0)
            {
                path = ResolveLocalTexture(snapshot.Name, source.PropertyName, localDirectory);
            }
            else if (textureMap.TryGetValue(source.PathId, out string textureName))
            {
                path = FindFileIgnoringCase(commonDirectory, textureName + ".png");
            }

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

    private string CreatePreviewPrefab(string assetRoot, Material material, Texture2D mainTexture)
    {
        string identifier = analyzedMaterial.Name.Replace("_mat", string.Empty);
        string prefabPath = CombineAssetPath(assetRoot, identifier + "_Preview.prefab");
        var root = new GameObject(
            identifier + "_Preview",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage),
            typeof(AspectRatioFitter));

        try
        {
            float offsetY = ReadOffsetY(sourceDirectory);
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

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return prefabPath;
        }
        finally
        {
            DestroyImmediate(root);
        }
    }

    private static float ReadOffsetY(string directory)
    {
        string path = Directory.EnumerateFiles(directory, "offset_*.json", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (string.IsNullOrEmpty(path))
            return 0f;

        OffsetData data = JsonUtility.FromJson<OffsetData>(File.ReadAllText(path));
        return data?.OffsetY ?? 0f;
    }

    private static void ConfigureTextureImporter(string assetPath, string propertyName)
    {
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
            throw new InvalidOperationException($"无法取得 TextureImporter：{assetPath}");

        bool isStillTexture = propertyName is "_MainTex" or "_MaskTex";
        importer.textureType = TextureImporterType.Default;
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

    private static string FindVariantCardMaterialBinary(string directory)
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

        if (variantCardMatches.Count == 1)
            return variantCardMatches[0];
        if (variantCardMatches.Count > 1)
            throw new InvalidOperationException(
                $"目录内找到 {variantCardMatches.Count} 个 VariantCard 材质，无法自动决定主材质：\n{string.Join("\n", variantCardMatches.Select(Path.GetFileName))}");

        throw new InvalidOperationException(
            $"目录内没有找到原版 VariantCardShader（PathID {OriginalVariantCardShaderPathId}）。这个背景可能使用其他 Shader。\n{string.Join("\n", detectedShaders)}");
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
