using System;
using System.Threading.Tasks;

namespace AdvancedRpcLib
{
    public interface IRpcSerializer
    {

        byte[] SerializeMessage<T>(T message) where T : RpcMessage;


        T DeserializeMessage<T>(ReadOnlySpan<byte> data) where T : RpcMessage;
    }

    
    public interface IRpcChannel
    {
        object CallRpcMethod(int instanceId, string methodName, object[] args);
    }


    public interface IRpcServerChannel : IRpcChannel
    {
        Task ListenAsync();
    }

    public interface IRpcClientChannel : IRpcChannel
    {
        Task ConnectAsync();
        Task<TResult> GetServerObjectAsync<TResult>();
    }

    public interface IRpcMessageFactory
    {
        RpcGetServerObjectMessage CreateGetServerObjectMessage(string typeId);

        RpcMethodCallMessage CreateMethodCallMessage(int instanceId, string methodName, object[] arguments);
    }

    public interface IRpcObjectRepository
    {
        string CreateTypeId<T>();

        void RegisterSingleton<T>(object singleton);

        RpcObjectHandle GetObject(string typeId);

        T GetObject<T>(IRpcChannel channel, int instanceId);

        object GetInstance(int instanceId);

        void AddInstance<T>(object instance);
    }
}
