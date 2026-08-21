using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 支持持续长按回调，并根据按住时长逐渐缩短触发间隔的按钮。
/// </summary>
public class LongPressButton : Button
{
    [SerializeField, Min(0f)]
    private float longPressDelay = 0.3f;

    [SerializeField, Min(0.01f)]
    private float initialRepeatInterval = 0.3f;

    [SerializeField, Min(0.01f)]
    private float minimumRepeatInterval = 0.1f;

    [SerializeField, Min(0.01f)]
    private float accelerationDuration = 2f;

    [SerializeField]
    private bool useUnscaledTime = true;

    [SerializeField]
    private bool suppressClickAfterLongPress = true;

    [SerializeField]
    private UnityEvent onLongPress = new UnityEvent();

    private bool isPointerDown;
    private bool isPointerInside;
    private bool hasTriggeredLongPress;
    private bool suppressNextClick;
    private float pressStartTime;
    private float nextTriggerTime;

    /// <summary>
    /// 长按期间按照动态间隔重复触发的事件。
    /// </summary>
    public UnityEvent OnLongPress => onLongPress;

    public override void OnPointerDown(PointerEventData eventData)
    {
        base.OnPointerDown(eventData);

        if (eventData.button != PointerEventData.InputButton.Left || !IsActive() || !IsInteractable())
        {
            return;
        }

        isPointerDown = true;
        isPointerInside = true;
        hasTriggeredLongPress = false;
        suppressNextClick = false;
        pressStartTime = CurrentTime;
        nextTriggerTime = pressStartTime + longPressDelay;
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        base.OnPointerEnter(eventData);
        isPointerInside = true;
    }

    public override void OnPointerExit(PointerEventData eventData)
    {
        base.OnPointerExit(eventData);
        isPointerInside = false;
    }

    public override void OnPointerUp(PointerEventData eventData)
    {
        base.OnPointerUp(eventData);

        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        isPointerDown = false;
        suppressNextClick = suppressClickAfterLongPress && hasTriggeredLongPress;
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && suppressNextClick)
        {
            suppressNextClick = false;
            return;
        }

        base.OnPointerClick(eventData);
    }

    private void Update()
    {
        if (!isPointerDown || !isPointerInside || !IsActive() || !IsInteractable())
        {
            return;
        }

        float currentTime = CurrentTime;
        if (currentTime < nextTriggerTime)
        {
            return;
        }

        hasTriggeredLongPress = true;
        onLongPress.Invoke();

        float heldTimeAfterFirstTrigger = Mathf.Max(0f, currentTime - pressStartTime - longPressDelay);
        float accelerationProgress = Mathf.Clamp01(heldTimeAfterFirstTrigger / accelerationDuration);
        float currentInterval = Mathf.Lerp(
            initialRepeatInterval,
            minimumRepeatInterval,
            accelerationProgress);

        nextTriggerTime = currentTime + currentInterval;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        ResetPressState();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        longPressDelay = Mathf.Max(0f, longPressDelay);
        minimumRepeatInterval = Mathf.Max(0.01f, minimumRepeatInterval);
        initialRepeatInterval = Mathf.Max(minimumRepeatInterval, initialRepeatInterval);
        accelerationDuration = Mathf.Max(0.01f, accelerationDuration);
    }
#endif

    private float CurrentTime => useUnscaledTime ? Time.unscaledTime : Time.time;

    private void ResetPressState()
    {
        isPointerDown = false;
        isPointerInside = false;
        hasTriggeredLongPress = false;
        suppressNextClick = false;
    }
}
