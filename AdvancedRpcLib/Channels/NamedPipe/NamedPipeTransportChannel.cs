using System.IO;
using System.IO.Pipes;

namespace AdvancedRpcLib.Channels.NamedPipe
{
    public class NamedPipeTransportChannel : ITransportChannel
    {
        private readonly PipeStream _pipeStream;

        public NamedPipeTransportChannel(PipeStream pipeStream) {
            _pipeStream = pipeStream;
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
