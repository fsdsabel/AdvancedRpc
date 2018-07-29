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
        private readonly IRpcMessageFactory _messageFactory;
        private readonly IPAddress _address;
        private readonly int _port;
        private readonly IRpcSerializer _serializer;
        private readonly IRpcObjectRepository _localRepository, _remoteRepository;
        private TcpListener _listener;
        private readonly List<WeakReference<TcpClient>> _createdClients = new List<WeakReference<TcpClient>>();

        public IRpcObjectRepository ObjectRepository => _localRepository;

        public TcpRpcServerChannel(
            IRpcSerializer serializer,
            IRpcMessageFactory messageFactory,
            IPAddress address, int port,
            IRpcObjectRepository localRepository = null,
            IRpcObjectRepository remoteRepository = null)
        {
            _messageFactory = messageFactory;
            _address = address;
            _port = port;
            _serializer = serializer;
            _remoteRepository = remoteRepository ?? new RpcObjectRepository();
            _localRepository = localRepository ?? new RpcObjectRepository();
        }

        private bool HandleReceivedData(TcpClient client, ReadOnlySpan<byte> data)
        {
            var msg = _serializer.DeserializeMessage<RpcMessage>(data);
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
                case RpcMessageType.CallMethod:
                    {
                        var m = _serializer.DeserializeMessage<RpcMethodCallMessage>(data);
                        var obj = _localRepository.GetInstance(m.InstanceId);

                        var targetMethod = obj.GetType().GetMethod(m.MethodName);
                        var targetParameterTypes = targetMethod.GetParameters().Select(p => p.ParameterType).ToArray();
                        var args = new object[m.Arguments.Length];
                        for (int i = 0; i < m.Arguments.Length; i++)
                        {
                            args[i] = Convert.ChangeType(m.Arguments[i], targetParameterTypes[i]);
                        }

                        var result = targetMethod.Invoke(obj, args);

                        var resultMessage = new RpcCallResultMessage
                        {
                            CallId = m.CallId,
                            Type = RpcMessageType.CallMethod,
                            Result = result
                        };

                        if (targetMethod.ReturnType.IsInterface)
                        {
                            // create a proxy
                            var handle = _localRepository.AddInstance(targetMethod.ReturnType, result);
                            resultMessage.ResultType = RpcType.Proxy;
                            resultMessage.Result = handle.InstanceId;
                        }


                        var response = _serializer.SerializeMessage(resultMessage);
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
                    RegisterMessageCallback(data => HandleReceivedData(client, data), false);
                    //TODO: Verbindung schließen behandeln
                    RunReaderLoop(client.GetStream());
                }
            });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            await initEvent.WaitAsync();
        }

        public object CallRpcMethod(int instanceId, string methodName, object[] args, Type resultType)
        {
            throw new NotImplementedException();
        }

        public void RemoveInstance(int localInstanceId, int remoteInstanceId)
        {
            throw new NotImplementedException();
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
