using System;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AdvancedRpcLib.Channels.NamedPipe
{
    public class NamedPipeRpcClientChannel:RpcClientChannel<NamedPipeTransportChannel>
    {
        private readonly string _pipeName;
        private readonly TokenImpersonationLevel _tokenImpersonationLevel;
        private NamedPipeTransportChannel _channel;
        private ILogger<NamedPipeRpcClientChannel> _logger;

        public NamedPipeRpcClientChannel(
           IRpcSerializer serializer,
           IRpcMessageFactory messageFactory,
           string pipeName,
           TokenImpersonationLevel tokenImpersonationLevel = TokenImpersonationLevel.None,
           IRpcObjectRepository localRepository = null,
           Func<IRpcObjectRepository> remoteRepository = null,
           ILoggerFactory loggerFactory = null)
           : base(serializer, messageFactory, localRepository, remoteRepository, loggerFactory)
        {
            _pipeName = pipeName;
            _tokenImpersonationLevel = tokenImpersonationLevel;
            _logger = loggerFactory?.CreateLogger<NamedPipeRpcClientChannel>();
        }

        protected override NamedPipeTransportChannel TransportChannel => _channel;

        public override async Task ConnectAsync(TimeSpan timeout = default)
        {
            timeout = timeout == default ? Timeout.InfiniteTimeSpan : timeout;
            _logger?.LogDebug($"Connecting to pipe '{_pipeName}' with timeout '{timeout}'.");
            var stream = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, 
                _tokenImpersonationLevel);
            _channel = new NamedPipeTransportChannel(this, stream);
            await stream.ConnectAsync((int)timeout.TotalMilliseconds);

            _logger?.LogDebug($"Connected to pipe '{_pipeName}'. Starting message loop.");

            RegisterMessageCallback(_channel, HandleReceivedData, false);
            RunReaderLoop(_channel, ()=> OnDisconnected(new ChannelConnectedEventArgs<NamedPipeTransportChannel>(_channel)));

            _logger?.LogDebug($"Message loop running.");
        }
    }
}
