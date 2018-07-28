using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace AdvancedRpcLib
{
    public class RpcObjectRepository : IRpcObjectRepository
    {
        private readonly Dictionary<RpcObjectHandle, RpcObjectHandle> _rpcObjects = new Dictionary<RpcObjectHandle, RpcObjectHandle>();

        public string CreateTypeId<T>()
        {
            return typeof(T).FullName;
        }

        public string CreateTypeId(object obj)
        {
            return CreateTypeId(obj.GetType());
        }

        public string CreateTypeId(Type type)
        {
            return type.FullName;
        }

        public RpcObjectHandle GetObject(string typeId)
        {
            lock (_rpcObjects)
            {
                foreach (var obj in _rpcObjects)
                {
                    if (CreateTypeId(obj.Value.InterfaceType) == typeId)
                    {
                        return obj.Value;
                    }
                }
            }
            return null;
        }

        public void RegisterSingleton<T>(object singleton)
        {
            lock (_rpcObjects)
            {
                var v = new RpcObjectHandle(typeof(T), singleton);
                _rpcObjects.Add(v, v);
            }
        }

        public RpcObjectHandle AddInstance(Type interfaceType, object instance)
        {
            lock (_rpcObjects)
            {
                var existing = _rpcObjects.FirstOrDefault(o => ReferenceEquals(o.Value.Object, instance));
                if (existing.Key == null)
                {
                    var v = new RpcObjectHandle(interfaceType, instance);
                    _rpcObjects.Add(v, v);
                    return v;
                }
                return existing.Key;
            }
        }

        public object GetInstance(int instanceId)
        {
            lock (_rpcObjects)
            {
                if (_rpcObjects.TryGetValue(RpcObjectHandle.ComparisonHandle(instanceId), out var obj))
                {
                    return obj.Object;
                }
            }
            return null;
        }

        public object GetObject(IRpcChannel channel, Type interfaceType, int instanceId)
        {
            lock (_rpcObjects)
            {

                if (_rpcObjects.TryGetValue(RpcObjectHandle.ComparisonHandle(instanceId), out var obj))
                {
                    return obj.Object;
                }
                var result = new RpcObjectHandle(interfaceType, null);
                _rpcObjects.Add(result, result);
                result.Object = ImplementInterface(interfaceType, channel, instanceId);
                return result.Object;
            }
        }

        public T GetObject<T>(IRpcChannel channel, int instanceId)
        {
            return (T)GetObject(channel, typeof(T), instanceId);
        }

        private object ImplementInterface(Type interfaceType, IRpcChannel channel, int instanceId)
        {
            var ab = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RpcDynamicTypes"),
                AssemblyBuilderAccess.RunAndCollect);
#if !NETSTANDARD && DEBUG
            ab = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RpcDynamicTypes"),
                AssemblyBuilderAccess.RunAndSave);
#endif

            var dm = ab.DefineDynamicModule("RpcDynamicTypes.dll");
            var tb = dm.DefineType(interfaceType.Name + "Shadow");
            tb.AddInterfaceImplementation(interfaceType);

            var invokerField = tb.DefineField("_invoker", typeof(IRpcChannel), FieldAttributes.Private);
            var constructor = tb.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new[] { typeof(IRpcChannel) });
            var cil = constructor.GetILGenerator();
            // just store the target
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Ldarg_1);
            cil.Emit(OpCodes.Stfld, invokerField);
            cil.Emit(OpCodes.Ret);


            foreach (var method in interfaceType.GetMethods())
            {
                ImplementMethod(tb, method, invokerField, instanceId);
            }

            
            var type = tb.CreateTypeInfo().AsType();
#if !NETSTANDARD && DEBUG
            ab.Save(@"RpcDynamicTypes.dll");
#endif

            return Activator.CreateInstance(type, channel);
        }

        private void ImplementMethod(TypeBuilder tb, MethodInfo method, FieldBuilder invokerField, int instanceId)
        {
            var margs = method.GetParameters().Select(p => p.ParameterType).ToArray();
            var mb = tb.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual,
                method.ReturnType, margs);
            var il = mb.GetILGenerator();


            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, invokerField);


            il.Emit(OpCodes.Ldc_I4, instanceId);
            il.Emit(OpCodes.Ldstr, method.Name);


            il.Emit(OpCodes.Ldc_I4, margs.Length);
            il.Emit(OpCodes.Newarr, typeof(object));

            int ai = 1;
            foreach (var arg in margs)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, ai - 1);
                il.Emit(OpCodes.Ldarg, ai);
                if (!arg.IsClass)
                {
                    il.Emit(OpCodes.Box, arg);
                }
                il.Emit(OpCodes.Stelem_Ref);
                ai++;
            }

            il.Emit(OpCodes.Ldtoken, method.ReturnType);
            il.EmitCall(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle)), null);

            var m = typeof(IRpcChannel).GetMethod(nameof(IRpcChannel.CallRpcMethod));
            il.EmitCall(OpCodes.Callvirt, m, null);
            if (method.ReturnType.IsClass || method.ReturnType.IsInterface)
            {
                il.Emit(OpCodes.Isinst, method.ReturnType);
            }
            else if(method.ReturnType == typeof(void))
            {
                il.Emit(OpCodes.Pop);
            }
            else
            {
                il.Emit(OpCodes.Ldtoken, method.ReturnType);
                il.EmitCall(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle)), null);
                il.EmitCall(OpCodes.Call, typeof(Convert).GetMethod(nameof(Convert.ChangeType), new[] { typeof(object), typeof(Type) }), null);
                il.Emit(OpCodes.Unbox_Any, method.ReturnType);
            }
            il.Emit(OpCodes.Ret);

            tb.DefineMethodOverride(mb, method);
        }

      
    }

}
