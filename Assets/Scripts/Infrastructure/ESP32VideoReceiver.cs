// ESP32VideoReceiver.cs — receives JPEG bytes per robot and shows the active robot's stream
using UnityEngine;                              // For MonoBehaviour, Texture2D, Debug
using UnityEngine.UI;                           // For RawImage

public sealed class ESP32VideoReceiver : MonoBehaviour
{
    public static ESP32VideoReceiver Instance;  // Singleton instance any code can call

    [Header("UI target (assign a RawImage in the Inspector)")]
    [SerializeField] private RawImage target;   // Where the video appears

    private Texture2D _tex;                     // Reusable texture for decoded JPEGs
    private string _activeRobotId;              // Robot whose frames we accept/render
    private int _frameCount;                    // How many frames we have rendered
    private int _lastLogged;                    // Last count we logged (for throttling)

    private void Awake()                        // Unity lifecycle: object creation
    {
        Debug.Log("Video Receiver Awake"); // Log for awareness)
        if (Instance != null && Instance != this) { Destroy(gameObject); return; } // Enforce singleton
        Instance = this;                        // Register singleton

        _tex = new Texture2D(2, 2, TextureFormat.RGB24, false); // Small placeholder texture
        if (target != null) target.texture = _tex; // Ensure the RawImage uses our texture
    }

    public void SetActiveRobot(string robotId)  // Called when user selects a robot to view
    {
        
        _activeRobotId = robotId;               // Remember which robot to display
        _frameCount = 0;                        // Reset counters on switch
        _lastLogged = -1;                       // Force immediate first log
        Debug.Log($"[VideoRX] Active robot set to {robotId}"); // Log selection

        if (target != null && _tex != null)     // If we have a UI and texture
        {
            _tex.Reinitialize(2, 2);            // Reset texture to tiny size (clears previous image)
            _tex.Apply(false, false);           // Upload blank texture to GPU
            target.texture = _tex;              // Reassign to be safe
            // Optional: fit the RawImage to the incoming frame sizes automatically:
            // target.SetNativeSize();
        }
    }

    public void ClearActiveRobot()                          // Deselect and blank the display
    {
        _activeRobotId = null;                              // No active robot
        if (target != null && _tex != null)                 // If UI is wired
        {
            _tex.Reinitialize(2, 2);                        // Reset to tiny texture (appears blank)
            _tex.Apply(false, false);                       // Apply
            target.texture = _tex;                          // Keep bound
        }
    }

    public void SetTarget(RawImage ri)          // Optional: assign target from code instead of Inspector
    {
        target = ri;                             // Store the UI control
        if (target != null && _tex != null)      // If both exist
            target.texture = _tex;               // Ensure UI shows our texture
    }

    public void ReceiveFrame(string robotId, byte[] jpegBytes) // Host calls this on the main thread
    {
        
        if (string.IsNullOrEmpty(_activeRobotId)) return;      // Nothing selected yet
        if (robotId != _activeRobotId) return;                 // Ignore frames from other robots
        if (jpegBytes == null || jpegBytes.Length == 0) return;// Ignore empties
        if (_tex == null) return;                              // Shouldn't happen

        // Decode the JPEG into our reusable Texture2D (LoadImage auto-resizes the texture as needed).
        bool decoded = _tex.LoadImage(jpegBytes, markNonReadable: false); // Keep it readable for future decodes
        if (!decoded)                                                     // If decode failed
        {
            Debug.LogWarning($"[VideoRX] JPEG decode failed (len={jpegBytes.Length})"); // Warn once
            return;                                                       // Bail out
        }

        _tex.Apply(false, false);                 // Upload pixels to GPU (no mipmaps, keep readable)

        if (target != null && target.texture != _tex) // If RawImage lost our texture
            target.texture = _tex;                // Reassign it

        _frameCount++;                            // Bump frame counter

        // Throttle logs to every 15 frames to avoid spam, but still prove progress.
        if (_frameCount / 15 != _lastLogged / 15) // If we crossed another 15-frame boundary
        {
            _lastLogged = _frameCount;           // Remember milestone
            //Debug.Log($"[VideoRX] Frames displayed: {_frameCount} (last JPEG {jpegBytes.Length} bytes)"); // Log
        }
    }
}
