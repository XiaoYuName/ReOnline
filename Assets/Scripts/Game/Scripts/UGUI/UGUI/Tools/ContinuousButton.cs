using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ContinuousButton : Button
{
    
    public UnityEvent ContinuousButtonPressed;
    
    public UnityEvent ContinuousButtonReleased;


    private bool isPressed = false;
    public override void OnPointerDown(PointerEventData eventData)
    {
        base.OnPointerDown(eventData);
        isPressed = true;
    }

    public override void OnPointerUp(PointerEventData eventData)
    {
        base.OnPointerUp(eventData);
        isPressed = false;
        ContinuousButtonReleased?.Invoke();
    }

    private void Update()
    {
        if (isPressed)
        {
            ContinuousButtonPressed?.Invoke();
        }
    }
}
