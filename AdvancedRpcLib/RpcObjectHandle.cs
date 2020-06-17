using System;
using System.Linq;
using System.Threading;
using AdvancedRpcLib.Channels;

namespace AdvancedRpcLib
{
    public sealed class RpcObjectTypeHandle : RpcHandle
    {
        private Type _type;

        public RpcObjectTypeHandle(Type type)
        {
            InstanceId = CreateInstanceId(); 
            _type = type;
            Pin(type);
            InterfaceTypes = type.GetInterfaces();
        }

        public RpcObjectHandle CreateObject()
        {
            var obj = Activator.CreateInstance(_type);
            return new RpcObjectHandle(obj, true, InstanceId);
        }
    }

    public class RpcHandle
    {
        private static int _idCounter;
        private static readonly object IdCounterLock = new object();
        private object _pin;

        protected RpcHandle() {}

        private RpcHandle(int instanceId)
        {
            InstanceId = instanceId;
        }

        public bool IsPinned => _pin != null;
        public int InstanceId { get; protected set; }

        protected int CreateInstanceId(int? instanceId = null)
        {
            lock (IdCounterLock)
            {
                if (instanceId != null)
                {
                    _idCounter = Math.Max(unchecked(_idCounter + 1), instanceId.Value);
                    return instanceId.Value;
                }
                return unchecked(++_idCounter);
            }
        }

        protected void Pin(object obj)
        {
            _pin = obj;
        }

        public Type[] InterfaceTypes { get; protected set; }

        public override int GetHashCode()
        {
            return InstanceId;
        }

        public override bool Equals(object obj)
        {
            if (obj is RpcHandle)
            {
                return InstanceId == ((RpcHandle)obj).InstanceId;
            }
            return base.Equals(obj);
        }

        public static RpcHandle ComparisonHandle(int instanceId)
        {
            return new RpcHandle(instanceId);
        }
    }

    public sealed class RpcObjectHandle : RpcHandle
    {
        
        public RpcObjectHandle(object obj, bool pinned = false, int? instanceId = null)
        {
            InstanceId = CreateInstanceId(instanceId); 
            
            Object = new WeakReference<object>(obj);
                        
            if(pinned)
            {
                // prevent from garbage collection as long as the handle exists
                Pin(obj);
            }
        }


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

        
        public ITransportChannel AssociatedChannel { get; set; }

    
    }
}
