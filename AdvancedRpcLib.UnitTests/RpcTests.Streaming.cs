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

        [TestMethod]
        public async Task ObjectArrayResultsWork()
        {
            var o = new ObjectResult();
            var co = await Init<IObjectResult>(o, ChannelType.NamedPipe);

            var objects = new SubObject[]
            {
                new SubObject {Name = "1"},
                new SubObject {Name = "2"},
                new SubObject {Name = "3"},
            };

            var result = co.GetObjects(objects);

            Assert.AreEqual(objects.Length, result.Length);
            for (int i = 0; i < objects.Length; i++)
            {
                Assert.AreEqual(objects[i].Name, result[i].Name);
            }
        }

        [TestMethod]
        public async Task ArrayOfBuiltInTypesIsSerializedAsArray()
        {
            var o = new ByteArrayTest();
            var co = await Init<IByteArrayTest>(o, ChannelType.NamedPipe);

            var result = co.GetDataBytes(new byte[] {1,2,3});


            CollectionAssert.AreEqual(new byte[] {1, 2, 3}.ToList(), result.ToList());
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

        public interface IByteArrayTest
        {
            byte[] GetDataBytes(byte[] data);
        }

        class ByteArrayTest : IByteArrayTest
        {
            public byte[] GetDataBytes(byte[] data)
            {
                return data;
            }
        }

        public interface IObjectResult
        {
            ISubObject[] GetObjects(ISubObject[] objects);
        }

        public class ObjectResult : IObjectResult
        {
            public ISubObject[] GetObjects(ISubObject[] objects)
            {
                return objects;
            }
        }
    }
}
