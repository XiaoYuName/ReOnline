#if UNITY_EDITOR

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace XFramework
{
    /// <summary>
    /// 角色资源配置窗口 —— 拖资产，一键写回 Excel。
    ///
    /// 解决的问题：`CharacterForm` 表里那一排资源列（头像 / 略缩图 / 名字图 / 立绘 /
    /// 预览图预制体 / UI展示预制体 / 战斗Spine预制体）填的是
    /// Addressable 完整路径，在 Excel 里只能手打，打错没提示、资源改名不同步，
    /// 只在运行时表现成「加载不出来」。这里拖资产，路径由工具算。
    ///
    /// **Excel 仍然是唯一真相源**（2026-08-24 试过挪进 ScriptableObject，当天又退回来了）。
    /// 这个窗口只是个录入界面：读 Luban 导出的 json 显示现状，改完点「写入 Excel」，
    /// 走 <c>ExcelTable.ps1</c> 把那几列写回 <c>CharacterForm.xlsx</c>，然后重新导出配置。
    ///
    /// 数据流：
    /// <code>
    /// CharacterForm.xlsx --(Luban 导出)--> tbcharacterform.json --(本窗口读)--> 界面
    ///                    &lt;--(ExcelTable.ps1 UpdateRows)-- 「写入 Excel」按钮
    /// </code>
    ///
    /// ⚠️ 界面上显示的是**上次导出**的内容。如果有人绕过窗口直接改了 Excel 又没导出，
    /// 窗口是看不到的 —— 所以打开窗口时会先提示你「先导出一次再改」。
    /// 好在写回只动那几个资源列、按 JobId+FormId 定位，不会碰别人改的其它列。
    /// </summary>
    public class CharacterResourceWindow : OdinEditorWindow
    {
        private const string FormTableJson =
            "Assets/AddressableAssets/Remote/Configs/LubanJson/tbcharacterform.json";

        private const string JobTableJson =
            "Assets/AddressableAssets/Remote/Configs/LubanJson/tbcharacterjob.json";

        private const string ExcelToolPath = "ExcelTool/LubanTools/ExcelTable.ps1";
        private const string FormWorkbook = "ExcelTool/LubanTools/DataTables/Datas/CharacterForm.xlsx";
        private const string FormSheet = "CharacterForm";

        private const int FormTypeBase = 1;
        private const int FormTypeBurst = 2;

        [MenuItem("Tools/XFramework/配置/角色资源配置", false, 201)]
        private static void Open()
        {
            var window = GetWindow<CharacterResourceWindow>("角色资源配置");
            window.minSize = new Vector2(980, 600);
            window.Reload();
        }

        // ------------------------------------------------------------------
        // 选角色
        // ------------------------------------------------------------------

        [TitleGroup("角色", "从 Luban 导出的职业表里读；改完 Excel 记得先跑一次配置导出", TitleAlignments.Left)]
        [HorizontalGroup("角色/行")]
        [LabelText("角色"), LabelWidth(50)]
        [ValueDropdown(nameof(JobDropdown))]
        [OnValueChanged(nameof(Reload))]
        public int JobId;

        [HorizontalGroup("角色/行", Width = 110)]
        [Button("重新读取", ButtonSizes.Medium)]
        [PropertyTooltip("重新读 Luban 导出的 json。改完 Excel 或重新导出配置后点它。")]
        private void Reload()
        {
            Rows.Clear();
            problems.Clear();
            dirty = false;

            JArray forms = ReadJson(FormTableJson);

            if (forms == null)
            {
                problems.Add($"读不到 {FormTableJson} —— 先跑一次 Tools > XFramework > 配置 > LuaConfig（F6）导出配置。");
                return;
            }

            List<JToken> own = forms
                .Where(f => (int?)f["JobId"] == JobId)
                .OrderBy(f => (int?)f["FormType"] ?? 0)
                .ThenBy(f => (int?)f["SortOrder"] ?? 0)
                .ToList();

            if (own.Count == 0)
            {
                problems.Add($"Luban 形态表里没有 JobId={JobId} 的形态 —— " +
                             "先在 CharacterForm.xlsx 里把这个角色的形态行加好，再导出配置。");
                return;
            }

            int lowestBaseStar = own
                .Where(f => (int?)f["FormType"] == FormTypeBase)
                .Select(f => (int?)f["UnlockStar"] ?? 0)
                .DefaultIfEmpty(0)
                .Min();

            foreach (JToken f in own)
            {
                int formType = (int?)f["FormType"] ?? 0;
                int unlockStar = (int?)f["UnlockStar"] ?? 0;

                Rows.Add(new FormRow
                {
                    FormId = (int?)f["FormId"] ?? 0,
                    FormName = (string)f["Name"] ?? string.Empty,
                    FormType = formType,
                    UnlockStar = unlockStar,
                    IsBaseForm = formType == FormTypeBase && unlockStar == lowestBaseStar,
                    IconKey = (string)f["IconKey"] ?? string.Empty,
                    UnitPlateIconKey = (string)f["UnitPlateIconKey"] ?? string.Empty,
                    NameIconKey = (string)f["NameIconKey"] ?? string.Empty,
                    ArtImage = (string)f["ArtImage"] ?? string.Empty,
                    StillUnitPrefab = (string)f["StillUnitPrefab"] ?? string.Empty,
                    SkeletonUI = (string)f["SkeletonUI"] ?? string.Empty,
                    SkeletonScreen = (string)f["SkeletonScreen"] ?? string.Empty,
                });
            }

            Validate();
        }

        private IEnumerable<ValueDropdownItem<int>> JobDropdown()
        {
            JArray jobs = ReadJson(JobTableJson);

            if (jobs == null)
            {
                yield return new ValueDropdownItem<int>("(读不到职业表，先导出配置)", 0);
                yield break;
            }

            foreach (JToken j in jobs.OrderBy(j => (int?)j["SortOrder"] ?? 0))
            {
                int id = (int?)j["JobId"] ?? 0;
                yield return new ValueDropdownItem<int>($"{id}  {(string)j["Name"]}", id);
            }
        }

        // ------------------------------------------------------------------
        // 校验
        // ------------------------------------------------------------------

        [TitleGroup("校验")]
        [ShowIf(nameof(HasProblems))]
        [InfoBox("$ProblemText", InfoMessageType.Warning)]
        [ShowInInspector, HideLabel, DisplayAsString(false)]
        private string ProblemPlaceholder => string.Empty;

        [TitleGroup("校验")]
        [ShowIf("@Rows.Count > 0 && !HasProblems")]
        [InfoBox("每个形态的资源都配齐了，路径指向的资产也都在。", InfoMessageType.Info)]
        [ShowInInspector, HideLabel, DisplayAsString(false)]
        private string OkPlaceholder => string.Empty;

        private readonly List<string> problems = new List<string>();

        private bool HasProblems => problems.Count > 0;

        private string ProblemText => string.Join("\n", problems);

        /// <summary>按填法约定和资产是否存在做校验。规则和服务端自检互补 —— 那边查不了资源。</summary>
        private void Validate()
        {
            problems.Clear();

            foreach (FormRow row in Rows)
            {
                string where = $"形态 {row.FormId}「{row.FormName}」";

                CheckAssetExists(where, "头像", row.IconKey);
                CheckAssetExists(where, "略缩图", row.UnitPlateIconKey);
                CheckAssetExists(where, "名字图", row.NameIconKey);
                CheckAssetExists(where, "立绘", row.ArtImage);
                CheckAssetExists(where, "预览图预制体", row.StillUnitPrefab);
                CheckAssetExists(where, "UI展示预制体", row.SkeletonUI);
                CheckAssetExists(where, "战斗Spine预制体", row.SkeletonScreen);

                if (string.IsNullOrEmpty(row.IconKey))
                {
                    problems.Add($"{where} 没有头像 —— 每个形态都该有。");
                }

                if (string.IsNullOrEmpty(row.SkeletonUI))
                {
                    problems.Add($"{where} 没有 UI展示预制体 —— 选人界面的格子上就不会有形象。");
                }
                else if (AssetDatabase.LoadAssetAtPath<GameObject>(row.SkeletonUI) is { } uiPrefab &&
                         uiPrefab.GetComponent<CharacterGraphicUI>() == null)
                {
                    // 光有预制体不够：待机动画名编在 CharacterGraphicUI 上，没这个组件就播不了动画
                    problems.Add($"{where} 的 UI展示预制体上没有 CharacterGraphicUI 组件，播不了待机动画。");
                }

                // 填法约定：立绘只有基础形态那一行填，觉醒 / 爆发形态用预览图预制体
                if (row.IsBaseForm)
                {
                    if (string.IsNullOrEmpty(row.ArtImage))
                    {
                        problems.Add($"{where} 是基础形态，应该填立绘。");
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(row.ArtImage))
                    {
                        problems.Add($"{where} 是觉醒/爆发形态，一般不填立绘（现在填了）。");
                    }
                }
            }
        }

        private void CheckAssetExists(string where, string label, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Object>(key) == null)
            {
                problems.Add($"{where} 的{label}指向的资产不存在了：{key}（资源被删或改名了）");
            }
        }

        // ------------------------------------------------------------------
        // 形态表格
        // ------------------------------------------------------------------

        [TitleGroup("形态资源", "行从 Luban 形态表读，只有这几列资源能改；数值和文本请到 Excel 里改", TitleAlignments.Left)]
        [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 280)]
        [LabelText("形态"), OnValueChanged(nameof(OnRowsChanged), IncludeChildren = true)]
        public List<FormRow> Rows = new List<FormRow>();

        private bool dirty;

        private void OnRowsChanged()
        {
            dirty = true;
            Validate();
        }

        [TitleGroup("写回")]
        [ShowIf(nameof(dirty))]
        [InfoBox("有改动还没写进 Excel —— 直接关窗口会丢。", InfoMessageType.Warning)]
        [ShowInInspector, HideLabel, DisplayAsString(false)]
        private string DirtyPlaceholder => string.Empty;

        [TitleGroup("写回")]
        [LabelText("写完自动重新导出配置")]
        [PropertyTooltip("勾上的话，写完 Excel 会顺手跑一次 Luban 导出（客户端 + 服务端），" +
                         "省得再按一次 F6。服务端那份改完仍然要 spacetime publish 才生效。")]
        public bool ExportAfterWrite = true;

        [TitleGroup("写回")]
        [Button("写入 Excel", ButtonSizes.Gigantic), GUIColor(0.4f, 0.8f, 0.95f)]
        [PropertyTooltip("按 JobId+FormId 定位行，只写那 7 个资源列，不碰 Excel 里的数值 / 名字 / 排序。")]
        [DisableIf("@Rows.Count == 0")]
        private void WriteToExcel()
        {
            if (Rows.Count == 0)
            {
                return;
            }

            // 只写那几个资源列，按联合主键定位 —— 别的列（数值、名字、排序）是在 Excel 里维护的，不能碰。
            var payload = new JArray();

            foreach (FormRow row in Rows)
            {
                payload.Add(new JObject
                {
                    ["JobId"] = JobId,
                    ["FormId"] = row.FormId,
                    ["IconKey"] = row.IconKey ?? string.Empty,
                    ["UnitPlateIconKey"] = row.UnitPlateIconKey ?? string.Empty,
                    ["NameIconKey"] = row.NameIconKey ?? string.Empty,
                    ["ArtImage"] = row.ArtImage ?? string.Empty,
                    ["StillUnitPrefab"] = row.StillUnitPrefab ?? string.Empty,
                    ["SkeletonUI"] = row.SkeletonUI ?? string.Empty,
                    ["SkeletonScreen"] = row.SkeletonScreen ?? string.Empty,
                });
            }

            string jsonPath = Path.Combine(Path.GetTempPath(), "rediv_character_res.json");
            File.WriteAllText(jsonPath, payload.ToString(), new UTF8Encoding(false));

            if (!RunExcelTool(jsonPath, out string output))
            {
                EditorUtility.DisplayDialog("写入失败",
                    "ExcelTable.ps1 报错了，Excel 没有被改动。\n\n" + output, "知道了");
                return;
            }

            dirty = false;
            Debug.Log($"[角色资源] 已写入 {FormWorkbook}\n{output}");

            if (ExportAfterWrite)
            {
                // 走 ConfigTools 那一套（参数取自 ConfigToolsSettings），别在这里另写一份导出逻辑
                bool ok = ConfigTools.ExportForExternalTool();
                AssetDatabase.Refresh();
                Reload();

                if (!ok)
                {
                    EditorUtility.DisplayDialog("导出失败",
                        "Excel 已经写好了，但重新导出配置失败 —— 看 Console 里 Luban 的报错。", "知道了");
                    return;
                }

                Debug.Log("[角色资源] 配置已重新导出。服务端那份要 spacetime publish 才生效。");
            }
            else
            {
                // 成功路径**不弹框**：每写一次都要点一下很烦，而且模态框会卡住无人值守的自动化
                //（用 unity command eval 驱动窗口时实测卡死过）。只有失败才值得打断人。
                Debug.Log("[角色资源] 已写入 Excel。还要跑一次配置导出（F6）才生效，服务端那份还要 spacetime publish。");
            }
        }

        /// <summary>
        /// 起一个 PowerShell 跑 ExcelTable.ps1。
        ///
        /// 三个不能省的处理（和 SpacetimeCli 里同样的坑）：中文输出要显式 UTF8，
        /// 否则中文 Windows 上乱码；要等它退出才知道成没成；工作目录必须是工程根，
        /// 因为脚本里的相对路径是按那个基准解析的。
        /// </summary>
        private static bool RunExcelTool(string jsonPath, out string output)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string script = Path.Combine(projectRoot, ExcelToolPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(script))
            {
                output = $"找不到 {script}";
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments =
                    $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" " +
                    $"-Action UpdateRows -Workbook \"{FormWorkbook}\" -Sheet \"{FormSheet}\" " +
                    $"-File \"{jsonPath}\" -KeyColumns \"JobId,FormId\"",
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            try
            {
                EditorUtility.DisplayProgressBar("角色资源", "正在写入 Excel（会后台开一个 Excel 进程）...", 0.5f);

                using var process = Process.Start(startInfo);
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                output = (stdout + "\n" + stderr).Trim();
                return process.ExitCode == 0;
            }
            catch (System.Exception e)
            {
                output = e.ToString();
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ------------------------------------------------------------------
        // 表格里的一行
        // ------------------------------------------------------------------

        /// <summary>
        /// 一个形态的一行。序列化下来的是**路径字符串**，
        /// 那些 <c>*Asset</c> 属性只是拖拽入口：get 把路径读成资产显示，set 把资产转成路径。
        /// 这样路径永远是工具算的，手打不了。
        /// </summary>
        public class FormRow
        {
            internal int FormType;
            internal int UnlockStar;
            internal bool IsBaseForm;

            [TableColumnWidth(130, Resizable = false)]
            [ShowInInspector, LabelText("形态"), DisplayAsString(false), PropertyOrder(-3)]
            public string Title =>
                $"{FormId}  {FormName}\n" +
                (FormType == FormTypeBurst ? "爆发形态" : $"基础线 {UnlockStar}★" + (IsBaseForm ? "(基础)" : string.Empty));

            [HideInTables]
            public int FormId;

            [HideInTables]
            public string FormName;

            [TableColumnWidth(76, Resizable = false)]
            [ShowInInspector, PreviewField(54), LabelText("头像"), PropertyOrder(1)]
            public Sprite IconAsset
            {
                get => Load<Sprite>(IconKey);
                set => IconKey = PathOf(value);
            }

            [TableColumnWidth(76, Resizable = false)]
            [ShowInInspector, PreviewField(54), LabelText("略缩图"), PropertyOrder(2)]
            public Sprite UnitPlateAsset
            {
                get => Load<Sprite>(UnitPlateIconKey);
                set => UnitPlateIconKey = PathOf(value);
            }

            [TableColumnWidth(76, Resizable = false)]
            [ShowInInspector, PreviewField(54), LabelText("名字图"), PropertyOrder(3)]
            public Sprite NameIconAsset
            {
                get => Load<Sprite>(NameIconKey);
                set => NameIconKey = PathOf(value);
            }

            /// <summary>立绘。只有基础形态那一行填，觉醒 / 爆发形态用预览图预制体。</summary>
            [TableColumnWidth(76, Resizable = false)]
            [ShowInInspector, PreviewField(54), LabelText("立绘"), PropertyOrder(4)]
            public Sprite ArtAsset
            {
                get => Load<Sprite>(ArtImage);
                set => ArtImage = PathOf(value);
            }

            [TableColumnWidth(140)]
            [ShowInInspector, LabelText("预览图预制体"), PropertyOrder(5)]
            public GameObject StillUnitAsset
            {
                get => Load<GameObject>(StillUnitPrefab);
                set => StillUnitPrefab = PathOf(value);
            }

            /// <summary>
            /// UI 展示预制体 —— **选人界面格子上用的就是它**。
            /// 待机动画名编在预制体的 <c>CharacterGraphicUI</c> 组件上，所以配置表里不存动画名。
            /// </summary>
            [TableColumnWidth(140)]
            [ShowInInspector, LabelText("UI展示预制体"), PropertyOrder(6)]
            public GameObject SkeletonUIAsset
            {
                get => Load<GameObject>(SkeletonUI);
                set => SkeletonUI = PathOf(value);
            }

            /// <summary>战斗里用的 Spine 预制体。选人界面不碰它。</summary>
            [TableColumnWidth(140)]
            [ShowInInspector, LabelText("战斗Spine预制体"), PropertyOrder(7)]
            public GameObject SkeletonScreenAsset
            {
                get => Load<GameObject>(SkeletonScreen);
                set => SkeletonScreen = PathOf(value);
            }

            // 真正写回 Excel 的就是这几个字符串
            [HideInTables] public string IconKey;
            [HideInTables] public string UnitPlateIconKey;
            [HideInTables] public string NameIconKey;
            [HideInTables] public string ArtImage;
            [HideInTables] public string StillUnitPrefab;
            [HideInTables] public string SkeletonUI;
            [HideInTables] public string SkeletonScreen;

            private static T Load<T>(string key) where T : Object =>
                string.IsNullOrEmpty(key) ? null : AssetDatabase.LoadAssetAtPath<T>(key);

            private static string PathOf(Object asset) =>
                asset == null ? string.Empty : AssetDatabase.GetAssetPath(asset);
        }

        // ------------------------------------------------------------------
        // 读 Luban 导出的 json
        // ------------------------------------------------------------------

        private static JArray ReadJson(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JArray.Parse(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[角色资源] 解析配置失败 {path}\n{e}");
                return null;
            }
        }
    }
}

#endif
