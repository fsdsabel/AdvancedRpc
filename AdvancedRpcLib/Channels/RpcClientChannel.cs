using System;
using System.Threading.Tasks;

namespace AdvancedRpcLib.Channels
{
    public abstract class RpcClientChannel<TChannel> : RpcChannel<TChannel>, IRpcClientChannel, IDisposable
        where TChannel : ITransportChannel
    {
        protected RpcClientChannel(
         IRpcSerializer serializer,
         IRpcMessageFactory messageFactory,
         IRpcObjectRepository localRepository = null,
         Func<IRpcObjectRepository> remoteRepository = null)
         : base(serializer, messageFactory, localRepository, remoteRepository)
        {
        }


        public IRpcObjectRepository ObjectRepository => GetRemoteRepository(TransportChannel);

        public abstract Task ConnectAsync();

        protected abstract TChannel TransportChannel { get; }

        protected bool HandleReceivedData(ReadOnlySpan<byte> data)
        {
            var msg = _serializer.DeserializeMessage<RpcMessage>(data);
            return HandleRemoteMessage(TransportChannel, data, msg);
        }

        public async Task<TResult> GetServerObjectAsync<TResult>()
        {
            try
            {
                var remoteRepo = GetRemoteRepository(TransportChannel);
                var response = await SendMessageAsync<RpcGetServerObjectResponseMessage>(TransportChannel,
                    () => _messageFactory.CreateGetServerObjectMessage(remoteRepo.CreateTypeId<TResult>()));
                return remoteRepo.GetProxyObject<TResult>(GetRpcChannelForClient(TransportChannel), response.InstanceId);
            }
            catch (Exception ex)
            {
                throw new RpcFailedException("Getting server object failed.", ex);
            }
        }

        public void Dispose()
        {
            TransportChannel.Dispose();
        }
    }

}
