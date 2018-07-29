﻿using System;
using System.Net;
using System.Threading.Tasks;
using AdvancedRpcLib.Channels.Tcp;
using AdvancedRpcLib.Serializers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedRpcLib.Tests
{
    [TestClass]
    public class UnitTest1
    {
        private TcpRpcServerChannel _serverChannel;
        private TcpRpcClientChannel _clientChannel;

        private async Task<T> Init<T>(T instance)
        {   
            var server = _serverChannel = new TcpRpcServerChannel(                
                new JsonRpcSerializer(),
                new RpcMessageFactory(),
                IPAddress.Loopback,
                11234);
            server.ObjectRepository.RegisterSingleton<T>(instance);
            await server.ListenAsync();


            var client = _clientChannel = new TcpRpcClientChannel(                
                new JsonRpcSerializer(),
                new RpcMessageFactory(),
                IPAddress.Loopback,
                11234);

            await client.ConnectAsync();
            return await client.GetServerObjectAsync<T>();
        }


        [TestCleanup]
        public void TestCleanup()
        {
            _serverChannel?.Dispose();
            _clientChannel?.Dispose();
            _serverChannel = null;
            _clientChannel = null;
        }

        [TestMethod]
        public async Task SimpleCallSucceeds()
        {
            var o = new TestObject();
            (await Init<ITestObject>(o)).CallMe();

            Assert.IsTrue(o.WasCalled);
        }

        [TestMethod]
        public async Task CallWithStringResultSucceeds()
        {
            var o = new TestObject();
            Assert.AreEqual("dummy", (await Init<ITestObject>(o)).SimpleStringResult());
        }

        [TestMethod]
        public async Task CallWithIntsSucceeds()
        {
            var o = new TestObject();
            Assert.AreEqual(42, (await Init<ITestObject>(o)).SimpleCalc(40, 2));
        }

        [TestMethod]
        public async Task CallWithStringsSucceeds()
        {
            var o = new TestObject();
            Assert.AreEqual("42", (await Init<ITestObject>(o)).SimpleStringConcat("4", "2"));
        }


        [TestMethod]
        public async Task PropertyGetSucceeds()
        {
            var o = new TestObject();
            Assert.AreEqual("Test", (await Init<ITestObject>(o)).Property);
        }

        [TestMethod]
        public async Task CallWithObjectResultSucceeds()
        {
            var o = new TestObject();
            var result = (await Init<ITestObject>(o)).GetSubObject("a name");
            Assert.AreEqual("a name", result.Name);
        }

        [TestMethod, ExpectedException(typeof(RpcFailedException))]
        public async Task CallWithNonExistingServerFails()
        {
            var proxy = await Init<ITestObject>(new TestObject());
            _serverChannel.Dispose();
            proxy.CallMe();
        }

        [TestMethod]
        public void ClientDestructorCalled()
        {
            InitPrivate();
            Assert.IsNotNull(_clientChannel.ObjectRepository.GetObject(_serverChannel.ObjectRepository.CreateTypeId<ITestObject>()));

            GC.Collect();
            GC.WaitForFullGCComplete();
            GC.WaitForPendingFinalizers();

            Assert.IsNull(_clientChannel.ObjectRepository.GetObject(_serverChannel.ObjectRepository.CreateTypeId<ITestObject>()));
            Assert.IsNotNull(_serverChannel.ObjectRepository.GetObject(_serverChannel.ObjectRepository.CreateTypeId<ITestObject>()));
        }

        private void InitPrivate()
        {
            // make sure to not keep a reference to ITestObject .. if we use Task await we will keep one
            Init<ITestObject>(new TestObject()).GetAwaiter().GetResult();
        }


        public interface ITestObject
        {
            string Property { get; set; }

            string SimpleStringResult();

            void CallMe();

            int SimpleCalc(int a, int b);

            string SimpleStringConcat(string a, string b);

            ISubObject GetSubObject(string name);
        }

        public interface ISubObject
        {
            string Name { get; }
        }

        class SubObject : ISubObject
        {
            public string Name { get; set; }
        }

        class TestObject : ITestObject
        {
            public bool WasCalled;


            public string Property { get; set; } = "Test";

            public void CallMe()
            {
                WasCalled = true;
            }

            public string SimpleStringResult()
            {
                return "dummy";
            }

            public int SimpleCalc(int a, int b)
            {
                return a + b;
            }

            public string SimpleStringConcat(string a, string b)
            {
                return a + b;
            }

            public ISubObject GetSubObject(string name)
            {
                return new SubObject { Name = name };
            }

        }
    }
}
