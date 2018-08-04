﻿using System;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace AdvancedRpcLib.Channels.NamedPipe
{
    public class NamedPipeRpcClientChannel:RpcClientChannel<NamedPipeTransportChannel>
    {
        private readonly string _pipeName;
        private NamedPipeTransportChannel _channel;

        public NamedPipeRpcClientChannel(
           IRpcSerializer serializer,
           IRpcMessageFactory messageFactory,
           string pipeName,
           IRpcObjectRepository localRepository = null,
           Func<IRpcObjectRepository> remoteRepository = null)
           : base(serializer, messageFactory, localRepository, remoteRepository)
        {
            _pipeName = pipeName;
        }


        protected override NamedPipeTransportChannel TransportChannel => _channel;


        public override async Task ConnectAsync()
        {
            var stream = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Impersonation);
            _channel = new NamedPipeTransportChannel(stream);
            await stream.ConnectAsync();
            RegisterMessageCallback(_channel, HandleReceivedData, false);
            RunReaderLoop(_channel);
        }
    }
}
