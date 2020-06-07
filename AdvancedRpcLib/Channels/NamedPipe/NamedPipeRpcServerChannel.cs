using Nito.AsyncEx;
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AdvancedRpcLib.Channels.NamedPipe
{
    public partial class NamedPipeRpcServerChannel : RpcServerChannel<NamedPipeTransportChannel>
    {
        private readonly string _pipeName;
        private readonly PipeSecurity _pipeSecurity;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public NamedPipeRpcServerChannel(
            IRpcSerializer serializer,
            IRpcMessageFactory messageFactory,
            string pipeName,
            PipeSecurity pipeSecurity = null,
            IRpcObjectRepository localRepository = null,
            Func<IRpcObjectRepository> remoteRepository = null,
            ILoggerFactory loggerFactory = null)
            : base(serializer, messageFactory, localRepository, remoteRepository, loggerFactory)
        {
            _pipeName = pipeName;
            _pipeSecurity = pipeSecurity;
        }

        public override async Task ListenAsync()
        {
            var initEvent = new AsyncManualResetEvent(false);
            Exception initException = null;

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(async delegate
            {                
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    NamedPipeServerStream pipe;
                    try
                    {
#if NETFRAMEWORK
                        pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut,
                            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous, 0, 0, _pipeSecurity);
#else
                        pipe = CreatePipe(_pipeName, _pipeSecurity);
#endif
                        _cancellationTokenSource.Token.Register(() => pipe.Dispose());
                    }
                    catch (Exception ex)
                    {
                        initException = ex;
                        initEvent.Set();
                        return;
                    }

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
            if (initException != null)
            {
                throw initException;
            }
        }

        protected override void Stop()
        {
            _cancellationTokenSource.Cancel();            
        }
    }
}
