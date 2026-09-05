using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class ReDivBattleBackgroundSceneRestorerWindow : EditorWindow
{
    private const string ShaderAssetPath = "Assets/Shader/ReDiv/VariantCard.shader";
    private const string DefaultOutputAssetPath = "Assets/Shader/ReDiv/BattleScenesRestored";
    private const string AnimationTextureRelativePath = "场景与背景/背景/bg/animationtexture_bundle";
    private const string AnimationTextureMapFileName = "animationtexture_asset_map.json";
    private const string DefaultSource =
        @"D:\AssetsStudio\Rediv\CN_分类完成\场景与背景\背景\bg\battle\background\bg_10001";

    [SerializeField] private string sourceDirectory = string.Empty;
    [SerializeField] private string animationTextureDirectory = string.Empty;
    [SerializeField] private string animationTextureMapPath = string.Empty;
    [SerializeField] private string outputAssetDirectory = DefaultOutputAssetPath;
    [SerializeField] private Shader variantCardShader;
    [SerializeField] private float pixelsPerUnit = 100f;
    [SerializeField] private int backgroundSortingOrder = -100;
    [SerializeField] private int foregroundSortingOrder = -90;
    [SerializeField] private int initialVariantIndex;
    [SerializeField] private bool createScene = true;
    [SerializeField] private bool createCamera = true;

    private readonly List<string> discoveredIds = new();
    private Vector2 scrollPosition;
    private string statusMessage = string.Empty;
    private MessageType statusType = MessageType.Info;

    [MenuItem("Tools/ReDiv/战斗背景/还原 SpriteRenderer 场景")]
    public static void Open()
    {
        GetWindow<ReDivBattleBackgroundSceneRestorerWindow>("战斗背景场景还原");
    }

    private void OnEnable()
    {
        if (variantCardShader == null)
            variantCardShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderAssetPath);
        if (string.IsNullOrWhiteSpace(sourceDirectory) && Directory.Exists(DefaultSource))
            sourceDirectory = DefaultSource;

        AutoDetectSharedData(overwriteExisting: false);
        InspectSource(showStatus: false);
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.HelpBox(
            "这是战斗背景专用工具，只生成世界空间 SpriteRenderer。完整背景 ID 是按副本波次切换的整张背景，不是横向拼图块；所有变体保持同一世界原点，一次只激活一个。每个变体内部按 Back + Front 两层对齐。",
            MessageType.Info);

        EditorGUILayout.LabelField("输入", EditorStyles.boldLabel);
        DrawExternalPathField("战斗背景场景组目录", ref sourceDirectory, selectFolder: true);
        if (GUILayout.Button("自动定位通用纹理和 PathID 映射"))
        {
            AutoDetectSharedData(overwriteExisting: true);
            InspectSource(showStatus: true);
        }
        DrawExternalPathField("通用效果纹理目录", ref animationTextureDirectory, selectFolder: true);
        DrawExternalPathField("PathID 映射 JSON", ref animationTextureMapPath, selectFolder: false);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("世界空间输出", EditorStyles.boldLabel);
        DrawAssetsFolderField("输出目录", ref outputAssetDirectory);
        variantCardShader = (Shader)EditorGUILayout.ObjectField(
            "VariantCard Shader", variantCardShader, typeof(Shader), false);
        pixelsPerUnit = EditorGUILayout.FloatField("Pixels Per Unit", pixelsPerUnit);
        backgroundSortingOrder = EditorGUILayout.IntField("Back Sorting Order", backgroundSortingOrder);
        foregroundSortingOrder = EditorGUILayout.IntField("Front Sorting Order", foregroundSortingOrder);
        if (discoveredIds.Count > 0)
        {
            initialVariantIndex = Mathf.Clamp(initialVariantIndex, 0, discoveredIds.Count - 1);
            initialVariantIndex = EditorGUILayout.Popup(
                "场景默认显示", initialVariantIndex, discoveredIds.ToArray());
        }
        createScene = EditorGUILayout.Toggle("生成 Unity Scene", createScene);
        using (new EditorGUI.DisabledScope(!createScene))
            createCamera = EditorGUILayout.Toggle("Scene 内生成正交相机", createCamera);

        EditorGUILayout.Space(8f);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("检查场景组", GUILayout.Height(30f)))
            InspectSource(showStatus: true);
        if (GUILayout.Button("一键还原 SpriteRenderer 场景", GUILayout.Height(30f)))
            Restore();
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(statusMessage))
            EditorGUILayout.HelpBox(statusMessage, statusType);

        if (discoveredIds.Count > 0)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"完整背景 ID（{discoveredIds.Count}）", EditorStyles.boldLabel);
            foreach (string id in discoveredIds)
                EditorGUILayout.LabelField("• " + id);
        }
        EditorGUILayout.EndScrollView();
    }

    private void InspectSource(bool showStatus)
    {
        discoveredIds.Clear();
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            if (showStatus)
            {
                statusMessage = "战斗背景场景组目录不存在。";
                statusType = MessageType.Error;
            }
            return;
        }

        discoveredIds.AddRange(Directory.GetFiles(sourceDirectory, "*_mat.bin", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => Regex.Match(name ?? string.Empty, @"\d+").Value)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal));

        initialVariantIndex = discoveredIds.Count == 0
            ? 0
            : Mathf.Clamp(initialVariantIndex, 0, discoveredIds.Count - 1);
        if (showStatus)
        {
            statusMessage = discoveredIds.Count == 0
                ? "目录内没有找到 *_mat.bin。"
                : $"找到 {discoveredIds.Count} 个完整背景 ID。还原时会进一步验证 Shader PathID 和全部纹理引用。";
            statusType = discoveredIds.Count == 0 ? MessageType.Error : MessageType.Info;
        }
        Repaint();
    }

    private void Restore()
    {
        try
        {
            InspectSource(showStatus: false);
            if (discoveredIds.Count == 0)
                throw new InvalidOperationException("目录内没有可还原的战斗背景材质。");
            if (foregroundSortingOrder <= backgroundSortingOrder)
                throw new InvalidOperationException("Front Sorting Order 必须大于 Back Sorting Order。");

            ReDivVariantCardMaterialRestorerWindow.BattleSpriteRestoreResult restored =
                ReDivVariantCardMaterialRestorerWindow.RestoreBattleSpriteAssets(
                    sourceDirectory,
                    animationTextureDirectory,
                    animationTextureMapPath,
                    outputAssetDirectory,
                    variantCardShader,
                    pixelsPerUnit);

            string groupName = new DirectoryInfo(sourceDirectory).Name;
            string prefabPath = CreateBattlePrefab(restored, groupName);
            string scenePath = createScene
                ? CreateBattleScene(restored, groupName, prefabPath)
                : string.Empty;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                string.IsNullOrEmpty(scenePath) ? prefabPath : scenePath);

            statusMessage = $"还原完成：{restored.Assets.Count} 个世界空间背景变体。\nPrefab：{prefabPath}";
            if (!string.IsNullOrEmpty(scenePath))
                statusMessage += $"\nScene：{scenePath}";
            statusType = MessageType.Info;
            Debug.Log($"[ReDiv Battle Background] {statusMessage}");
        }
        catch (Exception exception)
        {
            statusMessage = exception.Message;
            statusType = MessageType.Error;
            Debug.LogException(exception);
        }
        Repaint();
    }

    private string CreateBattlePrefab(
        ReDivVariantCardMaterialRestorerWindow.BattleSpriteRestoreResult restored,
        string groupName)
    {
        string prefabPath = CombineAssetPath(restored.AssetRoot, groupName + "_BattleBackground.prefab");
        var root = new GameObject(
            groupName + "_BattleBackground",
            typeof(BattleBackgroundVariantSet),
            typeof(BattleBackgroundScreenFitter));
        var ids = new List<string>();
        var roots = new List<GameObject>();

        try
        {
            foreach (ReDivVariantCardMaterialRestorerWindow.BattleSpriteAsset asset in restored.Assets)
            {
                GameObject variantRoot = CreateVariantRoot(asset, root.transform);
                ids.Add(asset.BackgroundId);
                roots.Add(variantRoot);
            }

            BattleBackgroundVariantSet set = root.GetComponent<BattleBackgroundVariantSet>();
            set.Configure(ids, roots, Mathf.Clamp(initialVariantIndex, 0, roots.Count - 1));
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return prefabPath;
        }
        finally
        {
            DestroyImmediate(root);
        }
    }

    private GameObject CreateVariantRoot(
        ReDivVariantCardMaterialRestorerWindow.BattleSpriteAsset asset,
        Transform parent)
    {
        var variant = new GameObject(asset.Identifier);
        variant.transform.SetParent(parent, false);
        variant.transform.localPosition = Vector3.zero;

        var back = new GameObject("Back", typeof(SpriteRenderer));
        back.transform.SetParent(variant.transform, false);
        back.transform.localPosition = Vector3.zero;
        SpriteRenderer backRenderer = back.GetComponent<SpriteRenderer>();
        backRenderer.sprite = asset.MainSprite;
        backRenderer.sharedMaterial = asset.Material;
        backRenderer.color = Color.white;
        backRenderer.sortingOrder = backgroundSortingOrder;

        if (asset.ForegroundSprite != null)
        {
            var front = new GameObject("Front", typeof(SpriteRenderer));
            front.transform.SetParent(variant.transform, false);
            front.transform.localPosition = Vector3.zero;
            SpriteRenderer frontRenderer = front.GetComponent<SpriteRenderer>();
            frontRenderer.sprite = asset.ForegroundSprite;
            frontRenderer.color = Color.white;
            frontRenderer.sortingOrder = foregroundSortingOrder;
        }

        return variant;
    }

    private string CreateBattleScene(
        ReDivVariantCardMaterialRestorerWindow.BattleSpriteRestoreResult restored,
        string groupName,
        string prefabPath)
    {
        string scenePath = CombineAssetPath(restored.AssetRoot, groupName + "_BattleScene.unity");
        Scene previousScene = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject backgroundInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

            if (createCamera)
            {
                var cameraObject = new GameObject("BattleBackgroundCamera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
                cameraObject.tag = "MainCamera";
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = restored.Assets
                    .Max(asset => asset.MainSprite.bounds.extents.y);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;

                BattleBackgroundScreenFitter fitter =
                    backgroundInstance.GetComponent<BattleBackgroundScreenFitter>();
                fitter.TargetCamera = camera;
                fitter.Mode = BattleBackgroundScreenFitter.FitMode.Cover;
                fitter.FitNow();
            }

            if (!EditorSceneManager.SaveScene(scene, scenePath))
                throw new InvalidOperationException($"Unity Scene 保存失败：{scenePath}");
            return scenePath;
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, removeScene: true);
            if (previousScene.IsValid() && previousScene.isLoaded)
                SceneManager.SetActiveScene(previousScene);
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

        string commonDirectory = Path.Combine(
            current.FullName,
            AnimationTextureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        string mapPath = Path.Combine(commonDirectory, AnimationTextureMapFileName);
        if ((overwriteExisting || string.IsNullOrWhiteSpace(animationTextureDirectory))
            && Directory.Exists(commonDirectory))
            animationTextureDirectory = commonDirectory;
        if ((overwriteExisting || string.IsNullOrWhiteSpace(animationTextureMapPath)) && File.Exists(mapPath))
            animationTextureMapPath = mapPath;
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
            string selected = EditorUtility.OpenFolderPanel(label, Application.dataPath, string.Empty);
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
        HandlePathDrop(dropRect, ref value, requireDirectory: true, requireAssetsPath: true);
    }

    private static void HandlePathDrop(
        Rect rect,
        ref string value,
        bool requireDirectory,
        bool requireAssetsPath = false)
    {
        Event current = Event.current;
        if (!rect.Contains(current.mousePosition)
            || (current.type != EventType.DragUpdated && current.type != EventType.DragPerform))
            return;

        string path = DragAndDrop.paths.FirstOrDefault();
        bool valid = !string.IsNullOrEmpty(path)
                     && (requireDirectory ? Directory.Exists(path) : File.Exists(path));
        string assetPath = requireAssetsPath && valid ? AbsolutePathToAssetPath(path) : null;
        if (requireAssetsPath)
            valid = !string.IsNullOrEmpty(assetPath);

        DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
        if (current.type == EventType.DragPerform && valid)
        {
            DragAndDrop.AcceptDrag();
            value = requireAssetsPath ? assetPath : path;
        }
        current.Use();
    }

    private static string AbsolutePathToAssetPath(string absolutePath)
    {
        string fullPath = Path.GetFullPath(absolutePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string assetsRoot = Path.GetFullPath(Application.dataPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!fullPath.Equals(assetsRoot, StringComparison.OrdinalIgnoreCase)
            && !fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;
        return "Assets" + fullPath.Substring(assetsRoot.Length).Replace('\\', '/');
    }

    private static string CombineAssetPath(params string[] parts)
    {
        return string.Join("/", parts.Select(part => part.Trim('/', '\\')));
    }
}
