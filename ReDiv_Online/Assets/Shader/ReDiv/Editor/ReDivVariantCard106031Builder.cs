using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.UI;

public static class ReDivVariantCard106031Builder
{
    private const string ShaderAssetPath = "Assets/Shader/ReDiv/VariantCard.shader";
    private const string SampleRoot = "Assets/Shader/ReDiv/Samples/106031";
    private const string TextureRoot = SampleRoot + "/Textures";
    private const string MaterialAssetPath = SampleRoot + "/still_unit_106031_mat.mat";
    private const string PrefabAssetPath = SampleRoot + "/StillUnit106031Preview.prefab";

    private static readonly IReadOnlyDictionary<string, string> TextureProperties =
        new Dictionary<string, string>
        {
            ["_MainTex"] = "still_unit_106031.png",
            ["_MaskTex"] = "still_unit_106031_mask.png",
            ["_Back1Tex"] = "tx_foil_rainbow_dark.png",
            ["_Back2Tex"] = "tx_foil_ring_thin.png",
            ["_DistortionTex"] = "tx_distortion.png",
            ["_Front1Tex"] = "tx_foil_spark_strong.png",
            ["_Front2Tex"] = "tx_foil_cloud.png",
        };

    [MenuItem("Tools/ReDiv/VariantCard/重建 106031 材质与预览 Prefab")]
    public static void BuildSample()
    {
        EnsureAssetFolder(SampleRoot);
        EnsureAssetFolder(TextureRoot);

        AssetDatabase.ImportAsset(ShaderAssetPath, ImportAssetOptions.ForceSynchronousImport);
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderAssetPath);
        if (shader == null)
            throw new InvalidOperationException($"无法加载 Shader：{ShaderAssetPath}");

        ThrowIfShaderHasErrors(shader);
        ConfigureTextureImporters();

        Material material = CreateOrLoadMaterial(shader);
        ApplyOriginalTextures(material);
        ApplyOriginalTextureTransforms(material);
        ApplyOriginalColors(material);
        ApplyOriginalFloats(material);

        // 原材质的有效关键字只有 USE_BACK2 与 USE_BACK_1_2_FLASH。
        ReDivVariantCardShaderGUI.SynchronizeMaterial(material, useFront2: false, useBack2: true);
        material.renderQueue = -1;
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();

