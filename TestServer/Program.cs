using AdvancedRpc;
using AdvancedRpcLib;
using AdvancedRpcLib.Channels.Tcp;
using AdvancedRpcLib.Serializers;
using System;
using System.Net;
using System.Threading.Tasks;

namespace TestServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var serverRepo = new RpcObjectRepository();
            serverRepo.RegisterSingleton<ITestObject>(new TestObject());
            var server = new TcpRpcServerChannel(
                serverRepo,
                new JsonRpcSerializer(),
                new RpcMessageFactory(),
                IPAddress.Loopback,
                11234);
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
