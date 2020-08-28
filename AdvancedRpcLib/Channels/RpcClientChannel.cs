using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AdvancedRpcLib.Channels
{
    public abstract class RpcClientChannel<TChannel> : RpcChannel<TChannel>, IRpcClientChannel, IDisposable
        where TChannel : ITransportChannel
    {
        protected RpcClientChannel(
         IRpcSerializer serializer,
         IRpcMessageFactory messageFactory,
         IRpcObjectRepository localRepository = null,
         Func<IRpcObjectRepository> remoteRepository = null,
         ILoggerFactory loggerFactory = null)
         : base(serializer, messageFactory, RpcChannelType.Client, localRepository, remoteRepository, loggerFactory)
        {
        }

        public event EventHandler<ChannelConnectedEventArgs<TChannel>> Disconnected;

        public IRpcObjectRepository ObjectRepository => GetRemoteRepository(TransportChannel);

        public abstract Task ConnectAsync(TimeSpan timeout = default);

        protected abstract TChannel TransportChannel { get; }

        protected bool HandleReceivedData(byte[] data, RpcMessage msg)
        {
            return HandleRemoteMessage(TransportChannel, data, msg);
        }

        public async Task<TResult> GetServerObjectAsync<TResult>()
        {
            try
            {
                var remoteRepo = GetRemoteRepository(TransportChannel);
                var response = await SendMessageAsync<RpcGetServerObjectResponseMessage>(TransportChannel,
                    () => MessageFactory.CreateGetServerObjectMessage(remoteRepo.CreateTypeId<TResult>()));

                if (response.Type == RpcMessageType.Exception)
                {
                    throw new TargetInvocationException((Exception) response.Exception.Value);
                }

                return remoteRepo.GetProxyObject<TResult>(GetRpcChannelForClient(TransportChannel),
                    response.InstanceId);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
            catch (Exception ex)
            {
                throw new RpcFailedException("Getting server object failed.", ex);
            }
        }

        protected virtual void OnDisconnected(ChannelConnectedEventArgs<TChannel> e)
        {
            CancelRequests(TransportChannel);
            Disconnected?.Invoke(this, e);
        }

        public void Dispose()
        {
            TransportChannel.Dispose();
        }
    }
}
