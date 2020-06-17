using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AdvancedRpcLib.Channels
{
    public abstract class RpcServerChannel<TChannel> : RpcChannel<TChannel>, IRpcServerChannel<TChannel>
         where TChannel : class, ITransportChannel
    {
        private readonly List<TChannel> _createdChannels = new List<TChannel>();

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


        public IReadOnlyCollection<TChannel> ConnectedChannels
        {
            get
            {
                lock (_createdChannels)
                {
                    return new ReadOnlyCollection<TChannel>(_createdChannels);
                }
            }
        }

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
            lock (_createdChannels)
            {
                _createdChannels.Add(channel);
            }

            OnClientConnected(new ChannelConnectedEventArgs<TChannel>(channel));
        }


        protected virtual void OnClientConnected(ChannelConnectedEventArgs<TChannel> e)
        {
            ClientConnected?.Invoke(this, e);
        }

        protected virtual void OnClientDisconnected(ChannelConnectedEventArgs<TChannel> e)
        {
            CancelRequests(e.TransportChannel);
            ClientDisconnected?.Invoke(this, e);
            LocalRepository.RemoveAllForChannel(e.TransportChannel);
            lock (_createdChannels)
            {
                _createdChannels.Remove(e.TransportChannel);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if(disposing)
            {
                Stop();
                lock (_createdChannels)
                {
                    foreach (var channel in _createdChannels.ToArray())
                    {
                        channel?.Dispose();
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
