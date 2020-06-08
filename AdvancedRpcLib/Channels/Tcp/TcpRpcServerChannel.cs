using Nito.AsyncEx;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AdvancedRpcLib.Channels.Tcp
{
    public class TcpRpcServerChannel : RpcServerChannel<TcpTransportChannel>
    {
        private readonly IPAddress _address;
        private readonly int _port;
       
        private TcpListener _listener;

        public TcpRpcServerChannel(
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
        
        public override async Task ListenAsync()
        {
            var initEvent = new AsyncManualResetEvent(false);
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(delegate
            {
                _listener = new TcpListener(_address, _port);
                _listener.Start();
                initEvent.Set();
                while (true)
                {
                    var client = new TcpTransportChannel(this, _listener.AcceptTcpClient());
                    AddChannel(client);
                    RegisterMessageCallback(client, data => HandleReceivedData(client, data), false);
                    
                    RunReaderLoop(client, () => OnClientDisconnected(new ChannelConnectedEventArgs<TcpTransportChannel>(client)));
                }
            });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            await initEvent.WaitAsync();
        }

        protected override void Stop()
        {
            _listener?.Stop();
            _listener = null;
        }
    }
}
