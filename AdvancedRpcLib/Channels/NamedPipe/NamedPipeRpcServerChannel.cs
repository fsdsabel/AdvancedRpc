using Nito.AsyncEx;
using System;
using System.IO;
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
        private readonly ILogger _logger;

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
            _logger = loggerFactory?.CreateLogger<NamedPipeRpcServerChannel>();
        }

        public override async Task ListenAsync()
        {
            _logger?.LogInformation($"Starting named pipe RPC server '{_pipeName}'.");
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
                    _logger?.LogInformation("Waiting for clients.");
                    await pipe.WaitForConnectionAsync(_cancellationTokenSource.Token);
                    _logger?.LogInformation("Client connected.");
                    if (!_cancellationTokenSource.IsCancellationRequested)
                    {
                        var client = new NamedPipeTransportChannel(this, pipe);
                        AddChannel(client);
                        RegisterMessageCallback(client, (data, msg) => HandleReceivedData(client, data, msg), false);
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
            _logger?.LogInformation("RPC server started.");
        }

        protected override void Stop()
        {
            _cancellationTokenSource.Cancel();            
        }

        protected override bool IsConnected(Stream stream)
        {
            return ((NamedPipeServerStream) stream).IsConnected;
        }
    }
}
