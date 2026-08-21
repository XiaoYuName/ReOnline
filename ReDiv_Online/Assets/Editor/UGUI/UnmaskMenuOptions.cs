using System;
using Coffee.UIExtensions;
using UnityEditor;
using UnityEditor.EventSystems;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UnmaskForUGUI 自带的 GameObject/UI/Unmask 菜单在 Unity 6 下是坏的：
/// UGUI 2.0 把 GameObject 菜单根从 "UI" 改成了 "UI (Canvas)"，而包里
/// ExecuteMenuItem("GameObject/UI/Legacy/Button") 这类写死的旧路径已经不存在，
/// 三个菜单项都会先报 "there is no menu named ..." 再空引用。
///
/// 这里按包里的逻辑重做一份，直接用 DefaultControls 建对象、不依赖菜单路径，
/// 生成的层级和包里的完全一致。包在 Library/PackageCache 里（git 依赖，改了下次拉取会被冲掉），
/// 所以它自带的 GameObject/UI/Unmask 菜单还会留在菜单里，忽略它、用这三项。
/// </summary>
internal static class UnmaskMenuOptions
{
    private const string MenuRoot = "GameObject/UI (Canvas)/Unmask/";

    // 排在 UGUI 的 Event System(2061) 之后、Legacy(2080) 之前
    private const int MenuPriority = 2062;

    private const string UILayerName = "UI";
    private const string StandardSpritePath = "UI/Skin/UISprite.psd";
    private const string BackgroundSpritePath = "UI/Skin/Background.psd";
    private const string InputFieldBackgroundPath = "UI/Skin/InputFieldBackground.psd";
    private const string KnobSpritePath = "UI/Skin/Knob.psd";
    private const string CheckmarkSpritePath = "UI/Skin/Checkmark.psd";
    private const string DropdownArrowSpritePath = "UI/Skin/DropdownArrow.psd";
    private const string MaskSpritePath = "UI/Skin/UIMask.psd";

    private static DefaultControls.Resources s_StandardResources;

    [MenuItem(MenuRoot + "Unmasked Panel", false, MenuPriority)]
    private static void CreateUnmaskedPanel(MenuCommand menuCommand)
    {
        Undo.IncrementCurrentGroup();
        var panel = BuildUnmaskedPanel(menuCommand, GetBuiltinSprite(StandardSpritePath), Image.Type.Sliced);
        Undo.SetCurrentGroupName("Create " + panel.name);
        Selection.activeGameObject = panel;
    }

    [MenuItem(MenuRoot + "Iris Shot", false, MenuPriority + 1)]
    private static void CreateIrisShot(MenuCommand menuCommand)
    {
        Undo.IncrementCurrentGroup();
        var panel = BuildUnmaskedPanel(menuCommand, GetBuiltinSprite(KnobSpritePath), Image.Type.Simple);
        panel.name = "Iris Shot";
        Undo.SetCurrentGroupName("Create " + panel.name);
        Selection.activeGameObject = panel;
    }

    [MenuItem(MenuRoot + "Tutorial Button", false, MenuPriority + 2)]
    private static void CreateTutorialButton(MenuCommand menuCommand)
    {
        Undo.IncrementCurrentGroup();

        var button = CreateWithEditorFactory(DefaultControls.CreateButton);
        button.name = "Tutorial Button";
        PlaceUIElementRoot(button, menuCommand);

        var panel = BuildUnmaskedPanel(menuCommand, GetBuiltinSprite(StandardSpritePath), Image.Type.Sliced);
        var unmask = panel.GetComponentInChildren<Unmask>();
        unmask.fitTarget = button.transform as RectTransform; // 挖的洞跟着按钮走
        unmask.fitOnLateUpdate = true;

        Undo.SetCurrentGroupName("Create " + button.name);
        Selection.activeGameObject = button;
    }

