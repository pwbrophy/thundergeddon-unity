using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.UI;
using ZXing;
using ZXing.QrCode;

public class QRCodeDisplay : MonoBehaviour
{
    public Image qrCodeImage; // Reference to the UI Image component

    void Start()
    {
        string ipAddress = GetLocalIPAddress();
        if (!string.IsNullOrEmpty(ipAddress))
        {
            GenerateQRCode(ipAddress);
        }
    }

    private string GetLocalIPAddress()
    {
        try
        {
            foreach (NetworkInterface netInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (netInterface.OperationalStatus == OperationalStatus.Up &&
                    (netInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                     netInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                {
                    foreach (UnicastIPAddressInformation addressInfo in netInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (addressInfo.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(addressInfo.Address))
                        {
                            Debug.Log($"Found IP address: {addressInfo.Address}");
                            return addressInfo.Address.ToString();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error getting local IP address: {ex.Message}");
        }
        return null;
    }

    private void GenerateQRCode(string text)
    {
        var qrWriter = new BarcodeWriter
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Height = 256,
                Width = 256
            }
        };

        Color32[] qrCode = qrWriter.Write(text);
        Texture2D qrCodeTexture = new Texture2D(256, 256);
        qrCodeTexture.SetPixels32(qrCode);
        qrCodeTexture.Apply();

        // Convert Texture2D to Sprite
        Sprite qrCodeSprite = Sprite.Create(qrCodeTexture, new Rect(0, 0, qrCodeTexture.width, qrCodeTexture.height), new Vector2(0.5f, 0.5f));

        // Set the Sprite to the UI Image component
        qrCodeImage.sprite = qrCodeSprite;
    }
}
