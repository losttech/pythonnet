using System;
using System.Collections.Generic;

using NUnit.Framework;
using Python.Runtime;

namespace Python.EmbeddingTest {
    class CallableObject {
        [OneTimeSetUp]
        public void SetUp() {
            PythonEngine.Initialize();
        }

        [OneTimeTearDown]
        public void Dispose() {
            PythonEngine.Shutdown();
        }

        [Test]
        public void CallMethodMakesObjectCallable() {
            var doubler = new Doubler();
            using (Py.GIL()) {
                dynamic applyObjectTo21 = PythonEngine.Eval("lambda o: o(21)");
                Assert.AreEqual(doubler.__call__(21), (int)applyObjectTo21(doubler.ToPython()));
            }
        }

        class Doubler {
            public int __call__(int arg) => 2 * arg;
        }
    }
}
