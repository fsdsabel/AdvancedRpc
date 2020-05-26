using Newtonsoft.Json;
using System;
using System.CodeDom;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Jil;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using ProtoBuf.Meta;
using ProtoBuf.Serializers;

namespace AdvancedRpcLib.Serializers
{
    public class JsonRpcSerializer : IRpcSerializer
    {
        public T DeserializeMessage<T>(ReadOnlySpan<byte> data) where T : RpcMessage
        {
            using (var reader = new JsonTextReader(new StreamReader(new MemoryStream(data.ToArray()))))
            {
                var converter = new JsonSerializer();
                var result = converter.Deserialize<T>(reader);
                
                //TODO kann man das noch an einen schöneren Ort verlagern?
                if (result is RpcCallResultMessage msg && msg.Type == RpcMessageType.Exception)
                {
                    var exceptionClassName = ((JToken)msg.Result)["ClassName"].Value<string>();
                    msg.Result = ((JToken)msg.Result).ToObject(Type.GetType(exceptionClassName) ?? typeof(Exception));
                }

                return result;
            }
                
        }

        public object ChangeType(object value, Type targetType)
        {
            return Convert.ChangeType(value, targetType);
        }

        public byte[] SerializeMessage<T>(T message) where T : RpcMessage
        {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
        }
    }

    public class JilRpcSerializer : IRpcSerializer
    {
        private static readonly Options _options = new Options(false, true, includeInherited:true);

        public byte[] SerializeMessage<T>(T message) where T : RpcMessage
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new StreamWriter(ms))
                {
                    JSON.SerializeDynamic(message, writer, _options);
                    writer.Flush();
                }

