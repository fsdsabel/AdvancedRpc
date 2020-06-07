using System;
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

        RpcMethodCallMessage CreateMethodCallMessage(IRpcObjectRepository localRepository,
            int instanceId, string methodName, Type[] argumentTypes, object[] arguments);

        RpcRemoveInstanceMessage CreateRemoveInstanceMessage(int instanceId);
    }

    public interface IRpcObjectRepository
    {
        string CreateTypeId<T>();

        string CreateTypeId(object obj);

        void RegisterSingleton(object singleton);

        RpcObjectHandle GetObject(string typeId);

        T GetProxyObject<T>(IRpcChannel channel, int remoteInstanceId);

        object GetProxyObject(IRpcChannel channel, Type interfaceType, int remoteInstanceId);

        object GetInstance(int instanceId);

        RpcObjectHandle AddInstance(Type interfaceType, object instance);

        void RemoveInstance(int instanceId);
    }
}
