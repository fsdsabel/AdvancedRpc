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
        private readonly IRpcMessageFactory _messageFactory;
        private readonly IRpcSerializer _serializer;
        private readonly IRpcObjectRepository _repository;

        public TcpRpcClientChannel(
            IRpcObjectRepository repository,
            IRpcSerializer serializer,
            IRpcMessageFactory messageFactory,
            IPAddress address, int port)
        {
            _address = address;
            _port = port;
            _messageFactory = messageFactory;
            _serializer = serializer;
            _repository = repository;
        }

        public async Task ConnectAsync()
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(_address, _port);
            RunReaderLoop(_tcpClient.GetStream());
        }

        public async Task<TResult> GetServerObjectAsync<TResult>()
        {
            var msg = _messageFactory.CreateGetServerObjectMessage(_repository.CreateTypeId<TResult>());
            var serializedMsg = _serializer.SerializeMessage(msg);
            var response = await SendMessageAsync<RpcGetServerObjectResponseMessage>(_tcpClient.GetStream(), _serializer, serializedMsg, msg.CallId);
            return _repository.GetObject<TResult>(this, response.InstanceId);
        }

        public object CallRpcMethod(int instanceId, string methodName, object[] args)
        {
            var msg = _messageFactory.CreateMethodCallMessage(instanceId, methodName, args);
            var serializedMsg = _serializer.SerializeMessage(msg);
            var response = SendMessageAsync<RpcCallResultMessage>(_tcpClient.GetStream(),
                _serializer, serializedMsg, msg.CallId).GetAwaiter().GetResult();

            return response.Result;
        }

        public void Dispose()
        {
            _tcpClient.Dispose();
        }
    }

}
