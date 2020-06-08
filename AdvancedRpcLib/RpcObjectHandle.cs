using System;
using System.Threading;
using AdvancedRpcLib.Channels;

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

        public RpcObjectHandle(object obj, bool pinned = false)
        {
            InstanceId = Interlocked.Increment(ref IdCounter); // this wraps around automatically
            Object = new WeakReference<object>(obj);
                        
            if(pinned)
            {
                // prevent from garbage collection as long as the handle exists
                _pin = obj;
            }
        }

        public bool IsPinned => _pin != null;

        public int InstanceId { get; }

        private WeakReference<object> _object;
        public WeakReference<object> Object {
            get => _object;
            internal set
            {
                InterfaceTypes = new Type[0];
                if (value != null && value.TryGetTarget(out var o))
                {
                    if (o != null)
                    {
                        InterfaceTypes = o.GetType().GetInterfaces();
                    }
                }
                _object = value;
            }
        }

        public Type[] InterfaceTypes { get; private set; }
        public ITransportChannel AssociatedChannel { get; set; }

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