                var s = Encoding.UTF8.GetString(ms.ToArray());
                return ms.ToArray();
            }
            
        }

        public T DeserializeMessage<T>(ReadOnlySpan<byte> data) where T : RpcMessage
        {
            using (var ms = new MemoryStream(data.ToArray()))
            {
                using (var reader = new StreamReader(ms))
                {
                    return JSON.Deserialize<T>(reader, _options);
                }
            }

        }

        public object ChangeType(object value, Type targetType)
        {
            return System.ComponentModel.TypeDescriptor.GetConverter(value).ConvertTo(value, targetType);
        }

        public static object DoChangeType(object value, Type targetType)
        {
            return System.ComponentModel.TypeDescriptor.GetConverter(value).ConvertTo(value, targetType);
        }
    }


    public class ProtobufRpcSerializer : IRpcSerializer
    {
        private static RuntimeTypeModel _model;

        static ProtobufRpcSerializer()
        {
            _model = RuntimeTypeModel.Create(null);

          
            //AddType(typeof(object));

            AddType(typeof(RpcMessage));
            AddType(typeof(RpcGetServerObjectMessage));
            AddType(typeof(RpcGetServerObjectResponseMessage));
            AddType(typeof(RpcArgument));
            AddType(typeof(RpcMethodCallMessage));

            AddType(typeof(RpcCallResultMessage));
            AddType(typeof(RpcRemoveInstanceMessage));
            //_model.CompileInPlace();
        }

        public static class SerializerBuilder
        {
            private const BindingFlags Flags = BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            private static readonly Dictionary<Type, HashSet<Type>> SubTypes = new Dictionary<Type, HashSet<Type>>();
            private static readonly ConcurrentBag<Type> BuiltTypes = new ConcurrentBag<Type>();
            private static readonly Type ObjectType = typeof(object);

            /// <summary>
            /// Build the ProtoBuf serializer from the generic <see cref="Type">type</see>.
            /// </summary>
            /// <typeparam name="T">The type of build the serializer for.</typeparam>
            public static void Build<T>()
            {
                var type = typeof(T);
                Build(type);
            }

            /// <summary>
            /// Build the ProtoBuf serializer from the data's <see cref="Type">type</see>.
            /// </summary>
            /// <typeparam name="T">The type of build the serializer for.</typeparam>
            /// <param name="data">The data who's type a serializer will be made.</param>
            // ReSharper disable once UnusedParameter.Global
            public static void Build<T>(T data)
            {
                Build<T>();
            }

            /// <summary>
            /// Build the ProtoBuf serializer for the <see cref="Type">type</see>.
            /// </summary>
            /// <param name="type">The type of build the serializer for.</param>
            public static void Build(Type type)
            {
                if (BuiltTypes.Contains(type))
                {
                    return;
                }
                if (type.IsArray)
                {
                    type = type.GetElementType();
                }
                lock (type)
                {
                    if (RuntimeTypeModel.Default.GetTypes().Cast<MetaType>().Any(t=>t.Type==type))
                    {
                        return;
                    }
                    

                    if (RuntimeTypeModel.Default.CanSerialize(type))
                    {
                        if (type.IsGenericType)
                        {
                            BuildGenerics(type);
                        }

                        return;
                    }

                    if (type == ObjectType)
                    {
                        return;
                    }

                    var meta = RuntimeTypeModel.Default.Add(type, false);
                    var fields = GetFields(type);

                    int i = 1;
                    foreach (var field in fields)
                    {
                        var mt = meta.Add(i++, field.Name);
                        if (field.FieldType == ObjectType)
                        {
                            mt.SerializerType = typeof(ObjectSerializer);
                            
                        }
                    }

               //     meta.Add(fields.Select(m => m.Name).ToArray());
                    meta.UseConstructor = false;

                    BuildBaseClasses(type);
                    BuildGenerics(type);

                    foreach (var memberType in fields.Select(f => f.FieldType).Where(t => !t.IsPrimitive))
                    {
                        Build(memberType);
                    }

                    BuiltTypes.Add(type);
                }
            }

            /// <summary>
            /// Gets the fields for a type.
            /// </summary>
            /// <param name="type">The type.</param>
            /// <returns></returns>
            private static FieldInfo[] GetFields(Type type)
            {
                return type.GetFields(Flags);
            }

            /// <summary>
            /// Builds the base class serializers for a type.
            /// </summary>
            /// <param name="type">The type.</param>
            private static void BuildBaseClasses(Type type)
            {
                var baseType = type.BaseType;
                var inheritingType = type;


                while (baseType != null && baseType != ObjectType)
                {
                    HashSet<Type> baseTypeEntry;

                    if (!SubTypes.TryGetValue(baseType, out baseTypeEntry))
                    {
                        baseTypeEntry = new HashSet<Type>();
                        SubTypes.Add(baseType, baseTypeEntry);
                    }

                    if (!baseTypeEntry.Contains(inheritingType))
                    {
                        Build(baseType);
                        RuntimeTypeModel.Default[baseType].AddSubType(baseTypeEntry.Count + 500, inheritingType);
                        baseTypeEntry.Add(inheritingType);
                    }

                    inheritingType = baseType;
                    baseType = baseType.BaseType;
                }
            }

            /// <summary>
            /// Builds the serializers for the generic parameters for a given type.
            /// </summary>
            /// <param name="type">The type.</param>
            private static void BuildGenerics(Type type)
            {
                if (type.IsGenericType || (type.BaseType != null && type.BaseType.IsGenericType))
                {
                    var generics = type.IsGenericType ? type.GetGenericArguments() : type.BaseType.GetGenericArguments();

                    foreach (var generic in generics)
                    {
                        Build(generic);
                    }
                }
            }
        }

        public class ObjectSerializer : ProtoBuf.Serializers.ISerializer<object>
        {
            public ObjectSerializer()
            {

            }

            public object Read(ref ProtoReader.State state, object value)
            {
                throw new NotImplementedException();
            }

            public void Write(ref ProtoWriter.State state, object value)
            {
                throw new NotImplementedException();
            }

            public SerializerFeatures Features => SerializerFeatures.CategoryMessage;
        }

        static MetaType AddType(Type type, Action<MetaType, int> configure = null)
        {
            SerializerBuilder.Build(type);
            return null;

            lock (_model)
            {
                var existing = _model.GetTypes().Cast<MetaType>().FirstOrDefault(mt => mt.Type == type);
                if (existing != null)
                {
                    return existing;
                }

                var t = _model.Add(type, false);
                var props = type.GetProperties().Select(p => (name:p.Name, type:p.PropertyType))
                    .OrderBy(n => n.name).ToArray();

                int i = 1;
                foreach (var prop in props)
                {
                    var pt = t.Add(i++, prop.name);
                    if (prop.type == typeof(object))
                    {
                        pt.SerializerType = typeof(ObjectSerializer);
                        
                        //pt.AsReferenceDefault = true;
                    }
                }

                var subType = type;
                var baseType = type.BaseType;
                MetaType currentMetaType;
                while (baseType != typeof(object))
                {
                    currentMetaType = AddType(baseType);
                    currentMetaType.AddSubType(currentMetaType.GetFields().Length + currentMetaType.GetSubtypes().Length + 1, subType);
                    subType = baseType;
                    baseType = baseType.BaseType;
                }
                

                configure?.Invoke(t, i);
                

                //t.CompileInPlace();
                return t;
            }
        }

        public byte[] SerializeMessage<T>(T message) where T : RpcMessage
        {
            using (var ms = new MemoryStream())
            {
                
                //_model.SerializeWithLengthPrefix(ms, message.GetType().FullName, typeof(string), PrefixStyle.Base128, 1);
                Serializer.Serialize(ms, message);
                
                //Serializer.Serialize(ms, message);
                return ms.ToArray();
            }
        }

        public T DeserializeMessage<T>(ReadOnlySpan<byte> data) where T : RpcMessage
        {
            /*var ms = new MemoryStream(data.ToArray());
            var typeName = (string)_model.DeserializeWithLengthPrefix(ms, null, typeof(string), PrefixStyle.Base128, 1);
            //var result = Activator.CreateInstance<T>();


            return (T)_model.Deserialize(ms, null, Type.GetType(typeName));*/

            return Serializer.Deserialize<T>(new MemoryStream(data.ToArray()), null);
        }

        public object ChangeType(object value, Type targetType)
        {
            return Convert.ChangeType(value, targetType);
        }
    }
}
