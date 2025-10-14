// TankArcadeDrive.cs
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class TankArcadeDrive : MonoBehaviour
{
    [Header("UI Joystick (fallback)")]
    [SerializeField] JoystickCircle joystick;

#if ENABLE_INPUT_SYSTEM
    [Header("Input System (preferred)")]
    [SerializeField] InputActionProperty moveAction; // Vector2 (left stick)
#endif

    [SerializeField] float maxTrackSpeed = 1f;
    [SerializeField] float rampPerSecond = 4f;
    [SerializeField] bool debugLog = false;
    [SerializeField] float logEpsilon = 0.02f;

    float _left, _right;
    float _lastLogL = 999, _lastLogR = 999, _lastLogX = 999, _lastLogY = 999;

    public float Left => _left;
    public float Right => _right;

#if ENABLE_INPUT_SYSTEM
    void OnEnable() { if (moveAction.action != null) moveAction.action.Enable(); }
    void OnDisable() { if (moveAction.action != null) moveAction.action.Disable(); }
#endif

    void Update()
    {
        Vector2 v = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        if (moveAction.action != null) v = moveAction.action.ReadValue<Vector2>();
#endif
        if (v == Vector2.zero && joystick) v = joystick.Value; // fallback

        float throttle = v.y;   // forward/back
        float turn = v.x;   // left/right

        float targetLeft = Mathf.Clamp(throttle + turn, -1f, 1f) * maxTrackSpeed;
        float targetRight = Mathf.Clamp(throttle - turn, -1f, 1f) * maxTrackSpeed;

        _left = Mathf.MoveTowards(_left, targetLeft, rampPerSecond * Time.deltaTime);
        _right = Mathf.MoveTowards(_right, targetRight, rampPerSecond * Time.deltaTime);

        if (debugLog
            && (Mathf.Abs(_left - _lastLogL) > logEpsilon
             || Mathf.Abs(_right - _lastLogR) > logEpsilon
             || Mathf.Abs(v.x - _lastLogX) > logEpsilon
             || Mathf.Abs(v.y - _lastLogY) > logEpsilon))
        {
            Debug.Log($"[Drive] Joy/Pad=({v.x:F2},{v.y:F2}) → L={_left:F2} R={_right:F2}");
            _lastLogL = _left; _lastLogR = _right; _lastLogX = v.x; _lastLogY = v.y;
        }
    }
}
