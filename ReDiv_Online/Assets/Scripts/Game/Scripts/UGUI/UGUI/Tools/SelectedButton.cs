using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

public class SelectedButton : Button
{
    
    [SerializeField]
    private bool isTweenerScale = true;
    [SerializeField]
    private float tweenerScale = 1.1f;
    [SerializeField]
    private float tweenerDuration = 0.1f;
    [SerializeField]
    private Ease tweenerEase = Ease.OutQuad;
    private Tweener _scaleTweener;
    
    
    [SerializeField]
    private Color enterColor;
    
    [SerializeField]
    private Color exitColor;
    
    [SerializeField]
    private Color selectedColor;

    public bool isSelected { get; private set; }
    
    public bool isPointerEnter { get; private set; }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        if (isSelected)
        {
            targetGraphic.color = selectedColor;
        }
        else
        {
            targetGraphic.color = isPointerEnter ? enterColor : exitColor;
        }
    }
    
    [SerializeField]
    private UnityEvent OnPointEnter;
    [SerializeField]
    private UnityEvent OnPointExit;
    [SerializeField]
    private UnityEvent OnPointUp;
    [SerializeField]
    private UnityEvent OnPointDown;
    
    [SerializeField]
    private TextMeshProUGUI ButtonText;

    private LocalizeStringEvent BtnStringEvent;
    
    [SerializeField]
    public Color enterTextColor;
    [SerializeField]
    public Color exitTextColor;
    
    public void SetLabel(LocalSelectedData label)
    {
        if (ButtonText == null) return;
        if (BtnStringEvent == null)
        {
            BtnStringEvent = ButtonText.GetComponent<LocalizeStringEvent>();
        }
        BtnStringEvent.StringReference.SetReference(label.Table,label.Value);
    }
    
    
    public override void OnPointerDown(PointerEventData eventData)
    {
        base.OnPointerDown(eventData);
        if (interactable)
        {
            OnPointDown?.Invoke();
        }
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        base.OnPointerEnter(eventData);
        if (interactable && isTweenerScale)
        {
            _scaleTweener?.Kill();
            _scaleTweener = transform.DOScale(tweenerScale, tweenerDuration).SetEase(tweenerEase);
            OnPointEnter?.Invoke();
        }

        if (interactable && !isSelected)
        {
            targetGraphic.color = enterColor;
        }

        if (interactable && ButtonText != null)
        {
            ButtonText.color = enterTextColor;
        }

        isPointerEnter = true;
    }

    public override void OnPointerExit(PointerEventData eventData)
    {
        base.OnPointerExit(eventData);
        if (interactable && isTweenerScale)
        {
            _scaleTweener?.Kill();
            _scaleTweener = transform.DOScale(Vector3.one, tweenerDuration).SetEase(tweenerEase);
            OnPointExit?.Invoke();
        }

        if (interactable && !isSelected)
        {
            targetGraphic.color = exitColor;
        }

        if (interactable && ButtonText != null)
        {
            ButtonText.color = exitTextColor;
        }

        isPointerEnter = false;
    }

    public override void OnPointerUp(PointerEventData eventData)
    {
        base.OnPointerUp(eventData);
        if (interactable)
        {
            OnPointUp?.Invoke();
        }

    }
}
