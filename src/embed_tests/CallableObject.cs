using System;
using System.Collections.Generic;

using NUnit.Framework;
using Python.Runtime;

namespace Python.EmbeddingTest {
    class CallableObject {
        [OneTimeSetUp]
        public void SetUp() {
            PythonEngine.Initialize();
            using (Py.GIL()) {
                var locals = new PyDict();
                PythonEngine.Exec(CallViaInheritance.BaseClassSource, locals: locals.Handle);
                CustomBaseTypeAttribute.BaseClass = locals[CallViaInheritance.BaseClassName];
            }
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

        [Test]
        public void CallMethodCanBeInheritedFromPython() {
            var callViaInheritance = new CallViaInheritance();
            using (Py.GIL()) {
                dynamic applyObjectTo14 = PythonEngine.Eval("lambda o: o(14)");
                Assert.AreEqual(callViaInheritance.Call(14), (int)applyObjectTo14(callViaInheritance.ToPython()));
            }
        }

        class Doubler {
            public int __call__(int arg) => 2 * arg;
        }

        [CustomBaseType]
        class CallViaInheritance {
            public const string BaseClassName = "Forwarder";
            public static readonly string BaseClassSource = $@"
class {BaseClassName}:
  def __call__(self, val):
    return self.Call(val)
";
            public int Call(int arg) => 3 * arg;
        }

        class CustomBaseTypeAttribute : BaseTypeAttributeBase {
            internal static PyObject BaseClass;

            public override IntPtr BaseType(Type type)
                => type != typeof(CallViaInheritance) ? IntPtr.Zero : BaseClass.Handle;
        }
    }
}