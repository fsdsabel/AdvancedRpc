using System;
using System.Linq;
using System.Threading;

namespace AdvancedRpcLib
{
    public class RpcMessageFactory : IRpcMessageFactory
    {
        private int _callId;

        public RpcGetServerObjectMessage CreateGetServerObjectMessage(string typeId)
        {
            return new RpcGetServerObjectMessage
            {
                Type = RpcMessageType.GetServerObject,
                TypeId = typeId,
                CallId = Interlocked.Increment(ref _callId) // wraps around
            };
        }

        public RpcMethodCallMessage CreateMethodCallMessage(IRpcObjectRepository localRepository,
            int instanceId, string methodName, Type[] argumentTypes, object[] arguments)
        {
            return new RpcMethodCallMessage
            {
                Type = RpcMessageType.CallMethod,
                CallId = Interlocked.Increment(ref _callId),
                MethodName = methodName,
                InstanceId = instanceId,
                Arguments = arguments.Select((a,idx) =>
                {
                    RpcType type = RpcType.Proxy;
                    if (a == null ||
                        a is IConvertible)
                    {
                        type = RpcType.Builtin;
                    }
                    else
                    {
                        a = localRepository.AddInstance(argumentTypes[idx], a).InstanceId;
                    }

                    return new RpcArgument
                    {
                        Type = type,
                        Value = a
                    };
                }).ToArray()
            };
        }

        public RpcRemoveInstanceMessage CreateRemoveInstanceMessage(int instanceId)
        {
            return new RpcRemoveInstanceMessage
            {
                Type = RpcMessageType.RemoveInstance,
                CallId = Interlocked.Increment(ref _callId),
                InstanceId = instanceId
            };
        }
    }

}
