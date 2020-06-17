using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using AdvancedRpcLib.Channels;

namespace AdvancedRpcLib
{
    public interface IRpcSerializer
    {
        byte[] SerializeMessage<T>(T message) where T : RpcMessage;

        T DeserializeMessage<T>(byte[] data) where T : RpcMessage;

        object ChangeType(object value, Type targetType);
    }

    public interface IRpcChannel
    {
        object CallRpcMethod(int instanceId, string methodName, Type[] argTypes, object[] args, Type resultType);

        void RemoveInstance(int localInstanceId, int remoteInstanceId);
    }

    public interface IRpcServerChannel : IDisposable
    {
        Task ListenAsync();

        IRpcObjectRepository ObjectRepository { get; }
    }

    public interface IRpcServerChannel<out TChannel> : IRpcServerChannel
        where TChannel : class, ITransportChannel
    {
        IReadOnlyCollection<TChannel> ConnectedChannels { get; }
    }

    public interface IRpcServerContextObject
    {
        ITransportChannel RpcChannel { set; }
    }

    public interface IRpcClientChannel : IDisposable
    {
        Task ConnectAsync(TimeSpan timeout = default);

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="RpcFailedException"></exception>
        /// <typeparam name="TResult"></typeparam>
        /// <returns></returns>
        Task<TResult> GetServerObjectAsync<TResult>();

        IRpcObjectRepository ObjectRepository { get; }
    }

    public class RpcFailedException : Exception
    {
        public RpcFailedException() 
        {
        }

        public RpcFailedException(string message) : base(message)
        {
        }

        public RpcFailedException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    public interface IRpcMessageFactory
    {
        RpcGetServerObjectMessage CreateGetServerObjectMessage(string typeId);

        RpcMethodCallMessage CreateMethodCallMessage(ITransportChannel channel, IRpcObjectRepository localRepository,
            int instanceId, string methodName, Type[] argumentTypes, object[] arguments);

        RpcCallResultMessage CreateCallResultMessage(ITransportChannel channel, IRpcObjectRepository localRepository,
            RpcMethodCallMessage call, MethodInfo calledMethod, object result);

        RpcCallResultMessage CreateExceptionResultMessage(RpcMethodCallMessage call, Exception exception);
        RpcRemoveInstanceMessage CreateRemoveInstanceMessage(int instanceId);

        object DecodeRpcCallResultMessage(IRpcChannel channel, IRpcObjectRepository remoteRepository,
            IRpcSerializer serializer, RpcCallResultMessage message, Type resultType);

        object DecodeRpcArgument(IRpcChannel channel, IRpcObjectRepository remoteRepository,
            IRpcSerializer serializer, RpcArgument argument, Type argumentType);
    }

    public interface IRpcObjectRepository
    {
        string CreateTypeId<T>();

        string CreateTypeId(object obj);

        void RegisterSingleton(object singleton);

        void RegisterSingleton<T>();

        RpcObjectHandle GetObject(string typeId);

        T GetProxyObject<T>(IRpcChannel channel, int remoteInstanceId);

        object GetProxyObject(IRpcChannel channel, Type interfaceType, int remoteInstanceId);

        object GetInstance(int instanceId);

        RpcObjectHandle AddInstance(Type interfaceType, object instance, ITransportChannel associatedChannel = null);

        void RemoveInstance(int instanceId);

        void RemoveAllForChannel(ITransportChannel channel);

        bool AllowNonPublicInterfaceAccess { get; set; }
    }
}
