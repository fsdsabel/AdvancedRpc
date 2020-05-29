using System;
using System.IO;
using System.IO.Pipes;

namespace AdvancedRpcLib.Channels.NamedPipe
{
    public class NamedPipeTransportChannel : ITransportChannel
    {
        public RpcChannel<NamedPipeTransportChannel> Channel { get; }
        private readonly PipeStream _pipeStream;

        public NamedPipeTransportChannel(RpcChannel<NamedPipeTransportChannel> channel, PipeStream pipeStream)
        {
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _pipeStream = pipeStream ?? throw new ArgumentNullException(nameof(pipeStream));
        }

        public void Dispose()
        {
            _pipeStream.Close();
            _pipeStream.Dispose();
        }

        public Stream GetStream()
        {
            return _pipeStream;
        }
    }
}
