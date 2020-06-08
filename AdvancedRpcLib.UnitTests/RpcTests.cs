using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using AdvancedRpcLib.Channels;
using AdvancedRpcLib.Channels.NamedPipe;
using AdvancedRpcLib.Channels.Tcp;
using AdvancedRpcLib.Serializers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;

namespace AdvancedRpcLib.UnitTests
{
    [TestClass]
    public partial class RpcTests
    {
        private IRpcServerChannel _serverChannel;
        private IRpcClientChannel _clientChannel;
        private string _pipeName;

        public enum ChannelType
        {
            Tcp,
            NamedPipe
        }

        private async Task<T> Init<T>(T instance, ChannelType type, IRpcSerializer serializer = null, 
            TokenImpersonationLevel tokenImpersonationLevel = TokenImpersonationLevel.None)
        {
            if (serializer == null)
            {
                serializer = new BinaryRpcSerializer();
            }
            switch (type)
            {
                case ChannelType.Tcp:
                    {
                        var server = _serverChannel = new TcpRpcServerChannel(
                            serializer,
                            new RpcMessageFactory(),
                            IPAddress.Loopback,
                            11234);
                        server.ObjectRepository.RegisterSingleton(instance);
                        await server.ListenAsync();


                        var client = _clientChannel = await CreateClient(type, serializer);
                        return await client.GetServerObjectAsync<T>();
                    }
                case ChannelType.NamedPipe:
                    {
                        _pipeName = Guid.NewGuid().ToString();
                        var server = _serverChannel = new NamedPipeRpcServerChannel(
                            serializer,
                            new RpcMessageFactory(),
                            _pipeName);
                        server.ObjectRepository.RegisterSingleton(instance);
                        await server.ListenAsync();


                        var client = _clientChannel = await CreateClient(type, serializer, tokenImpersonationLevel);
                        return await client.GetServerObjectAsync<T>();
                    }
                default:
                    throw new NotSupportedException();
            }


        }

        private async Task<IRpcClientChannel> CreateClient(ChannelType channelType, IRpcSerializer serializer = null,
            TokenImpersonationLevel tokenImpersonationLevel = TokenImpersonationLevel.None)
        {
            if (serializer == null)
            {
                serializer = new BinaryRpcSerializer();
            }
            switch (channelType)
            {
                case ChannelType.Tcp:
                {

                    var client = new TcpRpcClientChannel(
                        serializer,
                        new RpcMessageFactory(),
                        IPAddress.Loopback,
                        11234);

                    await client.ConnectAsync();
                    return client;
                }
                case ChannelType.NamedPipe:
                {
                    var client = _clientChannel = new NamedPipeRpcClientChannel(
                        serializer,
                        new RpcMessageFactory(),
                        _pipeName,
                        tokenImpersonationLevel);

                    await client.ConnectAsync();
                    return client;
                }
                default:
                    throw new NotSupportedException();
            }
        }


