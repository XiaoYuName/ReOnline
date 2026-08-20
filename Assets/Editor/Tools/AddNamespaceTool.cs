using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 批量给指定文件夹下所有未声明命名空间的 .cs 脚本包裹上 namespace XFramework。
/// 已经声明了任意命名空间的文件会被跳过，避免破坏已有结构。
/// </summary>
public class AddNamespaceTool
{
    [MenuItem("Tool/命名空间/批量添加 XFramework 命名空间...")]
    public static void AddXFrameworkNamespaceToFolder()
    {
        string folder = EditorUtility.OpenFolderPanel("选择要处理的文件夹", Application.dataPath, "");
        if (string.IsNullOrEmpty(folder)) return;

        string[] files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            EditorUtility.DisplayDialog("批量添加命名空间", "该文件夹下没有找到 .cs 文件", "确定");
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog("批量添加命名空间",
            $"将扫描 {files.Length} 个 .cs 文件，为尚未声明命名空间的脚本加上 namespace XFramework。\n已有命名空间的文件会被跳过。\n\n建议先提交/暂存 Git 再继续。确认继续吗？",
            "继续", "取消");
        if (!confirmed) return;

        int modified = 0;
        List<string> skipped = new List<string>();

        foreach (string file in files)
        {
            string content = File.ReadAllText(file);
            if (Regex.IsMatch(content, @"(?m)^\s*namespace\s+\S"))
            {
                skipped.Add(file);
                continue;
            }

            bool hasCRLF = content.Contains("\r\n");
            string newContent = WrapWithNamespace(content);
            if (hasCRLF) newContent = newContent.Replace("\n", "\r\n");

            File.WriteAllText(file, newContent, new UTF8Encoding(true));
            modified++;
        }

        AssetDatabase.Refresh();

        string message = $"处理完成。\n新增命名空间：{modified} 个\n已跳过(已有命名空间)：{skipped.Count} 个";
        Debug.Log(message + (skipped.Count > 0 ? "\n跳过的文件：\n" + string.Join("\n", skipped) : ""));
        EditorUtility.DisplayDialog("批量添加命名空间", message, "确定");
    }

    static string WrapWithNamespace(string content)
    {
        string[] lines = content.Replace("\r\n", "\n").Split('\n');

        // 头部：连续的 using / 预处理指令 / 空行，其余归为需要包裹的正文
        int splitIndex = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            bool isHeaderLine = t.Length == 0
                || t.StartsWith("using ")
                || t.StartsWith("#if") || t.StartsWith("#endif")
                || t.StartsWith("#else") || t.StartsWith("#elif")
                || t.StartsWith("#region") || t.StartsWith("#endregion");
            if (!isHeaderLine) break;
            splitIndex = i + 1;
        }

        List<string> headerLines = lines.Take(splitIndex)
            // 命名空间本身就是 XFramework 了，引用自身的 using 是多余的，顺手去掉
            .Where(l => l.Trim() != "using XFramework;")
            .ToList();
        List<string> bodyLines = lines.Skip(splitIndex).ToList();

        while (headerLines.Count > 0 && headerLines[headerLines.Count - 1].Trim().Length == 0)
            headerLines.RemoveAt(headerLines.Count - 1);
        while (bodyLines.Count > 0 && bodyLines[0].Trim().Length == 0)
            bodyLines.RemoveAt(0);
        while (bodyLines.Count > 0 && bodyLines[bodyLines.Count - 1].Trim().Length == 0)
            bodyLines.RemoveAt(bodyLines.Count - 1);

        StringBuilder sb = new StringBuilder();
        foreach (string line in headerLines)
            sb.Append(line).Append('\n');
        if (headerLines.Count > 0)
            sb.Append('\n');

        sb.Append("namespace XFramework\n{\n");
        foreach (string line in bodyLines)
            sb.Append(line.Length == 0 ? "" : "    " + line).Append('\n');
        sb.Append("}\n");

        return sb.ToString();
    }
}
