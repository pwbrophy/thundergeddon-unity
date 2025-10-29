using System;                               // For Action, Exception
using System.Collections.Generic;           // For Queue<T>
using System.Net;                           // For IPAddress, IPEndPoint, Dns
using System.Net.Sockets;                   // For UdpClient, SocketException
using System.Text;                          // For Encoding.UTF8
using System.Threading;                     // For Thread
using UnityEngine;                          // For MonoBehaviour, Debug

// Very small UDP discovery listener.
// - While in Lobby, wait for robot announces on a UDP port.
// - When we see {"robotId":"...","callsign":"..."}, we:
//     1) Update the RobotDirectory (on main thread).
//     2) Reply to THAT sender with {"ws":"ws://<our-ip>:<wsPort><wsPath>"}.
//
// Notes:
// - A background thread blocks on UDP.Receive (fast + simple).
// - That thread NEVER touches UnityEngine APIs.
// - We push any Unity work onto a tiny main-thread queue.
public class UdpDiscoveryListener : MonoBehaviour
{
    [Header("Ports and paths")]
    [SerializeField] private int discoveryPort = 30560;       // Where robots broadcast their "announce"
    [SerializeField] private int websocketPort = 8080;        // Your WebSocket server port
    [SerializeField] private string websocketPath = "/esp32"; // Your WebSocket service path (must match server)

    private IRobotDirectory _dir;    // Shared robot registry (set in AppBootstrap)
    private GameFlow _flow;          // Game state (we only run in Lobby)

    private UdpClient _udp;          // UDP socket
    private Thread _rxThread;        // Background thread that blocks in Receive
    private volatile bool _running;  // Flag to keep the thread loop alive

    // -------- Main-thread queue (Unity-safe) --------
    private readonly object _mtx = new object();              // Lock for the queue
    private readonly Queue<Action> _main = new Queue<Action>(); // Actions to run on Unity's main thread

    private void OnEnable()                                   // Called when this component is enabled
    {
        _dir = ServiceLocator.RobotDirectory;               // Get robot directory from your service locator
        _flow = ServiceLocator.GameFlow;                     // Get game flow to know current phase

        if (_dir == null || _flow == null)                   // If something is missing…
        {
            Debug.LogError("[UDP] RobotDirectory or GameFlow is null. Check AppBootstrap.");
            return;                                          // …we cannot proceed
        }

        _flow.OnPhaseChanged += HandlePhaseChanged;           // Listen for phase changes (Lobby <-> not Lobby)

        if (_flow.Phase == GamePhase.Lobby)                   // If we are already in Lobby…
        {
            StartListener();                                  // …start listening now
        }
    }

    private void OnDisable()                                  // Called when this component is disabled
    {
        if (_flow != null)                                    // If we previously subscribed…
        {
            _flow.OnPhaseChanged -= HandlePhaseChanged;       // …unsubscribe to avoid leaks
        }

        StopListener();                                       // Make sure the socket/thread are stopped
    }

    private void Update()                                     // Runs every frame on the main thread
    {
        // Drain the queued actions that need Unity main thread (UI, directory events, Debug.Log)
        Action a = null;                                      // Temporary variable
        while (true)                                          // Keep going until the queue is empty
        {
            lock (_mtx)                                       // Lock the queue while we inspect it
            {
                if (_main.Count == 0) break;                  // Nothing left to run
                a = _main.Dequeue();                          // Take one action out
            }
            try { if (a != null) a(); }                       // Execute the action
            catch (Exception ex) { Debug.LogException(ex); }  // If it throws, log the error
        }
    }

    private void HandlePhaseChanged(GamePhase phase)          // Called when GameFlow changes phase
    {
        if (phase == GamePhase.Lobby)                         // If we entered Lobby…
        {
            StartListener();                                  // …start UDP discovery
        }
        else                                                  // If we left Lobby…
        {
            StopListener();                                   // …stop UDP discovery
        }
    }

    private void StartListener()                              // Open UDP port and start background thread
    {
        if (_running) return;                                 // If already running, do nothing

        try
        {
            // Bind UDP socket on all interfaces for the given port.
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, discoveryPort)); // Listen on 0.0.0.0:discoveryPort
            _udp.EnableBroadcast = true;                      // Allow receiving broadcast packets

            _running = true;                                  // Set the loop flag to true

            _rxThread = new Thread(ReceiveLoop);              // Create the background thread
            _rxThread.IsBackground = true;                    // Mark as background so it won't block app exit
            _rxThread.Start();                                // Start the thread

