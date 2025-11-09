// RobotWebSocketServer.cs — WebSocket host for ESP32 robots (text control + binary JPEG frames)
using System;                                   // Basic C# types
using System.Collections.Generic;               // Dictionary, List, Queue
using UnityEngine;                              // MonoBehaviour, Debug, Time
using WebSocketSharp;                           // MessageEventArgs, CloseEventArgs
using WebSocketSharp.Server;                    // WebSocketServer, WebSocketBehavior
using System.Globalization;

public class RobotWebSocketServer : MonoBehaviour
{
    [Header("WebSocket Host Settings")]
    public int Port = 8080;                     // ws://<ip>:8080
    public string Path = "/esp32";              // ws://<ip>:<port>/esp32

    [Header("Timeouts")]
    public float TimeoutSeconds = 8f;           // Heartbeat timeout
    public float SweepIntervalSeconds = 2f;     // How often we scan for timeouts

    [Header("Debug")]
    public bool VerboseJoins = true;            // Log hellos
    public bool VerboseLeaves = true;           // Log disconnects
    public bool VerboseHeartbeats = false;      // (optional) log heartbeats

    // --- State / services ---
    private bool serverStarted = false;         // Have we started the WSS?
    private WebSocketServer _wss;               // The WebSocket server
    private IRobotDirectory _dir;               // Robot registry (via ServiceLocator)
    private GameFlow _flow;                     // Game phase (Lobby/Playing)

    // Per-session data
    private class SessionInfo
    {
        public string RobotId;                  // Bound after {"cmd":"hello","id":"..."}
        public float LastSeenTime;              // Time.time (s) of last heartbeat
        public int NumFrames;                   // Diagnostics: number of JPEG frames received
    }

    // Maps sessionId -> info, and robotId -> sessionId
    private readonly Dictionary<string, SessionInfo> _bySession = new Dictionary<string, SessionInfo>();
    private readonly Dictionary<string, string> _sessionByRobot = new Dictionary<string, string>();

    // Main-thread queue for Unity safety
    private readonly Queue<Action> _main = new Queue<Action>();
    private readonly object _mtx = new object();

    // Timeout sweep timer
    private float _nextSweepTime = 0f;

    // Expose via ServiceLocator
    private static RobotWebSocketServer _self;

    private void Awake()
    {
        _self = this;
        ServiceLocator.RobotServer = this;
    }

    private void OnDestroy()
    {
        if (_self == this) _self = null;
        if (ServiceLocator.RobotServer == this) ServiceLocator.RobotServer = null;
        StopServer();
    }

    // ===== Public control =====

    public void StartWebSocketServer()
    {
        if (serverStarted) return;

        _dir = ServiceLocator.RobotDirectory;  // This is IRobotDirectory in your codebase
        _flow = ServiceLocator.GameFlow;

        if (_dir == null || _flow == null)
        {
            Debug.LogError("[WS] RobotDirectory or GameFlow is null.");
            return;
        }

        string ip = PetersUtils.GetLocalIPAddress().ToString();
        string addr = "ws://" + ip + ":" + Port;

        Debug.Log("[WS] Starting server at " + addr + Path);

        _wss = new WebSocketServer(addr);
        _wss.KeepClean = false;

        // Add one service at the configured path; each client gets its own ESP32Service instance
        _wss.AddWebSocketService<ESP32Service>(Path, svc =>
        {
            svc.Parent = this;                  // allow the service to call back into this host
        });

        _wss.Start();
        Debug.Log("[WS] Started");

        _nextSweepTime = Time.time + SweepIntervalSeconds;
        serverStarted = true;

        ServiceLocator.RobotServer = this;      // keep SL current
    }

    public void StopServer()
    {
        if (!serverStarted) return;
        try { _wss.Stop(); } catch { /* ignore */ }
        _wss = null;
        serverStarted = false;
    }

    // ===== Unity Update loop =====

    private void Update()
    {
        PumpMain();

        if (serverStarted && Time.time >= _nextSweepTime)
        {
            _nextSweepTime = Time.time + SweepIntervalSeconds;
            SweepForTimeouts();
        }
    }

    // Post work to run on Unity main thread
    public void PostMain(Action a)
    {
        if (a == null) return;
        lock (_mtx) _main.Enqueue(a);
    }

