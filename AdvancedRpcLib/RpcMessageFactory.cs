using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using AdvancedRpcLib.Channels;

namespace AdvancedRpcLib
{
    public class RpcMessageFactory : IRpcMessageFactory
    {
        private int _callId;

        public RpcGetServerObjectMessage CreateGetServerObjectMessage(string typeId)
        {
            return new RpcGetServerObjectMessage
            {
                Type = RpcMessageType.GetServerObject,
                TypeId = typeId,
                CallId = Interlocked.Increment(ref _callId) // wraps around
            };
        }

        public RpcCallResultMessage CreateCallResultMessage(ITransportChannel channel, IRpcObjectRepository localRepository, 
            RpcMethodCallMessage call, MethodInfo calledMethod, object result)
        {

            var resultArgument = CreateRpcArgument(channel, localRepository, result, calledMethod.ReturnType);
            var resultMessage = new RpcCallResultMessage
            {
                CallId = call.CallId,
                Type = RpcMessageType.CallMethodResult,
                Result = resultArgument
            };

            return resultMessage;
        }

        public RpcCallResultMessage CreateExceptionResultMessage(RpcMethodCallMessage call, Exception exception)
        {
            return new RpcCallResultMessage
            {
                CallId = call.CallId,
                Type = RpcMessageType.Exception,
                Result = new RpcArgument
                {
                    Type = RpcType.Serialized,
                    Value = exception
                }
            };
        }

        public RpcMethodCallMessage CreateMethodCallMessage(ITransportChannel channel, IRpcObjectRepository localRepository,
            int instanceId, string methodName, Type[] argumentTypes, object[] arguments)
        {
            return new RpcMethodCallMessage
            {
                Type = RpcMessageType.CallMethod,
                CallId = Interlocked.Increment(ref _callId),
                MethodName = methodName,
                InstanceId = instanceId,
                Arguments = arguments
                    .Select((a,idx) => CreateRpcArgument(channel, localRepository, a, argumentTypes[idx]))
                    .ToArray()
            };
        }

        public RpcRemoveInstanceMessage CreateRemoveInstanceMessage(int instanceId)
        {
            return new RpcRemoveInstanceMessage
            {
                Type = RpcMessageType.RemoveInstance,
                CallId = Interlocked.Increment(ref _callId),
                InstanceId = instanceId
            };
        }

        public object DecodeRpcCallResultMessage(IRpcChannel channel, IRpcObjectRepository localRepository, 
            IRpcObjectRepository remoteRepository, IRpcSerializer serializer, RpcCallResultMessage message, Type resultType)
        {
            if (message.Type == RpcMessageType.Exception)
            {
                throw new TargetInvocationException((Exception)message.Result.Value);
            }
            return DecodeRpcArgument(channel, localRepository, remoteRepository, serializer, message.Result, resultType);
        }

        public object DecodeRpcArgument(IRpcChannel channel, IRpcObjectRepository localRepository, 
            IRpcObjectRepository remoteRepository, IRpcSerializer serializer, RpcArgument argument, Type argumentType)
        {
            switch (argument.Type)
            {
                case RpcType.Builtin:
                    if (argument.Value == null || argumentType == typeof(void)) return null;
                    if (argumentType.IsAssignableFrom(argument.Value.GetType()))
                    {
                        return argument.Value;
                    }

                    return serializer.ChangeType(argument.Value, argumentType);
                case RpcType.Proxy:
                    var instanceId = (int) serializer.ChangeType(argument.Value, typeof(int));
                    if (argument.TypeId != null)
                    {
                        argumentType = Type.GetType(argument.TypeId) ?? argumentType; 
                    }
                    return localRepository.GetInstance(instanceId) ?? remoteRepository.GetProxyObject(channel, argumentType, instanceId);
                case RpcType.Serialized:
                    var type = Type.GetType(argument.TypeId);
                    return serializer.ChangeType(argument.Value, type);
                case RpcType.ObjectArray:
                    var arrayType = Type.GetType(argument.TypeId);
                    var elementType = arrayType?.GetElementType() ?? throw new InvalidOperationException();
                    var array = Array.CreateInstance(elementType, (int)argument.Value);

                    for (int i = 0; i < array.Length; i++)
                    {
                        array.SetValue(DecodeRpcArgument(channel, localRepository, remoteRepository, serializer, argument.ArrayElements[i],
                                elementType), i);
                    }

                    return array;
                default:
                    throw new InvalidDataException();
            }
        }

        private RpcArgument CreateRpcArgument(ITransportChannel channel, IRpcObjectRepository localRepository, 
            object argument, Type argumentType)
        {
            string typeid = null;
            RpcType type = RpcType.Proxy;
            RpcArgument[] arrayElements = null;
            if (argument == null ||
                argument is IConvertible)
            {
                type = RpcType.Builtin;
            }
            else if(argument is Array array)
            {
                if ((argument.GetType().GetElementType()?.IsPrimitive ?? false) ||
                    argument.GetType().GetElementType() == typeof(string))
                {
                    type = RpcType.Builtin;
                }
                else
                {
                    type = RpcType.ObjectArray;
                    // make sure we use the correct interface definition as base type (we might get a specific array here)
                    var arrayTemplate = Array.CreateInstance(argumentType.GetElementType(), 0);
                    typeid = localRepository.CreateTypeId(arrayTemplate);
                    argument = array.Length;
                    var elementType = argumentType.GetElementType();
                    arrayElements = new RpcArgument[array.Length];
                    for (int i = 0; i < array.Length; i++)
                    {
                        arrayElements[i] = CreateRpcArgument(channel, localRepository, array.GetValue(i), elementType);
                    }

                }
            }
            else if (argumentType != typeof(object) &&
                     !argumentType.IsSubclassOf(typeof(Delegate)) &&
                     argumentType.GetCustomAttribute<SerializableAttribute>() != null)
            {
                type = RpcType.Serialized;
                typeid = localRepository.CreateTypeId(argument);
            }
            else
            {
                typeid = localRepository.CreateTypeId(argument);
                

                if (argument is IRpcObjectProxy proxy)
                {
                    /*var instance = localRepository.GetInstance(proxy.LocalInstanceId);
                    if (instance != null) // todo this check is not necessary
                    {
                        argument = proxy.LocalInstanceId;
                    }*/
                    argument = proxy.RemoteInstanceId;
                }
                else
                {
                    argument = localRepository.AddInstance(argumentType, argument, channel).InstanceId;
                }
            }

            return new RpcArgument
            {
                Type = type,
                Value = argument,
                TypeId = typeid,
                ArrayElements = arrayElements
            };
        }
    }
}
