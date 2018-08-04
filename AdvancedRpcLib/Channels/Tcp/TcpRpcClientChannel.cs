using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AdvancedRpcLib.Channels.Tcp
{

    public class TcpRpcClientChannel : RpcClientChannel<TcpTransportChannel>
    {
        private readonly IPAddress _address;
        private TcpTransportChannel _tcpClient;
        private readonly int _port;
        

        public TcpRpcClientChannel(
            IRpcSerializer serializer,
            IRpcMessageFactory messageFactory,
            IPAddress address, int port,
            IRpcObjectRepository localRepository = null,
            Func<IRpcObjectRepository> remoteRepository = null)
            : base(serializer, messageFactory, localRepository, remoteRepository)
        {
            _address = address;
            _port = port;
        }


        protected override TcpTransportChannel TransportChannel => _tcpClient;


        public override async Task ConnectAsync()
        {
            _tcpClient = new TcpTransportChannel(new TcpClient());
            await _tcpClient.Client.ConnectAsync(_address, _port);
            RegisterMessageCallback(_tcpClient, data => HandleReceivedData(data), false);
            RunReaderLoop(_tcpClient);
        }     
    }

}
