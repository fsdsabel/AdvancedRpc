using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using AdvancedRpcLib.Channels;
using AdvancedRpcLib.Helpers;

namespace AdvancedRpcLib
{

    public class RpcObjectRepository : RpcObjectRepositoryBase
    {
        class RpcTypeDefinition
        {
            public Type Type { get; set; }

            public FieldInfo RemoteIdField { get; set; }

            public FieldInfo LocalIdField { get; set; }
        }
       

        
        private static readonly ModuleBuilder _moduleBuilder;
        private static readonly Dictionary<int, RpcTypeDefinition> _proxyImplementations = new Dictionary<int, RpcTypeDefinition>();

        static RpcObjectRepository()
        {
            var name = new AssemblyName("RpcDynamicTypes");
            name.SetPublicKey(typeof(RpcObjectRepository).Assembly.GetName().GetPublicKey());
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(name,
                AssemblyBuilderAccess.Run);
            _moduleBuilder = assemblyBuilder.DefineDynamicModule("RpcDynamicTypes.dll");
        }

        public RpcObjectRepository(bool clientRepository) : base(clientRepository)
        {
        }

        public override object GetProxyObject(IRpcChannel channel, Type[] interfaceTypes, Guid remoteInstanceId)
        {
            lock (_rpcObjects)
            {
                Purge();
                if (_rpcObjects.TryGetValue(RpcHandle.ComparisonHandle(remoteInstanceId), out var obj))
                {
                    if (((RpcObjectHandle) obj).Object.TryGetTarget(out var inst))
                    {
                        return inst;
                    }
                }

                bool isDelegate = interfaceTypes.Length == 1 && interfaceTypes[0].IsSubclassOf(typeof(Delegate));

                var result = new RpcObjectHandle(null, pinned:/*isDelegate && !_clientRepository,*/false, instanceId: remoteInstanceId);
                _rpcObjects.Add(result);

                var instance = isDelegate
                    ? ImplementDelegate(interfaceTypes[0], channel, remoteInstanceId, result.InstanceId)
                    : ImplementInterfaces(interfaceTypes, channel, remoteInstanceId, result.InstanceId);

                

                result.Object = new WeakReference<object>(instance);
                return instance;
            }
        }


        protected override bool IsDelegateAssociatedWithChannel(Delegate d, ITransportChannel channel)
        {
            var invokerField = d.Target.GetType().GetField("_invoker", BindingFlags.Instance | BindingFlags.NonPublic);
            if(invokerField != null)
            {
                if (invokerField.GetValue(d.Target) is IRpcChannel rpcChannel)
                {
                    return rpcChannel.Channel == channel;
                }
            }
            return false;
        }

        private int CreateTypesHash(params Type[] types)
        {
            var hash = new HashCode();
            foreach (var type in types)
            {
                hash.Add(type);
            }

            return hash.ToHashCode();
        }

        private object ImplementDelegate(Type delegateType, IRpcChannel channel, Guid remoteInstanceId, Guid localInstanceId)
        {
            RpcTypeDefinition type;
            lock (_proxyImplementations)
            {
                var hash = CreateTypesHash(delegateType);
                if (!_proxyImplementations.TryGetValue(hash, out type))
                {
                    var tb = _moduleBuilder.DefineType($"{delegateType.Name}Shadow{Guid.NewGuid():n}"); 
                    var invokerField = CreateConstructor(tb);
                    var (localField, remoteField) = ImplementIRpcObjectProxy(tb);

                    CreateDestructor(tb, invokerField, localField, remoteField);

                    ImplementMethod(tb, delegateType.GetMethod("Invoke"), invokerField, remoteField, false);

                    type = new RpcTypeDefinition
                    {
                        Type = tb.CreateTypeInfo().AsType()
                    };
                    type.LocalIdField = type.Type.GetField(localField.Name, BindingFlags.NonPublic | BindingFlags.Instance);
                    type.RemoteIdField = type.Type.GetField(remoteField.Name, BindingFlags.NonPublic | BindingFlags.Instance);

                    _proxyImplementations.Add(hash, type);
                }
            }



            var delObj = Activator.CreateInstance(type.Type, channel);
            Debug.Assert(type.LocalIdField != null, "type.LocalIdField != null");
            Debug.Assert(type.RemoteIdField != null, "type.RemoteIdField != null");
            type.LocalIdField.SetValue(delObj, localInstanceId);
            type.RemoteIdField.SetValue(delObj, remoteInstanceId);

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


