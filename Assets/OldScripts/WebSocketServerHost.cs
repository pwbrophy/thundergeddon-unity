// WebSocketServerHost.cs — optimized orchestration + robust hit parsing
using System;
using System.Collections;
using System.Collections.Generic;
using WebSocketSharp;
using WebSocketSharp.Server;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UDebug = UnityEngine.Debug;

//blarp

public class WebSocketServerHost : MonoBehaviour
{
    [Header("Drive Input (optional)")]
    public TankArcadeDrive Drive;
    public float sendHz = 20f;
    public float changeEpsilon = 0.01f;

#if ENABLE_INPUT_SYSTEM
    [SerializeField] InputActionProperty turretAction;
#endif
    public JoystickCircle TurretJoystick;
    public bool turretUseYAxis = false;
    [Range(0f, 1f)] public float turretDeadzone = 0.08f;
    [Range(0.5f, 3f)] public float turretResponsePower = 1.4f;
    public float turretRampPerSecond = 4f;

    [Header("Network")]
    public int Port = 8080;
    public string TestBoardRole = "test_board";
    public string RobotTankRole = "robot_tank";
    public string ActiveDriveRole = "robot_tank";

    [Header("Timeouts (s)")]
    public float ackTimeout = 1.0f;  // wait "ready"
    public float emitTimeout = 0.5f;  // wait "emit_done"
    public float resultTimeout = 1.8f;  // wait "scan_results" at end

    private WebSocketServer Wss;
    private Coroutine sendLoop;

    // connection registries
    private static readonly object _lock = new object();
    private static readonly Dictionary<string, ClientInfo> ClientsBySession = new();
    private static readonly Dictionary<string, HashSet<string>> SessionsByRole = new();
    private class ClientInfo { public string sessionId, deviceId, role; public DateTime lastSeen; }

    // main-thread queue
    private readonly object _mtx = new object();
    private readonly Queue<Action> _main = new();

    // payloads
    [Serializable] private struct DriveMsg { public string cmd; public float left; public float right; }
    [Serializable] private struct TurretMsg { public string cmd; public float speed; }
    [Serializable] private struct HelloMsg { public string cmd; public string id; public string role; }

    [Serializable] private struct PrepareMsg { public string cmd; }                         // "listen_prepare"
    [Serializable] private struct ReadyMsg { public string cmd; }                         // "ready" (rx)
    [Serializable] private struct CarrierMsg { public string cmd; }                         // "carrier_on" / "carrier_off"
    [Serializable] private struct EmitDirMsg { public string cmd; public string dir; }      // "emit"
    [Serializable] private struct MarkMsg { public string cmd; public string dir; }      // "mark"
    [Serializable] private struct FinishMsg { public string cmd; }                         // "listen_finish"
    [Serializable] private struct MotorsOnMsg { public string cmd; }                         // "motors_on"

    private float lastLeft = 999f, lastRight = 999f, lastTurret = 999f, _turret;

    // flags/results
    private bool _scanInProgress, _readyFlag, _emitDoneFlag, _resultsFlag;
    private List<string> _resultsHits = new();

