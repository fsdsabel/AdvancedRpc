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
            var proxy = new RpcObjectRepository().GetProxyObject<ISimple1>(mock.Object, 0);
            proxy.Test();
            mock.Verify(c => c.CallRpcMethod(0, "Test", new Type[0], new object[0], typeof(void)));
        }

        [TestMethod]
        public void GetProxyObject_SimpleType_Succeeds2()
        {
            var mock = new Mock<IRpcChannel>();
            mock.Setup(p => p.CallRpcMethod(0, "Test2", new Type[] { typeof(int), typeof(string) }, new object[] { 1, "a" }, typeof(int))).Returns(2);
            var proxy = new RpcObjectRepository().GetProxyObject<ISimple2>(mock.Object, 0);
            

            proxy.Test();
            mock.Verify(c => c.CallRpcMethod(0, "Test", new Type[0], new object[0], typeof(void)));

            proxy.Test2(1, "a");
            mock.Verify(c => c.CallRpcMethod(0, "Test2", new Type[] { typeof(int), typeof(string) }, new object[] { 1, "a" }, typeof(int)));
        }

        [TestMethod]
        public void GetProxyObject_TypeWithEvent_Succeeds()
        {
            var mock = new Mock<IRpcChannel>();
            var proxy = new RpcObjectRepository().GetProxyObject<ISimpleEvent>(mock.Object, 0);

            proxy.TestEvent += (s, e) =>
            {

            };

            
            
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

        public interface ISimpleEvent
        {
            event EventHandler<MyEventArgs> TestEvent;
        }
    }
}