    private void PumpMain()
    {
        for (; ; )
        {
            Action a = null;
            lock (_mtx)
            {
                if (_main.Count == 0) break;
                a = _main.Dequeue();
            }
            try { a?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }

    // ===== Behavior for each WebSocket connection =====

    private class ESP32Service : WebSocketBehavior
    {
        public RobotWebSocketServer Parent;

        protected override void OnOpen()
        {
            if (Parent != null) Parent.PostMain(() => Parent.OnOpened(ID));
        }

        protected override void OnClose(CloseEventArgs e)
        {
            if (Parent != null) Parent.PostMain(() => Parent.OnClosed(ID, e));
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            if (Parent == null) return;

            if (e.IsText)
            {
                //Debug.Log("Text message received: " + e.Data);
                string data = e.Data; // capture text on IO thread
                Parent.PostMain(() => Parent.HandleText(ID, data));
                return;
            }

            if (e.IsBinary)
            {
                // Safe to pass RawData; we immediately hop to main thread and use it there.
                //Debug.Log("Binary message received, len=" + (e.RawData?.Length ?? 0));
                var bytes = e.RawData;
                Parent.PostMain(() => Parent.HandleBinary(ID, bytes));
                return;
            }
        }
    }

    // ===== Host callbacks for service =====

    private void OnOpened(string sid)
    {
        if (!_bySession.ContainsKey(sid))
        {
            _bySession[sid] = new SessionInfo
            {
                RobotId = null,
                LastSeenTime = Time.time,
                NumFrames = 0
            };
        }
    }

    private void OnClosed(string sid, CloseEventArgs e)
    {
        if (_bySession.TryGetValue(sid, out var info))
        {
            var rid = info.RobotId;

            _bySession.Remove(sid);

            if (!string.IsNullOrEmpty(rid))
            {
                if (_sessionByRobot.TryGetValue(rid, out var mapSid) && mapSid == sid)
                    _sessionByRobot.Remove(rid);

                _dir?.Remove(rid); // your directory uses Remove(robotId)
                if (VerboseLeaves) Debug.Log("[WS] Robot left: " + rid);
            }
        }
    }

    // Handle text JSON (hello + heartbeat)
    public void HandleText(string sid, string json)
    {
        if (string.IsNullOrEmpty(json)) return;

        string cmd = ExtractString(json, "cmd");
        if (string.IsNullOrEmpty(cmd)) return;

        if (cmd == "hello")
        {
            // You only accept new hellos in Lobby
            if (_flow != null && _flow.Phase != GamePhase.Lobby)
            {
                // Politely close: must close a *service* session, not the server.
                try { ServiceSessions()?.CloseSession(sid); } catch { /* ignore */ }
                return;
            }

            string id = ExtractString(json, "id");
            if (string.IsNullOrEmpty(id)) return;

            if (!_bySession.TryGetValue(sid, out var info))
            {
                info = new SessionInfo();
                _bySession[sid] = info;
            }
            info.RobotId = id;
            info.LastSeenTime = Time.time;
            info.NumFrames = 0;

            _sessionByRobot[id] = sid;

            // Upsert into the directory (no Get/Add/Touch in your interface)
            // We don't know the IP here, so pass empty for now.
            _dir?.Upsert(id, id, ip: "");

            if (VerboseJoins) Debug.Log("[WS] Robot hello: " + id);
            return;
        }

        if (cmd == "hb")
        {
            if (_bySession.TryGetValue(sid, out var info))
            {
                info.LastSeenTime = Time.time;
                if (VerboseHeartbeats && info.NumFrames % 30 == 0)
                    Debug.Log("[WS] hb from " + (info.RobotId ?? sid));
            }
            return;
        }

        // (extend for other text commands if you add them later)
    }

    // Handle one binary JPEG frame (on main thread)
    public void HandleBinary(string sid, byte[] data)
    {
        //Debug.Log("Binary frame received, len=" + (data?.Length ?? 0));
        if (data == null || data.Length == 0) return;

        if (!_bySession.TryGetValue(sid, out var info)) return;
        string robotId = info.RobotId;
        if (string.IsNullOrEmpty(robotId)) return;

        info.NumFrames++;
        //if ((info.NumFrames % 10) == 0)
            //Debug.Log($"[WS] {robotId} frames: {info.NumFrames}, last {data.Length} bytes");

        var rx = ESP32VideoReceiver.Instance;
        //Debug.Log("Passing frame to VideoReceiver for robot " + robotId);
        if (rx != null) rx.ReceiveFrame(robotId, data);
    }

    // Periodic heartbeat timeout sweep
    private void SweepForTimeouts()
    {
        if (_bySession.Count == 0) return;

        var now = Time.time;
        var toDrop = new List<string>();

        foreach (var kv in _bySession)
        {
            if (now - kv.Value.LastSeenTime > TimeoutSeconds)
                toDrop.Add(kv.Key);
        }

        foreach (var sid in toDrop)
        {
            if (_bySession.TryGetValue(sid, out var info))
            {
                var rid = info.RobotId;

                try { ServiceSessions()?.CloseSession(sid); } catch { /* ignore */ }
                _bySession.Remove(sid);

                if (!string.IsNullOrEmpty(rid))
                {
                    if (_sessionByRobot.TryGetValue(rid, out var mapSid) && mapSid == sid)
                        _sessionByRobot.Remove(rid);

                    _dir?.Remove(rid);
                    if (VerboseLeaves) Debug.Log("[WS] Robot timeout: " + rid);
                }
            }
        }
    }

    // ===== Public send helpers for UI code =====

    // Send arbitrary JSON text to a robot by id
    public bool SendJsonToRobot(string robotId, string json)
    {
        if (string.IsNullOrEmpty(robotId) || string.IsNullOrEmpty(json)) return false;
        if (_wss == null) return false;

        if (!_sessionByRobot.TryGetValue(robotId, out var sid)) return false;

        var sessions = ServiceSessions();
        if (sessions == null) return false;

        try { sessions.SendTo(json, sid); }
        catch { return false; }

        return true;
    }

    // Convenience wrappers
    public bool SendFlashCommand(string robotId, int pin, int ms)
    {
        string json = "{\"cmd\":\"flash\",\"pin\":" + pin + ",\"ms\":" + ms + "}";
        return SendJsonToRobot(robotId, json);
    }

    public bool SendStreamOff(string robotId)
    {
        return SendJsonToRobot(robotId, "{\"cmd\":\"stream_off\"}");
    }

    public bool SendStreamOn(string robotId)                    // Tell a robot to start its camera
    {
        return SendJsonToRobot(robotId, "{\"cmd\":\"stream_on\"}"); // Use same generic sender
    }

    public bool SendMotorsOn(string robotId)
    {
        bool ok = SendJsonToRobot(robotId, "{\"cmd\":\"motors_on\"}");
        Debug.Log(ok ? $"[WS->Robot] motors_on → {robotId}" : $"[WS->Robot] FAILED motors_on → {robotId}");
        return ok;
    }
    public bool SendMotorsOff(string robotId)
    {
        bool ok = SendJsonToRobot(robotId, "{\"cmd\":\"motors_off\"}");
        Debug.Log(ok ? $"[WS->Robot] motors_off → {robotId}" : $"[WS->Robot] FAILED motors_off → {robotId}");
        return ok;
    }

    public bool SendDrive(string robotId, float left, float right)
    {
        // Ensure decimals use '.' regardless of OS locale
        string l = left.ToString("F3", CultureInfo.InvariantCulture);
        string r = right.ToString("F3", CultureInfo.InvariantCulture);
        string json = $"{{\"cmd\":\"drive\",\"l\":{l},\"r\":{r}}}";
        Debug.Log($"[WS->Robot] drive l={l} r={r} → {robotId}");
        return SendJsonToRobot(robotId, json);
    }

    public bool SendTurret(string robotId, float speed)
    {
        string s = speed.ToString("F3", CultureInfo.InvariantCulture);
        string json = $"{{\"cmd\":\"turret\",\"speed\":{s}}}";
        Debug.Log($"[WS->Robot] turret {s} → {robotId}");
        return SendJsonToRobot(robotId, json);
    }

    // ===== Internals =====

    private WebSocketServer Host() => _wss;

    // Sessions live on the specific service (path), not on the server root
    private WebSocketSharp.Server.WebSocketSessionManager ServiceSessions()
    {
        if (_wss == null) return null;
        var svcHost = _wss.WebSocketServices[Path];
        return svcHost?.Sessions;
    }

    // Super-tiny string extractor for flat JSON: {"key":"value",...}
    private static string ExtractString(string s, string key)
    {
        if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(key)) return null;
        try
        {
            int k = s.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) return null;
            int colon = s.IndexOf(':', k);
            if (colon < 0) return null;
            int q1 = s.IndexOf('"', colon + 1);
            if (q1 < 0) return null;
            int q2 = s.IndexOf('"', q1 + 1);
            if (q2 < 0) return null;
            return s.Substring(q1 + 1, q2 - (q1 + 1));
        }
        catch
        {
            return null;
        }
    }
}
