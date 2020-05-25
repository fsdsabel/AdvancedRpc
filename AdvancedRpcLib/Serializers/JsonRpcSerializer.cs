using Newtonsoft.Json;
using System;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AdvancedRpcLib.Serializers
{
    public class JsonRpcSerializer : IRpcSerializer
    {
        public T DeserializeMessage<T>(ReadOnlySpan<byte> data) where T : RpcMessage
        {
#if NETCOREAPP
            var result = JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(data));
#else
            var result = JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(data.ToArray()));
#endif
            //TODO kann man das noch an einen schöneren Ort verlagern?
            if (result is RpcCallResultMessage msg && msg.Type == RpcMessageType.Exception)
            {
                var exceptionClassName = ((JToken) msg.Result)["ClassName"].Value<string>();
                msg.Result = ((JToken) msg.Result).ToObject(Type.GetType(exceptionClassName) ?? typeof(Exception));
            }

            return result;
        }

        public byte[] SerializeMessage<T>(T message) where T : RpcMessage
        {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
        }
    }

}
