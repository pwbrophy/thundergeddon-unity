using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Image))] // makes this object raycastable
public class RobotControlPanel : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("Joystick UI")]
    [SerializeField] private RectTransform baseRect;     // if empty, uses this RectTransform
    [SerializeField] private RectTransform handleRect;

    [Header("Turret")]
    [SerializeField] private Slider turretSlider;
    [SerializeField] private float turretDeadzone = 0.05f;

    [Header("Selection")]
    [SerializeField] private RobotSelectionPanel selectionPanel;

    [Header("Tuning")]
    [SerializeField] private float sendHz = 20f;         // max command rate while held
    [SerializeField] private float changeEpsilon = 0.01f;// only send if changed by this amount
    [SerializeField] private float resendEvery = 0.25f;  // resend same command while held (0 = never)
    [SerializeField] private float deadzone = 0.08f;     // stick deadzone
    [SerializeField] private float maxDeflection = 0.45f;// clamp radius (UI)

    private RobotWebSocketServer _ws;
    private Camera _uiCam;

    private Vector2 _joy;                  // -1..+1 (x=turn, y=throttle)
    private Vector2 _lastSent = new Vector2(999, 999);
    private float _nextSendAt;
    private float _nextResendAt;
    private bool _isHeld;                  // true while pointer is down

    private void Awake()
    {
        var img = GetComponent<Image>();
        img.raycastTarget = true;
        if (img.sprite == null) img.color = new Color(0, 0, 0, 0); // invisible but raycastable

        if (baseRect == null) baseRect = GetComponent<RectTransform>();
        _uiCam = GetComponentInParent<Canvas>()?.worldCamera;

        _ws = ServiceLocator.RobotServer;

        if (turretSlider != null)
        {
            turretSlider.onValueChanged.RemoveAllListeners();
            turretSlider.onValueChanged.AddListener(OnTurretChanged);
        }
    }

    private void OnEnable()
    {
        if (selectionPanel != null)
            selectionPanel.SelectionChanged += OnSelectionChanged;
        CenterControls();
    }

    private void OnDisable()
    {
        if (selectionPanel != null)
            selectionPanel.SelectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged(string _)
    {
        // Don’t auto-send stop here; selection panel already handles motors on/off.
        CenterControls();
        _lastSent = new Vector2(999, 999);
    }

    private void CenterControls()
    {
        _joy = Vector2.zero;
        if (handleRect) handleRect.anchoredPosition = Vector2.zero;
        if (turretSlider) turretSlider.SetValueWithoutNotify(0.5f);
        _nextSendAt = 0f;
        _nextResendAt = 0f;
        _isHeld = false;
    }

    public void OnPointerDown(PointerEventData e)
    {
        _isHeld = true;
        UpdateHandle(e);
        // force immediate send on press
        _nextSendAt = 0f;
        _nextResendAt = Time.unscaledTime;
    }

    public void OnDrag(PointerEventData e)
    {
        UpdateHandle(e);
    }

    public void OnPointerUp(PointerEventData e)
    {
        _isHeld = false;
        _joy = Vector2.zero;
        if (handleRect) handleRect.anchoredPosition = Vector2.zero;

        // Send ONE stop when released
        var ws = ServiceLocator.RobotServer;
        var robotId = selectionPanel != null ? selectionPanel.CurrentRobotId : null;
        if (ws != null && !string.IsNullOrEmpty(robotId))
        {
            ws.SendDrive(robotId, 0f, 0f);
            _lastSent = new Vector2(0f, 0f);
        }
    }

    private void UpdateHandle(PointerEventData e)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(baseRect, e.position, _uiCam, out var lp))
            return;

        var half = baseRect.rect.size * 0.5f;
        var norm = new Vector2(lp.x / half.x, lp.y / half.y);
        norm = Vector2.ClampMagnitude(norm, maxDeflection);

        if (handleRect) handleRect.anchoredPosition = new Vector2(norm.x * half.x, norm.y * half.y);

        var scaled = norm / Mathf.Max(0.0001f, maxDeflection);   // -1..+1
        _joy = (scaled.magnitude < deadzone) ? Vector2.zero : Vector2.ClampMagnitude(scaled, 1f);
    }

    private void Update()
    {
        // Only drive while held; turret slider still works via its callback.
        if (!_isHeld) return;

        if (Time.unscaledTime < _nextSendAt) return;
        _nextSendAt = Time.unscaledTime + (1f / Mathf.Max(1f, sendHz));

        var ws = ServiceLocator.RobotServer;
        var robotId = selectionPanel != null ? selectionPanel.CurrentRobotId : null;
        if (ws == null || string.IsNullOrEmpty(robotId)) return;

        float x = Mathf.Clamp(_joy.x, -1f, 1f); // turn
        float y = Mathf.Clamp(_joy.y, -1f, 1f); // throttle
        float left = Mathf.Clamp(y + x, -1f, 1f);
        float right = Mathf.Clamp(y - x, -1f, 1f);

        bool changed = (Mathf.Abs(left - _lastSent.x) > changeEpsilon) ||
                       (Mathf.Abs(right - _lastSent.y) > changeEpsilon);

        bool resend = (resendEvery > 0f) && (Time.unscaledTime >= _nextResendAt);

        if (changed || resend)
        {
            ws.SendDrive(robotId, left, right);
            _lastSent = new Vector2(left, right);
            if (resend) _nextResendAt = Time.unscaledTime + resendEvery;
        }
    }

    private void OnTurretChanged(float slider01)
    {
        var ws = ServiceLocator.RobotServer;
        var robotId = selectionPanel != null ? selectionPanel.CurrentRobotId : null;
        if (ws == null || string.IsNullOrEmpty(robotId)) return;

        float v = Mathf.Lerp(-1f, +1f, slider01);
        if (Mathf.Abs(v) < turretDeadzone) v = 0f;
        ws.SendTurret(robotId, v);
    }
}
