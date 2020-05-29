using System;
using System.IO.Pipes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AdvancedRpcLib.Channels.NamedPipe
{
    public class NamedPipeRpcClientChannel:RpcClientChannel<NamedPipeTransportChannel>
    {
        private readonly string _pipeName;
        private NamedPipeTransportChannel _channel;
        private ILogger<NamedPipeRpcClientChannel> _logger;

        public NamedPipeRpcClientChannel(
           IRpcSerializer serializer,
           IRpcMessageFactory messageFactory,
           string pipeName,
           IRpcObjectRepository localRepository = null,
           Func<IRpcObjectRepository> remoteRepository = null,
           ILoggerFactory loggerFactory = null)
           : base(serializer, messageFactory, localRepository, remoteRepository, loggerFactory)
        {
            _pipeName = pipeName;
            _logger = loggerFactory?.CreateLogger<NamedPipeRpcClientChannel>();
        }

        protected override NamedPipeTransportChannel TransportChannel => _channel;

        public override async Task ConnectAsync()
        {
            _logger?.LogTrace($"Connecting to pipe {_pipeName}.");
            var stream = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.None);
            _channel = new NamedPipeTransportChannel(this, stream);
            await stream.ConnectAsync();

            _logger?.LogTrace($"Connected to pipe {_pipeName}. Starting message loop.");

            RegisterMessageCallback(_channel, HandleReceivedData, false);
            RunReaderLoop(_channel, ()=> OnDisconnected(new ChannelConnectedEventArgs<NamedPipeTransportChannel>(_channel)));

            _logger?.LogTrace($"Message loop running.");
        }
    }
}
