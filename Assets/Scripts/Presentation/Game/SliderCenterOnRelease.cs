using UnityEngine;                     // Unity essentials
using UnityEngine.UI;                  // For Slider
using UnityEngine.EventSystems;        // For IPointer / IEndDrag

// Attach this to the SAME GameObject that has your Slider component.
// It will snap the slider back to 0.5 when you release the mouse/finger.
// Every line is commented to keep it simple.
public class SliderCenterOnRelease : MonoBehaviour, IPointerUpHandler, IEndDragHandler
{
    // A reference to the slider we will control.
    // If you forget to set it in the Inspector, we will auto-grab it in Awake.
    [SerializeField] private Slider slider;

    // Called when the script instance is being loaded.
    private void Awake()
    {
        // If the user didn’t plug the slider in, try to find it on this GameObject.
        if (slider == null) slider = GetComponent<Slider>();
    }

    // Called when the user releases the mouse/finger over this UI element.
    public void OnPointerUp(PointerEventData eventData)
    {
        // Set the slider back to the middle (0.5 = center).
        // Using 'slider.value' (not SetValueWithoutNotify) triggers your existing OnValueChanged,
        // which will send a turret value near zero.
        if (slider != null) slider.value = 0.5f;
    }

    // Called when a drag operation ends (e.g., releasing outside the control).
    public void OnEndDrag(PointerEventData eventData)
    {
        // Also snap to center here so it works whether you release on or off the slider.
        if (slider != null) slider.value = 0.5f;
    }
}
