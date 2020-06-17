namespace Python.EmbeddingTest {
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;

    using Python.Runtime;

    public class CollectionsMixins {
        [Test]
        public void Enumerables_Iterable() {
            var iterable = Enumerable.Repeat(42, 5).ToPython();
            var iterator = iterable.InvokeMethod("__iter__");
            int first = iterator.InvokeMethod("__next__").As<int>();
            Assert.AreEqual(42, first);
        }
        [Test]
        public void Dict_Items_Iterable() {
            var pyDict = MakeDict().ToPython();
            var items = pyDict.InvokeMethod("items");
            using var scope = Py.CreateScope();
            scope.Set("iterator", this.Iter.Invoke(items));
            scope.Set("s", "");
            scope.Exec("for i in iterator: s += str(i)");
            scope.Get<string>("s");
        }

        [Test]
        public void Dict()
        {
            var dict = MakeDict();
            var pyDict = dict.ToPython();
            Assert.IsTrue(pyDict.InvokeMethod("__contains__", "42".ToPython()).As<bool>());
            Assert.IsFalse(pyDict.InvokeMethod("__contains__", "21".ToPython()).As<bool>());
            Assert.AreEqual("12", pyDict.InvokeMethod("get", "21".ToPython(), "12".ToPython()).As<string>());
            Assert.AreEqual(null, pyDict.InvokeMethod("get", "21".ToPython()).As<string>());
            Assert.AreEqual("1", pyDict.InvokeMethod("pop", "10".ToPython(), "1".ToPython()).As<string>());
        }

        static Dictionary<object, object> MakeDict() => new Dictionary<object, object> {
            ["42"] = new object(),
            [new object()] = "21",
        };
        PyObject Iter => PythonEngine.Eval("iter");

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
