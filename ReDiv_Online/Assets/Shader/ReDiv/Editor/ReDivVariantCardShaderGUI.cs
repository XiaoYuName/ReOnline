using System;
using UnityEditor;
using UnityEngine;

public sealed class ReDivVariantCardShaderGUI : ShaderGUI
{
    private const string UseFront2Keyword = "USE_FRONT2";
    private const string UseBack2Keyword = "USE_BACK2";
    private const string UseFrontFlashKeyword = "USE_FRONT_1_2_FLASH";
    private const string UseBackFlashKeyword = "USE_BACK_1_2_FLASH";
    private const string UseDistortionFlashKeyword = "USE_DISTORTION_FLASH";
    private const string UseLayerDistortionKeyword = "USE_FRONT_BACK_1_2_DISTORTION";

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        var material = materialEditor.target as Material;
        if (material == null)
        {
            base.OnGUI(materialEditor, properties);
            return;
        }

        EditorGUILayout.HelpBox(
            "Cygames/VariantCardShader 的 URP 复原版。Mask R 控制 Front，G 控制 Back，B 控制主图 Distortion。隐藏 Flag 会按原版 CustomEditor 的规则自动同步。",
            MessageType.Info);

        EditorGUILayout.LabelField("原版变体开关", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        bool useFront2 = EditorGUILayout.Toggle("启用 Front 2", material.IsKeywordEnabled(UseFront2Keyword));
        bool useBack2 = EditorGUILayout.Toggle("启用 Back 2", material.IsKeywordEnabled(UseBack2Keyword));
        bool variantChanged = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space(4f);
        EditorGUI.BeginChangeCheck();
        base.OnGUI(materialEditor, properties);
        bool propertyChanged = EditorGUI.EndChangeCheck();

        if (variantChanged || propertyChanged)
        {
            foreach (UnityEngine.Object target in materialEditor.targets)
            {
                if (target is not Material targetMaterial)
                    continue;

                Undo.RecordObject(targetMaterial, "修改 VariantCard 材质");
                SynchronizeMaterial(targetMaterial, useFront2, useBack2);
                EditorUtility.SetDirty(targetMaterial);
            }
        }
    }

    public static void SynchronizeMaterial(Material material, bool useFront2, bool useBack2)
    {
        // Unity 的 [Toggle] Drawer 曾自动写入这些派生关键字；原版材质并不存在它们。
        // 先清理可避免旧材质在 Shader 更新后残留无效变体状态。
        foreach (string legacyKeyword in LegacyToggleKeywords)
            material.DisableKeyword(legacyKeyword);

        if (material == null)
            throw new ArgumentNullException(nameof(material));

        SetFloatIfPresent(material, "_FlagFront1MoveType", GetFloat(material, "_Front1_MoveType", 1f) - 2f);
        SetFloatIfPresent(material, "_FlagFront2MoveType", GetFloat(material, "_Front2_MoveType", 1f) - 2f);
        SetFloatIfPresent(material, "_FlagBack1MoveType", GetFloat(material, "_Back1_MoveType", 1f) - 2f);
        SetFloatIfPresent(material, "_FlagBack2MoveType", GetFloat(material, "_Back2_MoveType", 1f) - 2f);
        SetFloatIfPresent(material, "_FlagDistortionMoveType", GetFloat(material, "_Distortion_MoveType", 1f) - 2f);

        SetFloatIfPresent(material, "_FlagFront1RenderType", GetFloat(material, "_Front1_RenderType", 1f) - 2f);
        SetFloatIfPresent(material, "_FlagFront2RenderType", GetFloat(material, "_Front2_RenderType", 1f) - 2f);
        SetFloatIfPresent(material, "_FlagBack1RenderType", GetFloat(material, "_Back1_RenderType", 1f) - 2f);
        SetFloatIfPresent(material, "_FlagBack2RenderType", GetFloat(material, "_Back2_RenderType", 1f) - 2f);

        SetFloatIfPresent(material, "_FlagFront1Flash", GetFloat(material, "_Front1_Flash", 0f));
        SetFloatIfPresent(material, "_FlagFront2Flash", GetFloat(material, "_Front2_Flash", 0f));
        SetFloatIfPresent(material, "_FlagBack1Flash", GetFloat(material, "_Back1_Flash", 0f));
        SetFloatIfPresent(material, "_FlagBack2Flash", GetFloat(material, "_Back2_Flash", 0f));
        SetFloatIfPresent(material, "_FlagFront1Distortion", GetFloat(material, "_Front1_Distortion", 0f));
        SetFloatIfPresent(material, "_FlagFront2Distortion", GetFloat(material, "_Front2_Distortion", 0f));
        SetFloatIfPresent(material, "_FlagBack1Distortion", GetFloat(material, "_Back1_Distortion", 0f));
        SetFloatIfPresent(material, "_FlagBack2Distortion", GetFloat(material, "_Back2_Distortion", 0f));

        SetKeyword(material, UseFront2Keyword, useFront2);
        SetKeyword(material, UseBack2Keyword, useBack2);

        bool useFrontFlash = GetFloat(material, "_Front1_Flash", 0f) > 0.5f
                             || (useFront2 && GetFloat(material, "_Front2_Flash", 0f) > 0.5f);
        bool useBackFlash = GetFloat(material, "_Back1_Flash", 0f) > 0.5f
                            || (useBack2 && GetFloat(material, "_Back2_Flash", 0f) > 0.5f);
        bool useDistortionFlash = GetFloat(material, "_Distortion_Flash", 0f) > 0.5f;
        bool useLayerDistortion = GetFloat(material, "_Front1_Distortion", 0f) > 0.5f
                                  || (useFront2 && GetFloat(material, "_Front2_Distortion", 0f) > 0.5f)
                                  || GetFloat(material, "_Back1_Distortion", 0f) > 0.5f
                                  || (useBack2 && GetFloat(material, "_Back2_Distortion", 0f) > 0.5f);

        SetKeyword(material, UseFrontFlashKeyword, useFrontFlash);
        SetKeyword(material, UseBackFlashKeyword, useBackFlash);
        SetKeyword(material, UseDistortionFlashKeyword, useDistortionFlash);
        SetKeyword(material, UseLayerDistortionKeyword, useLayerDistortion);
    }

    private static readonly string[] LegacyToggleKeywords =
    {
        "_FRONT1_FLASH_ON",
        "_FRONT1_DISTORTION_ON",
        "_FRONT2_FLASH_ON",
        "_FRONT2_DISTORTION_ON",
        "_BACK1_FLASH_ON",
        "_BACK1_DISTORTION_ON",
        "_BACK2_FLASH_ON",
        "_BACK2_DISTORTION_ON",
        "_DISTORTION_FLASH_ON"
    };

    private static void SetKeyword(Material material, string keyword, bool enabled)
    {
        if (enabled)
            material.EnableKeyword(keyword);
        else
            material.DisableKeyword(keyword);
    }

    private static float GetFloat(Material material, string propertyName, float fallback)
    {
        return material.HasProperty(propertyName) ? material.GetFloat(propertyName) : fallback;
    }

    private static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
            material.SetFloat(propertyName, value);
    }
}
