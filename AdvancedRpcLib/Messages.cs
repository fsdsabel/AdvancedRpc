using System;
using System.Collections.Generic;
using System.Text;

namespace AdvancedRpcLib
{
    public enum RpcMessageType
    {
        Ok = 1,
        GetServerObject = 2,
        CallMethod = 3,
        RemoveInstance = 4,
        CallMethodResult = 5,
        Exception = 6
    }


    [Serializable]
    public class RpcMessage
    {
        public RpcMessageType Type { get; set; }
        public int CallId { get; set; }
    }

    [Serializable]
    public class RpcGetServerObjectMessage : RpcMessage
    {
        public string TypeId { get; set; }
    }

    [Serializable]
    public class RpcMethodCallMessage : RpcMessage
    {
        public int InstanceId { get; set; }

        public string MethodName { get; set; }

        public RpcArgument[] Arguments { get; set; }
    }

    [Serializable]
    public class RpcArgument
    {
        public object Value { get; set; }

        public RpcType Type { get; set; }

        public string TypeId { get; set; }
    }

    [Serializable]
    class RpcCallResultMessage : RpcMessage
    {
        public object Result { get; set; }

        public RpcType ResultType { get; set; }
    }


    [Serializable]
    public class RpcRemoveInstanceMessage : RpcMessage
    {
        public int InstanceId { get; set; }
    }

    public enum RpcType
    {
        Builtin = 0,
        Proxy = 1,
        Serialized = 2
    }

    [Serializable]
    class RpcGetServerObjectResponseMessage : RpcMessage
    {
        public int InstanceId { get; set; }
    }


    enum RpcChannelMessageType
    {
        Message = 0,
        LargeMessage = 1,
        Ping = 2,
        Pong = 3
    }
}
