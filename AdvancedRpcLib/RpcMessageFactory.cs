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

        public RpcMethodCallMessage CreateMethodCallMessage(int instanceId, string methodName, object[] arguments)
        {
            return new RpcMethodCallMessage
            {
                Type = RpcMessageType.CallMethod,
                CallId = Interlocked.Increment(ref _callId),
                MethodName = methodName,
                InstanceId = instanceId,
                Arguments = arguments
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
