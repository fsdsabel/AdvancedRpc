using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace AdvancedRpcLib.Channels.Tcp
{


    public class TcpRpcClientChannel : TcpRpcChannel, IRpcClientChannel, IDisposable
    {
        private readonly IPAddress _address;
        private TcpClient _tcpClient;
        private readonly int _port;
        

        public TcpRpcClientChannel(            
            IRpcSerializer serializer,
            IRpcMessageFactory messageFactory,
            IPAddress address, int port,
            IRpcObjectRepository localRepository = null,
            Func<IRpcObjectRepository> remoteRepository = null)
            : base(serializer, messageFactory, localRepository, remoteRepository)
        {
            _address = address;
            _port = port;
        }


        public IRpcObjectRepository ObjectRepository => GetRemoteRepository(_tcpClient);

        

        public async Task ConnectAsync()
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(_address, _port);
            RegisterMessageCallback(_tcpClient, data => HandleReceivedData(data), false);
            RunReaderLoop(_tcpClient);
        }

        private bool HandleReceivedData(ReadOnlySpan<byte> data)
        {
            var msg = _serializer.DeserializeMessage<RpcMessage>(data);
            return HandleRemoteMessage(_tcpClient, data, msg);            
        }

        public async Task<TResult> GetServerObjectAsync<TResult>()
        {
            try
            {
                var remoteRepo = GetRemoteRepository(_tcpClient);
                var response = await SendMessageAsync<RpcGetServerObjectResponseMessage>(_tcpClient, 
                    () => _messageFactory.CreateGetServerObjectMessage(remoteRepo.CreateTypeId<TResult>()));
                return remoteRepo.GetProxyObject<TResult>(GetRpcChannelForClient(_tcpClient), response.InstanceId);
            }
            catch (Exception ex)
            {
                throw new RpcFailedException("Getting server object failed.", ex);
            }
        }

        public void Dispose()
        {
            _tcpClient.Dispose();
        }

    }

}
