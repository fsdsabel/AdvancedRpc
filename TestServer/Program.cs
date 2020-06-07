using AdvancedRpc;
using AdvancedRpcLib;
using AdvancedRpcLib.Channels.Tcp;
using AdvancedRpcLib.Serializers;
using System;
using System.IO.Pipes;
using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using AdvancedRpcLib.Channels;
using AdvancedRpcLib.Channels.NamedPipe;
using Microsoft.Extensions.Logging;

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
            const LogLevel logLevel = LogLevel.Error;
            var loggerFactory = LoggerFactory.Create(builder =>
                builder
                    .AddFilter("AdvancedRpcLib", logLevel)
                    .AddConsole(o => o.LogToStandardErrorThreshold = logLevel));

            // allow all authenticated users to access the pipe
            var ps = new PipeSecurity();
            var sid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            var everyone = sid.Translate(typeof(NTAccount));
            ps.AddAccessRule(new PipeAccessRule(everyone, PipeAccessRights.ReadWrite, AccessControlType.Allow));
            // we need to add the current user so we can open more than one server pipes
            ps.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));

            var server = new NamedPipeRpcServerChannel(new BinaryRpcSerializer(), new RpcMessageFactory(), "test", ps,
                loggerFactory: loggerFactory);
            server.ObjectRepository.RegisterSingleton(new TestObject());
            await server.ListenAsync();

            Console.WriteLine("Press key to quit");
            Console.ReadKey();
        }
    }

    class TestObject : ITestObject, IRpcServerContextObject
    {
        public int Calculate(int a, int b)
        {
            return a + b;
        }

        public string SimpleCall()
        {
            return "42";
        }

        public string Username
        {
            get
            {
                WindowsIdentity result = null;
                RpcChannel.RunAsClient(() => result = WindowsIdentity.GetCurrent());
                Console.WriteLine($"Server user: {WindowsIdentity.GetCurrent().Name}, Client user: {result.Name}");
                return result.Name;
            }

        }

        public ITransportChannel RpcChannel { get; set; }
    }
}
