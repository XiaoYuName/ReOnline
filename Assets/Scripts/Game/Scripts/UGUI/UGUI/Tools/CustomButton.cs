using DG.Tweening;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Localization.Components;
using UnityEngine.UI;
using XFramework;

public class CustomButton : Button
{
    [SerializeField]
    private bool isTweenerScale = true;
    [LabelText("缩放比例"),SerializeField]
    private float tweenerScale = 1.1f;
    [SerializeField]
    private float tweenerDuration = 0.1f;
    [SerializeField]
    private Ease tweenerEase = Ease.OutQuad;
    private Tweener _scaleTweener;
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
    public Color EnterColor;
    [SerializeField]
    public Color ExitColor;

    public void SetLabel(LocalSelectedData label)
    {
        if (ButtonText == null) return;
        if (BtnStringEvent == null)
        {
            BtnStringEvent = ButtonText.GetComponent<LocalizeStringEvent>();
        }
        BtnStringEvent.SetText(label.Table,label.Value);
    }

    public void SetLabel(TbLocalzationKeyData label)
    {
        if (ButtonText == null) return;
        if (BtnStringEvent == null)
        {
            BtnStringEvent = ButtonText.GetComponent<LocalizeStringEvent>();
        }
        BtnStringEvent.SetText(label.Table,label.Value);
    }

    public void SetLabel(string label)
    {
        if (ButtonText == null) return;
        ButtonText.text = label;
    }

    public void SetLabel(string label, string text)
    {
        if (ButtonText == null) return;
        if (BtnStringEvent == null)
        {
            BtnStringEvent = ButtonText.GetComponent<LocalizeStringEvent>();
        }
        BtnStringEvent.SetText(label,text);
    }

    public void SetLabelVal(string name, string value)
    {
        if (ButtonText == null) return;
        if (BtnStringEvent == null)
        {
            BtnStringEvent = ButtonText.GetComponent<LocalizeStringEvent>();
        }
        BtnStringEvent.SetVar(name,value);
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

        if (ButtonText != null)
        {
            ButtonText.color = EnterColor;
        }
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

        if (ButtonText != null)
        {
            ButtonText.color = ExitColor;
        }
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
