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
                CustomBaseTypeProvider.BaseClass = locals[InheritanceTestBaseClassWrapper.ClassName];
                var baseTypeProviders = PythonEngine.InteropConfiguration.PythonBaseTypeProviders;
                baseTypeProviders.Add(new CustomBaseTypeProvider());
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
        public void OverloadResolution_ParamsOmitted() {
            var overloaded = new Overloaded();
            using (Py.GIL()) {
                var o = overloaded.ToPython();

                dynamic callWithInt = PythonEngine.Eval($"lambda o: o.{nameof(Overloaded.ArgPlusParams)}(42)");
                callWithInt(o);
                Assert.AreEqual(42, overloaded.Value);
            }
        }

        [Test]
        public void OverloadResolution_ParamsTypeMatch() {
            var overloaded = new Overloaded();
            using (Py.GIL()) {
                var o = overloaded.ToPython();

                var callWithInts = PythonEngine.Eval($"lambda o: o.{nameof(Overloaded.ArgPlusParams)}(42, 43)");
                callWithInts.Invoke(o);
                Assert.AreEqual(42, overloaded.Value);
            }
        }

        [Test]
        public void OverloadResolution_ParamsTypeMisMatch() {
            var overloaded = new Overloaded();
            using (Py.GIL()) {
                var o = overloaded.ToPython();

                var callWithIntAndStr = PythonEngine.Eval($"lambda o: o.{nameof(Overloaded.ArgPlusParams)}(42, object())");
                var error = Assert.Throws<PythonException>(() => callWithIntAndStr.Invoke(o), "Should have thrown PythonException");
                Assert.AreEqual(expected: Exceptions.TypeError, actual: error.PyType, "Should have thrown TypeError");
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
        [Ignore("Overload resolution does not correctly choose from base vs derived class")]
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
        public void GetAttrCanCallBase() {
            var obj = new GetSetAttrDoubleInherited();
            using (Py.GIL()) {
                var pyObj = obj.ToPython();
                dynamic getNonexistingAttr = PythonEngine.Eval("lambda o: o.non_existing_attr");
                string nonexistentAttrValue = getNonexistingAttr(pyObj);
                Assert.AreEqual("__getattr__:non_existing_attr", nonexistentAttrValue);
            }
        }

        const string GetAttrFallbackValue = "undefined";

        class Base {}
        class Derived: Base { }

        class Overloaded: Derived, IGetAttr
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

            public void ArgPlusParams(int arg0, params double[] floats) => this.Value = arg0;

            public bool TryGetAttr(string name, out PyObject value) {
                using (var self = this.ToPython()) {
                    if (GetAttr.TryGetBaseAttr(self, name, out value)
                        || GetAttr.GenericGetAttr(self, name, out value))
                        return true;
                }

                value = GetAttrFallbackValue.ToPython();
                return true;
            }
        }

        class GetSetAttrInherited : InheritanceTestBaseClassWrapper, IGetAttr {
            public bool TryGetAttr(string name, out PyObject value) {
                if (name == "NonInherited") {
                    value = "NonInherited".ToPython();
                    return true;
                }

                return GetAttr.TryGetBaseAttr(this.ToPython(), name, out value);
            }
        }

        class GetSetAttrDoubleInherited: GetSetAttrInherited { }
    }
}
