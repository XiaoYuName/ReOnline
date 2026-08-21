using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using XFramework;
using Object = UnityEngine.Object;

public static class AssetRefExtension
{
    /// <summary>
    /// 通过 AssetsManager 按 AssetReference 的 Key 异步加载资源，复用其缓存与引用计数（而非各自持有 AA 句柄）。
    /// </summary>
    public static UniTask<T> LoadAsset<T>(this AssetReference reference) where T : Object
    {
        return AssetsManager.Instance.LoadAssetsUniTask<T>(reference.AssetGUID);
    }
    public static T LoadAssets<T>(this AssetReference reference) where T : Object
    {
        return AssetsManager.Instance.LoadAssets<T>(reference.AssetGUID);
    }

    /// <summary>
    /// 通过 AssetsManager 释放该 AssetReference 加载的资源（引用计数归零后真正卸载）。需与 <see cref="LoadAsset{T}"/> 配对调用。
    /// </summary>
    public static void ReleaseAsset(this AssetReference reference)
    {
        AssetsManager.Instance.FreeAsset(reference.AssetGUID);
    }
}
