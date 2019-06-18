using NUnit.Framework;
using Python.Runtime;

namespace Python.EmbeddingTest {
    public class Inheritance {
        [OneTimeSetUp]
        public void SetUp() {
            PythonEngine.Initialize();
        }

        [OneTimeTearDown]
        public void Dispose() {
            PythonEngine.Shutdown();
        }

        [Test]
        public void IsInstance() {
            using (Py.GIL())
            using (var scope = Py.CreateScope()) {
                scope.Exec(AWrapper.Source);
                var inheritedFromA = new InheritedFromA();
                dynamic isinstanceA = scope.Eval("lambda o: isinstance(o, A)");
                bool isA = isinstanceA(inheritedFromA);
                Assert.IsTrue(isA);
            }
        }
    }

    class PythonWrapperBase { }

    class AWrapper : PythonWrapperBase {
        public const string Source = "class A: pass";
    }

    class InheritedFromA : AWrapper {
    }
}
