using System;

using NUnit.Framework;
using Python.Runtime;

namespace Python.EmbeddingTest {
    public class TestCallbacks {
        [OneTimeSetUp]
        public void SetUp() {
            string path = Environment.GetEnvironmentVariable("PATH");
        }

        [OneTimeTearDown]
        public void Dispose() {
            PythonEngine.Shutdown();
        }

        [Test]
        public void TestSimpleCallback() {
            int passed = 0;
            var aFunctionThatCallsIntoPython = new Action<int>(value => passed = value);
            using (Py.GIL()) {
                dynamic callWith42 = PythonEngine.Eval("lambda f: f(42)");
                callWith42(aFunctionThatCallsIntoPython.ToPython());
            }
            Assert.AreEqual(expected: 42, actual: passed);
        }

        // regression test for https://github.com/pythonnet/pythonnet/issues/795
        [Test]
        public void TestReentry() {
            int passed = 0;
            var aFunctionThatCallsIntoPython = new Action<int>(value => {
                using (Py.GIL()) {
                    passed = (int)(dynamic)PythonEngine.Eval("42");
                }
            });
            using (Py.GIL()) {
                dynamic callWith42 = PythonEngine.Eval("lambda f: f(42)");
                callWith42(aFunctionThatCallsIntoPython.ToPython());
            }
            Assert.AreEqual(expected: 42, actual: passed);
        }

        [Test]
        public void TestNoOverloadException() {
            int passed = 0;
            var aFunctionThatCallsIntoPython = new Action<int>(value => passed = value);
            using (Py.GIL()) {
                dynamic callWith42 = PythonEngine.Eval("lambda f: f([42])");
                var error =  Assert.Throws<PythonException>(() => callWith42(aFunctionThatCallsIntoPython.ToPython()));
                Assert.AreEqual("TypeError", error.PythonTypeName);
                StringAssert.EndsWith("(<class 'list'>)", error.Message);
            }
        }
    }
}