        CreatePreviewPrefab(material);
        ValidateMaterial(material);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabAssetPath);
        Debug.Log($"[ReDiv VariantCard] 106031 已重建：{MaterialAssetPath}；预览 Prefab：{PrefabAssetPath}");
    }

    // Unity 批处理验证入口。
    public static void BuildFromCommandLine()
    {
        BuildSample();
    }

    private static Material CreateOrLoadMaterial(Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
        if (material == null)
        {
            material = new Material(shader) { name = "still_unit_106031_mat" };
            AssetDatabase.CreateAsset(material, MaterialAssetPath);
        }
        else
        {
            material.shader = shader;
        }

        return material;
    }

    private static void ApplyOriginalTextures(Material material)
    {
        foreach ((string propertyName, string fileName) in TextureProperties)
        {
            string path = $"{TextureRoot}/{fileName}";
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                throw new FileNotFoundException($"缺少 106031 Shader 纹理：{path}");

            material.SetTexture(propertyName, texture);
        }
    }

    private static void ApplyOriginalTextureTransforms(Material material)
    {
        SetTextureTransform(material, "_MainTex", Vector2.one, Vector2.zero);
        SetTextureTransform(material, "_MaskTex", Vector2.one, Vector2.zero);
        SetTextureTransform(material, "_Back1Tex", new Vector2(10f, 10f), new Vector2(3.6f, 0.5f));
        SetTextureTransform(material, "_Back2Tex", Vector2.one, new Vector2(0.35f, 0.075f));
        SetTextureTransform(material, "_DistortionTex", new Vector2(0.35f, 0.25f), Vector2.zero);
        SetTextureTransform(material, "_Front1Tex", new Vector2(1.33f, 1f), new Vector2(0.3f, -0.95f));
        SetTextureTransform(material, "_Front2Tex", new Vector2(2f, 1.89f), new Vector2(0.7f, 0.13f));
    }

    private static void ApplyOriginalColors(Material material)
    {
        material.SetColor("_Color", new Color(1f, 1f, 1f, 1f));
        material.SetColor("_Back1Color", new Color(0.19117647f, 0.19117647f, 0.19117647f, 1f));
        material.SetColor("_Back2Color", new Color(0.5108966f, 0.20004326f, 0.5441177f, 1f));
        material.SetColor("_Front1Color", new Color(0.75912774f, 0.30147058f, 1f, 1f));
        material.SetColor("_Front2Color", new Color(0.44052023f, 0.33737025f, 0.7647059f, 1f));
    }

    private static void ApplyOriginalFloats(Material material)
    {
        var values = new Dictionary<string, float>
        {
            ["_Back1FlashMax"] = 1f,
            ["_Back1FlashMin"] = 0.35f,
            ["_Back1FlashSpeed"] = 0.8f,
            ["_Back1Rotate"] = 0.01f,
            ["_Back1ScrollAngle"] = 10f,
            ["_Back1ScrollU"] = 0.05f,
            ["_Back1ScrollV"] = -0.35f,
            ["_Back1Spiral"] = 0f,
            ["_Back1_Distortion"] = 0f,
            ["_Back1_Flash"] = 0f,
            ["_Back1_MoveType"] = 4f,
            ["_Back1_RenderType"] = 1f,

            ["_Back2FlashMax"] = 0.3f,
            ["_Back2FlashMin"] = 0.999f,
            ["_Back2FlashSpeed"] = 3f,
            ["_Back2Rotate"] = -0.05f,
            ["_Back2ScrollAngle"] = -2f,
            ["_Back2ScrollU"] = 0f,
            ["_Back2ScrollV"] = -0.03f,
            ["_Back2Spiral"] = 0f,
            ["_Back2_Distortion"] = 0f,
            ["_Back2_Flash"] = 1f,
            ["_Back2_MoveType"] = 4f,
            ["_Back2_RenderType"] = 1f,

            ["_DistortionFlashMax"] = 1f,
            ["_DistortionFlashMin"] = 0f,
            ["_DistortionFlashSpeed"] = 1f,
            ["_DistortionIntensityU"] = 0.01f,
            ["_DistortionIntensityV"] = 0.01f,
            ["_DistortionRotate"] = 1.2f,
            ["_DistortionScrollAngle"] = 0f,
            ["_DistortionScrollU"] = 0.1f,
            ["_DistortionScrollV"] = -0.2f,
            ["_DistortionSpiral"] = 0f,
            ["_Distortion_Flash"] = 0f,
            ["_Distortion_MoveType"] = 2f,

            ["_Front1FlashMax"] = 1f,
            ["_Front1FlashMin"] = 0.35f,
            ["_Front1FlashSpeed"] = 0.8f,
            ["_Front1Rotate"] = 0.1f,
            ["_Front1ScrollAngle"] = 0f,
            ["_Front1ScrollU"] = 0.05f,
            ["_Front1ScrollV"] = -0.03f,
            ["_Front1Spiral"] = -2.5f,
            ["_Front1_Distortion"] = 0f,
            ["_Front1_Flash"] = 0f,
            ["_Front1_MoveType"] = 3f,
            ["_Front1_RenderType"] = 1f,

            ["_Front2FlashMax"] = 0.28f,
            ["_Front2FlashMin"] = 0.6f,
            ["_Front2FlashSpeed"] = 0.1f,
            ["_Front2Rotate"] = 0.2f,
            ["_Front2ScrollAngle"] = 0f,
            ["_Front2ScrollU"] = 0.1f,
            ["_Front2ScrollV"] = -0.1f,
            ["_Front2Spiral"] = -1.5f,
            ["_Front2_Distortion"] = 0f,
            ["_Front2_Flash"] = 0f,
            ["_Front2_MoveType"] = 4f,
            ["_Front2_RenderType"] = 1f,
        };

        foreach ((string propertyName, float value) in values)
            material.SetFloat(propertyName, value);
    }

    private static void ConfigureTextureImporters()
    {
        foreach (string fileName in TextureProperties.Values.Distinct())
        {
            string path = $"{TextureRoot}/{fileName}";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                throw new InvalidOperationException($"无法读取 TextureImporter：{path}");

            bool clamp = fileName.StartsWith("still_unit_106031", StringComparison.Ordinal);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 0;
            importer.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.SaveAndReimport();
        }
    }

    private static void CreatePreviewPrefab(Material material)
    {
        var root = new GameObject(
            "StillUnit106031Preview",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage),
            typeof(AspectRatioFitter));

        try
        {
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1408f, 792f);

            var image = root.GetComponent<RawImage>();
            image.texture = material.GetTexture("_MainTex");
            image.material = material;
            image.color = Color.white;
            image.raycastTarget = false;

            var fitter = root.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 1408f / 792f;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabAssetPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void ValidateMaterial(Material material)
    {
        string[] expectedKeywords = { "USE_BACK2", "USE_BACK_1_2_FLASH" };
        string[] actualKeywords = material.shaderKeywords.OrderBy(value => value).ToArray();
        string[] sortedExpected = expectedKeywords.OrderBy(value => value).ToArray();
        if (!actualKeywords.SequenceEqual(sortedExpected))
        {
            throw new InvalidOperationException(
                $"106031 关键字不一致。实际：{string.Join(", ", actualKeywords)}；期望：{string.Join(", ", sortedExpected)}");
        }

        AssertApproximately(material.GetFloat("_DistortionIntensityU"), 0.01f, "_DistortionIntensityU");
        AssertApproximately(material.GetFloat("_DistortionIntensityV"), 0.01f, "_DistortionIntensityV");
        AssertApproximately(material.GetFloat("_FlagFront1MoveType"), 1f, "_FlagFront1MoveType");
        AssertApproximately(material.GetFloat("_FlagBack1MoveType"), 2f, "_FlagBack1MoveType");
        AssertApproximately(material.GetFloat("_FlagBack2Flash"), 1f, "_FlagBack2Flash");

        foreach (string propertyName in TextureProperties.Keys)
        {
            if (material.GetTexture(propertyName) == null)
                throw new InvalidOperationException($"材质纹理未绑定：{propertyName}");
        }
    }

    private static void ThrowIfShaderHasErrors(Shader shader)
    {
        ShaderMessage[] errors = ShaderUtil.GetShaderMessages(shader)
            .Where(message => message.severity == ShaderCompilerMessageSeverity.Error)
            .ToArray();
        if (errors.Length == 0)
            return;

        throw new InvalidOperationException(
            "VariantCard Shader 编译失败：\n" + string.Join("\n", errors.Select(error => error.message)));
    }

    private static void SetTextureTransform(Material material, string propertyName, Vector2 scale, Vector2 offset)
    {
        material.SetTextureScale(propertyName, scale);
        material.SetTextureOffset(propertyName, offset);
    }

    private static void AssertApproximately(float actual, float expected, string propertyName)
    {
        if (!Mathf.Approximately(actual, expected))
            throw new InvalidOperationException($"{propertyName} 不一致：实际 {actual}，期望 {expected}");
    }

    private static void EnsureAssetFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
            return;

        string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        string name = Path.GetFileName(assetPath);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            throw new InvalidOperationException($"无效的 Assets 目录：{assetPath}");

        EnsureAssetFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
