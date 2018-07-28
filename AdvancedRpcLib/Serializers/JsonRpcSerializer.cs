using Newtonsoft.Json;
using System;
using System.Text;

namespace AdvancedRpcLib.Serializers
{
    public class JsonRpcSerializer : IRpcSerializer
    {
        public T DeserializeMessage<T>(ReadOnlySpan<byte> data) where T : RpcMessage
        {
#if NETCOREAPP
            return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(data));
#else
            return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(data.ToArray()));
#endif
        }

        public byte[] SerializeMessage<T>(T message) where T : RpcMessage
        {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
        }
    }

}