    /// <summary>
    /// 生成的层级与包里 CreateUnmaskedPanel 一致：
    /// Unmasked Panel (Mask + UnmaskRaycastFilter)
    ///   ├ Unmask (Image + Unmask)
    ///   └ Screen (Image，半透明黑底)
    /// </summary>
    private static GameObject BuildUnmaskedPanel(MenuCommand menuCommand, Sprite unmaskSprite, Image.Type spriteType)
    {
        // DefaultControls 的注释提醒过：SetTransformParent 之后再 AddComponent 可能崩，
        // 所以先把三层的组件都加完，最后才挂到 Canvas 上。
        var root = CreateWithEditorFactory(DefaultControls.CreatePanel);
        root.name = "Unmasked Panel";
        root.GetComponent<Image>().sprite = null;
        root.AddComponent<Mask>().showMaskGraphic = false;
        var raycastFilter = root.AddComponent<UnmaskRaycastFilter>();

        var unmaskGo = CreateWithEditorFactory(DefaultControls.CreateImage);
        unmaskGo.name = "Unmask";
        var unmaskImage = unmaskGo.GetComponent<Image>();
        unmaskImage.sprite = unmaskSprite;
        unmaskImage.type = spriteType;
        raycastFilter.targetUnmask = unmaskGo.AddComponent<Unmask>();

        var screenGo = CreateWithEditorFactory(DefaultControls.CreatePanel);
        screenGo.name = "Screen";
        var screenImage = screenGo.GetComponent<Image>();
        screenImage.sprite = null;
        screenImage.color = new Color(0f, 0f, 0f, 0.8f);

        SetParentAndAlign(unmaskGo, root);
        SetParentAndAlign(screenGo, root);

        PlaceUIElementRoot(root, menuCommand);

        StretchToParent(root);
        StretchToParent(screenGo);
        return root;
    }

    /// <summary>
    /// Panel 是在还没有父节点时建的，Undo.SetTransformParent 会按世界坐标把当时的 0 尺寸固化下来，
    /// 所以挂好之后得把拉伸重置一次（UGUI 自己的 AddPanel 里也有这一步）
    /// </summary>
    private static void StretchToParent(GameObject panel)
    {
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }

    // 下面几个是 UnityEditor.UI.MenuOptions 里同名方法的移植 —— 那个类是 internal，外面调不到

    private static void PlaceUIElementRoot(GameObject element, MenuCommand menuCommand)
    {
        var parent = menuCommand.context as GameObject;
        if (parent == null)
        {
            parent = GetOrCreateCanvasGameObject();

            // 预制体模式下 Canvas 必须是预制体内容的一部分，否则退回预制体根节点
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && !prefabStage.IsPartOfPrefabContents(parent))
                parent = prefabStage.prefabContentsRoot;
        }

        if (parent.GetComponentsInParent<Canvas>(true).Length == 0)
        {
            // 目标节点不在任何 Canvas 下，就地建一个 Canvas 再挂进去
            var canvas = CreateNewUI();
            Undo.SetTransformParent(canvas.transform, parent.transform, "");
            parent = canvas;
        }

        GameObjectUtility.EnsureUniqueNameForSibling(element);
        SetParentAndAlign(element, parent);

