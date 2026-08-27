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
    /// 触发器摆位窗口 —— **在场景里对着背景拖，位置写回 `TownTrigger.xlsx`**。
    ///
    /// 和 <see cref="TownNpcPlacementWindow"/> 是同一套做法（那边是 NPC，这边是触发器），
    /// 解决的也是同一个问题：`PosX` / `PosY` 是**世界坐标**，在 Excel 里只能填数字、看不到背景。
    /// 触发器还多了宽高，更需要对着图看。
    ///
    /// <code>
    /// TownTrigger.xlsx --(Luban 导出)--> tbtowntrigger.json --(本窗口读)--> 场景里的预览对象
    ///                  &lt;--(ExcelTable.ps1 AddRows/UpdateRows/DeleteRows)-- 「写入 Excel」按钮
    /// </code>
    ///
    /// **位置在场景里拖，宽高在这个表里填数字**（改完 Scene 视图里的框立刻跟着变）——
    /// 拖矩形的边需要自定义 Handles，而宽高本来就是「门口多宽」这种一眼能定的数。
    ///
    /// 传送阵是**成对**的：一行的「对端」指向另一个城镇的某个传送点，
    /// 从这边过去就出现在对端那个传送点的**出口点**旁边。出口点也是在场景里拖的
    /// （预览里那个绿圈子节点），所以别在表里手填偏移。
    ///
    /// ⚠️ 窗口显示的是**上次导出**的内容。有人绕过窗口直接改了 Excel 又没导出的话这里看不到。
    ///
    /// ⚠️ 预览对象全都带 <see cref="HideFlags.DontSave"/>：**不会被存进场景**，
    /// 进 Play 或重开场景就没了。
    ///
    /// ⚠️ 触发器表是**纯客户端**的（`group` 全是 `c`），改完**不用 `spacetime publish`**。
    /// 「能不能去那个城镇」是服务端 <c>ChangeTown</c> 自己校验的，和这张表无关。
    /// </summary>
    public class TownTriggerPlacementWindow : OdinEditorWindow
    {
        private const string TriggerTableJson =
            "Assets/AddressableAssets/Remote/Configs/LubanJson/tbtowntrigger.json";

        private const string TownTableJson =
            "Assets/AddressableAssets/Remote/Configs/LubanJson/tbtown.json";

        private const string TriggerWorkbook = "ExcelTool/LubanTools/DataTables/Datas/TownTrigger.xlsx";
        private const string TriggerSheet = "TownTrigger";

        private const string PreviewRootName = "[触发器摆位预览]";

        /// <summary>类型：传送到别的城镇。和 <c>TownTriggers.KindChangeTown</c> 是同一个值。</summary>
        private const int KindChangeTown = 1;

        /// <summary>类型：打开副本界面。和 <c>TownTriggers.KindDungeon</c> 是同一个值。</summary>
        private const int KindDungeon = 2;

        [MenuItem("Tools/XFramework/配置/触发器摆位", false, 202)]
        private static void Open()
        {
            var window = GetWindow<TownTriggerPlacementWindow>("触发器摆位");
            window.minSize = new Vector2(980, 560);
            window.Reload();
        }

        // ------------------------------------------------------------------
        // 选城镇 / 时段
        // ------------------------------------------------------------------

        [TitleGroup("城镇", "触发器按城镇分；时段只影响预览用哪张背景，不影响数据（触发器三个时段共用）",
            TitleAlignments.Left)]
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
        [InfoBox("预览已打开：在 **Scene 视图**里直接拖触发器（列表里点「选中」能跳过去）。" +
                 "蓝框=传送、橙框=副本。宽高在下面的表里改，框会立刻跟着变。 " +
                 "传送点下面还有一个绿圈**出口点**子节点 —— 别人从对端传送过来时站那儿，" +
                 "把它拖到传送阵旁边的地面上（别拖到可行走边界外）。 " +
                 "拖完点「写入 Excel」。预览对象不会存进场景。", InfoMessageType.Info)]
        [ShowInInspector, HideLabel, DisplayAsString(false)]
        private string PreviewHint => string.Empty;

        // ------------------------------------------------------------------
        // 触发器列表
        // ------------------------------------------------------------------

        [TitleGroup("触发器", "一行一个。删掉行 = 写回时从 Excel 删掉那一行", TitleAlignments.Left)]
        [TableList(AlwaysExpanded = true, DrawScrollView = false, ShowIndexLabels = false)]
        [OnValueChanged(nameof(HandleRowChanged), IncludeChildren = true)]
        public List<TriggerRow> Rows = new List<TriggerRow>();

        [TitleGroup("触发器")]
        [HorizontalGroup("触发器/按钮")]
        [Button("新增传送点", ButtonSizes.Medium)]
        private void AddTeleport() => AddRow(KindChangeTown, "新传送点");

        [HorizontalGroup("触发器/按钮")]
        [Button("新增副本入口", ButtonSizes.Medium)]
        private void AddDungeon() => AddRow(KindDungeon, "新副本入口");

        [HorizontalGroup("触发器/按钮")]
        [Button("重新读取（丢弃未写入的改动）", ButtonSizes.Medium)]
        private void ReloadButton() => Reload();

        private void AddRow(int kind, string name)
        {
            int nextId = allIds.Count == 0 ? 1 : allIds.Max() + 1;

            var row = new TriggerRow
            {
                TriggerId = nextId,
                Kind = kind,
                TargetId = 0,
                Name = name,
                PosX = 0f,
                PosY = -3.3f,
                Width = 1.5f,
                Height = 1.5f,
                // 出口点默认在传送阵右边一点：刚好在框外面，玩家落地时不会又踩到它
                ArriveOffsetX = kind == KindChangeTown ? 1.2f : 0f,
                ArriveOffsetY = 0f,
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

        /// <summary>上次读表时**所有城镇**的 TriggerId，用来分配新 id。</summary>
        private readonly HashSet<int> allIds = new HashSet<int>();

        /// <summary>上次读表时**当前城镇**有哪些 TriggerId。写回时用它算出「被删掉的行」。</summary>
        private readonly HashSet<int> loadedIds = new HashSet<int>();

        [SerializeField, HideInInspector]
        private GameObject previewRoot;

        /// <summary>行内那个「选中」按钮要能回调窗口，所以留一个当前窗口的引用。</summary>
        private static TownTriggerPlacementWindow instance;

        private bool dirty;

        private bool IsPreviewOpen() => previewRoot != null;

        /// <summary>
        /// 表里改了任何一格：标脏，并把宽高推到场景里那个框上 ——
        /// 改完数字要能立刻在 Scene 视图里看到，不然还是「填一个数试一次」。
        /// </summary>
        private void HandleRowChanged()
        {
            dirty = true;

            foreach (TriggerRow row in Rows)
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

        /// <summary>给行内的「选中」按钮用。</summary>
        private static void SelectRow(TriggerRow row) => instance?.Select(row);

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

            foreach (JObject item in LoadArray(TriggerTableJson))
            {
                int id = item.Value<int>("TriggerId");
                allIds.Add(id);

                if (item.Value<int>("TownId") != TownId)
                {
                    continue;
                }

                loadedIds.Add(id);

                Rows.Add(new TriggerRow
                {
                    TriggerId = id,
                    Kind = item.Value<int>("Kind") == KindDungeon ? KindDungeon : KindChangeTown,
                    TargetId = item.Value<int>("TargetId"),
                    Name = item.Value<string>("Name") ?? string.Empty,
                    PosX = item.Value<float>("PosX"),
                    PosY = item.Value<float>("PosY"),
                    Width = item.Value<float>("Width"),
                    Height = item.Value<float>("Height"),
                    IconPrefab = item.Value<string>("IconPrefab") ?? string.Empty,
                    ArriveOffsetX = item.Value<float>("ArriveOffsetX"),
                    ArriveOffsetY = item.Value<float>("ArriveOffsetY"),
                });
            }

            Rows = Rows.OrderBy(r => r.TriggerId).ToList();

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
                Debug.LogWarning($"[触发器摆位] 找不到 {assetPath} —— 先跑一次配置导出（F6）");
                return new JArray();
            }

            return JArray.Parse(File.ReadAllText(full));
        }

        private static IEnumerable<ValueDropdownItem<int>> TownDropdown()
        {
            foreach (JObject town in LoadArray(TownTableJson))
            {
                int id = town.Value<int>("TownId");
                yield return new ValueDropdownItem<int>($"{id} {town.Value<string>("Name")}", id);
            }
        }

        /// <summary>
        /// 「对端」那一列的下拉。
        ///
        /// 传送点连的是**别的城镇的另一个传送点**（成对的传送阵），所以列表里只列
        /// `Kind=1` 且**不在当前城镇**的行 —— 同城镇互连不是传送，是原地挪位置，
        /// 运行时的校验也会拒。副本用不到这一列，所以第一项是 0。
        ///
        /// ⚠️ 列的是**上次导出**的表内容。刚在这个窗口里新加、还没写回 Excel 的传送点
        /// 不会出现在这里 —— 先写一次再来连。
        /// </summary>
        internal static IEnumerable<ValueDropdownItem<int>> TargetDropdown()
        {
            yield return new ValueDropdownItem<int>("0（副本用不到）", 0);

            int currentTownId = instance != null ? instance.TownId : 0;

            foreach (JObject item in LoadArray(TriggerTableJson))
            {
                if (item.Value<int>("Kind") != KindChangeTown)
                {
                    continue;
                }

                int townId = item.Value<int>("TownId");

                if (townId == currentTownId)
                {
                    continue;
                }

                int id = item.Value<int>("TriggerId");
                string label = $"#{id} {item.Value<string>("Name")}（城镇{townId} {TownName(townId)}）";

                yield return new ValueDropdownItem<int>(label, id);
            }
        }

        /// <summary>在**上次导出**的触发器表里按 id 找一行。找不到返回 null。</summary>
        internal static JObject FindTriggerInTable(int triggerId)
        {
            foreach (JObject item in LoadArray(TriggerTableJson))
            {
                if (item.Value<int>("TriggerId") == triggerId)
                {
                    return item;
                }
            }

            return null;
        }

        internal static string TownName(int townId)
        {
            foreach (JObject town in LoadArray(TownTableJson))
            {
                if (town.Value<int>("TownId") == townId)
                {
                    return town.Value<string>("Name") ?? string.Empty;
                }
            }

            return string.Empty;
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
        /// 在场景里摆出「这张背景 + 这个城镇的所有触发器」。
        ///
        /// 背景直接放在原点 —— 运行时也是这么挂的（都在 `Games/Backgrounds` 下，
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
                Debug.LogWarning($"[触发器摆位] 城镇 {TownId} 的这个时段没有背景（{bgKey}）" +
                                 $"—— 只摆触发器，没有参照物");
            }
            else
            {
                GameObject bg = Instantiate(bgPrefab, previewRoot.transform);
                bg.name = bgPrefab.name;
                bg.transform.position = Vector3.zero;
                MarkDontSave(bg);
            }

            foreach (TriggerRow row in Rows)
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

        /// <summary>
        /// 给一行生成场景里那个可拖的对象。
        ///
        /// 挂的是**运行时那个** <c>TownTriggerController</c>，所以画出来的框和游戏里的判定区
        /// 是同一份代码算的 —— 编辑器里所见即游戏里所得。
        /// </summary>
        private void CreateMarker(TriggerRow row)
        {
            if (previewRoot == null)
            {
                return;
            }

            var marker = new GameObject($"Trigger_{row.TriggerId}_{row.Name}");
            marker.transform.SetParent(previewRoot.transform, false);
            marker.transform.position = new Vector3(row.PosX, row.PosY, 0f);

            row.Controller = marker.AddComponent<TownTriggerController>();

            // 传送点还有一个**可拖的出口点**：别人从对端过来时站这儿。
            // 做成子节点是为了能用 Unity 自己的移动工具拖（拖数字太难对着背景看），
            // 写回时读它的 localPosition
            if (row.Kind == KindChangeTown)
            {
                var exit = new GameObject(TownTriggerController.ExitNodeName);
                exit.transform.SetParent(marker.transform, false);
                exit.transform.localPosition = new Vector3(row.ArriveOffsetX, row.ArriveOffsetY, 0f);
                row.Exit = exit.transform;
            }

            // 配了地面标记就把它也摆出来（现在没有哪个触发器配了图）
            var iconPrefab = string.IsNullOrEmpty(row.IconPrefab)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(row.IconPrefab);

            if (iconPrefab != null)
            {
                GameObject icon = Instantiate(iconPrefab, marker.transform);
                icon.name = iconPrefab.name;
                icon.transform.localPosition = Vector3.zero;
            }

            MarkDontSave(marker);
            row.Marker = marker.transform;
            row.ApplyToMarker();
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
            foreach (TriggerRow row in Rows)
            {
                row.Marker = null;
                row.Controller = null;
                row.Exit = null;
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

        private void Select(TriggerRow row)
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
        /// 把列表写回 `TownTrigger.xlsx`：删掉的行 `DeleteRows`、老行 `UpdateRows`、新行 `AddRows`。
        ///
        /// **顺序不能乱**：先删再改再加 —— 反过来的话新加的 id 可能和待删的撞上。
        /// 坐标从**场景里那个对象**读（预览没开就用列表里的旧值），所以拖完直接点这个按钮就行。
        /// </summary>
        private void WriteToExcel()
        {
            // 拖过的坐标和出口点先同步回行数据
            foreach (TriggerRow row in Rows)
            {
                if (row.Marker != null)
                {
                    Vector3 p = row.Marker.position;
                    row.PosX = Round(p.x);
                    row.PosY = Round(p.y);
                }

                // 出口点是**子节点**，所以读 localPosition 就是相对中心的偏移 ——
                // 这样拖完中心再拖出口点，两个值都不用互相换算
                if (row.Exit != null)
                {
                    Vector3 offset = row.Exit.localPosition;
                    row.ArriveOffsetX = Round(offset.x);
                    row.ArriveOffsetY = Round(offset.y);
                }
            }

            string invalid = Validate();

            if (invalid != null)
            {
                EditorUtility.DisplayDialog("写入失败", invalid, "知道了");
                return;
            }

            var currentIds = new HashSet<int>(Rows.Select(r => r.TriggerId));
            List<int> removed = loadedIds.Where(id => !currentIds.Contains(id)).ToList();

            if (removed.Count > 0 && !EditorUtility.DisplayDialog("确认删除",
                    $"这 {removed.Count} 个触发器会从 Excel 里删掉：{string.Join(", ", removed)}", "删", "取消"))
            {
                return;
            }

            if (removed.Count > 0 &&
                !ExcelTableRunner.DeleteRows(TriggerWorkbook, TriggerSheet,
                    string.Join(",", removed), out string delOutput))
            {
                Fail("删除行失败", delOutput);
                return;
            }

            var updates = new JArray();
            var adds = new JArray();

            foreach (TriggerRow row in Rows)
            {
                var payload = new JObject
                {
                    ["TriggerId"] = row.TriggerId,
                    ["TownId"] = TownId,
                    ["Kind"] = row.Kind,
                    ["TargetId"] = row.TargetId,
                    ["PosX"] = row.PosX,
                    ["PosY"] = row.PosY,
                    ["Width"] = Round(row.Width),
                    ["Height"] = Round(row.Height),
                    ["Name"] = row.Name ?? string.Empty,
                    ["IconPrefab"] = row.IconPrefab ?? string.Empty,
                    ["ArriveOffsetX"] = row.ArriveOffsetX,
                    ["ArriveOffsetY"] = row.ArriveOffsetY,
                };

                if (loadedIds.Contains(row.TriggerId))
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
                string json = ExcelTableRunner.WriteTempJson("rediv_trigger_update.json", updates.ToString());

                if (!ExcelTableRunner.UpdateRows(TriggerWorkbook, TriggerSheet, json,
                        "TriggerId", out string output))
                {
                    Fail("改行失败", output);
                    return;
                }
            }

            if (adds.Count > 0)
            {
                string json = ExcelTableRunner.WriteTempJson("rediv_trigger_add.json", adds.ToString());

                if (!ExcelTableRunner.AddRows(TriggerWorkbook, TriggerSheet, json, out string output))
                {
                    Fail("加行失败", output);
                    return;
                }
            }

            dirty = false;
            Debug.Log($"[触发器摆位] 已写入 {TriggerWorkbook}：" +
                      $"新增 {adds.Count} / 修改 {updates.Count} / 删除 {removed.Count}");

            if (!ExportAfterWrite)
            {
                // 成功路径**不弹框**：每写一次点一下很烦，而且模态框会卡住无人值守的自动化
                Debug.Log("[触发器摆位] 记得跑一次配置导出（F6）才生效。触发器表是纯客户端的，不用 publish。");
                return;
            }

            // 走 ConfigTools 那一套（参数取自 ConfigToolsSettings），别在这里另写一份导出逻辑
            bool ok = ConfigTools.ExportForExternalTool();
            AssetDatabase.Refresh();
            Reload();

            if (ok)
            {
                Debug.Log("[触发器摆位] 配置已重新导出。触发器表是纯客户端的（group=c），" +
                          "不用 spacetime publish。");
            }
            else
            {
                EditorUtility.DisplayDialog("导出失败",
                    "Excel 已经写好了，但重新导出配置失败 —— 看 Console 里 Luban 的报错。", "知道了");
            }
        }

        /// <summary>
        /// 写回前的校验。**运行时那套校验（<c>TownTriggers.InTown</c>）会把配错的行跳过并报错**，
        /// 这里只是让人在写进 Excel 之前就知道，省一轮「导出 → 进 Play → 看 Console」。
        /// </summary>
        private string Validate()
        {
            var duplicated = Rows.GroupBy(r => r.TriggerId).Where(g => g.Count() > 1)
                .Select(g => g.Key).ToList();

            if (duplicated.Count > 0)
            {
                return $"TriggerId 重复了：{string.Join(", ", duplicated)}。id 要全局唯一。";
            }

            foreach (TriggerRow row in Rows)
            {
                if (row.Width <= 0f || row.Height <= 0f)
                {
                    return $"触发器 {row.TriggerId}（{row.Name}）的宽高是 " +
                           $"{row.Width}×{row.Height}，那样永远踩不到。";
                }

                if (row.Kind != KindChangeTown)
                {
                    continue;
                }

                if (row.TargetId <= 0)
                {
                    return $"传送点 {row.TriggerId}（{row.Name}）没连对端传送点。";
                }

                if (row.TargetId == row.TriggerId)
                {
                    return $"传送点 {row.TriggerId}（{row.Name}）连的是它自己。";
                }

                JObject pair = FindTriggerInTable(row.TargetId);

                if (pair == null)
                {
                    return $"传送点 {row.TriggerId}（{row.Name}）连的对端 {row.TargetId} 在表里不存在" +
                           $"（如果它是刚在窗口里新加的，先写一次 Excel 再来连）。";
                }

                if (pair.Value<int>("Kind") != KindChangeTown)
                {
                    return $"传送点 {row.TriggerId}（{row.Name}）连的对端 {row.TargetId} 不是传送点。";
                }

                if (pair.Value<int>("TownId") == TownId)
                {
                    return $"传送点 {row.TriggerId}（{row.Name}）连的对端 {row.TargetId} 在同一个城镇里 ——" +
                           $"那不是传送，是原地挪位置。";
                }

                if (row.ArriveOffsetX == 0f && row.ArriveOffsetY == 0f)
                {
                    return $"传送点 {row.TriggerId}（{row.Name}）的出口点还在传送阵正中心。" +
                           $"在 Scene 视图里把那个绿圈「出口点」拖到旁边的地面上 —— " +
                           $"别人是从这儿出来的。";
                }
            }

            return null;
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
        /// 一个触发器。<see cref="IconPrefab"/> 存的是路径，<see cref="IconAsset"/> 只是
        /// **拖拽入口** —— 路径永远由工具算，手打不了（和 NPC 那边的 Spine 一个做法）。
        /// </summary>
        public class TriggerRow
        {
            [TableColumnWidth(55)]
            [LabelText("ID")]
            public int TriggerId;

            [TableColumnWidth(90)]
            [LabelText("类型")]
            [ValueDropdown(nameof(KindOptions))]
            public int Kind = KindChangeTown;

            [TableColumnWidth(190)]
            [LabelText("对端")]
            [ValueDropdown("@XFramework.TownTriggerPlacementWindow.TargetDropdown()")]
            [PropertyTooltip("传送=**别的城镇的那个传送点**（成对的传送阵，目标城镇由它推出来）；副本用不到")]
            public int TargetId;

            [TableColumnWidth(120)]
            [LabelText("提示文字")]
            public string Name;

            [TableColumnWidth(70)]
            [LabelText("宽")]
            [MinValue(0.05f)]
            public float Width = 1.5f;

            [TableColumnWidth(70)]
            [LabelText("高")]
            [MinValue(0.05f)]
            public float Height = 1.5f;

            [HideInInspector] public float PosX;
            [HideInInspector] public float PosY;
            [HideInInspector] public string IconPrefab;
            [HideInInspector] public float ArriveOffsetX;
            [HideInInspector] public float ArriveOffsetY;

            /// <summary>场景里那个可拖的对象。预览没开时是 null。</summary>
            [HideInInspector] public Transform Marker;

            /// <summary>预览对象上那个组件 —— 框就是它画的。</summary>
            [HideInInspector] public TownTriggerController Controller;

            /// <summary>
            /// 场景里那个可拖的**出口点**子节点（只有传送点有）。预览没开时是 null。
            /// 写回时读它的 localPosition 当偏移。
            /// </summary>
            [HideInInspector] public Transform Exit;

            [TableColumnWidth(160)]
            [LabelText("地面标记预制体")]
            [ShowInInspector]
            [AssetsOnly]
            [PropertyTooltip("可空。没配就只有一个看不见的判定区")]
            public GameObject IconAsset
            {
                get => string.IsNullOrEmpty(IconPrefab)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<GameObject>(IconPrefab);
                set => IconPrefab = value == null ? string.Empty : AssetDatabase.GetAssetPath(value);
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

            /// <summary>
            /// 出口点（相对中心的偏移）。**只读** —— 在 Scene 视图里拖那个绿圈，别在这儿填数字。
            /// </summary>
            [TableColumnWidth(120)]
            [LabelText("出口偏移")]
            [ShowInInspector, ReadOnly]
            [PropertyTooltip("别人从对端传送过来时站在「中心 + 这个偏移」上。在 Scene 视图里拖那个绿圈")]
            public string ArriveOffset
            {
                get
                {
                    if (Kind != KindChangeTown)
                    {
                        return "—";
                    }

                    return Exit == null
                        ? $"({ArriveOffsetX:F2}, {ArriveOffsetY:F2})"
                        : $"({Exit.localPosition.x:F2}, {Exit.localPosition.y:F2})";
                }
            }

            /// <summary>
            /// 把表里改的东西推到场景里那个框上（宽高、类型颜色、标签），改完立刻能看到。
            /// </summary>
            public void ApplyToMarker()
            {
                if (Controller != null)
                {
                    Controller.Configure(TriggerId, Kind, TargetId, Name, new Vector2(Width, Height),
                        new Vector2(ArriveOffsetX, ArriveOffsetY));
                }
            }

            private static IEnumerable<ValueDropdownItem<int>> KindOptions()
            {
                yield return new ValueDropdownItem<int>("传送", KindChangeTown);
                yield return new ValueDropdownItem<int>("副本", KindDungeon);
            }
        }
    }
}

#endif
