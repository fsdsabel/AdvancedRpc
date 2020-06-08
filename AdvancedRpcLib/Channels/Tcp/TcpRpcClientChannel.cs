using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

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
            Func<IRpcObjectRepository> remoteRepository = null,
            ILoggerFactory loggerFactory = null)
            : base(serializer, messageFactory, localRepository, remoteRepository, loggerFactory)
        {
            _address = address;
            _port = port;
        }

        protected override TcpTransportChannel TransportChannel => _tcpClient;

        public override async Task ConnectAsync(TimeSpan timeout = default)
        {
            timeout = timeout == default ? Timeout.InfiniteTimeSpan : timeout;
            _tcpClient = new TcpTransportChannel(this, new TcpClient());
            using (var cts = new CancellationTokenSource(timeout))
            {
                await _tcpClient.Client.ConnectAsync(_address, _port).WaitAsync(cts.Token);
            }
            RegisterMessageCallback(_tcpClient, HandleReceivedData, false);
            RunReaderLoop(_tcpClient, () => OnDisconnected(new ChannelConnectedEventArgs<TcpTransportChannel>(_tcpClient))); 
        }     
    }
}
