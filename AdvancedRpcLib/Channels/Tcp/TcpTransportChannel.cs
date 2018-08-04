using System.IO;
using System.Net.Sockets;

namespace AdvancedRpcLib.Channels.Tcp
{
    public class TcpTransportChannel : ITransportChannel
    {
        public TcpTransportChannel(TcpClient client)
        {
            Client = client ?? throw new System.ArgumentNullException(nameof(client));
        }

        public TcpClient Client { get; }

        public void Dispose()
        {
            Client.Close();
            Client.Dispose();
        }

        public Stream GetStream()
        {            
            return Client.GetStream();
        }
    }

}
