using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
        private readonly IRpcObjectRepository _objectRepository;
        private TcpListener _listener;

        public TcpRpcServerChannel(
            IRpcObjectRepository objectRepository,
            IRpcSerializer serializer,
            IRpcMessageFactory messageFactory,
            IPAddress address, int port)
        {
            _messageFactory = messageFactory;
            _address = address;
            _port = port;
            _serializer = serializer;
            _objectRepository = objectRepository;
        }

        private bool HandleReceivedData(TcpClient client, ReadOnlySpan<byte> data)
        {
            var msg = _serializer.DeserializeMessage<RpcMessage>(data);
            switch (msg.Type)
            {
                case RpcMessageType.GetServerObject:
                    {
                        var m = _serializer.DeserializeMessage<RpcGetServerObjectMessage>(data);
                        var obj = _objectRepository.GetObject(m.TypeId);
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
                        var obj = _objectRepository.GetInstance(m.InstanceId);

                        var targetMethod = obj.GetType().GetMethod(m.MethodName);
                        var targetParameterTypes = targetMethod.GetParameters().Select(p => p.ParameterType).ToArray();
                        var args = new object[m.Arguments.Length];
                        for (int i = 0; i < m.Arguments.Length; i++)
                        {
                            args[i] = Convert.ChangeType(m.Arguments[i], targetParameterTypes[i]);
                        }

                        var result = obj.GetType().GetMethod(m.MethodName).Invoke(obj, args);
                        var response = _serializer.SerializeMessage(new RpcCallResultMessage
                        {
                            CallId = m.CallId,
                            Type = RpcMessageType.CallMethod,
                            Result = result
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
                    RegisterMessageCallback(data => HandleReceivedData(client, data), false);
                    //TODO: Verbindung schließen behandeln
                    RunReaderLoop(client.GetStream());
                }
            });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            await initEvent.WaitAsync();
        }

        public object CallRpcMethod(int instanceId, string methodName, object[] args)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            _listener.Stop();            
        }
    }

}
