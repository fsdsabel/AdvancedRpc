using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedRpcLib.UnitTests
{
    partial class RpcTests
    {
        [TestMethod]
        public async Task StreamingResultsWork()
        {
            var o = new StreamingTestObject();
            var co = await Init<IStreamingTest>(o, ChannelType.NamedPipe);

            var result = co.GetStrings();

            // IEnumerables work, but might be slow for a lot of results (every MoveNext is an rpc call)
            CollectionAssert.AreEqual(Enumerable.Repeat("This is a test", 100).ToList(), result.ToList());
        }

        [TestMethod]
        public async Task StreamingArgumentsWork()
        {
            var o = new StreamingTestObject();
            var co = await Init<IStreamingTest>(o, ChannelType.NamedPipe);

            for (int i = 0; i < 10; i++)
            {
                var data = Enumerable.Repeat("This is a test", 10);

                var result = co.ReturnAsArray(data);
                
                // IEnumerables work, but might be slow for a lot of results (every MoveNext is an rpc call)
                CollectionAssert.AreEqual(data.ToList(), result.ToList());
            }
        }

        [TestMethod]
        public async Task ArrayResultsWork()
        {
            var o = new StreamingTestObject();
            var co = await Init<IStreamingTest>(o, ChannelType.NamedPipe);

            var result = co.GetStringsArray();

            CollectionAssert.AreEqual(Enumerable.Repeat("This is a test", 10000).ToList(), result.ToList());
        }

        public interface IStreamingTest
        {
            IEnumerable<string> GetStrings();
            
            string[] GetStringsArray();

            string[] ReturnAsArray(IEnumerable<string> value);
        }

        class StreamingTestObject : IStreamingTest
        {
            public IEnumerable<string> GetStrings()
            {
                return Enumerable.Repeat("This is a test", 100);
            }

            public string[] GetStringsArray()
            {
                return Enumerable.Repeat("This is a test", 10000).ToArray();
            }

            public string[] ReturnAsArray(IEnumerable<string> value)
            {
                return value.ToArray();
            }
        }
    }
}
