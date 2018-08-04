using Newtonsoft.Json;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdvancedRpcLib.Channels
{
    public interface ITransportChannel : IDisposable
    {
        Stream GetStream();
    }

    public abstract class RpcChannel<TChannel> where TChannel:ITransportChannel
    {
        class RpcChannelWrapper : IRpcChannel
        {
            private readonly RpcChannel<TChannel> _rpcChannel;
            private readonly TChannel _channel;

            public RpcChannelWrapper(RpcChannel<TChannel> rpcChannel, TChannel channel)
            {
                _rpcChannel = rpcChannel;
                _channel = channel;
            }


            public object CallRpcMethod(int instanceId, string methodName, Type[] argTypes, object[] args, Type resultType)
            {
                return _rpcChannel.CallRpcMethod(_channel, instanceId, methodName, argTypes, args, resultType);
            }

            public void RemoveInstance(int localInstanceId, int remoteInstanceId)
            {
                _rpcChannel.RemoveInstance(_channel, localInstanceId, remoteInstanceId);
            }
        }

        private readonly Dictionary<TChannel, AsyncNotification> _messageNotifications = new Dictionary<TChannel, AsyncNotification>();
        private readonly object _sendLock = new object();
        protected readonly IRpcSerializer _serializer;
        protected readonly IRpcObjectRepository _localRepository;
        private readonly Func<IRpcObjectRepository> _remoteRepository;
        private readonly Dictionary<TChannel, IRpcObjectRepository> _remoteRepositories = new Dictionary<TChannel, IRpcObjectRepository>();
        protected readonly IRpcMessageFactory _messageFactory;

        protected RpcChannel(IRpcSerializer serializer,
           IRpcMessageFactory messageFactory,
           IRpcObjectRepository localRepository = null,
           Func<IRpcObjectRepository> remoteRepository = null)
        {
            _messageFactory = messageFactory;
            _serializer = serializer;
            _remoteRepository = remoteRepository ?? (() => new RpcObjectRepository());
            _localRepository = localRepository ?? new RpcObjectRepository();
        }

        protected IRpcObjectRepository GetRemoteRepository(TChannel channel)
        {
            lock (_remoteRepositories)
            {
                if (!_remoteRepositories.ContainsKey(channel))
                {
                    _remoteRepositories.Add(channel, _remoteRepository());
                }
                return _remoteRepositories[channel];
            }
        }

        protected Task<TResult> SendMessageAsync<TResult>(TChannel channel, byte[] msg, int callId)
               where TResult : RpcMessage
        {
            var waitTask = WaitForMessageResultAsync<TResult>(channel, _serializer, callId);
            lock (_sendLock)
            {
                SendMessage(channel.GetStream(), msg);
            }
            return waitTask;
        }

        protected void SendMessage(Stream stream, byte[] msg)
        {
            lock (_sendLock)
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    if (msg.Length <= ushort.MaxValue)
                    {
                        writer.Write((byte)RpcChannelMessageType.Message);
                        writer.Write((ushort)msg.Length);
                    }
                    else
                    {
                        writer.Write((byte)RpcChannelMessageType.LargeMessage);
                        writer.Write(msg.Length);
                    }
                    writer.Write(msg);
                }
            }
        }

        protected Task<T> SendMessageAsync<T>(TChannel channel, Func<RpcMessage> msgFunc) where T : RpcMessage
        {
            var msg = msgFunc();
            var serializedMsg = _serializer.SerializeMessage(msg);
            return SendMessageAsync<T>(channel, serializedMsg, msg.CallId);
        }

        protected object CallRpcMethod(TChannel channel,
            int instanceId, string methodName, Type[] argTypes, object[] args, Type resultType)
        {
            try
            {
                var response = SendMessageAsync<RpcCallResultMessage>(channel, () => _messageFactory.CreateMethodCallMessage(_localRepository, instanceId, methodName, argTypes, args))
                    .GetAwaiter().GetResult();

                if (response.ResultType == RpcType.Proxy)
                {
                    return GetRemoteRepository(channel).GetProxyObject(GetRpcChannelForClient(channel), resultType, Convert.ToInt32(response.Result));
                }

                return response.Result;
            }
            catch (Exception ex)
            {
                throw new RpcFailedException($"Calling remote method {methodName} on object #{instanceId} failed.", ex);
            }
        }

        public void RemoveInstance(TChannel channel, int localInstanceId, int remoteInstanceId)
        {
            try
            {
                GetRemoteRepository(channel).RemoveInstance(localInstanceId);
                SendMessageAsync<RpcMessage>(channel, () => _messageFactory.CreateRemoveInstanceMessage(remoteInstanceId)).GetAwaiter().GetResult();
            }
            catch
            {
                // server not reachable, that's ok
            }
        }

        private async Task<TResult> WaitForMessageResultAsync<TResult>(TChannel channel, IRpcSerializer serializer, int callId)
            where TResult : RpcMessage
        {
            var re = new AsyncManualResetEvent(false);
            TResult result = default;
            RegisterMessageCallback(channel, (data) =>
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

        protected private void RegisterMessageCallback(TChannel channel, AsyncNotification.DataReceivedDelegate callback, bool autoremove)
        {
            lock (_messageNotifications)
            {
                if (!_messageNotifications.ContainsKey(channel))
                {
                    _messageNotifications.Add(channel, new AsyncNotification());
                }
                _messageNotifications[channel].Register(callback, autoremove);
            }
        }

        protected IRpcChannel GetRpcChannelForClient(TChannel channel)
        {
            return new RpcChannelWrapper(this, channel);
        }

        protected bool HandleRemoteMessage(TChannel channel, ReadOnlySpan<byte> data, RpcMessage msg)
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
                                    args[i] = GetRemoteRepository(channel).GetProxyObject(GetRpcChannelForClient(channel),
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
                        SendMessage(channel.GetStream(), response);
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
                        SendMessage(channel.GetStream(), response);
                        return true;
                    }
            }
            return false;
        }

        protected void RunReaderLoop(TChannel channel)
        {
            void NotifyMessage(byte[] data)
            {
                Task.Run(delegate
                {
                    try
                    {
                        //smallMessageBuffer
                        if (!_messageNotifications[channel].Notify(new ReadOnlySpan<byte>(data)))
                        {
                            Console.WriteLine("Failed to process message");
                        }

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed to run remote message");
                    }
                });
            }

            Task.Run(delegate
            {
                var reader = new BinaryReader(channel.GetStream(), Encoding.UTF8, true);
                var smallMessageBuffer = new byte[ushort.MaxValue];



                while (true)
                {
                    var type = (RpcChannelMessageType)reader.ReadByte();
                    switch (type)
                    {
                        case RpcChannelMessageType.LargeMessage:
                            {
                                var msgLen = reader.ReadInt32();
                                var data = reader.ReadBytes(msgLen);
                                NotifyMessage(data);
                                break;
                            }
                        case RpcChannelMessageType.Message:
                            {
                                var msgLen = reader.ReadUInt16();
                                int offset = 0;
                                while (offset < msgLen)
                                {
                                    offset += reader.Read(smallMessageBuffer, offset, msgLen - offset);
                                }

                                var copy = new byte[msgLen];
                                Array.Copy(smallMessageBuffer, copy, msgLen);

                                NotifyMessage(copy);

                                break;
                            }
                        default:
                            throw new NotSupportedException("Invalid message type encountered");
                    }
                }
            });
        }
    }
}
