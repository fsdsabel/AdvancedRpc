using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using AdvancedRpcLib.Channels;
using AdvancedRpcLib.Channels.NamedPipe;
using AdvancedRpcLib.Channels.Tcp;
using AdvancedRpcLib.Serializers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;

[assembly: InternalsVisibleTo("RpcDynamicTypes, PublicKey=00240000048000009400000006020000002400005253413100040000010001005D24F196FD9ACCF8894FFC5F6EA20B5EC25031179D601884ED16E337A9F6E48B8457839F02D5C15E37C97F7F0D9A1B918343B19351D931EB08EA6853349F823244746DEF9129DEF760AC196D7579E63C92E7ACEF48FE2587994F15FB35689EE32209E227D0F7E045882B0B64CCD303BB38C17F5F0F7C2EC8BC6E05494B9791B4")]

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
            TokenImpersonationLevel tokenImpersonationLevel = TokenImpersonationLevel.None) where T:class
        {
            _serverChannel = await CreateServer(instance, type, serializer, tokenImpersonationLevel);
            switch (type)
            {
                case ChannelType.Tcp:
                {
                    var client = _clientChannel = await CreateClient(type, serializer);
                    return await client.GetServerObjectAsync<T>();
                }
                case ChannelType.NamedPipe:
                {
                    var client = _clientChannel = await CreateClient(type, serializer, tokenImpersonationLevel);
                    return await client.GetServerObjectAsync<T>();
                }
                default:
                    throw new NotSupportedException();
            }
        }

        private async Task<IRpcServerChannel> CreateServer<T>(T instance, ChannelType channelType, IRpcSerializer serializer = null,
            TokenImpersonationLevel tokenImpersonationLevel = TokenImpersonationLevel.None,
            IRpcObjectRepository localRepository = null) where T: class
        {
            if (serializer == null)
            {
                serializer = new BinaryRpcSerializer();
            }
            switch (channelType)
            {
                case ChannelType.Tcp:
                {
                    var server = new TcpRpcServerChannel(
                        serializer,
                        new RpcMessageFactory(),
                        IPAddress.Loopback,
                        11234,
                        localRepository);
                    if(instance != null) server.ObjectRepository.RegisterSingleton(instance);
                    await server.ListenAsync();

                    return server;
                }
                case ChannelType.NamedPipe:
                {
                    _pipeName = Guid.NewGuid().ToString();
                    var server = new NamedPipeRpcServerChannel(
                        serializer,
                        new RpcMessageFactory(),
                        _pipeName,
                        localRepository:localRepository);
                    if (instance != null) server.ObjectRepository.RegisterSingleton(instance);
                    await server.ListenAsync();

                    return server;
                }
                default:
                    throw new NotSupportedException();
            }
        }

        private async Task<IRpcClientChannel> CreateClient(ChannelType channelType, IRpcSerializer serializer = null,
            TokenImpersonationLevel tokenImpersonationLevel = TokenImpersonationLevel.None,
            IRpcObjectRepository localRepository = null, Func<IRpcObjectRepository> remoteRepository = null)
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
                        11234,
                        localRepository,
                        remoteRepository);

                    await client.ConnectAsync();
                    return client;
                }
                case ChannelType.NamedPipe:
                {
                    var client = new NamedPipeRpcClientChannel(
                        serializer,
                        new RpcMessageFactory(),
                        _pipeName,
                        tokenImpersonationLevel,
                        localRepository,
                        remoteRepository);

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
            Assert.Inconclusive();

            // TODO should we really make this work?
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
        public async Task CallWithOverloadedMethodsAndDifferentArgCountSucceeds(ChannelType type)
        {
            var o = new Overload();
            var rpc = await Init<IOverload>(o, type);
            Assert.AreEqual(42, rpc.Test());
            Assert.AreEqual(43, rpc.Test(43));
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task CallWithOverloadedMethodsAndDifferentArgTypeSucceeds(ChannelType type)
        {
            Assert.Inconclusive("Different argument type not yet supported");
            var o = new Overload();
            var rpc = await Init<IOverload>(o, type);
            Assert.AreEqual("42", rpc.Test2("42"));
            Assert.AreEqual(42, rpc.Test2(42));
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task CallWithObjectsSucceeds(ChannelType type)
        {
            var o = new TestObject();
            var test = (await Init<ITestObject>(o, type)).ReflectObject(new SubObject {Name = "test"});

            Assert.IsInstanceOfType(test, typeof(ISubObject));
            Assert.AreEqual("test", ((ISubObject) test).Name);
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task CallWithSerializableObjectsSucceeds(ChannelType type)
        {
            var o = new TestObject();
            var test = (await Init<ITestObject>(o, type)).ReflectObject(new SerializableSubObject { Name = "test" });

            Assert.IsInstanceOfType(test, typeof(ISubObject));
            Assert.AreEqual("test", ((ISubObject)test).Name);
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task ReturningNullSucceeds(ChannelType type)
        {
            var o = new TestObject();
            var r = (await Init<ITestObject>(o, type)).ReflectObj(null);
            Assert.IsNull(r);
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
        public async Task CallbacksWork(ChannelType type)
        {
            bool callbackCalled = false;
            var o = new TestObject();
            var proxy = await Init<ITestObject>(o, type);
            
            proxy.SetCallback(i =>
            {
                Assert.AreEqual(42, i);
                callbackCalled = true;
            });
            
            GC.Collect(2);
            GC.WaitForPendingFinalizers();

            o.InvokeCallback();

            Assert.IsTrue(callbackCalled);
        }


        class InnerTest
        {
            public async Task Run(RpcTests tests, ChannelType type)
            {

                bool wasCalled = false;

                void OnChanged(object sender, CustomEventArgs e)
                {
                    wasCalled = true;
                }

                void AssertHasHandler(TestObject obj, bool shouldhave)
                {
                    var handler = typeof(TestObject).GetField(nameof(TestObject.TestEvent), BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj) as Delegate;

                    if (shouldhave)
                    {
                        Assert.IsNotNull(handler);
                    }
                    else
                    {
                        Assert.IsNull(handler);
                    }
                }

                var o = new TestObject();
                var proxy = await tests.Init<ITestObject>(o, type);
                proxy.TestEvent += OnChanged;

                GC.Collect(2);
                GC.WaitForPendingFinalizers();

                AssertHasHandler(o, true);
                o.InvokeTestEvent();
                Assert.IsTrue(wasCalled);
                wasCalled = false;
                proxy.TestEvent -= OnChanged;
                AssertHasHandler(o, false);
                o.InvokeTestEvent();
                Assert.IsFalse(wasCalled);
            }
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public void EventHandlerRemovedRemovesInstanceFromRepository(ChannelType type)
        {
            Task.Run(async () =>
            {
                await new InnerTest().Run(this, type); // make sure delegate is collectible
            }).Wait();


            GC.Collect(2);
            GC.WaitForPendingFinalizers();

            var localRepo = _clientChannel.GetPrivate<IRpcObjectRepository>("LocalRepository");
            var rpcObjects = localRepo.GetPrivate<HashSet<RpcHandle>>("_rpcObjects");

            Assert.AreEqual(0, rpcObjects.Count); // the event handler should be removed
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

            Assert.IsTrue(Task.WaitAll(threads.ToArray(), 20000));
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
            // fails atm .. investigate
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
        public async Task ConnectedChannels_ReturnsCorrectChannelCount()
        {
            var o = new TestObject();
            var rpc = await Init<ITestObject>(o, ChannelType.NamedPipe); // one client channel
            using (await CreateClient(ChannelType.NamedPipe))
            {
                var channels = ((IRpcServerChannel<NamedPipeTransportChannel>) _serverChannel).ConnectedChannels;
                
                Assert.AreEqual(2, channels.Count);
            }
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

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task InternalInterfaceWorks(ChannelType type)
        {
            // for this to work we need [assembly: InternalsVisibleTo("RpcDynamicTypes, Public Key=...")] see top of file
            var o = new TestObject();
            using (await CreateServer(o, type,
                localRepository: new RpcObjectRepository(false) {AllowNonPublicInterfaceAccess = true}))
            {
                using (var client = await CreateClient(type,
                    localRepository: new RpcObjectRepository(true) { AllowNonPublicInterfaceAccess = true },
                    remoteRepository: () => new RpcObjectRepository(false) { AllowNonPublicInterfaceAccess = true }))
                {

                    var totest = await client.GetServerObjectAsync<IInternalInterface>();
                    totest.ShouldNotBeVisible();
                }
            }
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task TypeSingletonWorks(ChannelType type)
        {
            // for this to work we need [assembly: InternalsVisibleTo("RpcDynamicTypes, Public Key=...")] see top of file
            
            using (var server = await CreateServer<object>(null, type))
            {
                server.ObjectRepository.RegisterSingleton<TestObject>();
                server.ObjectRepository.RegisterSingleton<SubObject>();
                using (var client = await CreateClient(type))
                {
                    var so = await client.GetServerObjectAsync<ISubObject>();
                    Assert.IsNull(so.Name);
                    var totest = await client.GetServerObjectAsync<ITestObject>();
                    Assert.AreEqual("test", totest.Reflect("test"));
                }
            }
        }


        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        public async Task RoundTripSucceeds(ChannelType type)
        {
            var o = new RoundTrip();
            var co = await Init<IRoundTrip>(o, type);

            var testObj = co.GetObject();
            var testObj2 = co.GetObject();
            var verified = co.VerifyObject(testObj);

            Assert.IsNotNull(verified);
            Assert.AreSame(testObj, verified);
            Assert.AreSame(testObj, testObj2);
        }

        [DataTestMethod]
        [DataRow(ChannelType.NamedPipe)]
        [DataRow(ChannelType.Tcp)]
        [ExpectedException(typeof(ArgumentException))]
        public async Task ExceptionInConstructorIsDelivered(ChannelType type)
        {

            using (var server = await CreateServer<IConstructorException>(null, type))
            {
                using (var client = await CreateClient(type))
                {

                    server.ObjectRepository.RegisterSingleton<ConstructorException>();

                    await client.GetServerObjectAsync<IConstructorException>();
                }
            }
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

            object ReflectObject(object data);

            ISubObject SetNameFromSubObject(ISubObject obj);

            string Reflect(string s);

            ISubObject ReflectObj(ISubObject s);

            void ThrowException();

            void SetCallback(Action<int> callback);

            void InvokeCallback();
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
            private Action<int> _callback;
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

            public object ReflectObject(object data)
            {
                return data;
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

            public ISubObject ReflectObj(ISubObject s)
            {
                return s;
            }

            public void ThrowException()
            {
                throw new ArgumentException("Fehler", "testparam");
            }

            public void SetCallback(Action<int> callback)
            {
                _callback = callback;
            }

            public void InvokeCallback()
            {
                _callback(42);
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

        public interface IRoundTrip
        {
            ISubObject GetObject();

            ISubObject VerifyObject(ISubObject obj);
        }

        class RoundTrip : IRoundTrip
        {
            private ISubObject _subObject = new SubObject {Name = "Test"};

            public ISubObject GetObject()
            {
                return _subObject;
            }

            public ISubObject VerifyObject(ISubObject obj)
            {
                return ReferenceEquals(_subObject, obj) ? obj : null;
            }
        }

        [Serializable]
        class SerializableSubObject : ISubObject
        {
            public string Name { get; set; }
        }

        public interface IOverload
        {
            int Test();

            int Test(int i);

            string Test2(string i);
            int Test2(int i);
        }

        class Overload : IOverload
        {
            public int Test()
            {
                return 42;
            }

            public int Test(int i)
            {
                return i;
            }

            public string Test2(string i)
            {
                return i;
            }

            public int Test2(int i)
            {
                return i;
            }
        }

        public interface IConstructorException {}

        class ConstructorException : IConstructorException
        {
            public ConstructorException()
            {
                throw new ArgumentException();
            }
        }
    }
}
