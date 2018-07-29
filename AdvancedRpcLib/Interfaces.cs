﻿using System;
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
        object CallRpcMethod(int instanceId, string methodName, object[] args, Type resultType);

        void RemoveInstance(int localInstanceId, int remoteInstanceId);
    }


    public interface IRpcServerChannel : IRpcChannel
    {
        Task ListenAsync();

        IRpcObjectRepository ObjectRepository { get; }
    }

    public interface IRpcClientChannel : IRpcChannel
    {
        Task ConnectAsync();

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="RpcFailedException"></exception>
        /// <typeparam name="TResult"></typeparam>
        /// <returns></returns>
        Task<TResult> GetServerObjectAsync<TResult>();
    }

    public class RpcFailedException : Exception
    {
        public RpcFailedException() : base()
        {
        }

        protected RpcFailedException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context)
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

        RpcMethodCallMessage CreateMethodCallMessage(int instanceId, string methodName, object[] arguments);

        RpcRemoveInstanceMessage CreateRemoveInstanceMessage(int instanceId);
    }

    public interface IRpcObjectRepository
    {
        string CreateTypeId<T>();

        void RegisterSingleton<T>(object singleton);

        RpcObjectHandle GetObject(string typeId);

        T GetProxyObject<T>(IRpcChannel channel, int remoteInstanceId);

        object GetProxyObject(IRpcChannel channel, Type interfaceType, int remoteInstanceId);

        object GetInstance(int instanceId);

        RpcObjectHandle AddInstance(Type interfaceType, object instance);

        void RemoveInstance(int instanceId);
    }
}
