using Newtonsoft.Json;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace AdvancedRpcLib.Channels.Tcp
{


    public abstract class TcpRpcChannel 
    {
        private readonly Dictionary<TcpClient, AsyncNotification> _messageNotifications = new Dictionary<TcpClient, AsyncNotification>();
        private readonly object _sendLock = new object();
        protected readonly IRpcSerializer _serializer;
        protected readonly IRpcObjectRepository _localRepository;
        private readonly Func<IRpcObjectRepository> _remoteRepository;
        private readonly Dictionary<TcpClient, IRpcObjectRepository> _remoteRepositories = new Dictionary<TcpClient, IRpcObjectRepository>();
        protected readonly IRpcMessageFactory _messageFactory;

        protected TcpRpcChannel(IRpcSerializer serializer,
            IRpcMessageFactory messageFactory,
            IRpcObjectRepository localRepository = null,
            Func<IRpcObjectRepository> remoteRepository = null)
        {
            _messageFactory = messageFactory;
            _serializer = serializer;
            _remoteRepository = remoteRepository ?? (() => new RpcObjectRepository());
            _localRepository = localRepository ?? new RpcObjectRepository();
        }

        protected IRpcObjectRepository GetRemoteRepository(TcpClient tcpClient)
        {
            lock(_remoteRepositories)
            {
                if(!_remoteRepositories.ContainsKey(tcpClient))
                {
                    _remoteRepositories.Add(tcpClient, _remoteRepository());
                }
                return _remoteRepositories[tcpClient];
            }
        }

        protected Task<TResult> SendMessageAsync<TResult>(TcpClient client, byte[] msg, int callId)
                where TResult : RpcMessage
        {
            var waitTask = WaitForMessageResultAsync<TResult>(client, _serializer, callId);
            lock (_sendLock)
            {
                using (var writer = new BinaryWriter(client.GetStream(), Encoding.UTF8, true))
                {
                    //TODO: longer messages > 64k, maybe with other messagetype
                    writer.Write((byte)TcpRpcChannelMessageType.Message);
                    writer.Write((ushort)msg.Length);
                    writer.Write(msg);
                }
            }
            return waitTask;
        }

        protected void SendMessage(Stream stream, byte[] msg)
        {
            lock (_sendLock)
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    //TODO: longer messages > 64k, maybe with other messagetype
                    writer.Write((byte)TcpRpcChannelMessageType.Message);
                    writer.Write((ushort)msg.Length);
                    writer.Write(msg);
                }
            }
        }

        protected Task<T> SendMessageAsync<T>(TcpClient tcpClient, Func<RpcMessage> msgFunc) where T : RpcMessage
        {
            var msg = msgFunc();
            var serializedMsg = _serializer.SerializeMessage(msg);
            return SendMessageAsync<T>(tcpClient, serializedMsg, msg.CallId);
        }

        protected object CallRpcMethod(TcpClient tcpClient, 
            int instanceId, string methodName, Type[] argTypes, object[] args, Type resultType)
        {
            try
            {
                var response = SendMessageAsync<RpcCallResultMessage>(tcpClient, () => _messageFactory.CreateMethodCallMessage(_localRepository, instanceId, methodName, argTypes, args))
                    .GetAwaiter().GetResult();

                if (response.ResultType == RpcType.Proxy)
                {
                    return GetRemoteRepository(tcpClient).GetProxyObject(this as IRpcChannel, resultType, Convert.ToInt32(response.Result));
                }

                return response.Result;
            }
            catch (Exception ex)
            {
                throw new RpcFailedException($"Calling remote method {methodName} on object #{instanceId} failed.", ex);
            }
        }

        public void RemoveInstance(TcpClient tcpClient, int localInstanceId, int remoteInstanceId)
        {
            try
            {
                GetRemoteRepository(tcpClient).RemoveInstance(localInstanceId);
                SendMessageAsync<RpcMessage>(tcpClient, () => _messageFactory.CreateRemoveInstanceMessage(remoteInstanceId)).GetAwaiter().GetResult();
            }
            catch
            {
                // server not reachable, that's ok
            }
        }

        private async Task<TResult> WaitForMessageResultAsync<TResult>(TcpClient client, IRpcSerializer serializer, int callId)
            where TResult : RpcMessage
        {
            var re = new AsyncManualResetEvent(false);
            TResult result = default;
            RegisterMessageCallback(client, (data) =>
            {
                var bareMsg = serializer.DeserializeMessage<RpcMessage>(data);
                if (bareMsg.CallId == callId)
                {
                    result = serializer.DeserializeMessage<TResult>(data);
                    re.Set();
                    return true;
                }
                return false;
            }, true);

            await re.WaitAsync();
            return result;
        }

        protected private void RegisterMessageCallback(TcpClient client, AsyncNotification.DataReceivedDelegate callback, bool autoremove)
        {
            lock(_messageNotifications)
            {
                if(!_messageNotifications.ContainsKey(client))
                {
                    _messageNotifications.Add(client, new AsyncNotification());
                }
                _messageNotifications[client].Register(callback, autoremove);
            }
         
        }

        class RpcChannelWrapper : IRpcChannel
        {
            private readonly IRpcChannel _channel;
            private readonly TcpRpcChannel _tcpChannel;
            private readonly TcpClient _client;

            public RpcChannelWrapper(TcpRpcChannel tcpChannel, TcpClient client, IRpcChannel channel)
            {
                _channel = channel;
                _tcpChannel = tcpChannel;
                _client = client;
            }

            public IRpcObjectRepository ObjectRepository => _channel.ObjectRepository;

            public object CallRpcMethod(int instanceId, string methodName, Type[] argTypes, object[] args, Type resultType)
            {
                return _tcpChannel.CallRpcMethod(_client, instanceId, methodName, argTypes, args, resultType);
            }

            public void RemoveInstance(int localInstanceId, int remoteInstanceId)
            {
                _tcpChannel.RemoveInstance(_client, localInstanceId, remoteInstanceId);
            }
        }

        protected IRpcChannel GetRpcChannelForClient(TcpClient client)
        {
            return new RpcChannelWrapper(this, client, this as IRpcChannel); 
        }

        protected bool HandleRemoteMessage(TcpClient client, ReadOnlySpan<byte> data, RpcMessage msg)
        {
            switch (msg.Type)
            {               
                case RpcMessageType.CallMethod:
                    {
                        var m = _serializer.DeserializeMessage<RpcMethodCallMessage>(data);
                        var obj = _localRepository.GetInstance(m.InstanceId);

                        var targetMethod = obj.GetType().GetMethod(m.MethodName);
                        var targetParameterTypes = targetMethod.GetParameters().Select(p => p.ParameterType).ToArray();
                        var args = new object[m.Arguments.Length];
                        for (int i = 0; i < m.Arguments.Length; i++)
                        {
                            switch (m.Arguments[i].Type)
                            {
                                case RpcType.Builtin:
                                    args[i] = Convert.ChangeType(m.Arguments[i].Value, targetParameterTypes[i]);
                                    break;
                                case RpcType.Proxy:
                                    args[i] = GetRemoteRepository(client).GetProxyObject(GetRpcChannelForClient(client),
                                                    targetParameterTypes[i], (int)Convert.ChangeType(m.Arguments[i].Value, typeof(int)));
                                    break;
                                case RpcType.Serialized:
                                    var type = Type.GetType(m.Arguments[i].TypeId);                                    
                                    args[i] = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(m.Arguments[i].Value), type);
                                    break;
                                default:
                                    throw new InvalidDataException();
                            }
                        }

                        var result = targetMethod.Invoke(obj, args);

                        var resultMessage = new RpcCallResultMessage
                        {
                            CallId = m.CallId,
                            Type = RpcMessageType.CallMethodResult,
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
                case RpcMessageType.RemoveInstance:
                    {
                        var m = _serializer.DeserializeMessage<RpcRemoveInstanceMessage>(data);
                        _localRepository.RemoveInstance(m.InstanceId);

                        var response = _serializer.SerializeMessage(new RpcMessage
                        {
                            CallId = m.CallId,
                            Type = RpcMessageType.Ok
                        });
                        SendMessage(client.GetStream(), response);
                        return true;
                    }
            }
            return false;
        }

        protected void RunReaderLoop(TcpClient client)
        {
            Task.Run(delegate
            {
                var reader = new BinaryReader(client.GetStream(), Encoding.UTF8, true);
                var smallMessageBuffer = new byte[ushort.MaxValue];



                while (true)
                {
                    var type = (TcpRpcChannelMessageType)reader.ReadByte();
                    switch (type)
                    {
                        case TcpRpcChannelMessageType.Message:
                            var msgLen = reader.ReadUInt16();
                            int offset = 0;
                            while (offset < msgLen)
                            {
                                offset += reader.Read(smallMessageBuffer, offset, msgLen - offset);
                            }

                            var copy = new byte[msgLen];
                            Array.Copy(smallMessageBuffer, copy, msgLen);


                            Task.Run(delegate
                            {
                                try
                                {
                                    //smallMessageBuffer
                                    if (!_messageNotifications[client].Notify(new ReadOnlySpan<byte>(copy)))
                                    {
                                        Console.WriteLine("Failed to process message");
                                    }

                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("Failed to run remote message");
                                }
                            });
                            
                            break;

                        default:
                            throw new NotSupportedException("Invalid message type encountered");
                    }
                }
            });
        }


    }

}
