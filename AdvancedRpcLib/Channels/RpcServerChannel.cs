using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdvancedRpcLib.Channels
{
    public abstract class RpcServerChannel<TChannel> : RpcChannel<TChannel>, IRpcServerChannel, IDisposable
         where TChannel : class, ITransportChannel
    {
        private readonly List<WeakReference<TChannel>> _createdChannels = new List<WeakReference<TChannel>>();

        protected RpcServerChannel(
           IRpcSerializer serializer,
           IRpcMessageFactory messageFactory,
           IRpcObjectRepository localRepository = null,
           Func<IRpcObjectRepository> remoteRepository = null)
           : base(serializer, messageFactory, RpcChannelType.Server, localRepository, remoteRepository)
        {
        }


        public IRpcObjectRepository ObjectRepository => _localRepository;

        public abstract Task ListenAsync();

      
        protected abstract void Stop();

        protected bool HandleReceivedData(TChannel channel, ReadOnlySpan<byte> data)
        {
            var msg = _serializer.DeserializeMessage<RpcMessage>(data);
            if (HandleRemoteMessage(channel, data, msg))
            {
                return true;
            }

            switch (msg.Type)
            {
                case RpcMessageType.GetServerObject:
                    {
                        var m = _serializer.DeserializeMessage<RpcGetServerObjectMessage>(data);
                        var obj = _localRepository.GetObject(m.TypeId);
                        var response = _serializer.SerializeMessage(new RpcGetServerObjectResponseMessage
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
        }

        protected void PurgeOldChannels()
        {
            foreach (var client in _createdChannels.ToArray())
            {
                if (!client.TryGetTarget(out var aliveClient))
                {
                    _createdChannels.Remove(client);
                }
            }
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
