using System;
using System.Collections.Generic;
using System.Text;

namespace AdvancedRpcLib
{
    public enum RpcMessageType
    {
        GetServerObject = 1,
        CallMethod = 2
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

        public object[] Arguments { get; set; }
    }

    [Serializable]
    class RpcCallResultMessage : RpcMessage
    {
        public object Result { get; set; }
    }

    [Serializable]
    class RpcGetServerObjectResponseMessage : RpcMessage
    {
        public int InstanceId { get; set; }
    }


    enum TcpRpcChannelMessageType
    {
        Message = 0,
        Ping = 1,
        Pong = 2
    }
}
