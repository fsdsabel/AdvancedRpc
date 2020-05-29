using System;
using System.Threading;
using System.Threading.Tasks;
using AdvancedRpcLib.Channels.NamedPipe;
using AdvancedRpcLib.Serializers;
using Microsoft.VisualStudio.TestTools.UnitTesting;


namespace AdvancedRpcLib.UnitTests
{
    [TestClass]
    public class NamedPipeRpcChannelTests
    {
        [TestMethod]
        public async Task NamedPipeRpcServerRaisesEventOnClientConnection()
        {
            var name = Guid.NewGuid().ToString();
            using (var server = new NamedPipeRpcServerChannel(
                new BinaryRpcSerializer(),
                new RpcMessageFactory(),
                name))
            {
                await server.ListenAsync();

                using (var client = new NamedPipeRpcClientChannel(new BinaryRpcSerializer(),
                    new RpcMessageFactory(), name))
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
        public async Task NamedPipeRpcServerRaisesEventOnClientDisconnect()
        {
            var name = Guid.NewGuid().ToString();
            using (var server = new NamedPipeRpcServerChannel(
                new BinaryRpcSerializer(),
                new RpcMessageFactory(),
                name))
            {
                await server.ListenAsync();

                using (var client = new NamedPipeRpcClientChannel(new BinaryRpcSerializer(),
                    new RpcMessageFactory(), name))
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
        public async Task NamedPipeRpcClientRaisesEventOnDisconnectWhenServerShutdown()
        {
            var name = Guid.NewGuid().ToString();
            using (var server = new NamedPipeRpcServerChannel(
                new BinaryRpcSerializer(),
                new RpcMessageFactory(),
                name))
            {
                await server.ListenAsync();

                using (var client = new NamedPipeRpcClientChannel(new BinaryRpcSerializer(),
                    new RpcMessageFactory(), name))
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