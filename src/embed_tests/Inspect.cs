using System;
using System.Collections.Generic;

using NUnit.Framework;
using Python.Runtime;

namespace Python.EmbeddingTest {
    public class Inspect {
        [SetUp]
        public void SetUp() {
            PythonEngine.Initialize();
        }

        [TearDown]
        public void Dispose() {
            PythonEngine.Shutdown();
        }

        [Test]
        public void BoundMethodsAreInspectable() {
            var obj = new Class();
            using (var scope = Py.CreateScope()) {
                scope.Import("inspect");
                scope.Set(nameof(obj), obj);
                var spec = scope.Eval($"inspect.getfullargspec({nameof(obj)}.{nameof(Class.Method)})");
            }
        }

        class Class {
            public void Method() { }
        }
    }
}
