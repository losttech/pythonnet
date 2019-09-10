using System.Globalization;

using NUnit.Framework;
using Python.Runtime;
using Python.Runtime.Slots;

namespace Python.EmbeddingTest {
    public class TestInstanceWrapping {
        [OneTimeSetUp]
        public void SetUp() {
            PythonEngine.Initialize();
            using (Py.GIL()) {
                var locals = new PyDict();
                PythonEngine.Exec(InheritanceTestBaseClassWrapper.ClassSourceCode, locals: locals.Handle);
                CustomBaseTypeAttribute.BaseClass = locals[InheritanceTestBaseClassWrapper.ClassName];
            }
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

        // regression test for https://github.com/pythonnet/pythonnet/issues/811
        [Test]
        public void OverloadResolution_UnknownToObject() {
            var overloaded = new Overloaded();
            using (Py.GIL()) {
                var o = overloaded.ToPython();

                dynamic callWithSelf = PythonEngine.Eval("lambda o: o.ObjOrClass(KeyError())");
                callWithSelf(o);
                Assert.AreEqual(Overloaded.Object, overloaded.Value);
            }
        }

        [Test]
        public void GetAttrCanBeOverriden() {
            var overloaded = new Overloaded();
            using (Py.GIL()) {
                var o = overloaded.ToPython();
                dynamic getNonexistingAttr = PythonEngine.Eval("lambda o: o.non_existing_attr");
                string nonexistentAttrValue = getNonexistingAttr(o);
                Assert.AreEqual(GetAttrFallbackValue, nonexistentAttrValue);
            }
        }

        [Test]
        public void SetAttrCanBeOverriden() {
            var overloaded = new Overloaded();
            using (Py.GIL())
            using (var scope = Py.CreateScope()) {
                var o = overloaded.ToPython();
                scope.Set(nameof(o), o);
                scope.Exec($"{nameof(o)}.non_existing_attr = 42");
                Assert.AreEqual(42, overloaded.Value);
            }
        }

        [Test]
        public void GetAttrCanCallBase() {
            var obj = new GetSetAttrDoubleInherited();
            using (Py.GIL()) {
                var pyObj = obj.ToPython();
                dynamic getNonexistingAttr = PythonEngine.Eval("lambda o: o.non_existing_attr");
                string nonexistentAttrValue = getNonexistingAttr(pyObj);
                Assert.AreEqual("__getattr__:non_existing_attr", nonexistentAttrValue);
            }
        }

        [Test]
        public void SetAttrCanCallBase() {
            var obj = new GetSetAttrDoubleInherited();
            using (Py.GIL())
            using (var scope = Py.CreateScope()) {
                var pyObj = obj.ToPython();
                dynamic receiver = scope.Eval("dict()");
                scope.Set(nameof(pyObj), pyObj);
                scope.Set(nameof(receiver), receiver);
                scope.Exec($"{nameof(pyObj)}.non_existing_attr = {nameof(receiver)}");
                Assert.AreEqual("non_existing_attr", receiver["non_existing_attr"]);
            }
        }

        const string GetAttrFallbackValue = "undefined";

        class Base {}
        class Derived: Base { }

        class Overloaded: Derived, IGetAttr, ISetAttr
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

            public bool TryGetAttr(string name, out PyObject value) {
                value = GetAttrFallbackValue.ToPython();
                return true;
            }

            public bool TrySetAttr(string name, PyObject value) {
                this.Value = value.As<int>();
                return true;
            }
        }

        class GetSetAttrInherited : InheritanceTestBaseClassWrapper, IGetAttr, ISetAttr {
            public bool TryGetAttr(string name, out PyObject value) {
                if (name == "NonInherited") {
                    value = "NonInherited".ToPython();
                    return true;
                }

                return GetAttr.TryGetBaseAttr(this.ToPython(), name, out value);
            }

            public bool TrySetAttr(string name, PyObject value) {
                if (name == "NonInherited") return false;

                var self = this.ToPython();
                bool result = SetAttr.TrySetBaseAttr(self, name, value);
                return result;
            }
        }

        class GetSetAttrDoubleInherited: GetSetAttrInherited { }
    }
}
