// RobotWebSocketServer.cs — super simple WebSocket host for ESP32 robots.
// Goals:
// - Start a WebSocket server at ws://<your-ip>:<Port><Path> (default: ws://PC_IP:8080/esp32)
// - Accept {"cmd":"hello","id":"..."} ONLY while in Lobby (your rule)
// - Track heartbeats {"cmd":"hb"} and remove robots on disconnect/timeout
// - Update your RobotDirectory (ServiceLocator.RobotDirectory) so the UI list changes
// - Use the same working pattern as WebSocketServerHost: WebSocketServer(addr) + AddWebSocketService + Sessions.SendTo
// - No WebSocketBehavior.Context usage. No Send(byte[]). No => syntax. Super compact.

using System;                                   // Basic C# types
using System.Collections.Generic;               // Dictionary, List
using UnityEngine;                              // MonoBehaviour, Debug, Time
using WebSocketSharp;                           // WebSocketSharp types (MessageEventArgs, CloseEventArgs)
using WebSocketSharp.Server;                    // WebSocketServer, WebSocketBehavior

public class RobotWebSocketServer : MonoBehaviour
{
    [Header("Network")]                         // Group these fields in the Inspector
    public int Port = 8080;                     // TCP port the WebSocket server listens on
    public string Path = "/esp32";              // URL path the ESP32 connects to (must match device)

    [Header("Heartbeat (seconds)")]             // Group heartbeat settings in the Inspector
    public float TimeoutSeconds = 6f;           // If we don't see a heartbeat for this long → remove robot
    public float SweepIntervalSeconds = 1.0f;   // How often we check for timeouts

    private WebSocketServer _wss;               // The WebSocket server instance
    private IRobotDirectory _dir;               // Your robot registry (set in AppBootstrap)
    private GameFlow _flow;                     // Your game flow (to enforce "Lobby only")

    [Header("Logging")]
    public bool VerboseHeartbeats = true;

    // --- Session tracking (very small, very simple) ---
    private class SessionInfo                    // Holds data about one websocket session
    {
        public string RobotId;                  // The robot id for this session (from hello)
        public float LastSeenTime;              // Time.time (seconds) when we last saw a heartbeat
    }

    private readonly Dictionary<string, SessionInfo> _bySession = new Dictionary<string, SessionInfo>(); // sessionId -> info
    private readonly Dictionary<string, string> _sessionByRobot = new Dictionary<string, string>();      // robotId -> sessionId

    // --- Main-thread queue (so we stay Unity-safe) ---
    private readonly Queue<Action> _main = new Queue<Action>(); // Actions to run on main thread
    private readonly object _mtx = new object();                // Lock for the queue

    // --- Timeout sweep timer (driven from Update for simplicity) ---
    private float _nextSweepTime = 0f;         // When to run the next timeout sweep

    private void OnEnable()                    // Called when this component becomes active
    {
        _dir = ServiceLocator.RobotDirectory;  // Grab shared robot directory
        _flow = ServiceLocator.GameFlow;       // Grab game flow

        if (_dir == null || _flow == null)     // If either is missing, we can't proceed
        {
            Debug.LogError("[WS] RobotDirectory or GameFlow is null. Check AppBootstrap.");
            return;                            // Stop here if not set up
        }

        // Build "ws://<ip>:<port>" just like your working WebSocketServerHost
        var ip = PetersUtils.GetLocalIPAddress().ToString();   // Get your local LAN IP (your helper method)
        var addr = "ws://" + ip + ":" + Port;                  // Compose the server address string
        Debug.Log("[WS] Starting server at " + addr + Path);   // Log where we will listen

        _wss = new WebSocketServer(addr);                      // Create the WebSocketServer bound to that address
        _wss.KeepClean = false;                                // Disable aggressive cleanup (helps during dev)
        _wss.AddWebSocketService<ESP32Service>(                // Register one service at the given Path
            Path,                                              // The URL path (e.g., /esp32)
            svc => { svc.Parent = this; }                      // For each connection, give the behavior a back-reference
        );
        _wss.Start();                                          // Start listening for connections
        Debug.Log("[WS] Started");                              // Log confirmation

        _nextSweepTime = Time.time + SweepIntervalSeconds;     // Schedule the first timeout sweep
    }

    private void OnDisable()                                    // Called when this component is disabled/destroyed
    {
        // Stop the server if it exists
        try
        {
            if (_wss != null)                                   // If we have a server instance
            {
                _wss.Stop();                                    // Stop it
                _wss = null;                                    // Release reference
                Debug.Log("[WS] Stopped");                      // Log
            }
        }
        catch { /* ignore errors on shutdown */ }               // Ignore any exceptions

        // Clear maps to release references
        _bySession.Clear();                                     // Forget sessions
        _sessionByRobot.Clear();                                // Forget robot->session map
    }

