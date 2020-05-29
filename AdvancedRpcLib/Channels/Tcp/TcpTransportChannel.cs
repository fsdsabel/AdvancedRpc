using System;
using System.IO;
using System.Net.Sockets;

namespace AdvancedRpcLib.Channels.Tcp
{
    public class TcpTransportChannel : ITransportChannel
    {
        public TcpTransportChannel(RpcChannel<TcpTransportChannel> channel, TcpClient client)
        {
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
            Client = client ?? throw new System.ArgumentNullException(nameof(client));
        }

        public RpcChannel<TcpTransportChannel> Channel { get; }
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
