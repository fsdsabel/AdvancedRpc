using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AdvancedRpcLib.Channels.Tcp;
using AdvancedRpcLib.Serializers;
using Microsoft.VisualStudio.TestTools.UnitTesting;


namespace AdvancedRpcLib.UnitTests
{
    [TestClass]
    public class TcpRpcChannelTests
    {
        [TestMethod]
        public async Task TcpRpcServerRaisesEventOnClientConnection()
        {
            using (var server = new TcpRpcServerChannel(
                new BinaryRpcSerializer(),
                new RpcMessageFactory(),
                IPAddress.Loopback,
                11234))
            {
                await server.ListenAsync();

                using (var client = new TcpRpcClientChannel(new BinaryRpcSerializer(),
                    new RpcMessageFactory(),
                    IPAddress.Loopback,
                    11234))
                {
                    var wait = new ManualResetEventSlim(false);
                    server.ClientConnected += (s, e) =>
                    {
                        Assert.AreSame(server, e.TransportChannel.Channel);
                        wait.Set();
                    };

                    await client.ConnectAsync();

                    Assert.IsTrue(wait.Wait(1000));
                }

            }
        }

        [TestMethod]
        public async Task TcpRpcServerRaisesEventOnClientDisconnect()
        {
            using (var server = new TcpRpcServerChannel(
                new BinaryRpcSerializer(),
                new RpcMessageFactory(),
                IPAddress.Loopback,
                11234))
            {
                await server.ListenAsync();

                using (var client = new TcpRpcClientChannel(new BinaryRpcSerializer(),
                    new RpcMessageFactory(),
                    IPAddress.Loopback,
                    11234))
                {
                    

                    await client.ConnectAsync();
                    
                    var wait = new ManualResetEventSlim(false);
                    server.ClientDisconnected += (s, e) =>
                    {
                        Assert.AreSame(server, e.TransportChannel.Channel);
                        wait.Set();
                    };

                    client.Dispose();
                    Assert.IsTrue(wait.Wait(1000));
                }

            }
        }

        [TestMethod]
        public async Task TcpRpcClientRaisesEventOnDisconnectWhenServerShutdown()
        {
            using (var server = new TcpRpcServerChannel(
                new BinaryRpcSerializer(),
                new RpcMessageFactory(),
                IPAddress.Loopback,
                11234))
            {
                await server.ListenAsync();

                using (var client = new TcpRpcClientChannel(new BinaryRpcSerializer(),
                    new RpcMessageFactory(),
                    IPAddress.Loopback,
                    11234))
                {
                    
                    
                    await client.ConnectAsync();

                    var wait = new ManualResetEventSlim(false);
                    client.Disconnected += (s, e) =>
                    {
                        Assert.AreSame(client, e.TransportChannel.Channel);
                        wait.Set();
                    };

                    server.Dispose();

                    Assert.IsTrue(wait.Wait(1000));
                }

            }
        }
    }
}