        [TestCleanup]
        public void TestCleanup()
        {
            (_serverChannel as IDisposable)?.Dispose();
            (_clientChannel as IDisposable)?.Dispose();
            _serverChannel = null;
            _clientChannel = null;
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task SimpleCallSucceeds(ChannelType type)
        {
            var o = new TestObject();
            var co = await Init<ITestObject2>(o, type);
            co.CallMe();
            Assert.AreEqual("callme2", co.CallMe2());
            Assert.IsTrue(o.WasCalled);
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task CallsSameObjectWithDifferentInterfacesSucceeds(ChannelType type)
        {
            var o = new TestObject();
            var co = await Init<ITestObject>(o, type);
            var co2 = await _clientChannel.GetServerObjectAsync<ITestObject2>();
            co.CallMe();
            Assert.IsTrue(o.WasCalled);
            Assert.IsTrue(co2.WasCalled);
            o.WasCalled = false;
            co2.CallMe();
            Assert.IsTrue(o.WasCalled);
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task CallWithStringResultSucceeds(ChannelType type)
        {
            var o = new TestObject();
            Assert.AreEqual("dummy", (await Init<ITestObject>(o, type)).SimpleStringResult());
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task CallWithIntsSucceeds(ChannelType type)
        {
            var o = new TestObject();
            Assert.AreEqual(42, (await Init<ITestObject>(o, type)).SimpleCalc(40, 2));
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task CallWithStringsSucceeds(ChannelType type)
        {
            var o = new TestObject();
            Assert.AreEqual("42", (await Init<ITestObject>(o, type)).SimpleStringConcat("4", "2"));
        }


        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task PropertyGetSucceeds(ChannelType type)
        {
            var o = new TestObject();
            Assert.AreEqual("Test", (await Init<ITestObject>(o, type)).Property);
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task CallWithObjectResultSucceeds(ChannelType type)
        {
            var o = new TestObject();
            var result = (await Init<ITestObject>(o, type)).GetSubObject("a name");
            Assert.AreEqual("a name", result.Name);
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task CallWithLoopedObjectResultSucceeds(ChannelType type)
        {
            var o = new TestObject();
            var list = new List<ISubObject>();
            var t = await Init<ITestObject>(o, type);
            for (int i = 0; i < 10; i++)
            {
                list.Add(t.GetSubObject("a name" + i));
            }
            GC.Collect(2);
            GC.WaitForPendingFinalizers();

            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual("a name" + i, list[i].Name);
            }
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task SendAndReceiveNullSucceeds(ChannelType type)
        {
            var o = new TestObject();
            Assert.IsNull((await Init<ITestObject>(o, type)).Reflect(null));
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task ObjectParameterSucceeds(ChannelType type)
        {
            var o = new TestObject();
            Assert.AreEqual("test", (await Init<ITestObject>(o, type)).SetNameFromSubObject(new SubObject { Name = "test" }).Name);
        }


        [DataTestMethod, ExpectedException(typeof(RpcFailedException))]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task CallWithNonExistingServerFails(ChannelType type)
        {
            var proxy = await Init<ITestObject>(new TestObject(), type);
            (_serverChannel as IDisposable).Dispose();
            proxy.CallMe();
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task EventHandlersWork(ChannelType type)
        {
            bool eventHandlerCalled = false;
            var o = new TestObject();
            var proxy = await Init<ITestObject>(o, type);
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

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task EventHandlersWithMultipleClientsWork(ChannelType type)
        {
            bool eventHandlerCalled = false;
            bool eventHandler2Called = false;
            var o = new TestObject();
            var proxy = await Init<ITestObject>(o, type);


            var client2 = _clientChannel = await CreateClient(type);

            await client2.ConnectAsync();
            var proxy2 = await client2.GetServerObjectAsync<ITestObject>();

            proxy.TestEvent += (s, e) =>
            {
                eventHandlerCalled = true;
                Assert.AreEqual("test", e.Data);
                // try to call back
                Assert.AreEqual("data", proxy.Reflect("data"));
            };


            proxy2.TestEvent += (s, e) =>
            {
                eventHandler2Called = true;
            };

            o.InvokeTestEvent();

            Assert.IsTrue(eventHandlerCalled);
            Assert.IsTrue(eventHandler2Called);
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public void ClientDestructorCalled(ChannelType type)
        {
            InitPrivate(type);
            Assert.IsNotNull(_clientChannel.ObjectRepository.GetObject(_serverChannel.ObjectRepository.CreateTypeId<ITestObject>()));

            GC.Collect();
            GC.WaitForFullGCComplete();
            GC.WaitForPendingFinalizers();

            Assert.IsNull(_clientChannel.ObjectRepository.GetObject(_serverChannel.ObjectRepository.CreateTypeId<ITestObject>()));
            Assert.IsNotNull(_serverChannel.ObjectRepository.GetObject(_serverChannel.ObjectRepository.CreateTypeId<ITestObject>()));
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task CallWithLargeObjectsSucceeds(ChannelType type)
        {
            var o = new TestObject();
            var largeString = "".PadLeft(1024 * 1024 * 20, 'A');
            Assert.AreEqual(largeString, (await Init<ITestObject>(o, type)).Reflect(largeString));
        }

        private void InitPrivate(ChannelType type)
        {
            // make sure to not keep a reference to ITestObject .. if we use Task await we will keep one
            Init<ITestObject>(new TestObject(), type).GetAwaiter().GetResult();
        }


        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task HeavyMultithreadingWorks(ChannelType type)
        {
            var proxy = await Init<ITestObject>(new TestObject(), type);
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

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task MultipleClientsWork(ChannelType type)
        {
            var proxy = await Init<ITestObject>(new TestObject(), type);
            ITestObject proxy2 = null;

            if (type == ChannelType.Tcp)
            {
                var client2 = new TcpRpcClientChannel(
                    new BinaryRpcSerializer(),
                    new RpcMessageFactory(),
                    IPAddress.Loopback,
                    11234);

                await client2.ConnectAsync();
                proxy2 = await client2.GetServerObjectAsync<ITestObject>();
            }
            else if (type == ChannelType.NamedPipe)
            {
                var client2 = new NamedPipeRpcClientChannel(
                    new BinaryRpcSerializer(), 
                    new RpcMessageFactory(),
                   _pipeName);

                await client2.ConnectAsync();
                proxy2 = await client2.GetServerObjectAsync<ITestObject>();
            }






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

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        [ExpectedException(typeof(ArgumentException), AllowDerivedTypes = false)]
        public async Task RemoteExceptionIsPropagatedToClient(ChannelType type)
        {
            var o = new TestObject();
            ITestObject co = await Init<ITestObject>(o, type);
            try
            {
                co.ThrowException();
            }
            catch (ArgumentException ex)
            {
                StringAssert.StartsWith(ex.Message, "Fehler");
                Assert.AreEqual("testparam", ex.ParamName);
                // check if we can still use the channel
                Assert.AreEqual("Test", co.Property);
                throw;
            }
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        [ExpectedException(typeof(RpcFailedException))]
        public async Task InternalInterfaceShouldNotBeAvailable(ChannelType type)
        {
            var o = new TestObject();
            (await Init<IInternalInterface>(o, type)).ShouldNotBeVisible();
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        [ExpectedException(typeof(RpcFailedException))]
        public async Task ServerDownThrowsException(ChannelType type)
        {
            var o = new TestObject();
            var rpc = await Init<ITestObject>(o, type);
            
            _serverChannel.Dispose();
            rpc.CallMe();
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        [ExpectedException(typeof(RpcFailedException))]
        public async Task ClientDownThrowsException(ChannelType type)
        {
            var o = new TestObject();
            var rpc = await Init<ITestObject>(o, type);

            _clientChannel.Dispose();
            rpc.CallMe();
        }

        [TestMethod]
        public async Task NamedPipeServerContextObjectGetImpersonationUserNameWorks()
        {
            var o = new NamedPipeContextObject();
            var rpc = await Init<IContextObject>(o, ChannelType.NamedPipe, tokenImpersonationLevel: TokenImpersonationLevel.Identification);

            var remoteUser = rpc.UserName;
            Assert.AreEqual(Environment.UserName, remoteUser);
        }

        [TestMethod]
        public async Task NamedPipeServerContextObjectRunAsClientWorks()
        {
            var o = new NamedPipeContextObject();
            var rpc = await Init<IContextObject>(o, ChannelType.NamedPipe, tokenImpersonationLevel: TokenImpersonationLevel.Impersonation);

            var remoteUser = rpc.FullUserName;
            Assert.AreEqual(Environment.UserDomainName + "\\" + Environment.UserName, remoteUser);
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

            void ThrowException();
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

        internal interface IInternalInterface
        {
            void ShouldNotBeVisible();
        }

        class TestObject : ITestObject2, IInternalInterface
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

            public void ThrowException()
            {
                throw new ArgumentException("Fehler", "testparam");
            }

            internal void InvokeTestEvent()
            {
                TestEvent?.Invoke(this, new CustomEventArgs { Data = "test" });
            }

            public void ShouldNotBeVisible()
            {
            }
        }

        public interface IContextObject
        {
            string UserName { get; }

            string FullUserName { get; }
        }

        class NamedPipeContextObject : IContextObject, IRpcServerContextObject
        {
            public string UserName => RpcChannel.GetImpersonationUserName();
            public string FullUserName => GetRemoteIdentity().Name;

            public ITransportChannel RpcChannel { get; set; }


            
            private WindowsIdentity GetRemoteIdentity()
            {
                WindowsIdentity result = null;
                RpcChannel.RunAsClient(()=> result = WindowsIdentity.GetCurrent());
                return result;
            }


        }
    }
}
