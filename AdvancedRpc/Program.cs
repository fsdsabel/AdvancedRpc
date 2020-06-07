using AdvancedRpcLib;
using AdvancedRpcLib.Channels.Tcp;
using AdvancedRpcLib.Serializers;
using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using AdvancedRpcLib.Channels.NamedPipe;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace AdvancedRpc
{
    
    class Dummy
    {
        private IRpcChannel _dummy;

        public static void Invoke(object sender, EventArgs e)
        {
            Invoke(sender, e);
        }

        /*int Test(int a, int b)
        {
            return (int)Convert.ChangeType(_dummy.CallRpcMethod(20, "iijji",new object[] { a, b }, typeof(int)), typeof(int));
        }*/
    }
   


    class Program
    {
        

        static async Task Main(string[] args)
        {
            /*
            var server = new TcpRpcServerChannel(
                new JsonRpcSerializer(),
                new RpcMessageFactory(),
                IPAddress.Loopback,
                11234);
            server.ObjectRepository.RegisterSingleton(new TestObject());
            await server.ListenAsync();*/


            /*var client = new TcpRpcClientChannel(         
                new JsonRpcSerializer(),
                new RpcMessageFactory(),
                IPAddress.Loopback,
                11234);*/

            const LogLevel logLevel = LogLevel.Error;

            var client = new NamedPipeRpcClientChannel(
                new BinaryRpcSerializer(),
                new RpcMessageFactory(),
                "test",
                TokenImpersonationLevel.Impersonation,
                loggerFactory: LoggerFactory.Create(builder =>
                    builder
                        .AddFilter("AdvancedRpcLib", logLevel)
                        .AddConsole(o => o.LogToStandardErrorThreshold = logLevel)));

            await client.ConnectAsync(TimeSpan.FromSeconds(5));
            
            var testObj = await client.GetServerObjectAsync<ITestObject>();

            Console.WriteLine($"Remote user: {testObj.Username}");

            Console.WriteLine(testObj.SimpleCall());

            var sw = new Stopwatch();
            sw.Start();
            int j = 0;
            for(int i=0;i<1000;i++)
            {
                j+=testObj.Calculate(2, 8);
            }
            sw.Stop();
            Console.WriteLine(sw.Elapsed);
            
            Console.ReadLine();
        }

        private static void Run(TcpRpcClientChannel client)
        {
            client.GetServerObjectAsync<ITestObject>().GetAwaiter().GetResult();
        }
    }
    
    
    public interface ITestObjectBase
    {
        string SimpleCall();
    }

    public interface ITestObject : ITestObjectBase
    {
        string Username { get; }
        int Calculate(int a, int b);
    }

}
