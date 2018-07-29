using System;
using System.Collections.Generic;
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
            server.ObjectRepository.RegisterSingleton(instance);
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
            var co = await Init<ITestObject2>(o);
            co.CallMe();
            Assert.AreEqual("callme2", co.CallMe2());
            Assert.IsTrue(o.WasCalled);
        }

        [TestMethod]
        public async Task CallsSameObjectWithDifferentInterfacesSucceeds()
        {
            var o = new TestObject();
            var co = await Init<ITestObject>(o);
            var co2 = await _clientChannel.GetServerObjectAsync<ITestObject2>();
            co.CallMe();
            Assert.IsTrue(o.WasCalled);
            Assert.IsTrue(co2.WasCalled);
            o.WasCalled = false;
            co2.CallMe();
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

        [TestMethod]
        public async Task SendAndReceiveNullSucceeds()
        {
            var o = new TestObject();
            Assert.IsNull((await Init<ITestObject>(o)).Reflect(null));
        }

        [TestMethod]
        public async Task ObjectParameterSucceeds()
        {
            var o = new TestObject();
            Assert.AreEqual("test", (await Init<ITestObject>(o)).SetNameFromSubObject(new SubObject { Name = "test" }).Name);
        }


        [TestMethod, ExpectedException(typeof(RpcFailedException))]
        public async Task CallWithNonExistingServerFails()
        {
            var proxy = await Init<ITestObject>(new TestObject());
            _serverChannel.Dispose();
            proxy.CallMe();
        }

        [TestMethod]
        public async Task EventHandlersWork()
        {
            bool eventHandlerCalled = false;
            var o = new TestObject();
            var proxy = await Init<ITestObject>(o);
            proxy.TestEvent += (s, e) =>
            {
                eventHandlerCalled = true;
                Assert.AreEqual("test", e.Data);
                // try to call back
                Assert.AreEqual("data", proxy.Reflect("data"));
            };

            o.InvokeTestEvent();

            Assert.IsTrue(eventHandlerCalled);
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


        [TestMethod]
        public async Task HeavyMultithreadingWorks()
        {
            var proxy = await Init<ITestObject>(new TestObject());
            var threads = new List<Task<bool>>();
            for (int i = 0; i < 10; i++)
            {
                int mi = i;
                threads.Add(new Task<bool>(delegate
                {
                    for (int j = 0; j < 100; j++)
                    {
                        if (proxy.SimpleCalc(mi * 100, j) != mi * 100 + j)
                        {
                            return false;
                        }
                    }
                    return true;
                }));
            }

            foreach (var t in threads) t.Start();

            Assert.IsTrue(Task.WaitAll(threads.ToArray(), 200000));
            Assert.IsTrue(threads.TrueForAll(t => t.Result));
        }

        [TestMethod]
        public async Task MultipleClientsWork()
        {
            var proxy = await Init<ITestObject>(new TestObject());
            var client2 = new TcpRpcClientChannel(
                new JsonRpcSerializer(),
                new RpcMessageFactory(),
                IPAddress.Loopback,
                11234);

            await client2.ConnectAsync();
            var proxy2 = await client2.GetServerObjectAsync<ITestObject>();



            var threads = new List<Task<bool>>();
            for (int i = 0; i < 10; i++)
            {
                int mi = i;
                threads.Add(new Task<bool>(delegate
                {
                    ITestObject pr = mi % 2 == 0 ? proxy : proxy2;

                    for (int j = 0; j < 100; j++)
                    {
                        if (pr.SimpleCalc(mi * 100, j) != mi * 100 + j)
                        {
                            return false;
                        }
                    }
                    return true;
                }));
            }

            foreach (var t in threads) t.Start();

            Assert.IsTrue(Task.WaitAll(threads.ToArray(), 20000));
            Assert.IsTrue(threads.TrueForAll(t => t.Result));
        }


        [Serializable]
        public class CustomEventArgs : EventArgs
        {
            public string Data { get; set; }
        }

        public interface ITestObject
        {
            event EventHandler<CustomEventArgs> TestEvent;
            
            bool WasCalled { get; set; }

            string Property { get; set; }

            string SimpleStringResult();

            void CallMe();

            int SimpleCalc(int a, int b);

            string SimpleStringConcat(string a, string b);

            ISubObject GetSubObject(string name);

            ISubObject SetNameFromSubObject(ISubObject obj);

            string Reflect(string s);
        }

        public interface ISubObject
        {
            string Name { get; }
        }

        class SubObject : ISubObject
        {
            public string Name { get; set; }
        }

        public interface ITestObject2 : ITestObject
        {
            string CallMe2();
        }

        class TestObject : ITestObject2
        {
            public bool WasCalled { get; set; }


            public string Property { get; set; } = "Test";

            public event EventHandler<CustomEventArgs> TestEvent; // TODO: CustomEventArgs

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

            public string CallMe2()
            {
                return "callme2";
            }

            public ISubObject SetNameFromSubObject(ISubObject obj)
            {
                return new SubObject { Name = obj.Name };
            }

            public string Reflect(string s)
            {
                return s;
            }

            internal void InvokeTestEvent()
            {
                TestEvent?.Invoke(this, new CustomEventArgs { Data = "test" });
            }
        }
    }
}
