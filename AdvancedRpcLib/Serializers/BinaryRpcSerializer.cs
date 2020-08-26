using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace AdvancedRpcLib.Serializers
{
    public class BinaryRpcSerializer : IRpcSerializer
    {
        private readonly ThreadLocal<BinaryFormatter> _formatter;

        public BinaryRpcSerializer()
        {
            _formatter = new ThreadLocal<BinaryFormatter>(CreateBinaryFormatter);
        }

        protected virtual BinaryFormatter CreateBinaryFormatter()
        {
            return new BinaryFormatter();
        }

        public virtual byte[] SerializeMessage<T>(T message) where T : RpcMessage
        {
            using (var ms = new MemoryStream())
            {
                _formatter.Value.Serialize(ms, message);
                return ms.ToArray();
            }
        }

        public virtual T DeserializeMessage<T>(byte[] data) where T : RpcMessage
        {
            using (var ms = new MemoryStream(data))
            {
                return (T)_formatter.Value.Deserialize(ms);
            }
        }


        public virtual object ChangeType(object value, Type targetType)
        {
            if (targetType.IsInstanceOfType(value))
            {
                return value;
            }
            return Convert.ChangeType(value, targetType);
        }
    }
}
