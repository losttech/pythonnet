using System;

using NUnit.Framework;
using Python.Runtime;

namespace Python.EmbeddingTest {
    using System.Globalization;

    public class TestInstanceWrapping {
        [OneTimeSetUp]
        public void SetUp() {
            PythonEngine.Initialize();
        }

        [OneTimeTearDown]
        public void Dispose() {
            PythonEngine.Shutdown();
        }

        [Test]
        public void SimpleOverloadResolution() {
            var overloaded = new Overloaded();
            using (Py.GIL()) {
                var o = overloaded.ToPython();
                dynamic callWithInt = PythonEngine.Eval("lambda o: o.CallMe(42)");
                callWithInt(o);
                Assert.AreEqual(42, overloaded.Value);

                dynamic callWithStr = PythonEngine.Eval("lambda o: o.CallMe('43')");
                callWithStr(o);
                Assert.AreEqual(43, overloaded.Value);
            }
        }

        class Overloaded
        {
            public int Value { get; set; }
            public void CallMe(int arg) => this.Value = arg;
            public void CallMe(string arg) =>
                this.Value = int.Parse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
    }
}
