using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using AdvancedRpcLib.Helpers;
using Microsoft.Extensions.Logging;

namespace AdvancedRpcLib.Channels
{
    public interface ITransportChannel : IDisposable
    {
        Stream GetStream();
    }

    public enum RpcChannelType
    {
        Client,
        Server
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
        protected readonly IRpcSerializer Serializer;
        protected readonly IRpcObjectRepository LocalRepository;
        private readonly Func<IRpcObjectRepository> _remoteRepository;
        private readonly Dictionary<TChannel, IRpcObjectRepository> _remoteRepositories = new Dictionary<TChannel, IRpcObjectRepository>();
        protected readonly IRpcMessageFactory MessageFactory;
        private readonly ILogger<RpcChannel<TChannel>> _logger;

        protected RpcChannel(IRpcSerializer serializer,
           IRpcMessageFactory messageFactory,
           RpcChannelType channelType,
           IRpcObjectRepository localRepository = null,
           Func<IRpcObjectRepository> remoteRepository = null,
           ILoggerFactory loggerFactory = null)
        {
            MessageFactory = messageFactory;
            Serializer = serializer;
            _logger = loggerFactory?.CreateLogger<RpcChannel<TChannel>>();
            _remoteRepository = remoteRepository ?? (() => new RpcObjectRepository(channelType == RpcChannelType.Server));
            LocalRepository = localRepository ?? new RpcObjectRepository(channelType == RpcChannelType.Client);
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
            var waitTask = WaitForMessageResultAsync<TResult>(channel, Serializer, callId);
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
            var serializedMsg = Serializer.SerializeMessage(msg);
            return SendMessageAsync<T>(channel, serializedMsg, msg.CallId);
        }

        protected object CallRpcMethod(TChannel channel,
            int instanceId, string methodName, Type[] argTypes, object[] args, Type resultType)
        {
            RpcCallResultMessage response;
            try
            {
                response = SendMessageAsync<RpcCallResultMessage>(channel,
                        () => MessageFactory.CreateMethodCallMessage(LocalRepository, instanceId, methodName,
                            argTypes, args))
                    .GetAwaiter().GetResult();

                if (response.ResultType == RpcType.Proxy)
                {
                    return GetRemoteRepository(channel).GetProxyObject(GetRpcChannelForClient(channel), resultType,
                        Convert.ToInt32(response.Result));
                }
            }
            catch (Exception ex)
            {
                throw new RpcFailedException($"Calling remote method {methodName} on object #{instanceId} failed.", ex);
            }

            if (response.Type == RpcMessageType.Exception)
            {
                throw (Exception) response.Result;
            }

            return response.Result;
        }

        public void RemoveInstance(TChannel channel, int localInstanceId, int remoteInstanceId)
        {
            try
            {
                GetRemoteRepository(channel).RemoveInstance(localInstanceId);
                SendMessageAsync<RpcMessage>(channel, () => MessageFactory.CreateRemoveInstanceMessage(remoteInstanceId)).GetAwaiter().GetResult();
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

        private protected void RegisterMessageCallback(TChannel channel, AsyncNotification.DataReceivedDelegate callback, bool autoremove)
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
                    RpcCallResultMessage resultMessage;
                    var m = Serializer.DeserializeMessage<RpcMethodCallMessage>(data);
                    try
                    {
                        var obj = LocalRepository.GetInstance(m.InstanceId);

                        var targetMethod = obj.GetType().GetMethod(m.MethodName);
                        if (targetMethod == null)
                        {
                            throw new MissingMethodException(obj.GetType().FullName, m.MethodName);
                        }
                        var targetParameterTypes = targetMethod.GetParameters().Select(p => p.ParameterType).ToArray();
                        var args = new object[m.Arguments.Length];
                        for (int i = 0; i < m.Arguments.Length; i++)
                        {
                            switch (m.Arguments[i].Type)
                            {
                                case RpcType.Builtin:
                                    args[i] = Serializer.ChangeType(m.Arguments[i].Value, targetParameterTypes[i]);
                                    break;
                                case RpcType.Proxy:
                                    args[i] = GetRemoteRepository(channel).GetProxyObject(
                                        GetRpcChannelForClient(channel),
                                        targetParameterTypes[i],
                                        (int)Serializer.ChangeType(m.Arguments[i].Value, typeof(int)));
                                    break;
                                case RpcType.Serialized:
                                    var type = Type.GetType(m.Arguments[i].TypeId);
                                    /*args[i] = Serializer.DeserializeObject(Serializer.SerializeObject(m.Arguments[i].Value),
                                        type);*/
                                    args[i] = Serializer.ChangeType(m.Arguments[i].Value, type);
                                    break;
                                default:
                                    throw new InvalidDataException();
                            }
                        }

                        var result = targetMethod.Invoke(obj, args);

                        resultMessage = new RpcCallResultMessage
                        {
                            CallId = m.CallId,
                            Type = RpcMessageType.CallMethodResult,
                            Result = result
                        };

                        if (targetMethod.ReturnType.IsInterface)
                        {
                            // create a proxy
                            var handle = LocalRepository.AddInstance(targetMethod.ReturnType, result);
                            resultMessage.ResultType = RpcType.Proxy;
                            resultMessage.Result = handle.InstanceId;
                        }
                    }
                    catch (TargetInvocationException ex)
                    {
                        resultMessage = new RpcCallResultMessage
                        {
                            CallId = m.CallId,
                            Type = RpcMessageType.Exception,
                            ResultType = RpcType.Serialized,
                            Result = ex.InnerException 
                        };
                    }
                    catch (Exception ex)
                    {
                        resultMessage = new RpcCallResultMessage
                        {
                            CallId = m.CallId,
                            Type = RpcMessageType.Exception,
                            ResultType = RpcType.Serialized,
                            Result = ex
                        };
                    }
                    
                    var response = Serializer.SerializeMessage(resultMessage);
                    SendMessage(channel.GetStream(), response);
                    return true;
                }
                case RpcMessageType.RemoveInstance:
                    {
                        var m = Serializer.DeserializeMessage<RpcRemoveInstanceMessage>(data);
                        LocalRepository.RemoveInstance(m.InstanceId);

                        var response = Serializer.SerializeMessage(new RpcMessage
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

        protected void RunReaderLoop(TChannel channel, Action onDone)
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
                            _logger?.LogError($"Failed to process message.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to run remote message");
                    }
                });
            }

            Task.Run(delegate
            {
                try
                {
                    var reader = new BinaryReader(channel.GetStream(), Encoding.UTF8, true);
                    var smallMessageBuffer = new byte[ushort.MaxValue];

                    while (true)
                    {
                        var type = (RpcChannelMessageType) reader.ReadByte();
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
                }
                catch(Exception ex) when (ex is EndOfStreamException || 
                                          ex is ObjectDisposedException || 
                                          ex is InvalidOperationException ||
                                          ex is IOException)
                {
                    _logger?.LogTrace("Remote channel closed connection.");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Reader loop failed.");
                }
                finally
                {
                    onDone();
                }
            });
        }
    }
}
