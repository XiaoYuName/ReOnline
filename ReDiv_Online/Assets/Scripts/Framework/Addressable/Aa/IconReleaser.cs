using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using XFramework;

public class IconReleaser : MonoBehaviour
{
    string key;
    AssetReferenceSprite reference;

    public void Load(Image image, string key)
    {
        if (reference == null && this.key == key) return;

        Release();
        this.key = key;
        LoadAsync(image, key).Forget();
    }

    public void Load(Image image, AssetReferenceSprite reference)
    {
        if (this.reference == reference) return;

        Release();
        this.reference = reference;
        LoadAsync(image, reference).Forget();
    }

    /// <summary>
    /// 清空图标：归还 AA 引用并置空 sprite。
    /// 直接 image.sprite = null 会残留加载标识，下次 Load 同一张图会被防重复早退导致不加载。
    /// </summary>
    public void Clear(Image image)
    {
        Release();
        image.sprite = null;
    }

    async UniTaskVoid LoadAsync(Image image, string key)
    {
        Sprite sprite = await AssetsManager.Instance.LoadAssetsUniTask<Sprite>(key);
        if (reference != null || this.key != key) return;

        image.sprite = sprite;
    }

    async UniTaskVoid LoadAsync(Image image, AssetReferenceSprite reference)
    {
        Sprite sprite = await AssetsManager.Instance.LoadAssetsUniTask<Sprite>(reference);
        if (this.reference != reference) return;

        image.sprite = sprite;
    }

    void Release()
    {
        if (reference != null)
        {
            AssetsManager.Instance.FreeAsset(reference);
            reference = null;
        }
        else if (!string.IsNullOrEmpty(key))
        {
            AssetsManager.Instance.FreeAsset(key);
        }

        key = null;
    }

    void OnDestroy() => Release();
}
