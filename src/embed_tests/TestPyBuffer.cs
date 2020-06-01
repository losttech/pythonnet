using System;
using System.Text;
using NUnit.Framework;
using Python.Runtime;
using Python.Runtime.Codecs;

namespace Python.EmbeddingTest {
    public class TestPyBuffer
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            PythonEngine.Initialize();
            TupleCodec<ValueTuple>.Register();
        }

        [OneTimeTearDown]
        public void Dispose()
        {
            PythonEngine.Shutdown();
        }

        [Test]
        public void TestBufferWrite()
        {
            if (Runtime.Runtime.PythonVersion < new Version(3,5)) return;

            string bufferTestString = "hello world! !$%&/()=?";

            using (Py.GIL())
            {
                using (var scope = Py.CreateScope())
                {
                    scope.Exec($"arr = bytearray({bufferTestString.Length})");
                    PyObject pythonArray = scope.Get("arr");
                    byte[] managedArray = new UTF8Encoding().GetBytes(bufferTestString);

                    using (PyBuffer buf = pythonArray.GetBuffer())
                    {
                        buf.Write(managedArray, 0, managedArray.Length);
                    }

                    string result = scope.Eval("arr.decode('utf-8')").ToString();
                    Assert.IsTrue(result == bufferTestString);
                }
            }
        }

        [Test]
        public void TestBufferRead()
        {
            if (Runtime.Runtime.PythonVersion < new Version(3, 5)) return;

            string bufferTestString = "hello world! !$%&/()=?";

            using (Py.GIL())
            {
                using (var scope = Py.CreateScope())
                {
                    scope.Exec($"arr = b'{bufferTestString}'");
                    PyObject pythonArray = scope.Get("arr");
                    byte[] managedArray = new byte[bufferTestString.Length];

                    using (PyBuffer buf = pythonArray.GetBuffer())
                    {
                        buf.Read(managedArray, 0, managedArray.Length);
                    }

                    string result = new UTF8Encoding().GetString(managedArray);
                    Assert.IsTrue(result == bufferTestString);
                }
            }
        }

        [Test]
        public void ArrayHasBuffer()
        {
            var array = new[,] {{1, 2}, {3,4}};
            var memoryView = PythonEngine.Eval("memoryview");
            var mem = memoryView.Invoke(array.ToPython());
            Assert.AreEqual(1, mem[(0, 0).ToPython()].As<int>());
            Assert.AreEqual(array[1,0], mem[(1, 0).ToPython()].As<int>());
        }

        [Test]
        public void ReferenceTypeArrayHasNoBuffer()
        {
            var array = new[] {new object()};
            var memoryView = PythonEngine.Eval("memoryview");
            var error = Assert.Throws<PythonException>(() => memoryView.Invoke(array.ToPython()));
            Assert.AreEqual("TypeError", error.PythonTypeName);
        }
    }
}
