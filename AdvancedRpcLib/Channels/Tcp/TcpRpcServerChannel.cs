using Newtonsoft.Json;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AdvancedRpcLib.Channels.Tcp
{


    public class TcpRpcServerChannel : TcpRpcChannel, IRpcServerChannel, IDisposable
    {
        private readonly IPAddress _address;
        private readonly int _port;
       
        private TcpListener _listener;
        private readonly List<WeakReference<TcpClient>> _createdClients = new List<WeakReference<TcpClient>>();


        public TcpRpcServerChannel(
            IRpcSerializer serializer,
            IRpcMessageFactory messageFactory,
            IPAddress address, int port,
            IRpcObjectRepository localRepository = null,
            Func<IRpcObjectRepository> remoteRepository = null)
            : base(serializer, messageFactory, localRepository, remoteRepository)
        {
            _address = address;
            _port = port;
        }


        public IRpcObjectRepository ObjectRepository => _localRepository;

        private bool HandleReceivedData(TcpClient client, ReadOnlySpan<byte> data)
        {
            var msg = _serializer.DeserializeMessage<RpcMessage>(data);
            if(HandleRemoteMessage(client, data, msg))
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
                        SendMessage(client.GetStream(), response);
                        return true;
                    }             
            }
            return false;
        }

        public async Task ListenAsync()
        {
            var initEvent = new AsyncAutoResetEvent(false);
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(delegate
            {
                _listener = new TcpListener(_address, _port);
                _listener.Start();
                initEvent.Set();
                while (true)
                {
                    var client = _listener.AcceptTcpClient();
                    PurgeOldClients();
                    _createdClients.Add(new WeakReference<TcpClient>(client));
                    RegisterMessageCallback(client, data => HandleReceivedData(client, data), false);
                    //TODO: Verbindung schließen behandeln
                    RunReaderLoop(client);
                }
            });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            await initEvent.WaitAsync();
        }
        
        private void PurgeOldClients()
        {
            foreach (var client in _createdClients.ToArray())
            {
                if (!client.TryGetTarget(out var aliveClient))
                {
                    _createdClients.Remove(client);
                }
            }
        }

        public void Dispose()
        {            
            _listener.Stop();            
            foreach(var client in _createdClients)
            {
                if(client.TryGetTarget(out var aliveClient))
                {
                    aliveClient.Close();
                    aliveClient.Dispose();
                }
            }
        }
    }

}
