using System;
using System.Threading;

namespace AdvancedRpcLib
{
    public class RpcObjectHandle
    {
        private static int IdCounter;

        private RpcObjectHandle(int instanceId)
        {
            InstanceId = instanceId;
        }

        public RpcObjectHandle(Type interfaceType, object obj)
        {
            InstanceId = Interlocked.Increment(ref IdCounter);
            Object = obj;
            InterfaceType = interfaceType;
        }

        public int InstanceId { get; }

        public object Object { get; internal set; }


        public Type InterfaceType { get; }

        public override int GetHashCode()
        {
            return InstanceId;
        }

        public override bool Equals(object obj)
        {
            if (obj is RpcObjectHandle)
            {
                return InstanceId == ((RpcObjectHandle)obj).InstanceId;
            }
            return base.Equals(obj);
        }

        public static RpcObjectHandle ComparisonHandle(int instanceId)
        {
            return new RpcObjectHandle(instanceId);
        }
    }

}
