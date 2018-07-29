using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace AdvancedRpcLib.Channels.Tcp
{


    public abstract class TcpRpcChannel
    {
        private readonly Dictionary<TcpClient, AsyncNotification> _messageNotifications = new Dictionary<TcpClient, AsyncNotification>();
        private readonly object _sendLock = new object();

        protected Task<TResult> SendMessageAsync<TResult>(TcpClient client, IRpcSerializer serializer, byte[] msg, int callId)
                where TResult : RpcMessage
        {
            var waitTask = WaitForMessageResultAsync<TResult>(client, serializer, callId);
            lock (_sendLock)
            {
                using (var writer = new BinaryWriter(client.GetStream(), Encoding.UTF8, true))
                {
                    //TODO: longer messages > 64k, maybe with other messagetype
                    writer.Write((byte)TcpRpcChannelMessageType.Message);
                    writer.Write((ushort)msg.Length);
                    writer.Write(msg);
                }
            }
            return waitTask;
        }

        protected void SendMessage(Stream stream, byte[] msg)
        {
            lock (_sendLock)
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    //TODO: longer messages > 64k, maybe with other messagetype
                    writer.Write((byte)TcpRpcChannelMessageType.Message);
                    writer.Write((ushort)msg.Length);
                    writer.Write(msg);
                }
            }
        }

        private async Task<TResult> WaitForMessageResultAsync<TResult>(TcpClient client, IRpcSerializer serializer, int callId)
            where TResult : RpcMessage
        {
            var re = new AsyncManualResetEvent(false);
            TResult result = default;
            RegisterMessageCallback(client, (data) =>
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

        protected private void RegisterMessageCallback(TcpClient client, AsyncNotification.DataReceivedDelegate callback, bool autoremove)
        {
            lock(_messageNotifications)
            {
                if(!_messageNotifications.ContainsKey(client))
                {
                    _messageNotifications.Add(client, new AsyncNotification());
                }
                _messageNotifications[client].Register(callback, autoremove);
            }
         
        }


        protected void RunReaderLoop(TcpClient client)
        {
            Task.Run(delegate
            {
                var reader = new BinaryReader(client.GetStream(), Encoding.UTF8, true);
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
                            
                            if(!_messageNotifications[client].Notify(new ReadOnlySpan<byte>(smallMessageBuffer, 0, msgLen)))
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
