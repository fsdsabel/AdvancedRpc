using AdvancedRpc;
using AdvancedRpcLib;
using AdvancedRpcLib.Channels.Tcp;
using AdvancedRpcLib.Serializers;
using System;
using System.Net;
using System.Threading.Tasks;
using AdvancedRpcLib.Channels.NamedPipe;

namespace TestServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            
            /*var server = new TcpRpcServerChannel(                
                new JsonRpcSerializer(),
                new RpcMessageFactory(),
                IPAddress.Loopback,
                11234);
            server.ObjectRepository.RegisterSingleton(new TestObject());
            await server.ListenAsync();*/

            var server = new NamedPipeRpcServerChannel(new JsonRpcSerializer(), new RpcMessageFactory(), "test");
            server.ObjectRepository.RegisterSingleton(new TestObject());
            await server.ListenAsync();

            Console.WriteLine("Press key to quit");
            Console.ReadKey();
        }
    }

    class TestObject : ITestObject
    {
        public int Calculate(int a, int b)
        {
            return a + b;
        }

        public string SimpleCall()
        {
            return "42";
        }
    }
}
