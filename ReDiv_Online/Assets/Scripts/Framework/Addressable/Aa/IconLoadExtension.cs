using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using XFramework;

namespace XFramework
{
    /// <summary>
    /// Image 图标异步加载扩展：数据层只持有 AA Key，由 UI 层即发即忘地异步加载，避免在属性 getter 里同步阻塞主线程。
    /// 加载引用由 Image 上自动挂载的 IconReleaser 托管，换图/销毁时自动 FreeAsset，调用方无需手动释放。
    /// </summary>
    public static class IconLoadExtension
    {
        public static void SetIcon(this Image image, string key)
        {
            if (!image.TryGetComponent(out IconReleaser releaser))
                releaser = image.gameObject.AddComponent<IconReleaser>();
            releaser.Load(image, key);
        }

        public static void SetIcon(this Image image, AssetReferenceSprite reference)
        {
            if (reference == null || !reference.RuntimeKeyIsValid())
            {
                image.ClearIcon();
                Debug.LogError(reference.SubObjectName + "引用的图标不存在", image);
                return;
            }

            if (!image.TryGetComponent(out IconReleaser releaser))
                releaser = image.gameObject.AddComponent<IconReleaser>();
            releaser.Load(image, reference);
        }

        /// <summary>清空图标并归还 AA 引用，与 SetIcon 配对使用；不要直接 image.sprite = null。</summary>
        public static void ClearIcon(this Image image)
        {
            if (image.TryGetComponent<IconReleaser>(out var releaser))
                releaser.Clear(image);
            else
                image.sprite = null;
        }
    }
}
