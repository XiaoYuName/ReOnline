using UnityEngine;
using System;
using TMPro;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using XFramework;

public static partial class LocStringEventExtensions
{
    // 参数保留 LocalSelectedData（而非本文件夹自维护的 LocKeyRef）：
    // CustomDropdownUI.cs（他人脚本）在用这个重载，为了不改动他人脚本而保留兼容。
    public static void SetText(this LocalizeStringEvent e, LocalSelectedData data, bool refresh = true)
    {
        e.StringReference.SetReference(data.Table, data.Value);
        if (refresh)
        {
            e.RefreshString();
        }
    }

    public static void SetText(this LocalizeStringEvent e, TbLocalzationKeyData data, bool refresh = true)
    {
        e.StringReference.SetReference(data.Table, data.Value);
        if (refresh)
        {
            e.RefreshString();
        }
    }

    /// <summary>
    /// 直接使用表名和键值设置本地化文本
    /// </summary>
    public static void SetText(this LocalizeStringEvent e, string table, string key, bool refresh = true)
    {
        e.StringReference.SetReference(table, key);
        if (refresh)
        {
            e.RefreshString();
        }
    }

    /// <summary>
    /// 设置本地化字符串的变量值
    /// </summary>
    public static void SetVar(this LocalizeStringEvent e, string name, string value, bool refresh = true)
    {
        e.StringReference.Arguments = new object[] { new { name = name, value = value } };
        if (refresh)
        {
            e.RefreshString();
        }
    }
}
