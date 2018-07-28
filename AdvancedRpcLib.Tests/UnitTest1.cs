using System;
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
            var serverRepo = new RpcObjectRepository();
            serverRepo.RegisterSingleton<T>(instance);
            var server = _serverChannel = new TcpRpcServerChannel(
                serverRepo,
                new JsonRpcSerializer(),
                new RpcMessageFactory(),
                IPAddress.Loopback,
                11234);
            await server.ListenAsync();


            var client = _clientChannel = new TcpRpcClientChannel(
                new RpcObjectRepository(),
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

        public interface ITestObject
        {
            string SimpleStringResult();

            void CallMe();

            int SimpleCalc(int a, int b);

            string SimpleStringConcat(string a, string b);
        }

        class TestObject : ITestObject
        {
            public bool WasCalled;

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
        }
    }
}
