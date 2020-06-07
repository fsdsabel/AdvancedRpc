using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AdvancedRpcLib.Channels
{
    public abstract class RpcServerChannel<TChannel> : RpcChannel<TChannel>, IRpcServerChannel
         where TChannel : class, ITransportChannel
    {
        private readonly List<WeakReference<TChannel>> _createdChannels = new List<WeakReference<TChannel>>();

        protected RpcServerChannel(
           IRpcSerializer serializer,
           IRpcMessageFactory messageFactory,
           IRpcObjectRepository localRepository = null,
           Func<IRpcObjectRepository> remoteRepository = null,
           ILoggerFactory loggerFactory = null)
           : base(serializer, messageFactory, RpcChannelType.Server, localRepository, remoteRepository, loggerFactory)
        {
        }

        public event EventHandler<ChannelConnectedEventArgs<TChannel>> ClientConnected;
        public event EventHandler<ChannelConnectedEventArgs<TChannel>> ClientDisconnected;

        public IRpcObjectRepository ObjectRepository => LocalRepository;

        public abstract Task ListenAsync();

      
        protected abstract void Stop();

        protected bool HandleReceivedData(TChannel channel, byte[] data)
        {
            var msg = Serializer.DeserializeMessage<RpcMessage>(data);
            if (HandleRemoteMessage(channel, data, msg))
            {
                return true;
            }

            switch (msg.Type)
            {
                case RpcMessageType.GetServerObject:
                    {
                        var m = Serializer.DeserializeMessage<RpcGetServerObjectMessage>(data);
                        var obj = LocalRepository.GetObject(m.TypeId);
                        var response = Serializer.SerializeMessage(new RpcGetServerObjectResponseMessage
                        {
                            CallId = m.CallId,
                            Type = RpcMessageType.GetServerObject,
                            InstanceId = obj.InstanceId
                        });
                        SendMessage(channel.GetStream(), response);
                        return true;
                    }
            }
            return false;
        }

        protected void AddChannel(TChannel channel)
        {
            _createdChannels.Add(new WeakReference<TChannel>(channel));
            OnClientConnected(new ChannelConnectedEventArgs<TChannel>(channel));
        }

        protected void PurgeOldChannels()
        {
            foreach (var client in _createdChannels.ToArray())
            {
                if (!client.TryGetTarget(out _))
                {
                    _createdChannels.Remove(client);
                }
            }
        }

        protected virtual void OnClientConnected(ChannelConnectedEventArgs<TChannel> e)
        {
            ClientConnected?.Invoke(this, e);
        }

        protected virtual void OnClientDisconnected(ChannelConnectedEventArgs<TChannel> e)
        {
            ClientDisconnected?.Invoke(this, e);
        }

        protected virtual void Dispose(bool disposing)
        {
            if(disposing)
            {
                Stop();
                foreach (var client in _createdChannels)
                {
                    if (client.TryGetTarget(out var aliveClient))
                    {
                        aliveClient.Dispose();
                    }
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
