using System;
using System.Net;
using System.Collections;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using TMPro;

public class UDPBroadcastTest : MonoBehaviour
{
    public int broadcastPort = 8888;
    public float broadcastInterval = 1.0f; // in seconds

    private UdpClient udpClient;
    private IPEndPoint broadcastEndPoint;
    private float nextBroadcastTime;

    // Reference to the TextMeshProUGUI component
    public TextMeshProUGUI IPAddressText;

    void Start()
    {
        udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;
        broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, broadcastPort);

        nextBroadcastTime = Time.time;

        // Get and display the local IP address
        string localIPAddress = PetersUtils.GetLocalIPAddress().ToString();
        if (!string.IsNullOrEmpty(localIPAddress))
        {
            DisplayIPAddress(localIPAddress);
        }
    }

    void Update()
    {
        if (Time.time >= nextBroadcastTime)
        {
            SendBroadcast();
            nextBroadcastTime = Time.time + broadcastInterval;
        }
    }

    void SendBroadcast()
    {
        string message = PetersUtils.GetLocalIPAddress().ToString();
        if (!string.IsNullOrEmpty(message))
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            udpClient.Send(data, data.Length, broadcastEndPoint);
            //Debug.Log($"Broadcasted IP address: {message}");
        }
        else
        {
            Debug.LogWarning("Could not find local IP address.");
        }
    }

    void DisplayIPAddress(string ipAddress)
    {
        if (IPAddressText != null)
        {
            IPAddressText.text = $"IP Address: {ipAddress}";
        }
        else
        {
            Debug.LogError("IPAddressText is not assigned.");
        }
    }

    void OnDestroy()
    {
        if (udpClient != null)
        {
            udpClient.Close();
        }
    }
}