    private static readonly string[] kDirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    private static readonly float[] kDirDeg = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };

    void Start()
    {
        var ip = PetersUtils.GetLocalIPAddress().ToString();
        var addr = $"ws://{ip}:{Port}";
        UDebug.Log($"[WSS] Starting on {addr}");
        Wss = new WebSocketServer(addr);
        Wss.AddWebSocketService<ESP32Service>("/esp32", svc => svc.Parent = this);
        Wss.KeepClean = false;
        Wss.Start();
        UDebug.Log("[WSS] Started");
        sendLoop = StartCoroutine(SendLoop());
    }

    void Update()
    {
        Action a;
        while (true)
        {
            lock (_mtx) { if (_main.Count == 0) break; a = _main.Dequeue(); }
            try { a?.Invoke(); } catch (Exception ex) { UDebug.LogException(ex); }
        }
    }

    void OnDestroy()
    {
        if (sendLoop != null) StopCoroutine(sendLoop);
        if (Wss != null) { Wss.Stop(); UDebug.Log("[WSS] Stopped"); }
        lock (_lock) { ClientsBySession.Clear(); SessionsByRole.Clear(); }
    }

    private void PostMain(Action a) { if (a == null) return; lock (_mtx) _main.Enqueue(a); }

    // ================= WS service =================
    public class ESP32Service : WebSocketBehavior
    {
        public WebSocketServerHost Parent;
        protected override void OnOpen() { Parent?.RegisterOrUpdate(ID, null); UDebug.Log($"[WS] Open session={ID}"); }
        protected override void OnClose(CloseEventArgs e) { Parent?.Unregister(ID); UDebug.Log($"[WS] Close session={ID} code={e.Code} reason={e.Reason}"); }
        protected override void OnMessage(MessageEventArgs e)
        {
            if (e.IsText)
            {
                Parent?.PostMain(() => Parent?.HandleInboundText(ID, e.Data));
                return;
            }
            if (e.IsBinary)
            {
                var bytes = e.RawData;
                Parent?.PostMain(() => { var rx = ESP32VideoReceiver.Instance; if (rx != null) rx.ReceiveFrame(bytes); });
            }
        }
    }

    private void HandleInboundText(string sid, string txt)
    {
        if (txt.Contains("\"cmd\":\"hello\""))
        {
            try { var h = JsonUtility.FromJson<HelloMsg>(txt); if (h.cmd == "hello") RegisterHello(sid, h.id, h.role); }
            catch { }
            UDebug.Log($"[WS] hello from session={sid} -> role={GetRoleForSession(sid)}");
            return;
        }

        var role = GetRoleForSession(sid);
        if (txt.Contains("\"cmd\":\"ready\"") && role == TestBoardRole) { _readyFlag = true; return; }
        if ((txt.Contains("\"cmd\":\"emit_done\"") || txt.Contains("\"cmd\":\"hit_detect_done\"")) && role == RobotTankRole) { _emitDoneFlag = true; return; }

        if (txt.Contains("\"cmd\":\"scan_results\"") && role == TestBoardRole)
        {
            _resultsHits = ParseHitsList(txt);
            _resultsFlag = true;
            return;
        }
    }

    // Parse: {"cmd":"scan_results","hits":["N","E",...]}
    private List<string> ParseHitsList(string json)
    {
        var hits = new List<string>();
        int hk = json.IndexOf("\"hits\"");
        if (hk < 0) return hits;
        int lb = json.IndexOf('[', hk);
        int rb = json.IndexOf(']', lb + 1);
        if (lb < 0 || rb < 0) return hits;
        string inner = json.Substring(lb + 1, rb - lb - 1);
        int i = 0;
        while (i < inner.Length)
        {
            int q1 = inner.IndexOf('"', i); if (q1 < 0) break;
            int q2 = inner.IndexOf('"', q1 + 1); if (q2 < 0) break;
            string dir = inner.Substring(q1 + 1, q2 - q1 - 1);
            if (!string.IsNullOrEmpty(dir)) hits.Add(dir);
            i = q2 + 1;
        }
        return hits;
    }

    // =============== registry ===============
    private void RegisterOrUpdate(string sid, string role)
    {
        lock (_lock)
        {
            if (!ClientsBySession.TryGetValue(sid, out var info))
            {
                info = new ClientInfo { sessionId = sid };
                ClientsBySession[sid] = info;
            }
            info.role = role ?? info.role;
            info.lastSeen = DateTime.UtcNow;
        }
    }

    private void RegisterHello(string sid, string deviceId, string role)
    {
        lock (_lock)
        {
            if (!ClientsBySession.TryGetValue(sid, out var info))
                ClientsBySession[sid] = info = new ClientInfo { sessionId = sid };
            info.deviceId = deviceId; info.role = role; info.lastSeen = DateTime.UtcNow;

            if (!string.IsNullOrEmpty(role))
            {
                if (!SessionsByRole.TryGetValue(role, out var set)) SessionsByRole[role] = set = new HashSet<string>();
                set.Add(sid);
            }
        }
    }

    private void Unregister(string sid)
    {
        lock (_lock)
        {
            if (ClientsBySession.TryGetValue(sid, out var info))
            {
                if (!string.IsNullOrEmpty(info.role) && SessionsByRole.TryGetValue(info.role, out var set))
                { set.Remove(sid); if (set.Count == 0) SessionsByRole.Remove(info.role); }
                ClientsBySession.Remove(sid);
            }
        }
    }

    private string GetRoleForSession(string sid)
    {
        lock (_lock) { return ClientsBySession.TryGetValue(sid, out var i) ? i.role : null; }
    }

    private WebSocketServiceHost Host() => Wss != null ? Wss.WebSocketServices["/esp32"] : null;

    private int SendJsonToRole<T>(string role, T payload)
    {
        var host = Host(); if (host == null || string.IsNullOrEmpty(role)) return 0;
        string json = JsonUtility.ToJson(payload);
        List<string> targets = null;
        lock (_lock)
        {
            if (!SessionsByRole.TryGetValue(role, out var set) || set.Count == 0) return 0;
            targets = new List<string>(set);
        }
        for (int i = 0; i < targets.Count; i++) host.Sessions.SendTo(json, targets[i]);
        return targets.Count;
    }

    // =============== drive/turret loop (optional) ===============
    IEnumerator SendLoop()
    {
        var wait = new WaitForSeconds(1f / Mathf.Max(1f, sendHz));
        float lastTurret = 999f;
        while (true)
        {
            float left = (Drive != null) ? Drive.Left : 0f;
            float right = (Drive != null) ? Drive.Right : 0f;

            left = Mathf.Clamp(left, -1f, 1f);
            right = Mathf.Clamp(right, -1f, 1f);

            Vector2 tv = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (turretAction.action != null) tv = turretAction.action.ReadValue<Vector2>();
#endif
            if (tv == Vector2.zero && TurretJoystick != null) tv = TurretJoystick.Value;

            float raw = turretUseYAxis ? tv.y : tv.x;
            if (Mathf.Abs(raw) < turretDeadzone) raw = 0f;
            raw = Mathf.Sign(raw) * Mathf.Pow(Mathf.Abs(raw), turretResponsePower);

            float target = Mathf.Clamp(raw, -1f, 1f);
            _turret = Mathf.MoveTowards(_turret, target, turretRampPerSecond * Time.deltaTime);

            bool driveCh = Mathf.Abs(left - lastLeft) > changeEpsilon || Mathf.Abs(right - lastRight) > changeEpsilon;
            bool turretCh = Mathf.Abs(_turret - lastTurret) > changeEpsilon;

            if (driveCh) { SendJsonToRole(ActiveDriveRole, new DriveMsg { cmd = "drive", left = left, right = right }); lastLeft = left; lastRight = right; }
            if (turretCh) { SendJsonToRole(ActiveDriveRole, new TurretMsg { cmd = "turret", speed = _turret }); lastTurret = _turret; }

            yield return wait;
        }
    }

    // =================== Orchestration button ===================
    public void OnScanEightDirectionsButtonClicked()
    {
        if (_scanInProgress) return;
        StartCoroutine(ScanOptimized());
    }

    private IEnumerator ScanOptimized()
    {
        _scanInProgress = true;
        _resultsFlag = _readyFlag = _emitDoneFlag = false;
        _resultsHits.Clear();

        // 1) test board: turn off motors + arm IR receiver  -> wait "ready"
        SendJsonToRole(TestBoardRole, new PrepareMsg { cmd = "listen_prepare" });
        float t0 = Time.time;
        while (!_readyFlag && (Time.time - t0) < ackTimeout) yield return null;

        // 3) tank: carrier ON
        SendJsonToRole(RobotTankRole, new CarrierMsg { cmd = "carrier_on" });
        yield return null; // small spacing

        // 4..7) iterate each LED direction
        for (int i = 0; i < kDirs.Length; i++)
        {
            string dir = kDirs[i];
            _emitDoneFlag = false;

            // 4) tank: emit dir (1 ms)
            SendJsonToRole(RobotTankRole, new EmitDirMsg { cmd = "emit", dir = dir });

            // 5) wait for emit_done (or timeout)
            t0 = Time.time;
            while (!_emitDoneFlag && (Time.time - t0) < emitTimeout) yield return null;

            // give receiver ISR + loop a breath before mark
            yield return null;

            // 6) test board: mark dir now
            SendJsonToRole(TestBoardRole, new MarkMsg { cmd = "mark", dir = dir });
        }

        // 8) test board: finish -> sends "scan_results"
        _resultsFlag = false; _resultsHits.Clear();
        SendJsonToRole(TestBoardRole, new FinishMsg { cmd = "listen_finish" });
        t0 = Time.time;
        while (!_resultsFlag && (Time.time - t0) < resultTimeout) yield return null;

        // 9) test board: motors back on
        SendJsonToRole(TestBoardRole, new MotorsOnMsg { cmd = "motors_on" });

        // 10) tank: carrier OFF
        SendJsonToRole(RobotTankRole, new CarrierMsg { cmd = "carrier_off" });

        // 11) Report
        if (_resultsHits.Count == 0) UDebug.Log("Hits: (none)");
        else UDebug.Log("Hits: " + string.Join(", ", _resultsHits));
        string estimated = EstimateIncomingDirection(_resultsHits);
        UDebug.Log("Estimated incoming direction: " + estimated);

        _scanInProgress = false;
    }

    // =================== Direction estimation ===================
    private string EstimateIncomingDirection(List<string> hitsList)
    {
        if (hitsList == null || hitsList.Count == 0) return "Unknown";

        // Map hits to ring
        bool[] ring = new bool[8];
        foreach (var d in hitsList)
            for (int i = 0; i < 8; i++) if (kDirs[i] == d) ring[i] = true;

        // Clusters
        var clusters = new List<(int start, int len)>();
        for (int i = 0; i < 8; i++)
        {
            int prev = (i + 7) & 7;
            if (ring[i] && !ring[prev])
            {
                int len = 0;
                while (len < 8 && ring[(i + len) & 7]) len++;
                clusters.Add((i, len));
                i += len - 1;
            }
        }

        bool hasAdjacent = false; foreach (var c in clusters) if (c.len >= 2) { hasAdjacent = true; break; }
        List<int> use = new();

        if (hasAdjacent)
        {
            (int start, int len) best = clusters[0];
            for (int k = 1; k < clusters.Count; k++) if (clusters[k].len > best.len) best = clusters[k];
            for (int k = 0; k < best.len; k++) use.Add((best.start + k) & 7);
        }
        else
        {
            for (int i = 0; i < 8; i++) if (ring[i]) use.Add(i);
        }

        float[] kDirDeg = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };
        float sx = 0f, sy = 0f;
        foreach (int idx in use)
        {
            float rad = kDirDeg[idx] * Mathf.Deg2Rad;
            sx += Mathf.Cos(rad);
            sy += Mathf.Sin(rad);
        }
        if (Mathf.Approximately(sx, 0f) && Mathf.Approximately(sy, 0f)) return "Unknown";
        float ang = Mathf.Atan2(sy, sx) * Mathf.Rad2Deg; if (ang < 0) ang += 360f;
        int snapped = Mathf.RoundToInt(ang / 45f) % 8;
        return kDirs[snapped];
    }
}
