using  System;

using NUnit.Framework;
using Python.Runtime;

namespace Python.EmbeddingTest {
    public class TestCallbacks {
        [OneTimeSetUp]
        public void SetUp() {
            PythonEngine.Initialize();
        }

        [OneTimeTearDown]
        public void Dispose() {
            PythonEngine.Shutdown();
        }

        // regression test for https://github.com/pythonnet/pythonnet/issues/795
        [Test]
        public void TestReentry() {
            int passed = 0;
            var aFunctionThatCallsIntoPython = new Action<int>(CallMe);
            using (Py.GIL()) {
                dynamic callWith42 = PythonEngine.Eval("lambda f: f(42)");
                callWith42(aFunctionThatCallsIntoPython.ToPython());
            }
            Assert.AreEqual(expected: 42, actual: passed);
        }
        static void CallMe(int value) { PythonEngine.Eval("42"); }
    }
}
