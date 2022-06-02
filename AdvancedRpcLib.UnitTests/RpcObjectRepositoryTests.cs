using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace AdvancedRpcLib.UnitTests
{
    [TestClass]
    public class RpcObjectRepositoryTests
    {
        [TestMethod]
        public void GetProxyObject_SimpleType_Succeeds()
        {

            var mock = new Mock<IRpcChannel>();
            var proxy = new RpcObjectRepository(false).GetProxyObject<ISimple1>(mock.Object, Guid.Empty);
            proxy.Test();
            mock.Verify(c => c.CallRpcMethod(Guid.Empty, "Test", new Type[0], new object[0], typeof(void)));
        }

        [TestMethod]
        public void GetProxyObject_SimpleType_Succeeds2()
        {
            var mock = new Mock<IRpcChannel>();
            mock.Setup(p => p.CallRpcMethod(Guid.Empty, "Test2", new Type[] { typeof(int), typeof(string) }, new object[] { 1, "a" }, typeof(int))).Returns(2);
            var proxy = new RpcObjectRepository(false).GetProxyObject<ISimple2>(mock.Object, Guid.Empty);
            

            proxy.Test();
            mock.Verify(c => c.CallRpcMethod(Guid.Empty, "Test", new Type[0], new object[0], typeof(void)));

            proxy.Test2(1, "a");
            mock.Verify(c => c.CallRpcMethod(Guid.Empty, "Test2", new Type[] { typeof(int), typeof(string) }, new object[] { 1, "a" }, typeof(int)));
        }

        [TestMethod]
        public void GetProxyObject_TypeWithEvent_Succeeds()
        {
            var mock = new Mock<IRpcChannel>();
            var proxy = new RpcObjectRepository(false).GetProxyObject<ISimpleEvent>(mock.Object, Guid.Empty);

            proxy.TestEvent += (s, e) =>
            {

            };

        }

        [TestMethod]
        public void GetProxyObject_TypeWithProperty_CreatesCorrectTypeDefinition()
        {
            var mock = new Mock<IRpcChannel>();
            var proxy = new RpcObjectRepository(false).GetProxyObject<IProperty>(mock.Object, Guid.Empty);

            Assert.IsNotNull(proxy.GetType().GetProperty(nameof(IProperty.TestProperty)));
        }

        public interface ISimple1
        {
            void Test();
        }

        public interface ISimple2 : ISimple1
        {
            int Test2(int a, string s);
        }

        public class MyEventArgs : EventArgs
        {

        }

        public interface IProperty
        {
            string TestProperty { get; set; }
        }

        public interface ISimpleEvent
        {
            event EventHandler<MyEventArgs> TestEvent;
        }
    }
}
