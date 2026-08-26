#if UNITY_EDITOR

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace XFramework
{
    /// <summary>
    /// NPC 摆位窗口 —— **在场景里对着背景拖，位置写回 `TownNpc.xlsx`**。
    ///
    /// 解决的问题：`TownNpc` 表里的 `PosX` / `PosY` 是**世界坐标**，在 Excel 里只能填数字、
    /// 看不到背景，得「填一个数 → 导出 → 进 Play 看一眼 → 再改」来回试。
    /// 这里直接把那张背景和 NPC 生成到场景里，用 Unity 自己的移动工具拖，拖完一键写回。
    ///
    /// 数据流（和角色资源那套一个思路，Excel 仍然是唯一真相源）：
    /// <code>
    /// TownNpc.xlsx --(Luban 导出)--> tbtownnpc.json --(本窗口读)--> 场景里的预览对象
    ///              &lt;--(ExcelTable.ps1 AddRows/UpdateRows/DeleteRows)-- 「写入 Excel」按钮
    /// </code>
    ///
    /// ⚠️ 窗口显示的是**上次导出**的内容。有人绕过窗口直接改了 Excel 又没导出的话这里看不到。
    ///
    /// ⚠️ 预览对象全都带 <see cref="HideFlags.DontSave"/>：**不会被存进场景**，
    /// 进 Play 或重开场景就没了。所以别在预览对象上挂任何想留下来的东西。
    /// </summary>
    public class TownNpcPlacementWindow : OdinEditorWindow
    {
        private const string NpcTableJson = "Assets/AddressableAssets/Remote/Configs/LubanJson/tbtownnpc.json";
        private const string TownTableJson = "Assets/AddressableAssets/Remote/Configs/LubanJson/tbtown.json";

        private const string NpcWorkbook = "ExcelTool/LubanTools/DataTables/Datas/TownNpc.xlsx";
        private const string NpcSheet = "TownNpc";

        private const string PreviewRootName = "[NPC摆位预览]";

        [MenuItem("Tools/XFramework/配置/NPC 摆位", false, 201)]
        private static void Open()
        {
            var window = GetWindow<TownNpcPlacementWindow>("NPC 摆位");
            window.minSize = new Vector2(900, 560);
            window.Reload();
        }

        // ------------------------------------------------------------------
        // 选城镇 / 时段
        // ------------------------------------------------------------------

        [TitleGroup("城镇", "NPC 是按城镇分的；时段只影响预览用哪张背景，不影响数据", TitleAlignments.Left)]
        [HorizontalGroup("城镇/行")]
        [LabelText("城镇"), LabelWidth(50)]
        [ValueDropdown(nameof(TownDropdown))]
        [OnValueChanged(nameof(Reload))]
        public int TownId = 1;

        [HorizontalGroup("城镇/行")]
        [LabelText("预览背景"), LabelWidth(70)]
        [ValueDropdown(nameof(BandDropdown))]
        [OnValueChanged(nameof(RebuildPreviewIfOpen))]
        public int BandId = 1;

        [HorizontalGroup("城镇/行", Width = 110)]
        [Button("打开预览", ButtonSizes.Medium), GUIColor(0.4f, 0.85f, 0.45f)]
        [DisableIf(nameof(IsPreviewOpen))]
        private void OpenPreviewButton() => BuildPreview();

        [HorizontalGroup("城镇/行", Width = 110)]
        [Button("关闭预览", ButtonSizes.Medium)]
        [EnableIf(nameof(IsPreviewOpen))]
        private void ClosePreviewButton() => ClearPreview();

        [TitleGroup("城镇")]
        [ShowIf(nameof(IsPreviewOpen))]
        [InfoBox("预览已打开：在 **Scene 视图**里直接拖 NPC（选中行点「选中」能跳过去）。" +
                 "拖完点下面的「写入 Excel」。预览对象不会存进场景。", InfoMessageType.Info)]
        [ShowInInspector, HideLabel, DisplayAsString(false)]
        private string PreviewHint => string.Empty;

        // ------------------------------------------------------------------
        // NPC 列表
        // ------------------------------------------------------------------

        [TitleGroup("NPC", "一行一个。删掉行 = 写回时从 Excel 删掉那一行", TitleAlignments.Left)]
        [TableList(AlwaysExpanded = true, DrawScrollView = false, ShowIndexLabels = false)]
        [OnValueChanged(nameof(MarkDirty), IncludeChildren = true)]
        public List<NpcRow> Rows = new List<NpcRow>();

        [TitleGroup("NPC")]
        [HorizontalGroup("NPC/按钮")]
        [Button("新增 NPC", ButtonSizes.Medium)]
        private void AddNpc()
        {
            int nextId = allIds.Count == 0 ? 1 : allIds.Max() + 1;

            var row = new NpcRow
            {
                NpcId = nextId,
                Name = "新NPC",
                Facing = 1,
                PosX = 0f,
                PosY = -4f,
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

        [HorizontalGroup("NPC/按钮")]
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

        /// <summary>上次读表时**所有城镇**的 NpcId，用来分配新 id 和判断哪些被删了。</summary>
        private readonly HashSet<int> allIds = new HashSet<int>();

        /// <summary>上次读表时**当前城镇**有哪些 NpcId。写回时用它算出「被删掉的行」。</summary>
        private readonly HashSet<int> loadedIds = new HashSet<int>();

        [SerializeField, HideInInspector]
        private GameObject previewRoot;

        /// <summary>行内那个「选中」按钮要能回调窗口，所以留一个当前窗口的引用。</summary>
        private static TownNpcPlacementWindow instance;

        private bool dirty;

        private bool IsPreviewOpen() => previewRoot != null;

        private void MarkDirty() => dirty = true;

        // ------------------------------------------------------------------
        // 读表
        // ------------------------------------------------------------------

        protected override void OnEnable()
        {
            base.OnEnable();
            instance = this;
            Reload();
        }

        /// <summary>给行内的「选中」按钮用。</summary>
        private static void SelectRow(NpcRow row) => instance?.Select(row);

        private void OnDestroy()
        {
            // 窗口关掉就把预览收干净，别在场景里留一堆孤儿
            ClearPreview();
        }

        /// <summary>从 Luban 导出的 json 重新读一遍（会丢掉没写回的改动）。</summary>
        private void Reload()
        {
            bool wasOpen = IsPreviewOpen();

            ClearPreview();
            Rows.Clear();
            allIds.Clear();
            loadedIds.Clear();
            dirty = false;

            foreach (JObject item in LoadArray(NpcTableJson))
            {
                int id = item.Value<int>("NpcId");
                allIds.Add(id);

                if (item.Value<int>("TownId") != TownId)
                {
                    continue;
                }

                loadedIds.Add(id);

                Rows.Add(new NpcRow
                {
                    NpcId = id,
                    Name = item.Value<string>("Name") ?? string.Empty,
                    PosX = item.Value<float>("PosX"),
                    PosY = item.Value<float>("PosY"),
                    Facing = item.Value<int>("Facing") == -1 ? -1 : 1,
                    SkeletonTown = item.Value<string>("SkeletonTown") ?? string.Empty,
                });
            }

            Rows = Rows.OrderBy(r => r.NpcId).ToList();

            if (wasOpen)
            {
                BuildPreview();
            }
        }

        private static JArray LoadArray(string assetPath)
        {
            string full = Path.Combine(ExcelTableRunner.ProjectRoot, assetPath);

            if (!File.Exists(full))
            {
                Debug.LogWarning($"[NPC摆位] 找不到 {assetPath} —— 先跑一次配置导出（F6）");
                return new JArray();
            }

            return JArray.Parse(File.ReadAllText(full));
        }

        private IEnumerable<ValueDropdownItem<int>> TownDropdown()
        {
            foreach (JObject town in LoadArray(TownTableJson))
            {
                int id = town.Value<int>("TownId");
                yield return new ValueDropdownItem<int>($"{id} {town.Value<string>("Name")}", id);
            }
        }

        private static IEnumerable<ValueDropdownItem<int>> BandDropdown()
        {
            yield return new ValueDropdownItem<int>("早", 1);
            yield return new ValueDropdownItem<int>("中", 2);
            yield return new ValueDropdownItem<int>("晚", 3);
        }

        /// <summary>当前城镇 + 当前时段的背景预制体路径。取不到是空串。</summary>
        private string BackgroundKey()
        {
            string column = BandId switch { 2 => "BgNoon", 3 => "BgNight", _ => "BgMorning" };

            foreach (JObject town in LoadArray(TownTableJson))
            {
                if (town.Value<int>("TownId") == TownId)
                {
                    return town.Value<string>(column) ?? string.Empty;
                }
            }

            return string.Empty;
        }

        // ------------------------------------------------------------------
        // 场景预览
        // ------------------------------------------------------------------

        /// <summary>
        /// 在场景里摆出「这张背景 + 这个城镇的所有 NPC」。
        ///
        /// 全部带 <see cref="HideFlags.DontSave"/>，所以不会被存进场景、进 Play 就没。
        /// 背景直接放在原点 —— 运行时也是这么挂的（外层控制器在 `Games/Backgrounds` 下，
        /// 那一串节点都是原点 + 缩放 1），所以这里拖出来的世界坐标和游戏里一致。
        /// </summary>
        private void BuildPreview()
        {
            ClearPreview();

            previewRoot = new GameObject(PreviewRootName);
            previewRoot.hideFlags = HideFlags.DontSave;

            string bgKey = BackgroundKey();
            var bgPrefab = string.IsNullOrEmpty(bgKey)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(bgKey);

            if (bgPrefab == null)
            {
                Debug.LogWarning($"[NPC摆位] 城镇 {TownId} 的这个时段没有背景（{bgKey}）—— 只摆 NPC，没有参照物");
            }
            else
            {
                GameObject bg = Instantiate(bgPrefab, previewRoot.transform);
                bg.name = bgPrefab.name;
                bg.transform.position = Vector3.zero;
                MarkDontSave(bg);
            }

            foreach (NpcRow row in Rows)
            {
                CreateMarker(row);
            }

            Selection.activeGameObject = previewRoot;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        private void RebuildPreviewIfOpen()
        {
            if (IsPreviewOpen())
            {
                BuildPreview();
            }
        }

        /// <summary>给一行生成场景里那个可拖的对象。没配 Spine 就摆一个空节点（Gizmo 看得见）。</summary>
        private void CreateMarker(NpcRow row)
        {
            if (previewRoot == null)
            {
                return;
            }

            GameObject marker;
            var prefab = string.IsNullOrEmpty(row.SkeletonTown)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(row.SkeletonTown);

            if (prefab == null)
            {
                marker = new GameObject();
            }
            else
            {
                marker = Instantiate(prefab, previewRoot.transform);
            }

            marker.name = $"NPC_{row.NpcId}_{row.Name}";
            marker.transform.SetParent(previewRoot.transform, false);
            marker.transform.position = new Vector3(row.PosX, row.PosY, 0f);

            // 朝向和运行时一个做法：翻 localScale.x（见 TownSkeletonController.ApplyFacing）
            Vector3 scale = marker.transform.localScale;
            marker.transform.localScale = new Vector3(Mathf.Abs(scale.x) * (row.Facing >= 0 ? 1 : -1),
                                                      scale.y, scale.z);

            MarkDontSave(marker);
            row.Marker = marker.transform;
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
            foreach (NpcRow row in Rows)
            {
                row.Marker = null;
            }

            if (previewRoot != null)
            {
                DestroyImmediate(previewRoot);
                previewRoot = null;
            }

            // 窗口重开 / 域重载之后引用可能丢了，按名字兜一次底
            foreach (GameObject stray in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (stray.name == PreviewRootName && stray.scene.IsValid())
                {
                    DestroyImmediate(stray);
                }
            }
        }

        private void Select(NpcRow row)
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
        /// 把列表写回 `TownNpc.xlsx`：删掉的行 `DeleteRows`、老行 `UpdateRows`、新行 `AddRows`。
        ///
        /// **顺序不能乱**：先删再改再加 —— 反过来的话新加的 id 可能和待删的撞上。
        /// 坐标从**场景里那个对象**读（预览没开就用列表里的旧值），所以拖完直接点这个按钮就行。
        /// </summary>
        private void WriteToExcel()
        {
            // 拖过的坐标先同步回行数据
            foreach (NpcRow row in Rows)
            {
                if (row.Marker != null)
                {
                    Vector3 p = row.Marker.position;
                    row.PosX = Round(p.x);
                    row.PosY = Round(p.y);
                }
            }

            var duplicated = Rows.GroupBy(r => r.NpcId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

            if (duplicated.Count > 0)
            {
                EditorUtility.DisplayDialog("写入失败",
                    $"NpcId 重复了：{string.Join(", ", duplicated)}。id 要全局唯一。", "知道了");
                return;
            }

            var currentIds = new HashSet<int>(Rows.Select(r => r.NpcId));
            List<int> removed = loadedIds.Where(id => !currentIds.Contains(id)).ToList();

            if (removed.Count > 0 && !EditorUtility.DisplayDialog("确认删除",
                    $"这 {removed.Count} 个 NPC 会从 Excel 里删掉：{string.Join(", ", removed)}", "删", "取消"))
            {
                return;
            }

            if (removed.Count > 0 &&
                !ExcelTableRunner.DeleteRows(NpcWorkbook, NpcSheet, string.Join(",", removed), out string delOutput))
            {
                Fail("删除行失败", delOutput);
                return;
            }

            var updates = new JArray();
            var adds = new JArray();

            foreach (NpcRow row in Rows)
            {
                var payload = new JObject
                {
                    ["NpcId"] = row.NpcId,
                    ["TownId"] = TownId,
                    ["Name"] = row.Name ?? string.Empty,
                    ["PosX"] = row.PosX,
                    ["PosY"] = row.PosY,
                    ["Facing"] = row.Facing >= 0 ? 1 : -1,
                    ["SkeletonTown"] = row.SkeletonTown ?? string.Empty,
                };

                if (loadedIds.Contains(row.NpcId))
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
                string json = ExcelTableRunner.WriteTempJson("rediv_npc_update.json", updates.ToString());

                if (!ExcelTableRunner.UpdateRows(NpcWorkbook, NpcSheet, json, "NpcId", out string output))
                {
                    Fail("改行失败", output);
                    return;
                }
            }

            if (adds.Count > 0)
            {
                string json = ExcelTableRunner.WriteTempJson("rediv_npc_add.json", adds.ToString());

                if (!ExcelTableRunner.AddRows(NpcWorkbook, NpcSheet, json, out string output))
                {
                    Fail("加行失败", output);
                    return;
                }
            }

            dirty = false;
            Debug.Log($"[NPC摆位] 已写入 {NpcWorkbook}：新增 {adds.Count} / 修改 {updates.Count} / 删除 {removed.Count}");

            if (!ExportAfterWrite)
            {
                // 成功路径**不弹框**：每写一次点一下很烦，而且模态框会卡住无人值守的自动化
                Debug.Log("[NPC摆位] 记得跑一次配置导出（F6）才生效。NPC 表是纯客户端的，不用 publish。");
                return;
            }

            // 走 ConfigTools 那一套（参数取自 ConfigToolsSettings），别在这里另写一份导出逻辑
            bool ok = ConfigTools.ExportForExternalTool();
            AssetDatabase.Refresh();
            Reload();

            if (ok)
            {
                Debug.Log("[NPC摆位] 配置已重新导出。NPC 表是纯客户端的（group=c），不用 spacetime publish。");
            }
            else
            {
                EditorUtility.DisplayDialog("导出失败",
                    "Excel 已经写好了，但重新导出配置失败 —— 看 Console 里 Luban 的报错。", "知道了");
            }
        }

        private static void Fail(string title, string output)
        {
            EditorUtility.DisplayDialog(title, "ExcelTable.ps1 报错了，Excel 没有被改动。\n\n" + output, "知道了");
        }

        /// <summary>坐标留三位小数就够 —— 世界单位下 0.001 已经是亚像素了，位数多了表里难看。</summary>
        private static float Round(float value) => Mathf.Round(value * 1000f) / 1000f;

        // ------------------------------------------------------------------
        // 表格里的一行
        // ------------------------------------------------------------------

        /// <summary>
        /// 一个 NPC。<see cref="SkeletonTown"/> 存的是路径，
        /// <see cref="SkeletonAsset"/> 只是**拖拽入口** —— 路径永远由工具算，手打不了。
        /// </summary>
        public class NpcRow
        {
            [TableColumnWidth(60)]
            [LabelText("ID")]
            public int NpcId;

            [TableColumnWidth(120)]
            [LabelText("名字")]
            public string Name;

            [TableColumnWidth(70)]
            [LabelText("朝向")]
            [ValueDropdown(nameof(FacingOptions))]
            public int Facing = 1;

            [HideInInspector] public float PosX;
            [HideInInspector] public float PosY;
            [HideInInspector] public string SkeletonTown;

            /// <summary>场景里那个可拖的对象。预览没开时是 null。</summary>
            [HideInInspector] public Transform Marker;

            [TableColumnWidth(200)]
            [LabelText("城镇 Spine 预制体")]
            [ShowInInspector]
            [AssetsOnly]
            public GameObject SkeletonAsset
            {
                get => string.IsNullOrEmpty(SkeletonTown)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonTown);
                set => SkeletonTown = value == null ? string.Empty : AssetDatabase.GetAssetPath(value);
            }

            [TableColumnWidth(70)]
            [Button("选中")]
            [PropertyTooltip("在 Scene 视图里选中并聚焦到它，然后直接拖")]
            private void SelectInScene() => SelectRow(this);

            [TableColumnWidth(130)]
            [LabelText("坐标")]
            [ShowInInspector, ReadOnly]
            public string Position => Marker == null
                ? $"({PosX:F2}, {PosY:F2})"
                : $"({Marker.position.x:F2}, {Marker.position.y:F2})";

            private static IEnumerable<ValueDropdownItem<int>> FacingOptions()
            {
                yield return new ValueDropdownItem<int>("朝右", 1);
                yield return new ValueDropdownItem<int>("朝左", -1);
            }
        }
    }
}

#endif
