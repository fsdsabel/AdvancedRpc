#if JSON
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AdvancedRpcLib.Serializers
{
    public class JsonRpcSerializer : IRpcSerializer
    {
        public T DeserializeMessage<T>(byte[] data) where T : RpcMessage
        {
            using (var reader = new JsonTextReader(new StreamReader(new MemoryStream(data))))
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
}
#endif