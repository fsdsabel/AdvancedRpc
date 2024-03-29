﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AdvancedRpcLib.Helpers;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Exception = System.Exception;

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

    public abstract class RpcChannel<TChannel> : IDisposable where TChannel:ITransportChannel 
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

            public ITransportChannel Channel => _channel;

            private static object _lock = new object();
            private void Log(string msg)
            {
                //lock (_lock)
                {
                   // Debug.WriteLine($"{DateTime.Now:hh:mm:ss.fff} - {msg}");
                }
            }

            public object CallRpcMethod(Guid instanceId, string methodName, Type[] argTypes, object[] args, Type resultType)
            {
                try
                {
                    Log($"enter {methodName} ({instanceId})");
                    return _rpcChannel.CallRpcMethod(_channel, instanceId, methodName, argTypes, args, resultType);
                }
                catch (Exception ex)
                {
                    Log("EXCEPTION: "+ex.ToString());
                    throw;
                }
                finally
                {
                    Log($"exit {methodName} ({instanceId})");
                }
            }

            public void RemoveInstance(Guid localInstanceId, Guid remoteInstanceId)
            {
                Log($"remove local:{localInstanceId} remote:{remoteInstanceId}");
                _rpcChannel.RemoveInstance(_channel, localInstanceId, remoteInstanceId);
            }
        }

        class ChannelInfo
        {
            public ChannelInfo(IRpcObjectRepository repository)
            {
                Repository = repository;
            }

            public IRpcObjectRepository Repository { get; }

            public CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();
        }

        private readonly Dictionary<TChannel, AsyncNotification> _messageNotifications = new Dictionary<TChannel, AsyncNotification>();
        private readonly object _sendLock = new object();
        protected readonly IRpcSerializer Serializer;
        protected readonly IRpcObjectRepository LocalRepository;
        private readonly Func<IRpcObjectRepository> _remoteRepository;
        private readonly Dictionary<TChannel, ChannelInfo> _channelInfos = new Dictionary<TChannel, ChannelInfo>();
        protected readonly IRpcMessageFactory MessageFactory;
        private readonly RpcChannelType _channelType;
        private readonly ILogger<RpcChannel<TChannel>> _logger;
        private readonly ILogger<AsyncNotification> _asyncNotificationLogger;
        private bool _disposedValue;

        protected RpcChannel(IRpcSerializer serializer,
           IRpcMessageFactory messageFactory,
           RpcChannelType channelType,
           IRpcObjectRepository localRepository = null,
           Func<IRpcObjectRepository> remoteRepository = null,
           ILoggerFactory loggerFactory = null)
        {
            MessageFactory = messageFactory;
            _channelType = channelType;
            Serializer = serializer;
            _logger = loggerFactory?.CreateLogger<RpcChannel<TChannel>>();
            _asyncNotificationLogger = loggerFactory?.CreateLogger<AsyncNotification>();
            _remoteRepository = remoteRepository ?? (() => new RpcObjectRepository(channelType == RpcChannelType.Server));
            LocalRepository = localRepository ?? new RpcObjectRepository(channelType == RpcChannelType.Client);
        }

        protected IRpcObjectRepository GetRemoteRepository(TChannel channel)
        {
            lock (_channelInfos)
            {
                if (!_channelInfos.ContainsKey(channel))
                {
                    _channelInfos.Add(channel, new ChannelInfo(_remoteRepository()));
                }
                return _channelInfos[channel].Repository;
            }
        }

        private CancellationTokenSource GetCancellationTokenSource(TChannel channel)
        {
            lock (_channelInfos)
            {
                if (!_channelInfos.ContainsKey(channel))
                {
                    _channelInfos.Add(channel, new ChannelInfo(_remoteRepository()));
                }
                return _channelInfos[channel].CancellationTokenSource;
            }
        }

        protected Task<TResult> SendMessageAsync<TResult>(TChannel channel, byte[] msg, int callId)
            where TResult : RpcMessage
        {
            var waitTask = WaitForMessageResultAsync<TResult>(channel, Serializer, callId, 
                GetCancellationTokenSource(channel).Token);
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
                
                if (msg.Length <= ushort.MaxValue)
                {
                    stream.WriteByte((byte) RpcChannelMessageType.Message);
                    var len = BitConverter.GetBytes((ushort)msg.Length);
                    stream.Write(len, 0, len.Length);
                }
                else
                {
                    stream.WriteByte((byte) RpcChannelMessageType.LargeMessage);
                    var len = BitConverter.GetBytes(msg.Length);
                    stream.Write(len, 0, len.Length);
                }

                stream.Write(msg, 0, msg.Length);
            }
        }

        protected Task<T> SendMessageAsync<T>(TChannel channel, Func<RpcMessage> msgFunc) where T : RpcMessage
        {
            var msg = msgFunc();
            var serializedMsg = Serializer.SerializeMessage(msg);
            return SendMessageAsync<T>(channel, serializedMsg, msg.CallId);
        }

        protected object CallRpcMethod(TChannel channel,
            Guid instanceId, string methodName, Type[] argTypes, object[] args, Type resultType)
        {
            RpcCallResultMessage response;
            try
            {
                LogTrace($"Calling remote method '{methodName}' on object '{instanceId}'");

                response = Task.Run(async () =>
                    {
                        return await SendMessageAsync<RpcCallResultMessage>(channel,
                            () => MessageFactory.CreateMethodCallMessage(channel, LocalRepository, instanceId,
                                methodName,
                                argTypes, args));
                    })
                    .GetAwaiter().GetResult();

                LogTrace($"Received response for calling '{methodName}' on object '{instanceId}'");

                return MessageFactory.DecodeRpcCallResultMessage(GetRpcChannelForClient(channel),
                    LocalRepository, GetRemoteRepository(channel), Serializer, response, resultType);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
            catch (Exception ex)
            {
                throw new RpcFailedException($"Calling remote method {methodName} on object #{instanceId} failed.", ex);
            }
        }

        public void RemoveInstance(TChannel channel, Guid localInstanceId, Guid remoteInstanceId)
        {
            try
            {
                GetRemoteRepository(channel).RemoveInstance(localInstanceId, TimeSpan.Zero);
                SendMessageAsync<RpcMessage>(channel, () => MessageFactory.CreateRemoveInstanceMessage(remoteInstanceId)).GetAwaiter().GetResult();
            }
            catch
            {
                // server not reachable, that's ok
            }
        }

        private async Task<TResult> WaitForMessageResultAsync<TResult>(TChannel channel, IRpcSerializer serializer, int callId,
            CancellationToken cancellationToken)
            where TResult : RpcMessage
        {
            var re = new AsyncManualResetEvent(false);
            using (cancellationToken.Register(() =>
            {
                re.Set();
            }))
            {
                TResult result = default;
                RegisterMessageCallback(channel, (data, bareMsg) =>
                {
                    if (bareMsg.CallId == callId)
                    {
                        if (bareMsg is TResult msg)
                        {
                            result = msg;
                        }
                        else
                        {
                            result = serializer.DeserializeMessage<TResult>(data);
                        }

                        re.Set();
                        return true;
                    }

                    return false;
                }, true);

                await re.WaitAsync();
                return result;
            }
        }

        private protected void RegisterMessageCallback(TChannel channel, AsyncNotification.DataReceivedDelegate callback, bool autoremove)
        {
            lock (_messageNotifications)
            {
                if (!_messageNotifications.ContainsKey(channel))
                {
                    _messageNotifications.Add(channel, new AsyncNotification(_asyncNotificationLogger));
                }
                _messageNotifications[channel].Register(callback, autoremove);
            }
        }

        protected IRpcChannel GetRpcChannelForClient(TChannel channel)
        {
            return new RpcChannelWrapper(this, channel);
        }

        private void LogTrace(string message)
        {
            _logger?.LogTrace(message);
        }

        protected bool HandleRemoteMessage(TChannel channel, byte[] data, RpcMessage msg)
        {
            switch (msg.Type)
            {
                case RpcMessageType.CallMethod:
                {
                    Exception resultException = null;
                    RpcCallResultMessage resultMessage;
                    IRpcServerContextObject remoteRpcServerContextObject = null;
                    var m = msg as RpcMethodCallMessage ?? Serializer.DeserializeMessage<RpcMethodCallMessage>(data);
                    try
                    {
                        LogTrace($"Received method call '{m.MethodName}' with instance id '{m.InstanceId}'");
                            
                        var obj = LocalRepository.GetInstance(m.InstanceId);

                        MethodInfo targetMethod;
                        try
                        {
                            targetMethod = obj.GetType().GetMethod(m.MethodName,
                                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        }
                        catch (AmbiguousMatchException)
                        {
                            targetMethod = obj.GetType()
                                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                                .SingleOrDefault(fm =>
                                    fm.Name == m.MethodName && fm.GetParameters().Length == m.Arguments.Length);
                        }

                        if (targetMethod == null)
                        {
                            targetMethod = FindExplicitInterfaceImplementation(obj.GetType(), m.MethodName);
                        }

                        if (targetMethod == null)
                        {
                            throw new MissingMethodException(obj.GetType().FullName, m.MethodName);
                        }

                        LogTrace($"Resolved method '{targetMethod}' on object '{obj.GetType()}'");

                        var targetParameterTypes = targetMethod.GetParameters().Select(p => p.ParameterType).ToArray();
                        var args = new object[m.Arguments.Length];
                        var remoteRepository = GetRemoteRepository(channel);
                        var rpcChannel = GetRpcChannelForClient(channel);
                        for (int i = 0; i < m.Arguments.Length; i++)
                        {
                            args[i] = MessageFactory.DecodeRpcArgument(rpcChannel, remoteRepository, LocalRepository,
                                Serializer, m.Arguments[i], targetParameterTypes[i]);
                        }


                        remoteRpcServerContextObject = obj as IRpcServerContextObject;
                        if (remoteRpcServerContextObject != null)
                        {
                            LogTrace($"Object {m.InstanceId} implements IRpcServerContextObject, setting context");
                            remoteRpcServerContextObject.RpcChannel = channel;
                        }

                        var result = targetMethod.Invoke(obj, args);

                        LogTrace("Method called without exception.");

                        resultMessage = MessageFactory.CreateCallResultMessage(channel, LocalRepository, m, targetMethod, result);

                    }
                    catch (TargetInvocationException ex)
                    {
                        LogTrace($"Method call resulted in exception: {ex}");
                        resultException = ex.InnerException;
                        resultMessage = MessageFactory.CreateExceptionResultMessage(m, ex.InnerException);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"Failed to process message call: {ex}");
                        resultException = ex;
                        resultMessage = MessageFactory.CreateExceptionResultMessage(m, ex);
                    }
                    finally
                    {
                        if (remoteRpcServerContextObject != null)
                        {
                            LogTrace($"Object {m.InstanceId} implements IRpcServerContextObject, removing context"); 
                            remoteRpcServerContextObject.RpcChannel = null;
                        }
                    }

                    LogTrace("Serializing response.");
                    byte[] response;
                    try
                    {
                        response = Serializer.SerializeMessage(resultMessage);
                    }
                    catch(Exception ex)
                    {
                        if (resultMessage.Type == RpcMessageType.Exception)
                        {
                            _logger?.LogError(ex, $"Failed to serialize result exception of type '{resultException?.GetType()}'");
                            // if the exception is not serializable, do our best
                            resultMessage = MessageFactory.CreateExceptionResultMessage(m,
                                new Exception(resultException?.Message ?? "Internal Error"));
                            response = Serializer.SerializeMessage(resultMessage);
                        }
                        else
                        {
                            throw;
                        }
                    }

                    LogTrace("Sending response.");
                    SendMessage(channel.GetStream(), response);
                    LogTrace("Sent response");
                    return true;
                }
                case RpcMessageType.RemoveInstance:
                    {
                        var m = Serializer.DeserializeMessage<RpcRemoveInstanceMessage>(data);
                        // sometimes we can get into a race condition with garbage collection and instance method calls
                        // give some additional time to the object lifetime, so we still have access to the object for some time
                        LogTrace($"Removing instance '{m.InstanceId}' with delay (60s)");
                        LocalRepository.RemoveInstance(m.InstanceId, TimeSpan.FromSeconds(60));

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

        private MethodInfo FindExplicitInterfaceImplementation(Type type, string methodName)
        {
            var method = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.IsPrivate && m.IsFinal && m.Name.EndsWith("." + methodName)); // explicit interface implementation
            return method;
        }

        protected void RunReaderLoop(TChannel channel, Action onDone)
        {
            void NotifyMessage(byte[] data)
            {
                Task.Run(delegate
                {
                    try
                    {
                        if (!_disposedValue)
                        {
                            // ReSharper disable once InconsistentlySynchronizedField
                            if (!_messageNotifications[channel].Notify(data, Serializer))
                            {
                                _logger?.LogError($"Failed to process message.");
                            }
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
                    var stream = channel.GetStream();
                    byte[] lengthBytes = new byte[4];

                    while (!_disposedValue)
                    {
                        var type = (RpcChannelMessageType) stream.ReadByte();
                        if (_disposedValue)
                        {
                            // we got disposed while waiting
                            break;
                        }
                        switch (type)
                        {
                            case RpcChannelMessageType.LargeMessage:
                            {
                                stream.Read(lengthBytes, 0, 4);
                                var msgLen = BitConverter.ToInt32(lengthBytes, 0);

                                var data = ReadBytes(stream, msgLen);
                                NotifyMessage(data);
                                break;
                            }
                            case RpcChannelMessageType.Message:
                            {
                                stream.Read(lengthBytes, 0, 2);
                                var msgLen = BitConverter.ToUInt16(lengthBytes, 0);
                                var data = ReadBytes(stream, msgLen);
                                NotifyMessage(data);
                                break;
                            }
                            case (RpcChannelMessageType) (-1):
                                // stream closed
                                _logger?.LogTrace("Remote channel closed connection.");
                                return;
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

        
        private byte[] ReadBytes(Stream stream, int length)
        {
            int offset = 0;
            var result = new byte[length];
            while (offset < length)
            {
                offset += stream.Read(result, offset, length - offset);
                if (!IsConnected(stream))
                {
                    throw new IOException("Cannot read from disconnected stream.");
                }
            }

            return result;
        }

        protected void CancelRequests(TChannel channel)
        {
            GetCancellationTokenSource(channel).Cancel();
            lock (_channelInfos)
            {
                _channelInfos.Remove(channel);
            }
        }

        protected virtual bool IsConnected(Stream stream) => true;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                _disposedValue = true;
            }
        }
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