    private void Update()                                       // Runs every frame on main thread
    {
        // Process any queued main-thread actions (like handling messages)
        Action a = null;                                        // Temporary holder
        while (true)                                            // Loop until queue is empty
        {
            lock (_mtx)                                         // Lock the queue while we check
            {
                if (_main.Count == 0) break;                    // Nothing to do → exit loop
                a = _main.Dequeue();                            // Pop one action
            }
            try { a?.Invoke(); }                                // Run it safely
            catch (Exception ex) { Debug.LogException(ex); }    // Log exceptions
        }

        // Periodically sweep for heartbeat timeouts (simple, frame-driven)
        if (Time.time >= _nextSweepTime)                        // If it's time to sweep
        {
            SweepTimeouts();                                    // Remove any stale sessions/robots
            _nextSweepTime = Time.time + SweepIntervalSeconds;  // Schedule next sweep
        }
    }

    private void PostMain(Action a)                             // Helper to queue work onto main thread
    {
        if (a == null) return;                                  // Ignore null actions
        lock (_mtx) { _main.Enqueue(a); }                       // Enqueue under lock
    }

    // --- WebSocket service class (one instance per connection) ---
    public class ESP32Service : WebSocketBehavior               // Subclass WebSocketBehavior per websocket-sharp pattern
    {
        public RobotWebSocketServer Parent;                     // Back-reference to host so we can call into it

        protected override void OnOpen()                        // Called when a new client connects (TCP+handshake complete)
        {
            if (Parent != null) Parent.OnOpened(ID);            // Notify host: a session with ID opened
            Debug.Log("[WS] Open session=" + ID);               // Log the session id
        }

        protected override void OnClose(CloseEventArgs e)       // Called when the client disconnects
        {
            if (Parent != null) Parent.OnClosed(ID, e);         // Notify host: session closed
            Debug.Log("[WS] Close session=" + ID + " code=" + e.Code + " reason=" + e.Reason); // Log details
        }

        protected override void OnMessage(MessageEventArgs e)   // Called when a message arrives
        {
            if (e.IsText)                                       // If it's a text frame (JSON in our case)
            {
                if (Parent != null)                             // If host exists
                {
                    Parent.PostMain(() =>                       // Queue handling on main thread (Unity-safe)
                    {
                        Parent.HandleText(ID, e.Data);          // Let host parse and act on the text
                    });
                }
                return;                                         // Done with text message
            }

            // We ignore binary frames in this simple server (no video/images yet)
        }
    }

    // --- Host callbacks used by the service ---

    private void OnOpened(string sid)                           // Called when a session opens
    {
        // We do not register the robot yet; we wait for "hello" with the robot id
    }

    private void OnClosed(string sid, CloseEventArgs e)         // Called when a session closes
    {
        // If this session belonged to a robot, remove that robot from the directory
        string robotId = null;                                  // To store id (if any)
        if (_bySession.TryGetValue(sid, out var info))          // Look up the session in our map
        {
            robotId = info.RobotId;                             // Grab the associated robot id (may be null if hello never arrived)
            _bySession.Remove(sid);                             // Remove the session from our map
        }

        if (!string.IsNullOrEmpty(robotId))                     // If we actually had a robot id
        {
            _sessionByRobot.Remove(robotId);                    // Remove reverse mapping robot->session
            Debug.Log($"[WS] Disconnect: robotId={robotId} sid={sid} code={e.Code} reason={e.Reason}");
            _dir.Remove(robotId);                               // Remove from RobotDirectory so it disappears from UI
        }
        else
        {
            Debug.Log($"[WS] Disconnect (no hello yet): sid={sid} code={e.Code} reason={e.Reason}");
        }
    }

