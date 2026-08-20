using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class FitBackgroundToCamera : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool cover = true;

    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void Start()
    {
        Fit();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (targetCamera == null) targetCamera = Camera.main;
            Fit();
        }
    }
#endif
    
#if UNITY_EDITOR
    private void Update()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
        if (targetCamera == null) targetCamera = Camera.main;
        Fit();
    }
#endif

    public void Fit()
    {
        if (targetCamera == null || spriteRenderer == null || spriteRenderer.sprite == null)
        {
            return;
        }

        float cameraHeight = targetCamera.orthographicSize * 2f;
        float cameraWidth = cameraHeight * targetCamera.aspect;

        Vector2 spriteSize = spriteRenderer.sprite.bounds.size;

        float scaleX = cameraWidth / spriteSize.x;
        float scaleY = cameraHeight / spriteSize.y;

        float scale = cover
            ? Mathf.Max(scaleX, scaleY)
            : Mathf.Min(scaleX, scaleY);

        transform.localScale = new Vector3(scale, scale, 1f);
        transform.position = new Vector3(
            targetCamera.transform.position.x,
            targetCamera.transform.position.y,
            transform.position.z
        );
    }
}