            Debug.Log("[UDP] Discovery listener started on 0.0.0.0:" + discoveryPort); // Log start
        }
        catch (Exception ex)                                   // If something fails during startup…
        {
            Debug.LogError("[UDP] Start failed: " + ex.Message); // Log the reason
            StopListener();                                     // Clean up partially created resources
        }
    }

    private void StopListener()                               // Stop the background thread and close the socket
    {
        if (!_running) return;                                // If not running, nothing to do

        _running = false;                                     // Tell the thread to exit

        try
        {
            if (_udp != null)                                 // If we have a socket…
            {
                _udp.Close();                                 // …close it (this unblocks Receive)
                _udp = null;                                  // …and drop the reference
            }
        }
        catch { /* ignore */ }                                // Ignore any errors closing the socket

        try
        {
            if (_rxThread != null)                            // If the thread exists…
            {
                _rxThread.Join(200);                          // …wait briefly for it to exit
                _rxThread = null;                             // …and drop the reference
            }
        }
        catch { /* ignore */ }                                // Ignore join errors

        Debug.Log("[UDP] Discovery listener stopped");        // Log stop
    }

    private void ReceiveLoop()                                // Runs on the background thread
    {
        // IMPORTANT: This method must NOT call UnityEngine APIs (like Debug.Log, GetComponent, etc.)
        // We only do pure .NET networking here and queue any Unity work to the main thread.

        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0); // This will hold the sender's IP/port

        while (_running)                                      // Loop until StopListener flips the flag
        {
            try
            {
                // Block here until a packet arrives or the socket is closed.
                byte[] data = _udp.Receive(ref remote);       // Receive bytes from whoever sent us a packet

                // Convert bytes to string (robots send small JSON like {"robotId":"...","callsign":""})
                string text = Encoding.UTF8.GetString(data);  // Interpret bytes as UTF-8 text

                // Pull out robotId (and callsign if present) using a tiny string extractor
                string robotId = ExtractJsonString(text, "robotId");   // Get the robot id
                string callsign = ExtractJsonString(text, "callsign");  // Get the callsign (can be empty)

                if (!string.IsNullOrEmpty(robotId))           // Only act if we actually got an id
                {
                    string senderIp = remote.Address.ToString(); // The IP address the packet came from

                    // 1) Update the registry on the main thread so UI stays happy.
                    PostMain(() =>
                    {
                        _dir.Upsert(robotId, callsign, senderIp); // Add/update robot in the directory
                    });

                    // 2) Reply to THIS sender with our WebSocket URL.
                    //    Use the UDP socket's *local* address to avoid sending a wrong NIC/VPN IP.
                    IPEndPoint localEp = (IPEndPoint)_udp.Client.LocalEndPoint;  // Our local bind (IP:port)
                    string wsUrl = "ws://" + localEp.Address + ":" + websocketPort + websocketPath; // Build URL text
                    string reply = "{\"ws\":\"" + wsUrl + "\"}"; // Minimal JSON reply the robot expects
                    byte[] outBytes = Encoding.UTF8.GetBytes(reply); // Turn reply into bytes

                    _udp.Send(outBytes, outBytes.Length, remote);   // Send reply back to the sender (unicast)
                }
            }
            catch (SocketException)                          // Thrown when socket is closed while blocked in Receive
            {
                if (_running)                                // If we didn't intend to stop…
                {
                    // Do nothing here (no Unity logs from this thread).
                    // Next loop iteration will continue (or exit if StopListener was called).
                }
            }
            catch (Exception)                                // Any other error while receiving/parsing
            {
                // Keep the loop alive for robustness, but do not call Unity from here.
            }
        }
    }

    private void PostMain(Action a)                          // Queue an action to the main thread
    {
        if (a == null) return;                               // Ignore null actions
        lock (_mtx) { _main.Enqueue(a); }                    // Put the action in the queue under a lock
    }

    private static string ExtractJsonString(string s, string key) // Tiny helper to parse {"key":"value"} from a short JSON
    {
        try                                                  // Keep it safe (no throw)
        {
            string needle = "\"" + key + "\"";               // Build the '"key"' token to search for
            int k = s.IndexOf(needle, StringComparison.Ordinal); // Find the key in the text
            if (k < 0) return null;                          // If not found, return null
            int colon = s.IndexOf(':', k);                   // Find the ':' after the key
            if (colon < 0) return null;                      // If no colon, give up
            int q1 = s.IndexOf('"', colon + 1);              // Find first quote after the colon
            int q2 = s.IndexOf('"', q1 + 1);                 // Find closing quote
            if (q1 < 0 || q2 < 0) return null;               // If quotes missing, give up
            return s.Substring(q1 + 1, q2 - (q1 + 1));       // Return the text between quotes
        }
        catch { return null; }                               // On any error, just return null
    }
}
