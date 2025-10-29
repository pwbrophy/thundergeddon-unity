using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class JoystickCircle : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    [SerializeField] RectTransform handle;
    [SerializeField] float maxRadiusPixels = 140f;
    [SerializeField] float deadzone = 0.08f;
    [SerializeField] bool snapBackOnRelease = true;

    public Vector2 Value { get; private set; }

    RectTransform _rt;
    Vector2 _centerScreenPos;

    void Awake()
    {
        EnsureRT();
        CacheCenter();
        CenterHandle();
    }

    void OnEnable() => EnsureRT();

    void EnsureRT()
    {
        if (_rt == null) _rt = GetComponent<RectTransform>();
    }

    void OnRectTransformDimensionsChange()
    {
        // This can fire before Awake(); make sure _rt exists and object is active
        EnsureRT();
        if (!_rt || !gameObject.activeInHierarchy) return;
        CacheCenter();
    }

    void CacheCenter()
    {
        // Safer center calc that doesn’t need world corners
        Vector2 worldCenter = _rt.TransformPoint(_rt.rect.center);
        _centerScreenPos = RectTransformUtility.WorldToScreenPoint(null, worldCenter);
    }

    public void OnPointerDown(PointerEventData e) => UpdateStick(e);
    public void OnDrag(PointerEventData e) => UpdateStick(e);

    public void OnPointerUp(PointerEventData e)
    {
        Value = Vector2.zero;
        if (snapBackOnRelease) CenterHandle();
    }

    void UpdateStick(PointerEventData e)
    {
        Vector2 delta = e.position - _centerScreenPos;
        delta = Vector2.ClampMagnitude(delta, maxRadiusPixels);
        if (handle) handle.anchoredPosition = delta;

        var norm = delta / maxRadiusPixels;
        Value = (norm.magnitude < deadzone) ? Vector2.zero : norm;
    }

    void CenterHandle()
    {
        if (handle) handle.anchoredPosition = Vector2.zero;
    }
}
