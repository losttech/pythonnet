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
        public void OverloadResolution_IntOrStr() {
            var overloaded = new Overloaded();
            using (Py.GIL()) {
                var o = overloaded.ToPython();

                dynamic callWithInt = PythonEngine.Eval("lambda o: o.IntOrStr(42)");
                callWithInt(o);
                Assert.AreEqual(42, overloaded.Value);

                dynamic callWithStr = PythonEngine.Eval("lambda o: o.IntOrStr('43')");
                callWithStr(o);
                Assert.AreEqual(43, overloaded.Value);
            }
        }

        [Test]
        public void OverloadResolution_ObjOrClass() {
            var overloaded = new Overloaded();
            using (Py.GIL()) {
                var o = overloaded.ToPython();

                dynamic callWithConcrete = PythonEngine.Eval("lambda o: o.ObjOrClass(o)");
                callWithConcrete(o);
                Assert.AreEqual(Overloaded.ConcreteClass, overloaded.Value);

                dynamic callWithUnknown = PythonEngine.Eval("lambda o: o.ObjOrClass([])");
                callWithUnknown(o);
                Assert.AreEqual(Overloaded.Object, overloaded.Value);
            }
        }

        [Test]
        public void OverloadResolution_BaseOrDerived() {
            var overloaded = new Overloaded();
            using (Py.GIL()) {
                var o = overloaded.ToPython();

                dynamic callWithSelf = PythonEngine.Eval("lambda o: o.BaseOrDerived(o)");
                callWithSelf(o);
                Assert.AreEqual(Overloaded.Derived, overloaded.Value);
            }
        }

        class Base {}
        class Derived: Base { }

        class Overloaded: Derived
        {
            public int Value { get; set; }
            public void IntOrStr(int arg) => this.Value = arg;
            public void IntOrStr(string arg) =>
                this.Value = int.Parse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture);

            public const int Object = 1;
            public const int ConcreteClass = 2;
            public void ObjOrClass(object _) => this.Value = Object;
            public void ObjOrClass(Overloaded _) => this.Value = ConcreteClass;

            public const int Base = ConcreteClass + 1;
            public const int Derived = Base + 1;
            public void BaseOrDerived(Base _) => this.Value = Base;
            public void BaseOrDerived(Derived _) => this.Value = Derived;
        }
    }
}
