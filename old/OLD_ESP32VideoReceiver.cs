using UnityEngine;
using UnityEngine.UI;
using System.Collections.Concurrent;

public class ESP32VideoReceiver : MonoBehaviour
{
    public static ESP32VideoReceiver Instance { get; private set; }
    public RawImage displayImage; // Assign this from the inspector
    private AspectRatioFitter aspectRatioFitter; // Add this line

    private ConcurrentQueue<byte[]> frameQueue = new ConcurrentQueue<byte[]>();
    private Texture2D videoTexture;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            aspectRatioFitter = displayImage.GetComponent<AspectRatioFitter>(); // Initialize the aspectRatioFitter
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Update()
    {
        if (frameQueue.TryDequeue(out var frameData))
        {
            if (videoTexture == null || videoTexture.width == 2)
            {
                videoTexture = new Texture2D(2, 2);
            }
            videoTexture.LoadImage(frameData); // Load JPEG data into the texture

            // Update aspect ratio
            if (aspectRatioFitter != null)
            {
                aspectRatioFitter.aspectRatio = (float)videoTexture.width / videoTexture.height;
            }

            displayImage.texture = videoTexture; // Display the updated texture
        }
    }

    public void ReceiveFrame(byte[] frame)
    {
        frameQueue.Enqueue(frame);
    }
}
