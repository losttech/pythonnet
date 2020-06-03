namespace Python.EmbeddingTest {
    using System;
    using System.Linq;

    using NUnit.Framework;

    using Python.Runtime;

    public class Arrays {
        [Test]
        public void Enumerate() {
            var objArray = new[] { new Uri("http://a"), new Uri("http://b") };
            using var scope = Py.CreateScope();
            scope.Set("arr", objArray);
            scope.Set("s", "");
            scope.Exec("for item in arr: s += str(item)");
            var result = scope.Eval<string>("s");
            Assert.AreEqual(string.Concat(args: objArray), result);
        }


        [OneTimeSetUp]
        public void SetUp() {
            PythonEngine.Initialize();
        }

        [OneTimeTearDown]
        public void Dispose() {
            PythonEngine.Shutdown();
        }
    }
}
