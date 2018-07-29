﻿using System;
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
            return CreateTypeId(typeof(T));
        }

        public string CreateTypeId(object obj)
        {
            return CreateTypeId(obj.GetType());
        }

        public string CreateTypeId(Type type)
        {
            return type.AssemblyQualifiedName;
        }

        private void Purge()
        {
            lock(_rpcObjects)
            {
                foreach(var o in _rpcObjects.ToArray())
                {
                    if(!o.Key.Object.TryGetTarget(out var _))
                    {
                        _rpcObjects.Remove(o.Key);
                    }
                }
            }
        }

        public RpcObjectHandle GetObject(string typeId)
        {
            lock (_rpcObjects)
            {
                Purge();
                foreach (var obj in _rpcObjects)
                {
                    foreach (var intf in obj.Value.InterfaceTypes)
                    {
                        if (CreateTypeId(intf) == typeId)
                        {
                            return obj.Value;
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
                var v = new RpcObjectHandle(singleton, true);
                _rpcObjects.Add(v, v);
            }
        }

        public RpcObjectHandle AddInstance(Type interfaceType, object instance)
        {
            lock (_rpcObjects)
            {
                Purge();
                var existing = _rpcObjects.FirstOrDefault(o =>
                {
                    if (o.Value.Object.TryGetTarget(out var obj))
                    {
                        return ReferenceEquals(obj, instance);
                    }
                    return false;
                });
                if (existing.Key == null)
                {
                    var v = new RpcObjectHandle(instance);
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
                Purge();
                if (_rpcObjects.TryGetValue(RpcObjectHandle.ComparisonHandle(instanceId), out var obj))
                {
                    if(obj.Object.TryGetTarget(out var instance))
                    {
                        return instance;
                    }
                }
            }
            return null;
        }

        public void RemoveInstance(int instanceId)
        {
            lock(_rpcObjects)
            {
                Purge();
                var ch = RpcObjectHandle.ComparisonHandle(instanceId);
                var toRemove = _rpcObjects.FirstOrDefault(o => !o.Key.IsPinned && o.Key.Equals(ch));
                if (toRemove.Key != null)
                {
                    _rpcObjects.Remove(toRemove.Key);
                }
            }
        }

        public object GetProxyObject(IRpcChannel channel, Type interfaceType, int remoteInstanceId)
        {
            lock (_rpcObjects)
            {
                Purge();
                if (_rpcObjects.TryGetValue(RpcObjectHandle.ComparisonHandle(remoteInstanceId), out var obj))
                {
                    if (obj.Object.TryGetTarget(out var inst))
                    {
                        return inst;
                    }
                }
                var result = new RpcObjectHandle(null);
                _rpcObjects.Add(result, result);
                var instance = 
                    interfaceType.IsSubclassOf(typeof(Delegate)) ?
                    ImplementDelegate(interfaceType, channel, remoteInstanceId, result.InstanceId) :
                    ImplementInterface(interfaceType, channel, remoteInstanceId, result.InstanceId);
                result.Object = new WeakReference<object>(instance);
                return instance;
            }
        }

        public T GetProxyObject<T>(IRpcChannel channel, int remoteInstanceId)
        {
            return (T)GetProxyObject(channel, typeof(T), remoteInstanceId);
        }

        private object ImplementDelegate(Type delegateType, IRpcChannel channel, int remoteInstanceId, int localInstanceId)
        {
            var ab = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RpcDynamicTypes"),
                AssemblyBuilderAccess.RunAndCollect);
            var dm = ab.DefineDynamicModule("RpcDynamicTypes.dll");
            var tb = dm.DefineType(delegateType.Name + $"Shadow{remoteInstanceId}");

            var invokerField = CreateConstructor(tb);

            ImplementMethod(tb, delegateType.GetMethod("Invoke"), invokerField, remoteInstanceId, false);
           

            var type = tb.CreateTypeInfo().AsType();
            var delObj = Activator.CreateInstance(type, channel);

            return Delegate.CreateDelegate(delegateType, delObj, "Invoke");

        }


        private FieldBuilder CreateConstructor(TypeBuilder tb)
        {
            var invokerField = tb.DefineField("_invoker", typeof(IRpcChannel), FieldAttributes.Private);

            var constructor = tb.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new[] { typeof(IRpcChannel) });
            var cil = constructor.GetILGenerator();
            // just store the target
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Ldarg_1);
            cil.Emit(OpCodes.Stfld, invokerField);
            cil.Emit(OpCodes.Ret);

            return invokerField;
        }

        private object ImplementInterface(Type interfaceType, IRpcChannel channel, int remoteInstanceId, int localInstanceId)
        {
            var ab = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RpcDynamicTypes"),
                AssemblyBuilderAccess.RunAndCollect);
#if !NETSTANDARD && DEBUG
            ab = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RpcDynamicTypes"),
                AssemblyBuilderAccess.RunAndSave);
#endif

            //add a destructor so we can inform other side about us not needing the object anymore

            var dm = ab.DefineDynamicModule("RpcDynamicTypes.dll");
            var tb = dm.DefineType(interfaceType.Name + "Shadow");
            
            var invokerField = CreateConstructor(tb);
            

            var destructor = tb.DefineMethod("Finalize", MethodAttributes.Family | MethodAttributes.Virtual |
                                                MethodAttributes.HideBySig,
                                                CallingConventions.Standard,
                                                typeof(void),
                                                Type.EmptyTypes);
            var dil = destructor.GetILGenerator();
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Ldfld, invokerField);
            dil.Emit(OpCodes.Ldc_I4, localInstanceId);
            dil.Emit(OpCodes.Ldc_I4, remoteInstanceId);
            dil.EmitCall(OpCodes.Callvirt, typeof(IRpcChannel).GetMethod(nameof(IRpcChannel.RemoveInstance)), null);
            dil.Emit(OpCodes.Ret);

            var allInterfaces = interfaceType.GetInterfaces().Concat(new[] { interfaceType }).Where(i => i.IsInterface);

            foreach (var intf in allInterfaces)
            {
                tb.AddInterfaceImplementation(intf);
                foreach (var method in intf.GetMethods())
                {
                    ImplementMethod(tb, method, invokerField, remoteInstanceId, true);
                }
            }


            
            var type = tb.CreateTypeInfo().AsType();
#if !NETSTANDARD && DEBUG
            ab.Save(@"RpcDynamicTypes.dll");
#endif

            return Activator.CreateInstance(type, channel);
        }

        private void ImplementMethod(TypeBuilder tb, MethodInfo method, FieldBuilder invokerField, int remoteInstanceId, bool overrideBase)
        {
            var margs = method.GetParameters().Select(p => p.ParameterType).ToArray();
            var mb = tb.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual,
                method.ReturnType, margs);
            var il = mb.GetILGenerator();


            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, invokerField);


            il.Emit(OpCodes.Ldc_I4, remoteInstanceId);
            il.Emit(OpCodes.Ldstr, method.Name);

            il.Emit(OpCodes.Ldc_I4, margs.Length);
            il.Emit(OpCodes.Newarr, typeof(Type));

            int ai = 1;
            foreach (var arg in margs)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, ai - 1);

                il.Emit(OpCodes.Ldtoken, arg);
                il.EmitCall(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle)), null);
                il.Emit(OpCodes.Stelem_Ref);
                ai++;
            }


            il.Emit(OpCodes.Ldc_I4, margs.Length);
            il.Emit(OpCodes.Newarr, typeof(object));

            ai = 1;
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

            if (overrideBase)
            {
                tb.DefineMethodOverride(mb, method);
            }
        }

        
    }

}
