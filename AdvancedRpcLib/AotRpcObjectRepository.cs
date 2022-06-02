using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using AdvancedRpcLib.Channels;
using AdvancedRpcLib.Helpers;

namespace AdvancedRpcLib
{
    [AttributeUsage(AttributeTargets.Interface)]
    public class AotRpcObjectAttribute : Attribute
    {
    }


    public class AotRpcObjectRepository : RpcObjectRepositoryBase
    {
        private readonly Dictionary<int, Type> _typeImplementations;

        public AotRpcObjectRepository(bool clientRepository, Dictionary<int, Type> typeImplementations) : base(clientRepository)
        {
            _typeImplementations = typeImplementations;
        }

        public static int CreateTypesHash(params Type[] types)
        {
            var hashCode = new HashCode();
            foreach (var type in types)
            {
                hashCode.Add(type);
            }
            return hashCode.ToHashCode();
        }

        public override object GetProxyObject(IRpcChannel channel, Type[] interfaceTypes, Guid remoteInstanceId)
        {
            lock (_rpcObjects)
            {
                Purge();
                if (_rpcObjects.TryGetValue(RpcHandle.ComparisonHandle(remoteInstanceId), out var obj))
                {
                    if (((RpcObjectHandle)obj).Object.TryGetTarget(out var inst))
                    {
                        return inst;
                    }
                }

                bool isDelegate = interfaceTypes.Length == 1 && interfaceTypes[0].IsSubclassOf(typeof(Delegate));

                var result = new RpcObjectHandle(null, pinned:/*isDelegate && !_clientRepository,*/false, instanceId: remoteInstanceId);
                _rpcObjects.Add(result);

                object instance;
                if (isDelegate)
                {
                    throw new NotSupportedException("We do not support delegates for now.");
                }
                else
                {
                    var typesHash = CreateTypesHash(interfaceTypes);                    
                    if(!_typeImplementations.TryGetValue(typesHash, out var implementationType))
                    {
                        throw new InvalidOperationException($"Implementation type for interfaces {string.Join(", ", interfaceTypes.Select(t => t.ToString()))} not found.");
                    }
                    var constructor = implementationType.GetConstructor(
                        BindingFlags.NonPublic | BindingFlags.Instance, 
                        null, 
                        new Type[] { typeof(IRpcChannel), typeof(int), typeof(int) }, 
                        null);
                    instance = constructor.Invoke(new object[] { channel, result.InstanceId, remoteInstanceId });
                }


                result.Object = new WeakReference<object>(instance);
                return instance;
            }
        }

        protected override bool IsDelegateAssociatedWithChannel(Delegate d, ITransportChannel channel)
        {            
            throw new NotSupportedException("We do not support delegates for now.");
        }
    }
}
