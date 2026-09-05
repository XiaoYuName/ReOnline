using UnityEngine;

/// <summary>
/// Uniformly scales a SpriteRenderer background group to an orthographic camera.
/// Cover mode fills the viewport and crops overflow instead of leaving letterbox bars.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class BattleBackgroundScreenFitter : MonoBehaviour
{
    public enum FitMode
    {
        Cover,
        Contain
    }

    [SerializeField] private Camera targetCamera;
    [SerializeField] private FitMode fitMode = FitMode.Cover;
    [SerializeField] private bool centerOnCamera = true;
    [SerializeField] private bool refitWhenChanged = true;
    [SerializeField, Min(0.01f)] private float scaleMultiplier = 1f;

    private BattleBackgroundVariantSet variantSet;
    private Camera lastCamera;
    private int lastPixelWidth = -1;
    private int lastPixelHeight = -1;
    private int lastVariantIndex = int.MinValue;
    private float lastOrthographicSize = float.NaN;
    private Vector2 lastCameraPosition = new(float.NaN, float.NaN);
    private bool fitPending = true;

    public Camera TargetCamera
    {
        get => targetCamera;
        set
        {
            targetCamera = value;
            Invalidate();
        }
    }

    public FitMode Mode
    {
        get => fitMode;
        set
        {
            fitMode = value;
            Invalidate();
        }
    }

    private void OnEnable()
    {
        variantSet = GetComponent<BattleBackgroundVariantSet>();
        Invalidate();
        FitNow();
    }

    private void Start()
    {
        FitNow();
    }

    private void LateUpdate()
    {
        if (refitWhenChanged && NeedsRefit())
            FitNow();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        scaleMultiplier = Mathf.Max(0.01f, scaleMultiplier);
        variantSet = GetComponent<BattleBackgroundVariantSet>();
        Invalidate();
    }
#endif

    /// <summary>
    /// Immediately fits all active SpriteRenderers below this transform.
    /// In a variant set this means the currently selected Back and Front layers.
    /// </summary>
    [ContextMenu("Fit To Camera Now")]
    public bool FitNow()
    {
        Camera camera = ResolveCamera();
        if (camera == null || !camera.orthographic || !TryGetActiveLocalBounds(out Bounds bounds))
            return false;

        float parentScaleX = GetParentAxisScale(Vector3.right);
        float parentScaleY = GetParentAxisScale(Vector3.up);
        float viewportHeight = camera.orthographicSize * 2f;
        float viewportWidth = viewportHeight * camera.aspect;
        float widthScale = viewportWidth / (bounds.size.x * parentScaleX);
        float heightScale = viewportHeight / (bounds.size.y * parentScaleY);
        float uniformScale = (fitMode == FitMode.Cover
            ? Mathf.Max(widthScale, heightScale)
            : Mathf.Min(widthScale, heightScale)) * scaleMultiplier;

        Vector3 localScale = transform.localScale;
        localScale.x = uniformScale;
        localScale.y = uniformScale;
        transform.localScale = localScale;

        if (centerOnCamera)
        {
            Vector3 worldCenter = transform.TransformPoint(bounds.center);
            Vector3 position = transform.position;
            position.x += camera.transform.position.x - worldCenter.x;
            position.y += camera.transform.position.y - worldCenter.y;
            transform.position = position;
        }

        RememberState(camera);
        return true;
    }

    public void Invalidate()
    {
        fitPending = true;
    }

    private Camera ResolveCamera()
    {
        if (targetCamera != null)
            return targetCamera;

        Camera camera = Camera.main;
        if (camera == null)
            camera = FindFirstObjectByType<Camera>();
        return camera;
    }

    private bool NeedsRefit()
    {
        Camera camera = ResolveCamera();
        if (fitPending || camera == null || camera != lastCamera)
            return true;

        int variantIndex = variantSet != null ? variantSet.ActiveVariantIndex : 0;
        Vector2 cameraPosition = camera.transform.position;
        return camera.pixelWidth != lastPixelWidth
            || camera.pixelHeight != lastPixelHeight
            || variantIndex != lastVariantIndex
            || !Mathf.Approximately(camera.orthographicSize, lastOrthographicSize)
            || cameraPosition != lastCameraPosition;
    }

    private void RememberState(Camera camera)
    {
        fitPending = false;
        lastCamera = camera;
        lastPixelWidth = camera.pixelWidth;
        lastPixelHeight = camera.pixelHeight;
        lastVariantIndex = variantSet != null ? variantSet.ActiveVariantIndex : 0;
        lastOrthographicSize = camera.orthographicSize;
        lastCameraPosition = camera.transform.position;
    }

    private bool TryGetActiveLocalBounds(out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: false);
        foreach (SpriteRenderer renderer in renderers)
        {
            if (!renderer.enabled || renderer.sprite == null)
                continue;

            Bounds spriteBounds = renderer.sprite.bounds;
            Matrix4x4 toRoot = transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            Vector3 min = spriteBounds.min;
            Vector3 max = spriteBounds.max;
            Encapsulate(ref bounds, ref hasBounds, toRoot.MultiplyPoint3x4(new Vector3(min.x, min.y, 0f)));
            Encapsulate(ref bounds, ref hasBounds, toRoot.MultiplyPoint3x4(new Vector3(min.x, max.y, 0f)));
            Encapsulate(ref bounds, ref hasBounds, toRoot.MultiplyPoint3x4(new Vector3(max.x, min.y, 0f)));
            Encapsulate(ref bounds, ref hasBounds, toRoot.MultiplyPoint3x4(new Vector3(max.x, max.y, 0f)));
        }

        return hasBounds && bounds.size.x > Mathf.Epsilon && bounds.size.y > Mathf.Epsilon;
    }

    private static void Encapsulate(ref Bounds bounds, ref bool hasBounds, Vector3 point)
    {
        if (!hasBounds)
        {
            bounds = new Bounds(point, Vector3.zero);
            hasBounds = true;
            return;
        }

        bounds.Encapsulate(point);
    }

    private float GetParentAxisScale(Vector3 axis)
    {
        if (transform.parent == null)
            return 1f;

        return Mathf.Max(transform.parent.TransformVector(axis).magnitude, Mathf.Epsilon);
    }
}
