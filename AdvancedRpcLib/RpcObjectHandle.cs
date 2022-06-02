using System;
using System.Collections.Generic;
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
            IsSingleton = true;
            Pin(type);
            InterfaceTypes = new HashSet<Type>(type.GetInterfaces());
        }

        public RpcObjectHandle CreateObject()
        {
            var obj = Activator.CreateInstance(_type);
            return new RpcObjectHandle(obj, true, true, InstanceId);
        }
    }

    public class RpcHandle
    {
        //private static int _idCounter;
        //private static readonly object IdCounterLock = new object();
        private object _pin;

        

        protected RpcHandle() {}

        private RpcHandle(Guid instanceId)
        {
            InstanceId = instanceId;
        }

        public bool IsPinned => _pin != null;
        public Guid InstanceId { get; protected set; }

        public bool IsSingleton { get; protected set; }

        protected Guid CreateInstanceId(Guid? instanceId = null)
        {
            if(instanceId != null)
            {
                return instanceId.Value;
            }
            return Guid.NewGuid();
        }

        protected void Pin(object obj)
        {
            _pin = obj;
        }

        public HashSet<Type> InterfaceTypes { get; protected set; }

        public override int GetHashCode()
        {
            return InstanceId.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is RpcHandle)
            {
                return InstanceId == ((RpcHandle)obj).InstanceId;
            }
            return base.Equals(obj);
        }

        public static RpcHandle ComparisonHandle(Guid instanceId)
        {
            return new RpcHandle(instanceId);
        }
    }

    public sealed class RpcObjectHandle : RpcHandle
    {
        private readonly bool _pinned;


        public RpcObjectHandle(object obj, bool pinned = false, bool singleton = false, Guid? instanceId = null)
        {
            _pinned = pinned;
            IsSingleton = singleton;
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
                InterfaceTypes = new HashSet<Type>();
                if (value != null && value.TryGetTarget(out var o))
                {
                    if (o != null)
                    {
                        InterfaceTypes = new HashSet<Type>(o.GetType().GetInterfaces());
                    }
                    if (_pinned)
                    {
                        Pin(o);
                    }
                }
                _object = value;
                
            }
        }

        
        public ITransportChannel AssociatedChannel { get; set; }

    
    }
}
