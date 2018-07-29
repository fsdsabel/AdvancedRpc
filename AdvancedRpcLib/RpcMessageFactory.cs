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
                CallId = _callId++
            };
        }

        public RpcMethodCallMessage CreateMethodCallMessage(int instanceId, string methodName, object[] arguments)
        {
            return new RpcMethodCallMessage
            {
                Type = RpcMessageType.CallMethod,
                CallId = _callId++,
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
                CallId = _callId++,
                InstanceId = instanceId
            };
        }
    }

}
