using System.Collections.Generic;
using UnityEngine;
using XFramework;

/// <summary>
/// 托管一组 AA 资源引用，通过 Release 统一 FreeAsset。
/// 一般不手动挂载，由 UIBase.LoadAsset 自动挂到面板上。
/// Image 图标请优先用 image.SetIcon（IconLoadExtension），支持换图时提前归还引用。
/// </summary>
public class AssetReleaser : MonoBehaviour
{
    [SerializeField] readonly List<string> keys = new();

    public void Track(string key)
    {
        if (!string.IsNullOrEmpty(key))
            keys.Add(key);
    }

    /// <summary>
    /// 释放当前托管的全部资源引用。清空列表保证重复调用安全。
    /// </summary>
    public void Release()
    {
        foreach (var key in keys)
            AssetsManager.Instance.FreeAsset(key);
        keys.Clear();
    }

    void OnDestroy()
    {
        // 非 UIBase 使用场景的兜底；正常 UI 关闭时会提前调用 Release。
        Release();
    }
}
