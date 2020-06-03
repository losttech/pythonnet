namespace Python.EmbeddingTest {
    using System;
    using System.Collections.Generic;

    using NUnit.Framework;

    using Python.Runtime;

    public class CollectionsMixins {
        [Test]
        public void Dict_Items_Iterable() {
            var pyDict = this.dict.ToPython();
            var items = pyDict.InvokeMethod("items");
            using var scope = Py.CreateScope();
            scope.Set("iterator", this.Iter.Invoke(items));
            scope.Set("s", "");
            scope.Exec("for i in iterator: s += str(i)");
        }

        readonly Dictionary<object, object> dict = new Dictionary<object, object> {
            ["42"] = new object(),
            [new object()] = "21",
        };
        readonly Lazy<PyObject> iter = new Lazy<PyObject>(() => PythonEngine.Eval("iter"));
        PyObject Iter => this.iter.Value;

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
