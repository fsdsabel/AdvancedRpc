using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace AdvancedRpcLib.Serializers
{
    public class BinaryRpcSerializer : IRpcSerializer

    {
        private readonly ThreadLocal<BinaryFormatter> _formatter = new ThreadLocal<BinaryFormatter>(()=>new BinaryFormatter());

        public byte[] SerializeMessage<T>(T message) where T : RpcMessage
        {
            using (var ms = new MemoryStream())
            {
                _formatter.Value.Serialize(ms, message);
                return ms.ToArray();
            }
        }

        public T DeserializeMessage<T>(byte[] data) where T : RpcMessage
        {
            using (var ms = new MemoryStream(data))
            {
                return (T)_formatter.Value.Deserialize(ms);
            }
        }


        public object ChangeType(object value, Type targetType)
        {
            return Convert.ChangeType(value, targetType);
        }
    }
}
