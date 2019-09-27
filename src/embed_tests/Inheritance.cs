using System;
using System.Collections.Generic;
using NUnit.Framework;
using Python.Runtime;

namespace Python.EmbeddingTest {
    public class Inheritance {
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
        public void IsInstance() {
            using (Py.GIL()) {
                var inherited = new Inherited();
                bool properlyInherited = PyIsInstance(inherited, CustomBaseTypeAttribute.BaseClass);
                Assert.IsTrue(properlyInherited);
            }
        }

        static dynamic PyIsInstance => PythonEngine.Eval("isinstance");

        [Test]
        public void InheritedClassIsNew() {
            using (Py.GIL()) {
                PyObject a = CustomBaseTypeAttribute.BaseClass;
                var inherited = new Inherited();
                dynamic getClass = PythonEngine.Eval("lambda o: o.__class__");
                PyObject inheritedClass = getClass(inherited);
                Assert.IsFalse(PythonReferenceComparer.Instance.Equals(a, inheritedClass));
            }
        }

        [Test]
        public void InheritedFromInheritedClassIsSelf() {
            using (Py.GIL())
            using (var scope = Py.CreateScope()) {
                scope.Exec($"from {typeof(Inherited).Namespace} import {nameof(Inherited)}");
                scope.Exec($"class B({nameof(Inherited)}): pass");
                PyObject b = scope.Eval("B");
                PyObject bInst = ((dynamic)b)(scope);
                dynamic getClass = scope.Eval("lambda o: o.__class__");
                PyObject inheritedClass = getClass(bInst);
                Assert.IsTrue(PythonReferenceComparer.Instance.Equals(b, inheritedClass));
            }
        }

        [Test]
        public void InheritedFromInheritedIsInstance() {
            using (Py.GIL())
            using (var scope = Py.CreateScope()) {
                scope.Exec($"from {typeof(Inherited).Namespace} import {nameof(Inherited)}");
                scope.Exec($"class B({nameof(Inherited)}): pass");
                PyObject b = scope.Eval("B");
                PyObject bInst = ((dynamic)b)(scope);
                bool properlyInherited = PyIsInstance(bInst, CustomBaseTypeAttribute.BaseClass);
                Assert.IsTrue(properlyInherited);
            }
        }

        [Test]
        public void CanCallInheritedMethodWithPythonBase() {
            var instance = new Inherited();
            using (Py.GIL()) {
                dynamic callBase = PythonEngine.Eval($"lambda o: o.{nameof(PythonWrapperBase.WrapperBaseMethod)}()");
                string result = (string)callBase(instance);
                Assert.AreEqual(result, nameof(PythonWrapperBase.WrapperBaseMethod));
            }
        }

        [Test]
        public void PythonCanCallOverridenMethod() {
            var instance = new Inherited();
            using (Py.GIL())
            using (var scope = Py.CreateScope()){
                scope.Set(nameof(instance), instance);
                int actual = scope.Eval<int>($"{nameof(instance)}.callVirt()");
                Assert.AreEqual(expected: Inherited.OverridenVirtValue, actual);
            }
        }

        [Test]
        public void PythonCanSetAdHocAttributes() {
            var instance = new Inherited();
            using (Py.GIL())
            using (var scope = Py.CreateScope()) {
                scope.Set(nameof(instance), instance);
                scope.Exec($"super({nameof(instance)}.__class__, {nameof(instance)}).set_x_to_42()");
                int actual = scope.Eval<int>($"{nameof(instance)}.x");
                Assert.AreEqual(expected: Inherited.X, actual);
            }
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    class CustomBaseTypeAttribute : BaseTypeAttributeBase {
        internal static PyObject BaseClass;
        public override PyTuple BaseTypes(Type type)
            => type != typeof(InheritanceTestBaseClassWrapper)
                ? base.BaseTypes(type)
                : new PyTuple(new []{ PyType.Get(type.BaseType), BaseClass });
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    class DefaultBaseTypeAttribute : BaseTypeAttributeBase {

    }

    [DefaultBaseType]
    public class PythonWrapperBase {
        public string WrapperBaseMethod() => nameof(WrapperBaseMethod);
    }

    [CustomBaseType]
    public class InheritanceTestBaseClassWrapper : PythonWrapperBase {
        public const string ClassName = "InheritanceTestBaseClass";
        public const string ClassSourceCode = "class " + ClassName +
@":
  def virt(self):
    return 42
  def set_x_to_42(self):
    self.x = 42
  def callVirt(self):
    return self.virt()
  def __getattr__(self, name):
    return '__getattr__:' + name
  def __setattr__(self, name, value):
    value[name] = name
" + ClassName + " = " + ClassName + "\n";
    }

    public class Inherited : InheritanceTestBaseClassWrapper {
        public const int OverridenVirtValue = -42;
        public const int X = 42;
        readonly Dictionary<string, object> extras = new Dictionary<string, object>();
        public int virt() => OverridenVirtValue;
        public int x {
            get {
                using (var scope = Py.CreateScope()) {
                    scope.Set("this", this);
                    try {
                        return scope.Eval<int>("super(this.__class__, this).x");
                    } catch (PythonException ex) when (ex.PyType == Exceptions.AttributeError) {
                        if (this.extras.TryGetValue(nameof(x), out object value))
                            return (int)value;
                        throw;
                    }
                }
            }
            set => this.extras[nameof(x)] = value;
        }
    }
}