    public void HandleText(string sid, string txt)              // Parse and handle one text message from session sid
    {
        // For simplicity, we'll use tiny string checks + a helper to extract values
        if (txt.Contains("\"cmd\":\"hello\""))                  // If this is a hello message
        {
            string id = ExtractJsonString(txt, "id");           // Get the robot id from the JSON
            if (string.IsNullOrEmpty(id)) return;               // If no id, ignore

            if (_flow.Phase != GamePhase.Lobby)                 // Only accept while in Lobby (your rule)
            {
                CloseSession(sid);                              // Politely close the connection
                return;                                         // Stop here
            }

            // Record / update session info
            if (!_bySession.TryGetValue(sid, out var s))        // If we have no entry yet for this sid
            {
                s = new SessionInfo();                          // Make a new one
                _bySession[sid] = s;                            // Store it
            }
            s.RobotId = id;                                     // Save the reported robot id
            s.LastSeenTime = Time.time;                         // Mark last-seen now

            _sessionByRobot[id] = sid;                          // Save reverse mapping: robot -> session

            _dir.Upsert(id, "", "");                            // Add/update robot in directory (no callsign/IP for now)

            // Optional tiny ack (not required). If you want: SendToSession(sid, "{\"ok\":\"hello\"}");
            return;                                             // Done with hello
        }

        if (txt.Contains("\"cmd\":\"hb\""))                     // If this is a heartbeat message
        {
            if (_bySession.TryGetValue(sid, out var s))         // Find the session info
            {
                s.LastSeenTime = Time.time;                     // Update last seen time to now
                if (VerboseHeartbeats)
                {
                    var rid = string.IsNullOrEmpty(s.RobotId) ? "(unknown)" : s.RobotId;
                    Debug.Log($"[WS] Heartbeat: robotId={rid} sid={sid}");
                }
            }
            return;                                             // Done with heartbeat
        }

        // Other commands can be added here later (rename, control, etc.)
    }

    private void CloseSession(string sid)
    {
        var host = Host();                 // Get the service host for our path
        if (host == null) return;          // If the server isn't running, nothing to do
        try
        {
            host.Sessions.CloseSession(sid); // Ask websocket-sharp to close that session
        }
        catch
        {
            // Ignore errors in this simple prototype
        }
    }

    // --- Timeout sweep removes dead sessions/robots ---
    private void SweepTimeouts()                                // Called every SweepIntervalSeconds from Update
    {
        if (_bySession.Count == 0) return;                      // Fast exit if no sessions

        float now = Time.time;                                  // Current time in seconds
        List<string> toClose = new List<string>();              // Sessions to close due to timeout

        foreach (var kv in _bySession)                          // Go through all live sessions
        {
            string sid = kv.Key;                                // Current session id
            SessionInfo s = kv.Value;                           // Its info
            float age = now - s.LastSeenTime;                   // Seconds since last heartbeat
            if (age > TimeoutSeconds)                           // If too old
            {
                toClose.Add(sid);                               // Mark for closing
            }
        }

        if (toClose.Count == 0) return;                         // Nothing to do

        var host = Host();
        for (int i = 0; i < toClose.Count; i++)
        {
            string sid = toClose[i];
            string rid = null;
            float lastSeen = 0f;

            if (_bySession.TryGetValue(sid, out var s))
            {
                rid = s.RobotId;
                lastSeen = s.LastSeenTime;
                _bySession.Remove(sid);
            }

            if (!string.IsNullOrEmpty(rid))
            {
                _sessionByRobot.Remove(rid);
                float age = Time.time - lastSeen;
                Debug.LogWarning($"[WS] Missed heartbeat: robotId={rid} sid={sid} age={age:0.00}s > {TimeoutSeconds:0.00}s → removing");
                _dir.Remove(rid);
            }
            else
            {
                Debug.LogWarning($"[WS] Missed heartbeat: sid={sid} (no robotId) → closing");
            }

            if (host != null)
            {
                try { host.Sessions.CloseSession(sid); } catch { }
            }
        }
    }

    // --- Helpers (keep them tiny and beginner-friendly) ---
    private WebSocketServiceHost Host()                         // Get the service host object for our Path
    {
        if (_wss == null) return null;                          // If server is missing, return null
        return _wss.WebSocketServices[Path];                    // Return the host for this Path (e.g., "/esp32")
    }

    private void SendToSession(string sid, string json)         // Send a JSON string to one session
    {
        var host = Host();                                      // Get the host
        if (host == null) return;                               // If missing, do nothing
        try { host.Sessions.SendTo(json, sid); }                // Use websocket-sharp API to send text
        catch { /* ignore send failures in prototype */ }       // Ignore errors for simplicity
    }

    private static string ExtractJsonString(string s, string key) // Very small string extractor for {"key":"value"}
    {
        try                                                     // Try to parse without throwing
        {
            string needle = "\"" + key + "\"";                  // Build the substring to find, e.g., "id"
            int k = s.IndexOf(needle, StringComparison.Ordinal); // Find the key
            if (k < 0) return null;                             // If not found, return null
            int colon = s.IndexOf(':', k);                      // Find the colon after the key
            if (colon < 0) return null;                         // If no colon, bad format
            int q1 = s.IndexOf('"', colon + 1);                 // Find first quote after colon
            int q2 = s.IndexOf('"', q1 + 1);                    // Find closing quote
            if (q1 < 0 || q2 < 0) return null;                  // If quotes missing, bad format
            return s.Substring(q1 + 1, q2 - (q1 + 1));          // Return text between the quotes
        }
        catch                                                   // If any parsing error happens
        {
            return null;                                        // Return null (we stay robust)
        }
    }
}
