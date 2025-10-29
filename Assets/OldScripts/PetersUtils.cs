using System.Net;
using System.Net.Sockets;

public static class PetersUtils
{
    public static IPAddress GetLocalIPAddress()
    {
        using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
        {
            try
            {
                socket.Connect("8.8.8.8", 65530);
                IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                return endPoint.Address;
            }
            catch (SocketException)
            {
                return IPAddress.Loopback;
            }
        }
    }
}
