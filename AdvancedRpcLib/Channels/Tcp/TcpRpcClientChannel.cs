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
        private readonly IRpcObjectRepository _remoteRepository, _localRepository;

        public TcpRpcClientChannel(            
            IRpcSerializer serializer,
            IRpcMessageFactory messageFactory,
            IPAddress address, int port,
            IRpcObjectRepository localRepository = null,
            IRpcObjectRepository remoteRepository = null)
        {
            _address = address;
            _port = port;
            _messageFactory = messageFactory;
            _serializer = serializer;
            _remoteRepository = remoteRepository ?? new RpcObjectRepository();
            _localRepository = localRepository ?? new RpcObjectRepository();
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
                var response = await SendMessageAsync<RpcGetServerObjectResponseMessage>(() => _messageFactory.CreateGetServerObjectMessage(_remoteRepository.CreateTypeId<TResult>()));
                return _remoteRepository.GetProxyObject<TResult>(this, response.InstanceId);
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
                var response = SendMessageAsync<RpcCallResultMessage>(() => _messageFactory.CreateMethodCallMessage(instanceId, methodName, args))
                    .GetAwaiter().GetResult();

                if(response.ResultType == RpcType.Proxy)
                {
                    return _remoteRepository.GetProxyObject(this, resultType, Convert.ToInt32(response.Result));
                }

                return response.Result;
            }
            catch (Exception ex)
            {
                throw new RpcFailedException($"Calling remote method {methodName} on object #{instanceId} failed.", ex);
            }
        }

        private Task<T> SendMessageAsync<T>(Func<RpcMessage> msgFunc) where T : RpcMessage
        {
            var msg = msgFunc();
            var serializedMsg = _serializer.SerializeMessage(msg);
            return SendMessageAsync<T>(_tcpClient.GetStream(), _serializer, serializedMsg, msg.CallId);
        }

        public void RemoveInstance(int localInstanceId, int remoteInstanceId)
        {
            try
            {
                _remoteRepository.RemoveInstance(localInstanceId);
                SendMessageAsync<RpcMessage>(() => _messageFactory.CreateRemoveInstanceMessage(remoteInstanceId)).GetAwaiter().GetResult();
            }
            catch
            {
                // server not reachable, that's ok
            }
        }

        public void Dispose()
        {
            _tcpClient.Dispose();
        }

    }

}
