#if UNITY_EDITOR

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace XFramework
{
    /// <summary>
    /// 副本摆位窗口 —— **在场景里对着真实界面拖格子，位置写回 `Dungeon.xlsx`**。
    ///
    /// 和 <see cref="TownNpcPlacementWindow"/> / <see cref="TownTriggerPlacementWindow"/>
    /// 是同一套做法（选目标 → 打开预览 → Scene 视图里拖 → 写回 Excel），
    /// 解决的也是同一个问题：坐标在 Excel 里只能填数字、看不到画面。
    ///
    /// <code>
    /// Dungeon.xlsx --(Luban 导出)--> tbdungeon.json --(本窗口读)--> Canvas 里的预览界面
    ///              &lt;--(ExcelTable.ps1 AddRows/UpdateRows/DeleteRows)-- 「写入 Excel」按钮
    /// </code>
    ///
    /// ⚠️⚠️ **这一个和另外两个有个本质区别：这里拖的是 UI 坐标，不是世界坐标。**
    /// 所以预览**必须挂在真实的 Canvas 下**（`GameManager/UISystem/UILayout/UIPanel`，
    /// 编辑器下场景里就有），而不是像 NPC / 触发器那样丢在世界原点 ——
    /// UI 坐标要经过 CanvasScaler 才有意义，挂错地方拖出来的数字是废的。
    ///
    /// 写回的是格子根节点的 `anchoredPosition`：预制体的 anchor 和 pivot 都是 (0.5, 0.5)，
    /// 所以那个值就是「相对屏幕中心的偏移」，和分辨率无关（canvas 的参考分辨率是 1920×1080）。
    ///
    /// ⚠️ 预览对象全带 <see cref="HideFlags.DontSave"/>：不会被存进场景，进 Play 就没。
    ///
    /// ⚠️ 副本两张表都是**纯客户端**的（`group` 全是 `c`），改完**不用 `spacetime publish`**。
    /// </summary>
    public class DungeonSlotPlacementWindow : OdinEditorWindow
    {
        private const string DungeonTableJson =
            "Assets/AddressableAssets/Remote/Configs/LubanJson/tbdungeon.json";

        private const string DungeonAreaTableJson =
            "Assets/AddressableAssets/Remote/Configs/LubanJson/tbdungeonarea.json";

        private const string DungeonWorkbook = "ExcelTool/LubanTools/DataTables/Datas/Dungeon.xlsx";
        private const string DungeonSheet = "Dungeon";

        private const string PopDungeonUIPrefab =
            "Assets/AddressableAssets/Remote/Prefabs/UGUI/PopDungeonUI/PopDungeonUI.prefab";

        private const string DungeonSlotPrefab =
            "Assets/AddressableAssets/Remote/Prefabs/UGUI/PopDungeonUI/DungeonSlot.prefab";

        /// <summary>预览挂在这个 Canvas 层下面 —— **UI 坐标必须在真实 Canvas 里才有意义**。</summary>
        private const string UIPanelPath = "GameManager/UISystem/UILayout/UIPanel";

        private const string PreviewRootName = "[副本摆位预览]";

        [MenuItem("Tools/XFramework/配置/副本摆位", false, 203)]
        private static void Open()
        {
            var window = GetWindow<DungeonSlotPlacementWindow>("副本摆位");
            window.minSize = new Vector2(980, 560);
            window.Reload();
        }

        // ------------------------------------------------------------------
        // 选区域
        // ------------------------------------------------------------------

        [TitleGroup("副本区域", "副本按区域分组；预览会把这个区域的背景和它下面所有副本格子摆出来",
            TitleAlignments.Left)]
        [HorizontalGroup("副本区域/行")]
        [LabelText("区域"), LabelWidth(50)]
        [ValueDropdown(nameof(AreaDropdown))]
        [OnValueChanged(nameof(Reload))]
        public int AreaId = 31001;

        [HorizontalGroup("副本区域/行", Width = 110)]
        [Button("打开预览", ButtonSizes.Medium), GUIColor(0.4f, 0.85f, 0.45f)]
        [DisableIf(nameof(IsPreviewOpen))]
        private void OpenPreviewButton() => BuildPreview();

        [HorizontalGroup("副本区域/行", Width = 110)]
        [Button("关闭预览", ButtonSizes.Medium)]
        [EnableIf(nameof(IsPreviewOpen))]
        private void ClosePreviewButton() => ClearPreview();

        [TitleGroup("副本区域")]
        [ShowIf(nameof(IsPreviewOpen))]
        [InfoBox("预览已打开：在 **Scene 视图**里直接拖格子（列表里点「选中」能跳过去）。 " +
                 "拖的是 **UI 坐标**（相对屏幕中心），所以预览挂在真实 Canvas 下 —— " +
                 "Scene 视图里想看得清就双击预览根节点聚焦过去。 " +
                 "拖完点「写入 Excel」。预览对象不会存进场景。", InfoMessageType.Info)]
        [ShowInInspector, HideLabel, DisplayAsString(false)]
        private string PreviewHint => string.Empty;

        // ------------------------------------------------------------------
        // 副本列表
        // ------------------------------------------------------------------

        [TitleGroup("副本", "一行一个格子。删掉行 = 写回时从 Excel 删掉那一行", TitleAlignments.Left)]
        [TableList(AlwaysExpanded = true, DrawScrollView = false, ShowIndexLabels = false)]
        [OnValueChanged(nameof(HandleRowChanged), IncludeChildren = true)]
        public List<DungeonRow> Rows = new List<DungeonRow>();

        [TitleGroup("副本")]
        [HorizontalGroup("副本/按钮")]
        [Button("新增副本", ButtonSizes.Medium)]
        private void AddDungeon()
        {
            int nextId = allIds.Count == 0 ? 31001 : allIds.Max() + 1;

            var row = new DungeonRow
            {
                DungeonId = nextId,
                Name = "新副本",
                MaxStar = 6,
                SortOrder = Rows.Count + 1,
                PosX = 0f,
                PosY = 0f,
            };

            allIds.Add(nextId);
            Rows.Add(row);
            dirty = true;

            if (IsPreviewOpen())
            {
                CreateMarker(row);
                Select(row);
            }
        }

        [HorizontalGroup("副本/按钮")]
        [Button("按当前顺序横排一次", ButtonSizes.Medium)]
        [PropertyTooltip("把所有格子按 SortOrder 均匀横排在屏幕中间 —— 只是给个起点，摆完还是要自己拖")]
        private void LayoutInRow()
        {
            List<DungeonRow> ordered = Rows.OrderBy(r => r.SortOrder).ThenBy(r => r.DungeonId).ToList();

            if (ordered.Count == 0)
            {
                return;
            }

            // 格子是 360×200 的定尺寸美术，留 24 的间距
            const float step = 360f + 24f;
            float start = -(ordered.Count - 1) * step * 0.5f;

            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].PosX = start + i * step;
                ordered[i].PosY = 0f;
                ordered[i].ApplyToMarker();
            }

            dirty = true;
        }

        [HorizontalGroup("副本/按钮")]
        [Button("重新读取（丢弃未写入的改动）", ButtonSizes.Medium)]
        private void ReloadButton() => Reload();

        [TitleGroup("写回")]
        [ShowIf(nameof(dirty))]
        [InfoBox("有改动还没写回 Excel。", InfoMessageType.Warning)]
        [ShowInInspector, HideLabel, DisplayAsString(false)]
        private string DirtyHint => string.Empty;

        [TitleGroup("写回")]
        [LabelText("写完自动重新导出配置")]
        public bool ExportAfterWrite = true;

        [TitleGroup("写回")]
        [Button("写入 Excel", ButtonSizes.Large), GUIColor(0.35f, 0.75f, 1f)]
        private void WriteToExcelButton() => WriteToExcel();

        // ------------------------------------------------------------------
        // 状态
        // ------------------------------------------------------------------

        /// <summary>上次读表时**所有区域**的 DungeonId，用来分配新 id。</summary>
        private readonly HashSet<int> allIds = new HashSet<int>();

        /// <summary>上次读表时**当前区域**有哪些 DungeonId。写回时用它算出「被删掉的行」。</summary>
        private readonly HashSet<int> loadedIds = new HashSet<int>();

        [SerializeField, HideInInspector]
        private GameObject previewRoot;

        /// <summary>预览界面里放格子的那个节点（`UIMask/Contents`）。</summary>
        [SerializeField, HideInInspector]
        private Transform previewContents;

        /// <summary>行内那个「选中」按钮要能回调窗口，所以留一个当前窗口的引用。</summary>
        private static DungeonSlotPlacementWindow instance;

        private bool dirty;

        private bool IsPreviewOpen() => previewRoot != null;

        private void HandleRowChanged()
        {
            dirty = true;

            foreach (DungeonRow row in Rows)
            {
                row.ApplyToMarker();
            }
        }

        // ------------------------------------------------------------------
        // 读表
        // ------------------------------------------------------------------

        protected override void OnEnable()
        {
            base.OnEnable();
            instance = this;
            Reload();
        }

        private static void SelectRow(DungeonRow row) => instance?.Select(row);

        private void OnDestroy()
        {
            ClearPreview();
        }

        private void Reload()
        {
            bool wasOpen = IsPreviewOpen();

            ClearPreview();
            Rows.Clear();
            allIds.Clear();
            loadedIds.Clear();
            dirty = false;

            foreach (JObject item in LoadArray(DungeonTableJson))
            {
                int id = item.Value<int>("DungeonId");
                allIds.Add(id);

                if (item.Value<int>("AreaId") != AreaId)
                {
                    continue;
                }

                loadedIds.Add(id);

                Rows.Add(new DungeonRow
                {
                    DungeonId = id,
                    Name = item.Value<string>("Name") ?? string.Empty,
                    ThumbnailKey = item.Value<string>("ThumbnailKey") ?? string.Empty,
                    MaxStar = item.Value<int>("MaxStar"),
                    SortOrder = item.Value<int>("SortOrder"),
                    PosX = item.Value<float>("PosX"),
                    PosY = item.Value<float>("PosY"),
                });
            }

            Rows = Rows.OrderBy(r => r.SortOrder).ThenBy(r => r.DungeonId).ToList();

            if (wasOpen)
            {
                BuildPreview();
            }
        }

        internal static JArray LoadArray(string assetPath)
        {
            string full = Path.Combine(ExcelTableRunner.ProjectRoot, assetPath);

            if (!File.Exists(full))
            {
                Debug.LogWarning($"[副本摆位] 找不到 {assetPath} —— 先跑一次配置导出（F6）");
                return new JArray();
            }

            return JArray.Parse(File.ReadAllText(full));
        }

        private static IEnumerable<ValueDropdownItem<int>> AreaDropdown()
        {
            foreach (JObject area in LoadArray(DungeonAreaTableJson))
            {
                int id = area.Value<int>("AreaId");
                yield return new ValueDropdownItem<int>($"{id} {area.Value<string>("Name")}", id);
            }
        }

        /// <summary>当前区域的背景预制体路径。取不到是空串。</summary>
        private string AreaBackgroundKey()
        {
            foreach (JObject area in LoadArray(DungeonAreaTableJson))
            {
                if (area.Value<int>("AreaId") == AreaId)
                {
                    return area.Value<string>("BackgroundKey") ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private string AreaName()
        {
            foreach (JObject area in LoadArray(DungeonAreaTableJson))
            {
                if (area.Value<int>("AreaId") == AreaId)
                {
                    return area.Value<string>("Name") ?? string.Empty;
                }
            }

            return string.Empty;
        }

        // ------------------------------------------------------------------
        // 预览（挂在真实 Canvas 下）
        // ------------------------------------------------------------------

        /// <summary>
        /// 把真实的副本界面摆到 Canvas 里：界面预制体 + 区域背景 + 这个区域的所有格子。
        ///
        /// **必须挂在 `UILayout/UIPanel` 下**（见类注释）——
        /// 那是运行时 `PopDungeonUI` 真正待的地方，所以拖出来的 `anchoredPosition`
        /// 和游戏里所见完全一致。
        /// </summary>
        private void BuildPreview()
        {
            ClearPreview();

            GameObject panel = GameObject.Find(UIPanelPath);

            if (panel == null)
            {
                EditorUtility.DisplayDialog("打不开预览",
                    $"场景里找不到 {UIPanelPath}。\n\n" +
                    "副本格子的坐标是 UI 坐标，必须挂在真实 Canvas 下才有意义 —— " +
                    "请先打开 Root 场景。", "知道了");
                return;
            }

            var uiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PopDungeonUIPrefab);

            if (uiPrefab == null)
            {
                Debug.LogError($"[副本摆位] 找不到界面预制体 {PopDungeonUIPrefab}");
                return;
            }

            previewRoot = Instantiate(uiPrefab, panel.transform);
            previewRoot.name = PreviewRootName;

            var rect = previewRoot.transform as RectTransform;
            Stretch(rect);

            previewContents = previewRoot.transform.Find("UIMask/Contents");

            if (previewContents == null)
            {
                Debug.LogError("[副本摆位] 预览界面里找不到 UIMask/Contents");
            }

            SetPreviewTitle(AreaName());
            BuildPreviewBackground();

            foreach (DungeonRow row in Rows)
            {
                CreateMarker(row);
            }

            MarkDontSave(previewRoot);

            Selection.activeGameObject = previewRoot;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        private void SetPreviewTitle(string title)
        {
            Transform text = previewRoot.transform.Find("UIMask/Background/Title/TitleTex");

            if (text != null && text.TryGetComponent(out TextMeshProUGUI tmp))
            {
                tmp.text = title ?? string.Empty;
            }
        }

        /// <summary>
        /// 把区域背景塞进预览（和运行时一样：`UIMask` 的第一个子节点 + 拉满整屏）。
        /// </summary>
        private void BuildPreviewBackground()
        {
            string key = AreaBackgroundKey();

            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(key);

            if (prefab == null)
            {
                Debug.LogWarning($"[副本摆位] 区域 {AreaId} 的背景加载不出来（{key}）—— 只摆格子，没有底图");
                return;
            }

            Transform mask = previewRoot.transform.Find("UIMask");

            if (mask == null)
            {
                return;
            }

            GameObject background = Instantiate(prefab, mask);
            background.name = prefab.name;
            background.transform.SetAsFirstSibling();
            Stretch(background.transform as RectTransform);
        }

        /// <summary>
        /// 给一行生成场景里那个可拖的格子。用**真实的 `DungeonSlot` 预制体** ——
        /// 略缩图和名字也填上，这样拖的时候看到的就是游戏里的样子。
        ///
        /// ⚠️ 预制体上的 `DungeonSlot` 组件在编辑器下**不会跑 `Init`**
        /// （`UIBase` 没有 `[ExecuteAlways]`），所以这里手动设 `RawImage.texture` 和名字文本。
        /// </summary>
        private void CreateMarker(DungeonRow row)
        {
            if (previewContents == null)
            {
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DungeonSlotPrefab);

            if (prefab == null)
            {
                Debug.LogError($"[副本摆位] 找不到格子预制体 {DungeonSlotPrefab}");
                return;
            }

            GameObject marker = Instantiate(prefab, previewContents);
            marker.name = $"Dungeon_{row.DungeonId}_{row.Name}";

            Transform nameText = marker.transform.Find("DungeonNameTex");

            if (nameText != null && nameText.TryGetComponent(out TextMeshProUGUI tmp))
            {
                tmp.text = row.Name ?? string.Empty;
            }

            Transform raw = marker.transform.Find("Back/RawImage");

            if (raw != null && raw.TryGetComponent(out RawImage rawImage) &&
                !string.IsNullOrEmpty(row.ThumbnailKey))
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture>(row.ThumbnailKey);

                if (texture != null)
                {
                    rawImage.texture = texture;
                }
            }

            row.Marker = marker.transform as RectTransform;
            row.ApplyToMarker();

            MarkDontSave(marker);
        }

        /// <summary>拉满整屏（界面根和区域背景都要）。</summary>
        private static void Stretch(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localPosition = new Vector3(0f, 0f, rect.localPosition.z);
        }

        private static void MarkDontSave(GameObject go)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.hideFlags = HideFlags.DontSave;
            }
        }

        private void ClearPreview()
        {
            foreach (DungeonRow row in Rows)
            {
                row.Marker = null;
            }

            if (previewRoot != null)
            {
                DestroyImmediate(previewRoot);
                previewRoot = null;
            }

            previewContents = null;

            // 窗口重开 / 域重载之后引用可能丢了，按名字兜一次底
            foreach (GameObject stray in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (stray.name == PreviewRootName && stray.scene.IsValid())
                {
                    DestroyImmediate(stray);
                }
            }
        }

        private void Select(DungeonRow row)
        {
            if (row.Marker == null)
            {
                return;
            }

            Selection.activeGameObject = row.Marker.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        // ------------------------------------------------------------------
        // 写回 Excel
        // ------------------------------------------------------------------

        /// <summary>
        /// 写回 `Dungeon.xlsx`：删掉的行 `DeleteRows`、老行 `UpdateRows`、新行 `AddRows`。
        /// **顺序不能乱**：先删再改再加 —— 反过来新加的 id 可能和待删的撞上。
        ///
        /// 坐标从**场景里那个格子**的 `anchoredPosition` 读（预览没开就用列表里的旧值）。
        /// </summary>
        private void WriteToExcel()
        {
            foreach (DungeonRow row in Rows)
            {
                if (row.Marker != null)
                {
                    Vector2 p = row.Marker.anchoredPosition;
                    row.PosX = Round(p.x);
                    row.PosY = Round(p.y);
                }
            }

            string invalid = Validate();

            if (invalid != null)
            {
                EditorUtility.DisplayDialog("写入失败", invalid, "知道了");
                return;
            }

            var currentIds = new HashSet<int>(Rows.Select(r => r.DungeonId));
            List<int> removed = loadedIds.Where(id => !currentIds.Contains(id)).ToList();

            if (removed.Count > 0 && !EditorUtility.DisplayDialog("确认删除",
                    $"这 {removed.Count} 个副本会从 Excel 里删掉：{string.Join(", ", removed)}", "删", "取消"))
            {
                return;
            }

            if (removed.Count > 0 &&
                !ExcelTableRunner.DeleteRows(DungeonWorkbook, DungeonSheet,
                    string.Join(",", removed), out string delOutput))
            {
                Fail("删除行失败", delOutput);
                return;
            }

            var updates = new JArray();
            var adds = new JArray();

            foreach (DungeonRow row in Rows)
            {
                var payload = new JObject
                {
                    ["DungeonId"] = row.DungeonId,
                    ["AreaId"] = AreaId,
                    ["Name"] = row.Name ?? string.Empty,
                    ["ThumbnailKey"] = row.ThumbnailKey ?? string.Empty,
                    ["MaxStar"] = Mathf.Clamp(row.MaxStar, 1, 6),
                    ["SortOrder"] = row.SortOrder,
                    ["PosX"] = row.PosX,
                    ["PosY"] = row.PosY,
                };

                if (loadedIds.Contains(row.DungeonId))
                {
                    updates.Add(payload);
                }
                else
                {
                    adds.Add(payload);
                }
            }

            if (updates.Count > 0)
            {
                string json = ExcelTableRunner.WriteTempJson("rediv_dungeon_update.json", updates.ToString());

                if (!ExcelTableRunner.UpdateRows(DungeonWorkbook, DungeonSheet, json,
                        "DungeonId", out string output))
                {
                    Fail("改行失败", output);
                    return;
                }
            }

            if (adds.Count > 0)
            {
                string json = ExcelTableRunner.WriteTempJson("rediv_dungeon_add.json", adds.ToString());

                if (!ExcelTableRunner.AddRows(DungeonWorkbook, DungeonSheet, json, out string output))
                {
                    Fail("加行失败", output);
                    return;
                }
            }

            dirty = false;
            Debug.Log($"[副本摆位] 已写入 {DungeonWorkbook}：" +
                      $"新增 {adds.Count} / 修改 {updates.Count} / 删除 {removed.Count}");

            if (!ExportAfterWrite)
            {
                // 成功路径**不弹框**（和另外两个摆位窗口一致）
                Debug.Log("[副本摆位] 记得跑一次配置导出（F6）才生效。副本表是纯客户端的，不用 publish。");
                return;
            }

            bool ok = ConfigTools.ExportForExternalTool();
            AssetDatabase.Refresh();
            Reload();

            if (ok)
            {
                Debug.Log("[副本摆位] 配置已重新导出。副本表是纯客户端的（group=c），不用 spacetime publish。");
            }
            else
            {
                EditorUtility.DisplayDialog("导出失败",
                    "Excel 已经写好了，但重新导出配置失败 —— 看 Console 里 Luban 的报错。", "知道了");
            }
        }

        private string Validate()
        {
            var duplicated = Rows.GroupBy(r => r.DungeonId).Where(g => g.Count() > 1)
                .Select(g => g.Key).ToList();

            if (duplicated.Count > 0)
            {
                return $"DungeonId 重复了：{string.Join(", ", duplicated)}。id 要全局唯一。";
            }

            foreach (DungeonRow row in Rows)
            {
                if (row.MaxStar < 1 || row.MaxStar > 6)
                {
                    return $"副本 {row.DungeonId}（{row.Name}）的 MaxStar 是 {row.MaxStar}，只能是 1~6。";
                }

                if (string.IsNullOrEmpty(row.Name))
                {
                    return $"副本 {row.DungeonId} 没填名字。";
                }
            }

            return null;
        }

        private static void Fail(string title, string output)
        {
            EditorUtility.DisplayDialog(title, "ExcelTable.ps1 报错了，Excel 没有被改动。\n\n" + output, "知道了");
        }

        /// <summary>
        /// UI 坐标**取整**就够 —— 那是像素（canvas 参考分辨率下的），小数没意义，
        /// 而且表里看着干净。
        /// </summary>
        private static float Round(float value) => Mathf.Round(value);

        // ------------------------------------------------------------------
        // 表格里的一行
        // ------------------------------------------------------------------

        /// <summary>
        /// 一个副本。<see cref="ThumbnailKey"/> 存的是路径，<see cref="ThumbnailAsset"/> 只是
        /// **拖拽入口** —— 路径永远由工具算，手打不了（和另外两个摆位窗口一个做法）。
        /// </summary>
        public class DungeonRow
        {
            [TableColumnWidth(70)]
            [LabelText("ID")]
            public int DungeonId;

            [TableColumnWidth(120)]
            [LabelText("副本名")]
            public string Name;

            [TableColumnWidth(70)]
            [LabelText("最高星")]
            [PropertyRange(1, 6)]
            [PropertyTooltip("配置允许的最高挑战星级。实际能选到几星还要看通关进度")]
            public int MaxStar = 6;

            [TableColumnWidth(65)]
            [LabelText("排序")]
            public int SortOrder;

            [HideInInspector] public float PosX;
            [HideInInspector] public float PosY;
            [HideInInspector] public string ThumbnailKey;

            /// <summary>场景里那个可拖的格子。预览没开时是 null。</summary>
            [HideInInspector] public RectTransform Marker;

            [TableColumnWidth(170)]
            [LabelText("略缩图")]
            [ShowInInspector]
            [AssetsOnly]
            public Texture ThumbnailAsset
            {
                get => string.IsNullOrEmpty(ThumbnailKey)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Texture>(ThumbnailKey);
                set => ThumbnailKey = value == null ? string.Empty : AssetDatabase.GetAssetPath(value);
            }

            [TableColumnWidth(70)]
            [Button("选中")]
            [PropertyTooltip("在 Scene 视图里选中并聚焦到它，然后直接拖")]
            private void SelectInScene() => SelectRow(this);

            [TableColumnWidth(130)]
            [LabelText("UI 坐标")]
            [ShowInInspector, ReadOnly]
            [PropertyTooltip("相对屏幕中心的偏移（canvas 参考分辨率 1920×1080 下的像素）")]
            public string Position => Marker == null
                ? $"({PosX:F0}, {PosY:F0})"
                : $"({Marker.anchoredPosition.x:F0}, {Marker.anchoredPosition.y:F0})";

            /// <summary>把表里的值推到场景里那个格子上（坐标 + 名字文本）。</summary>
            public void ApplyToMarker()
            {
                if (Marker == null)
                {
                    return;
                }

                Marker.anchoredPosition = new Vector2(PosX, PosY);
                Marker.gameObject.name = $"Dungeon_{DungeonId}_{Name}";

                Transform nameText = Marker.Find("DungeonNameTex");

                if (nameText != null && nameText.TryGetComponent(out TextMeshProUGUI tmp))
                {
                    tmp.text = Name ?? string.Empty;
                }
            }
        }
    }
}

#endif
