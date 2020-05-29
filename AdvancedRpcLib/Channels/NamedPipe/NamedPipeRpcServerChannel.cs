using Nito.AsyncEx;
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace AdvancedRpcLib.Channels.NamedPipe
{
    public class NamedPipeRpcServerChannel : RpcServerChannel<NamedPipeTransportChannel>
    {
        private readonly string _pipeName;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public NamedPipeRpcServerChannel(
            IRpcSerializer serializer,
            IRpcMessageFactory messageFactory,
            string pipeName,
            IRpcObjectRepository localRepository = null,
            Func<IRpcObjectRepository> remoteRepository = null)
            : base(serializer, messageFactory, localRepository, remoteRepository)
        {
            _pipeName = pipeName;
        }

        public override async Task ListenAsync()
        {
            var initEvent = new AsyncManualResetEvent(false);
            
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(async delegate
            {                
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    _cancellationTokenSource.Token.Register(() => pipe.Dispose());
                    initEvent.Set();
                    await pipe.WaitForConnectionAsync(_cancellationTokenSource.Token);
                    if (!_cancellationTokenSource.IsCancellationRequested)
                    {
                        var client = new NamedPipeTransportChannel(this, pipe);
                        PurgeOldChannels();
                        AddChannel(client);
                        RegisterMessageCallback(client, data => HandleReceivedData(client, data), false);
                        RunReaderLoop(client, () => OnClientDisconnected(new ChannelConnectedEventArgs<NamedPipeTransportChannel>(client)));
                    }
                }
            }, _cancellationTokenSource.Token);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            await initEvent.WaitAsync();
        }

        protected override void Stop()
        {
            _cancellationTokenSource.Cancel();            
        }
    }
}