        // 保证注册之后对对象做的修改也进同一个 Undo
        Undo.RegisterFullObjectHierarchyUndo(parent, "");
    }

    private static void SetParentAndAlign(GameObject child, GameObject parent)
    {
        Undo.SetTransformParent(child.transform, parent.transform, "");

        if (child.transform is RectTransform rectTransform)
        {
            rectTransform.anchoredPosition = Vector2.zero;
            var localPosition = rectTransform.localPosition;
            localPosition.z = 0f;
            rectTransform.localPosition = localPosition;
        }
        else
        {
            child.transform.localPosition = Vector3.zero;
        }

        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;
        SetLayerRecursively(child, parent.layer);
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        var t = go.transform;
        for (var i = 0; i < t.childCount; i++)
            SetLayerRecursively(t.GetChild(i).gameObject, layer);
    }

    private static GameObject GetOrCreateCanvasGameObject()
    {
        // 优先用选中对象所在的 Canvas
        var selected = Selection.activeGameObject;
        var canvas = selected != null ? selected.GetComponentInParent<Canvas>() : null;
        if (IsValidCanvas(canvas))
            return canvas.gameObject;

        // 其次用当前 Stage 里任意一个可用的 Canvas
        foreach (var candidate in StageUtility.GetCurrentStageHandle().FindComponentsOfType<Canvas>())
        {
            if (IsValidCanvas(candidate))
                return candidate.gameObject;
        }

        return CreateNewUI();
    }

    private static bool IsValidCanvas(Canvas canvas)
    {
        if (canvas == null || !canvas.gameObject.activeInHierarchy)
            return false;

        if (EditorUtility.IsPersistent(canvas) || (canvas.hideFlags & HideFlags.HideInHierarchy) != 0)
            return false;

        return StageUtility.GetStageHandle(canvas.gameObject) == StageUtility.GetCurrentStageHandle();
    }

    private static GameObject CreateNewUI()
    {
        var root = ObjectFactory.CreateGameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var uiLayer = LayerMask.NameToLayer(UILayerName);
        if (uiLayer != -1)
            root.layer = uiLayer;
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        StageUtility.PlaceGameObjectInCurrentStage(root);

        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null)
            Undo.SetTransformParent(root.transform, prefabStage.prefabContentsRoot.transform, "");
        else
            CreateEventSystemIfNeeded(); // 预制体模式是临时场景，不往里塞 EventSystem

        return root;
    }

    private static void CreateEventSystemIfNeeded()
    {
        if (StageUtility.GetCurrentStageHandle().FindComponentOfType<EventSystem>() != null)
            return;

        var go = ObjectFactory.CreateGameObject("EventSystem");
        StageUtility.PlaceGameObjectInCurrentStage(go);
        ObjectFactory.AddComponent<EventSystem>(go);
        InputModuleComponentFactory.AddInputModule(go); // 装了 Input System 时会自动换成对应的 InputModule
        Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
    }

    /// <summary>
    /// DefaultControls 默认用 new GameObject 建对象，编辑器里要换成 ObjectFactory 才能走 Preset 和 Undo
    /// </summary>
    private static GameObject CreateWithEditorFactory(Func<DefaultControls.Resources, GameObject> create)
    {
        var previous = DefaultControls.factory;
        DefaultControls.factory = EditorFactory.Default;
        try
        {
            return create(GetStandardResources());
        }
        finally
        {
            DefaultControls.factory = previous;
        }
    }

    private static DefaultControls.Resources GetStandardResources()
    {
        if (s_StandardResources.standard == null)
        {
            s_StandardResources.standard = GetBuiltinSprite(StandardSpritePath);
            s_StandardResources.background = GetBuiltinSprite(BackgroundSpritePath);
            s_StandardResources.inputField = GetBuiltinSprite(InputFieldBackgroundPath);
            s_StandardResources.knob = GetBuiltinSprite(KnobSpritePath);
            s_StandardResources.checkmark = GetBuiltinSprite(CheckmarkSpritePath);
            s_StandardResources.dropdown = GetBuiltinSprite(DropdownArrowSpritePath);
            s_StandardResources.mask = GetBuiltinSprite(MaskSpritePath);
        }

        return s_StandardResources;
    }

    private static Sprite GetBuiltinSprite(string path)
    {
        return AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
    }

    private class EditorFactory : DefaultControls.IFactoryControls
    {
        public static readonly EditorFactory Default = new EditorFactory();

        public GameObject CreateGameObject(string name, params Type[] components)
        {
            return ObjectFactory.CreateGameObject(name, components);
        }
    }
}