        private void CreateDestructor(TypeBuilder tb, FieldBuilder invokerField, FieldBuilder localField, FieldBuilder remoteField)
        {
            var destructor = tb.DefineMethod("Finalize", MethodAttributes.Family | MethodAttributes.Virtual |
                                                         MethodAttributes.HideBySig,
                CallingConventions.Standard,
                typeof(void),
                Type.EmptyTypes);
            var dil = destructor.GetILGenerator();
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Ldfld, invokerField);
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Ldfld, localField);
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Ldfld, remoteField);
            dil.EmitCall(OpCodes.Callvirt, typeof(IRpcChannel).GetMethod(nameof(IRpcChannel.RemoveInstance)),
                null);
            dil.Emit(OpCodes.Ret);
        }


        private object ImplementInterfaces(Type[] interfaceTypes, IRpcChannel channel, Guid remoteInstanceId, Guid localInstanceId)
        {
            if (!AllowNonPublicInterfaceAccess) 
            {
                if (interfaceTypes.All(interfaceType =>
                    interfaceType.IsInterface && !interfaceType.IsPublic && !interfaceType.IsNestedPublic))
                {
                    throw new RpcFailedException("Cannot get non public interface.");
                }
            }

            RpcTypeDefinition type;

            lock (_proxyImplementations)
            {
                var hash = CreateTypesHash(interfaceTypes);

                if (!_proxyImplementations.TryGetValue(hash, out type))
                {

                    /*
        #if !NETSTANDARD && DEBUG
                    ab = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RpcDynamicTypes"),
                        AssemblyBuilderAccess.RunAndSave);
        #endif*/

                    //add a destructor so we can inform other side about us not needing the object anymore


                    var tb = _moduleBuilder.DefineType($"I{Guid.NewGuid():N}Shadow");

                    var invokerField = CreateConstructor(tb);

                    var (localField, remoteField) = ImplementIRpcObjectProxy(tb);

                   CreateDestructor(tb, invokerField, localField, remoteField);

                   var allInterfaceTypes = AllowNonPublicInterfaceAccess
                       ? interfaceTypes
                       : interfaceTypes.Where(i => i.IsPublic || i.IsNestedPublic);

                    foreach (var intf in allInterfaceTypes)
                    {
                        tb.AddInterfaceImplementation(intf);
                        foreach (var method in intf.GetMethods().Where(m => !m.IsSpecialName))
                        {
                            ImplementMethod(tb, method, invokerField, remoteField, true);
                        }

                        foreach (var property in intf.GetProperties())
                        {
                            var prop = tb.DefineProperty(property.Name, property.Attributes, property.PropertyType,
                                null);
                            if (property.GetMethod != null)
                            {
                                prop.SetGetMethod(ImplementMethod(tb, property.GetMethod, invokerField,
                                    remoteField,
                                    true));
                            }

                            if (property.SetMethod != null)
                            {
                                prop.SetSetMethod(ImplementMethod(tb, property.SetMethod, invokerField,
                                    remoteField,
                                    true));
                            }
                        }

                        foreach (var evnt in intf.GetEvents())
                        {
                            if (evnt.AddMethod != null)
                            {
                                ImplementMethod(tb, evnt.AddMethod, invokerField, remoteField, true);
                            }

                            if (evnt.RemoveMethod != null)
                            {
                                ImplementMethod(tb, evnt.RemoveMethod, invokerField, remoteField, true, true);
                            }
                        }
                    }


                    type = new RpcTypeDefinition
                    {
                        Type = tb.CreateTypeInfo().AsType()
                    };
                    type.LocalIdField = type.Type.GetField(localField.Name, BindingFlags.NonPublic | BindingFlags.Instance);
                    type.RemoteIdField = type.Type.GetField(remoteField.Name, BindingFlags.NonPublic | BindingFlags.Instance);

                    _proxyImplementations.Add(hash, type);
                }

            }
            /*
#if !NETSTANDARD && DEBUG
            ab.Save(@"RpcDynamicTypes.dll");
#endif*/


            var result = Activator.CreateInstance(type.Type, channel);
            
            Debug.Assert(type.LocalIdField != null, "type.LocalIdField != null");
            Debug.Assert(type.RemoteIdField != null, "type.RemoteIdField != null");
            type.LocalIdField.SetValue(result, localInstanceId);
            type.RemoteIdField.SetValue(result, remoteInstanceId);

            return result;
        }

        private (FieldBuilder localField, FieldBuilder remoteField) ImplementIRpcObjectProxy(TypeBuilder tb)
        {
            tb.AddInterfaceImplementation(typeof(IRpcObjectProxy));

            FieldBuilder DefineProp(string propName)
            {
                var id = Guid.NewGuid();
                var field = tb.DefineField($"_instanceId{id:N}", typeof(Guid), FieldAttributes.Private);
                


                var pb = tb.DefineProperty(propName,
                    PropertyAttributes.None, typeof(Guid), Type.EmptyTypes);

                var mb = tb.DefineMethod($"get_InstanceId{id:N}",
                    MethodAttributes.Private |
                    MethodAttributes.HideBySig |
                    MethodAttributes.NewSlot |
                    MethodAttributes.Virtual |
                    MethodAttributes.Final, typeof(Guid), Type.EmptyTypes);
                var il = mb.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0); // this
                il.Emit(OpCodes.Ldfld, field);
                il.Emit(OpCodes.Ret);


                tb.DefineMethodOverride(mb,
                    typeof(IRpcObjectProxy).GetProperty(propName).GetMethod);
                pb.SetGetMethod(mb);
                return field;
            }

            var localField = DefineProp(nameof(IRpcObjectProxy.LocalInstanceId));
            var remoteField = DefineProp(nameof(IRpcObjectProxy.RemoteInstanceId));

            return (localField, remoteField);
        }

        private MethodBuilder ImplementMethod(TypeBuilder tb, MethodInfo method, FieldBuilder invokerField,
            FieldBuilder remoteField, bool overrideBase, bool isEventRemove = false)
        {
            var margs = method.GetParameters().Select(p => p.ParameterType).ToArray();
            var mb = tb.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual,
                method.ReturnType, margs);
            var il = mb.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, invokerField);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, remoteField);
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

            return mb;
        }

    }
}
