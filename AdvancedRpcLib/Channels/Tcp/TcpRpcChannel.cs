using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AdvancedRpcLib.Channels.Tcp
{


    public abstract class TcpRpcChannel
    {
        private readonly AsyncNotification _messageNotifications = new AsyncNotification();


        protected Task<TResult> SendMessageAsync<TResult>(Stream stream, IRpcSerializer serializer, byte[] msg, int callId)
                where TResult : RpcMessage
        {
            var waitTask = WaitForMessageResultAsync<TResult>(serializer, callId); 
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                //TODO: longer messages > 64k, maybe with other messagetype
                writer.Write((byte)TcpRpcChannelMessageType.Message);
                writer.Write((ushort)msg.Length);
                writer.Write(msg);
            }
            return waitTask;
        }

        protected void SendMessage(Stream stream, byte[] msg)
        {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                //TODO: longer messages > 64k, maybe with other messagetype
                writer.Write((byte)TcpRpcChannelMessageType.Message);
                writer.Write((ushort)msg.Length);
                writer.Write(msg);
            }
        }

        private async Task<TResult> WaitForMessageResultAsync<TResult>(IRpcSerializer serializer, int callId)
            where TResult : RpcMessage
        {
            var re = new AsyncManualResetEvent(false);
            TResult result = default;
            _messageNotifications.Register((data) =>
            {
                var bareMsg = serializer.DeserializeMessage<RpcMessage>(data);
                if (bareMsg.CallId == callId)
                {
                    result = serializer.DeserializeMessage<TResult>(data);
                    re.Set();
                    return true;
                }
                return false;
            }, true);

            await re.WaitAsync();
            return result;
        }

        protected private void RegisterMessageCallback(AsyncNotification.DataReceivedDelegate callback, bool autoremove)
        {
            _messageNotifications.Register(callback, autoremove);
        }


        protected void RunReaderLoop(Stream stream)
        {
            Task.Run(delegate
            {
                var reader = new BinaryReader(stream, Encoding.UTF8, true);
                var smallMessageBuffer = new byte[ushort.MaxValue];

                while (true)
                {
                    var type = (TcpRpcChannelMessageType)reader.ReadByte();
                    switch (type)
                    {
                        case TcpRpcChannelMessageType.Message:
                            var msgLen = reader.ReadUInt16();
                            int offset = 0;
                            while (offset < msgLen)
                            {
                                offset += reader.Read(smallMessageBuffer, offset, msgLen - offset);
                            }

                            if(!_messageNotifications.Notify(new ReadOnlySpan<byte>(smallMessageBuffer, 0, msgLen)))
                            {
                                Console.WriteLine("Failed to process message");
                            }
                            break;

                        default:
                            throw new NotSupportedException("Invalid message type encountered");
                    }
                }
            });
        }


    }

}
