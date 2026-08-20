
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Windows;

public  class SaveToolEditor: Editor
{
    [MenuItem("Tools/XFramework/实用工具/打开 Addressables 保存路径", false, 420)]
    public static void OpenUserPath()
    {
        string path = Application.persistentDataPath;
        if (Directory.Exists(path)) 
        {
            EditorUtility.RevealInFinder(path);
        }
        else
        {
            EditorUtility.DisplayDialog("读取文件夹", "未找到存储路径", "确定");
        }
    }

    
    [MenuItem("Tools/XFramework/实用工具/打开 UnityAPI", false, 421)]
    public static void OpenUnityUrl()
    {
        Application.OpenURL("https://docs.unity3d.com/cn/2020.2/ScriptReference/index.html");
    }
    [MenuItem("Tools/XFramework/实用工具/打开 C# API", false, 422)]
    public static void OpenNETUrl()
    {
        Application.OpenURL("https://learn.microsoft.com/zh-cn/dotnet/api/");
    }
    [MenuItem("Tools/XFramework/实用工具/打开 BilBil", false, 423)]
    public static void OpenBilBil()
    {
        Application.OpenURL("https://www.bilibili.com");
    }
}
