﻿using AdvancedRpcLib;
using AdvancedRpcLib.Channels.Tcp;
using AdvancedRpcLib.Serializers;
using Newtonsoft.Json;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AdvancedRpc
{
    class Dummy
    {
        private IRpcChannel _dummy;

        int Test(int a, int b)
        {
            return (int)Convert.ChangeType(_dummy.CallRpcMethod(20, "iijji",new object[] { a, b }), typeof(int));
        }
    }
   


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


            var client = new TcpRpcClientChannel(
                new RpcObjectRepository(),
                new JsonRpcSerializer(),
                new RpcMessageFactory(),
                IPAddress.Loopback,
                11234);

            await client.ConnectAsync();

            var testObj = await client.GetServerObjectAsync<ITestObject>();
            Console.WriteLine(testObj.SimpleCall());

            var sw = new Stopwatch();
            sw.Start();
            int j = 0;
            for(int i=0;i<1000;i++)
            {
                j+=testObj.Calculate(2, 8);
            }
            sw.Stop();
            Console.WriteLine(j);

            Console.ReadLine();
        }
    }



    //TODO: Ableitungshierarchien

    public interface ITestObject
    {
        string SimpleCall();

        int Calculate(int a, int b);
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
