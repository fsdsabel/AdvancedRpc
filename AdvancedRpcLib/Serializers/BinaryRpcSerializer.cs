using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace AdvancedRpcLib.Serializers
{
    public class BinaryRpcSerializer : IRpcSerializer

    {
        public byte[] SerializeMessage<T>(T message) where T : RpcMessage
        {
            using (var ms = new MemoryStream())
            {
                new BinaryFormatter().Serialize(ms, message);
                return ms.ToArray();
            }
        }

        public T DeserializeMessage<T>(ReadOnlySpan<byte> data) where T : RpcMessage
        {
            using (var ms = new MemoryStream(data.ToArray()))
            {
                return (T)new BinaryFormatter().Deserialize(ms);
            }
        }

        public object ChangeType(object value, Type targetType)
        {
            return Convert.ChangeType(value, targetType);
        }
    }
}
