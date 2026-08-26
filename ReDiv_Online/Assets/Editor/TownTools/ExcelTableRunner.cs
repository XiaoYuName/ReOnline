#if UNITY_EDITOR

using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace XFramework
{
    /// <summary>
    /// 从编辑器里跑 <c>ExcelTool/LubanTools/ExcelTable.ps1</c> 的公共壳子。
    ///
    /// 为什么必须走那个脚本、不能用 openpyxl 之类：它是 **Excel COM 自动化**，
    /// 表里的公式单元格要靠真正的 Excel 重算并写回缓存值，否则 Luban（只读缓存值）读到空。
    /// 理由写在脚本头部，别绕开。
    ///
    /// 三个不能省的处理（和 <c>SpacetimeCli</c> 里同样的坑）：
    /// <list type="bullet">
    ///   <item>中文输出要显式 **UTF8**，否则中文 Windows 上是乱码；</item>
    ///   <item>要 <c>WaitForExit</c> 才知道成没成 —— 脚本失败时**整份 Excel 不保存**，
    ///         所以失败了原表还是干净的；</item>
    ///   <item>工作目录必须是**工程根**，脚本里的相对路径按那个基准解析。</item>
    /// </list>
    ///
    /// ⚠️ 每次调用都会后台起一个不可见的 EXCEL.EXE，慢（秒级）。
    /// 所以调用方要把一批改动**攒成一次**写回，别一行一次。
    /// </summary>
    public static class ExcelTableRunner
    {
        private const string ScriptPath = "ExcelTool/LubanTools/ExcelTable.ps1";

        /// <summary>工程根目录（`Assets` 的上一级）。</summary>
        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        /// <summary>按主键改若干行的若干列（`-File` 指向的 JSON 里没出现的列不动）。</summary>
        public static bool UpdateRows(string workbook, string sheet, string jsonPath,
                                      string keyColumns, out string output) =>
            Run($"-Action UpdateRows -Workbook \"{workbook}\" -Sheet \"{sheet}\" " +
                $"-File \"{jsonPath}\" -KeyColumns \"{keyColumns}\"", out output);

        /// <summary>往表尾追加若干行。</summary>
        public static bool AddRows(string workbook, string sheet, string jsonPath, out string output) =>
            Run($"-Action AddRows -Workbook \"{workbook}\" -Sheet \"{sheet}\" -File \"{jsonPath}\"",
                out output);

        /// <summary>按主键删若干行（<paramref name="keys"/> 逗号分隔）。任一主键找不到就整份不保存。</summary>
        public static bool DeleteRows(string workbook, string sheet, string keys, out string output) =>
            Run($"-Action DeleteRows -Workbook \"{workbook}\" -Sheet \"{sheet}\" -Keys \"{keys}\"",
                out output);

        /// <summary>直接给参数跑一次。<paramref name="arguments"/> 是 `-Action ...` 那一串。</summary>
        public static bool Run(string arguments, out string output)
        {
            string script = Path.Combine(ProjectRoot, ScriptPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(script))
            {
                output = $"找不到 {script}";
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" {arguments}",
                WorkingDirectory = ProjectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            try
            {
                EditorUtility.DisplayProgressBar("Excel", "正在改表（会后台开一个 Excel 进程）...", 0.5f);

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

        /// <summary>把一段 JSON 写到临时文件（UTF8 无 BOM，脚本按 UTF8 读）。返回文件路径。</summary>
        public static string WriteTempJson(string fileName, string json)
        {
            string path = Path.Combine(Path.GetTempPath(), fileName);
            File.WriteAllText(path, json, new UTF8Encoding(false));
            return path;
        }
    }
}

#endif
