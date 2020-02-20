using System;

using NUnit.Framework;
using Python.Runtime;

namespace Python.EmbeddingTest {
    public class TestCallbacks {
        [OneTimeSetUp]
        public void SetUp() {
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

        [Test]
        public void TestNoInstanceOverloadException() {
            var instance = new FunctionContainer();
            using (Py.GIL()) {
                dynamic callWith42 = PythonEngine.Eval($"lambda f: f.{nameof(FunctionContainer.Instance)}([42])");
                var error = Assert.Throws<PythonException>(() => callWith42(instance.ToPython()));
                Assert.AreEqual("TypeError", error.PythonTypeName);
                string expectedMessageEnd =
                    $"{nameof(FunctionContainer)}.{nameof(FunctionContainer.Instance)}: (<class 'list'>)";
                StringAssert.EndsWith(expectedMessageEnd, error.Message);
            }
        }

        [Test]
        public void TestNoStaticOverloadException() {
            using (Py.GIL()) {
                var type = ((dynamic)new FunctionContainer().ToPython()).__class__;
                dynamic callWith42 = PythonEngine.Eval($"lambda t: t.{nameof(FunctionContainer.Static)}([42])");
                var error = Assert.Throws<PythonException>(() => callWith42(type));
                Assert.AreEqual("TypeError", error.PythonTypeName);
                string expectedMessageEnd =
                    $"{nameof(FunctionContainer)}::{nameof(FunctionContainer.Static)}: (<class 'list'>)";
                StringAssert.EndsWith(expectedMessageEnd, error.Message);
            }
        }

        [Test]
        public void TestExceptionInCallback() {
            var dotnetFunction = new Action<int>(_ => throw new ArgumentOutOfRangeException());
            using (Py.GIL()) {
                dynamic callWith42 = PythonEngine.Eval("lambda f: f(42)");
                var error = Assert.Throws<PythonException>(() => callWith42(dotnetFunction.ToPython()));
                Assert.AreEqual(
                    ClassManager.GetClass(typeof(ArgumentOutOfRangeException)).pyHandle,
                    error.PyType);
            }
        }

        class FunctionContainer {
            public void Instance(int arg) { }
            public static void Static(int arg) { }
        }
    }
}
