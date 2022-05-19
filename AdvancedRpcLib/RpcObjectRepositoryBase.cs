using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AdvancedRpcLib.Channels;
using AdvancedRpcLib.Helpers;
using Microsoft.Extensions.Logging;

namespace AdvancedRpcLib
{
    public abstract class RpcObjectRepositoryBase : IRpcObjectRepository
    {
        private readonly bool _clientRepository;
        protected readonly HashSet<RpcHandle> _rpcObjects = new HashSet<RpcHandle>();
        private readonly ConcurrentDictionary<int, DateTime> _instancesToRemoveDelayed = new ConcurrentDictionary<int, DateTime>();
        
        protected RpcObjectRepositoryBase(bool clientRepository)
        {
            _clientRepository = clientRepository;
        }

        public bool AllowNonPublicInterfaceAccess { get; set; }

        public string CreateTypeId<T>()
        {
            return CreateTypeId(typeof(T));
        }

        public string CreateTypeId(object obj)
        {
            return CreateTypeId(obj.GetType());
        }

        public virtual Type[] ResolveTypes(string typeId, Type localType)
        {
            return typeId.Split(';')
                .Select(t => Type.GetType(t) ?? localType)
                .Distinct()
                .ToArray();
        }

        public virtual string CreateTypeId(Type type)
        {
            if (type.IsArray || type.IsValueType || type.IsSubclassOf(typeof(Delegate)))
            {
                return type.AssemblyQualifiedName;
            }

            return string.Join(";",
                (type.IsInterface ? new[] { type.AssemblyQualifiedName } : new string[0])
                    .Concat(type.GetInterfaces()
                    .Select(i => i.AssemblyQualifiedName)));
        }

        protected void Purge()
        {
            //   if (_clientRepository) // never purge on server side, they are only removed by explicit client calls
            {
                lock (_rpcObjects)
                {
                    foreach(var instanceToRemoveDelayed in _instancesToRemoveDelayed.ToArray())
                    {
                        if(instanceToRemoveDelayed.Value <= DateTime.Now)
                        {
                            if (_instancesToRemoveDelayed.TryRemove(instanceToRemoveDelayed.Key, out _))
                            {
                                RemoveInstance(instanceToRemoveDelayed.Key, TimeSpan.Zero, false);
                            }                            
                        }
                    }

                    foreach (var o in _rpcObjects.OfType<RpcObjectHandle>().ToArray())
                    {
                        if (!o.Object.TryGetTarget(out var _))
                        {
                            _rpcObjects.Remove(o);
                        }
                    }
                }
            }
        }

        private RpcObjectHandle CreateObjectHandleFromTypeHandle(RpcObjectTypeHandle handle)
        {
            lock (_rpcObjects)
            {
                var created = handle.CreateObject();
                _rpcObjects.Remove(handle);
                _rpcObjects.Add(created);
                return created;
            }
        }

        public RpcObjectHandle GetObject(string typeId)
        {
            lock (_rpcObjects)
            {
                Purge();
                var objTypes = ResolveTypes(typeId, null);
                foreach (var obj in _rpcObjects)
                {
                    foreach (var objType in objTypes)
                    {
                        if (obj.InterfaceTypes.TryGetValue(objType, out _))
                        {
                            if (obj is RpcObjectHandle oh)
                            {
                                return oh;
                            }

                            // a type was registered - create lazily
                            return CreateObjectHandleFromTypeHandle((RpcObjectTypeHandle)obj);
                        }
                    }
                }
            }
            return null;
        }

        public void RegisterSingleton(object singleton)
        {
            lock (_rpcObjects)
            {
                Purge();
                var v = new RpcObjectHandle(singleton, true, true);
                _rpcObjects.Add(v);
            }
        }

        public void RegisterSingleton<T>()
        {
            lock (_rpcObjects)
            {
                var v = new RpcObjectTypeHandle(typeof(T));
                _rpcObjects.Add(v);
            }
        }

