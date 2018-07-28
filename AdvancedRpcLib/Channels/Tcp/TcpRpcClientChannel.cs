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
            try
            {
                var msg = _messageFactory.CreateGetServerObjectMessage(_repository.CreateTypeId<TResult>());
                var serializedMsg = _serializer.SerializeMessage(msg);
                var response = await SendMessageAsync<RpcGetServerObjectResponseMessage>(_tcpClient.GetStream(), _serializer, serializedMsg, msg.CallId);
                return _repository.GetObject<TResult>(this, response.InstanceId);
            }
            catch (Exception ex)
            {
                throw new RpcFailedException("Getting server object failed.", ex);
            }
        }

        public object CallRpcMethod(int instanceId, string methodName, object[] args, Type resultType)
        {
            try
            {
                var msg = _messageFactory.CreateMethodCallMessage(instanceId, methodName, args);
                var serializedMsg = _serializer.SerializeMessage(msg);
                var response = SendMessageAsync<RpcCallResultMessage>(_tcpClient.GetStream(),
                    _serializer, serializedMsg, msg.CallId).GetAwaiter().GetResult();

                if(response.ResultType == RpcType.Proxy)
                {
                    return _repository.GetObject(this, resultType, Convert.ToInt32(response.Result));
                }

                return response.Result;
            }
            catch (Exception ex)
            {
                throw new RpcFailedException($"Calling remote method {methodName} on object #{instanceId} failed.", ex);
            }
        }

        public void Dispose()
        {
            _tcpClient.Dispose();
        }
    }

}
