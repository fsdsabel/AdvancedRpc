using System;
using System.Threading;

namespace AdvancedRpcLib
{
    public class RpcObjectHandle
    {
        private static int IdCounter;

        private object _pin;

        private RpcObjectHandle(int instanceId)
        {
            InstanceId = instanceId;
        }

        public RpcObjectHandle(Type interfaceType, object obj, bool pinned = false)
        {
            InstanceId = Interlocked.Increment(ref IdCounter);
            Object = new WeakReference<object>(obj);
            InterfaceType = interfaceType;
            if(pinned)
            {
                // prevent from garbage collection as long as the handle exists
                _pin = obj;
            }
        }

        public bool IsPinned => _pin != null;

        public int InstanceId { get; }

        public WeakReference<object> Object { get; internal set; }


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