        public RpcObjectHandle AddInstance(Type interfaceType, object instance, ITransportChannel associatedChannel = null)
        {
            if (!_clientRepository && associatedChannel == null) throw new ArgumentNullException(nameof(associatedChannel));
            lock (_rpcObjects)
            {
                Purge();
                var existing = _rpcObjects.OfType<RpcObjectHandle>().FirstOrDefault(o =>
                {
                    if (o.Object.TryGetTarget(out var obj))
                    {
                        if (instance is Delegate && obj is Delegate)
                        {
                            // special handling for delegates
                            return obj.Equals(instance);
                        }

                        return ReferenceEquals(obj, instance);
                    }
                    return false;
                });
                if (existing == null)
                {
                    var v = new RpcObjectHandle(instance, /*!_clientRepository*/true); // always pin on server side as we never know when the client needs us again
                    v.AssociatedChannel = associatedChannel;
                    _rpcObjects.Add(v);
                    return v;
                }
                return existing;
            }
        }

        public object GetInstance(int instanceId)
        {
            lock (_rpcObjects)
            {
                Purge();
                if (_rpcObjects.TryGetValue(RpcHandle.ComparisonHandle(instanceId), out var obj))
                {
                    if (obj is RpcObjectHandle objectHandle)
                    {
                        if (objectHandle.Object.TryGetTarget(out var instance))
                        {
                            return instance;
                        }
                    }
                    else
                    {
                        return GetInstance(CreateObjectHandleFromTypeHandle((RpcObjectTypeHandle)obj).InstanceId);
                    }
                }
            }
            return null;
        }

        public void RemoveInstance(int instanceId, TimeSpan delay)
        {
            RemoveInstance(instanceId, delay, true);
        }

        private void RemoveInstance(int instanceId, TimeSpan delay, bool purge)
        {
            lock (_rpcObjects)
            {
                if (purge)
                {
                    Purge();
                }
                if (delay > TimeSpan.Zero)
                {
                    _instancesToRemoveDelayed.AddOrUpdate(instanceId, DateTime.Now + delay, (id, date) => DateTime.Now + delay);
                    return;
                }
                var ch = RpcHandle.ComparisonHandle(instanceId);
                var toRemove = _rpcObjects.FirstOrDefault(o => !o.IsSingleton /*(_clientRepository || !o.IsPinned)*/ && o.Equals(ch));
                if (toRemove != null)
                {
                    _rpcObjects.Remove(toRemove);
                }
            }
        }

        public void RemoveAllForChannel(ITransportChannel channel)
        {
            lock (_rpcObjects)
            {
                foreach (var obj in _rpcObjects.OfType<RpcObjectHandle>().Where(o => o.AssociatedChannel == channel).ToArray())
                {
                    _rpcObjects.Remove(obj);
                }
                // remove events that would point to the channel (calling them would throw otherwise)
                foreach(var obj in _rpcObjects.OfType<RpcObjectHandle>())
                {
                    if(obj.Object.TryGetTarget(out var target))
                    {
                        foreach (var ev in target.GetType().GetEvents())
                        {
                            var fi = target.GetType().GetField(ev.Name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                            Delegate del = (Delegate)fi.GetValue(target);
                            if (del != null)
                            {
                                var list = del.GetInvocationList();
                                foreach (var d in list)
                                {
                                    if (IsDelegateAssociatedWithChannel(d, channel))
                                    {
                                        ev.GetRemoveMethod().Invoke(target, new object[] { d });
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        protected abstract bool IsDelegateAssociatedWithChannel(Delegate d, ITransportChannel channel);

        public virtual T GetProxyObject<T>(IRpcChannel channel, int remoteInstanceId)
        {
            return (T)GetProxyObject(
                channel,
                new[] { typeof(T) }.Concat(typeof(T).GetInterfaces()).ToArray(),
                remoteInstanceId);
        }

        public abstract object GetProxyObject(IRpcChannel channel, Type[] interfaceTypes, int remoteInstanceId);


#if DEBUG
        // make sure we don't hold any references to objects anymore
        ~RpcObjectRepositoryBase()
        {
            GC.Collect(2);
            GC.WaitForPendingFinalizers();
            Purge();
            if (_rpcObjects.OfType<RpcObjectHandle>().Any(r => r.AssociatedChannel != null))
            {
                throw new Exception("RPC Object count should be 0");
            }
        }
#endif
    }
}
