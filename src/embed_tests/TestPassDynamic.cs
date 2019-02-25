namespace Python.EmbeddingTest {
    using System;
    using System.Collections.Generic;
    using System.Dynamic;
    using System.Text;
    using NUnit.Framework;
    using Python.Runtime;

    public class TestPassDynamic {
        [OneTimeSetUp]
        public void SetUp()
        {
            PythonEngine.Initialize();
        }

        [OneTimeTearDown]
        public void Dispose()
        {
            PythonEngine.Shutdown();
        }

        [Test]
        public void ExpandoProperties() {
            dynamic expando = new ExpandoObject();
            expando.test = 42;
            using (Py.GIL()) {
                dynamic getTest = PythonEngine.Eval("lambda o: o.test");
                int read = getTest(expando);
                Assert.AreEqual(42, read);
            }
        }
    }
}
