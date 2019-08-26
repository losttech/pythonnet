using System;

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
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    class CustomBaseTypeAttribute : BaseTypeAttributeBase {
        internal static PyObject BaseClass;
        public override IntPtr BaseType(Type type)
            => type != typeof(InheritanceTestBaseClassWrapper) ? IntPtr.Zero : BaseClass.Handle;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    class DefaultBaseTypeAttribute : BaseTypeAttributeBase {
        public override IntPtr BaseType(Type type) => IntPtr.Zero;
    }

    [DefaultBaseType]
    public class PythonWrapperBase { }

    [CustomBaseType]
    public class InheritanceTestBaseClassWrapper : PythonWrapperBase {
        public const string ClassName = "InheritanceTestBaseClass";
        public const string ClassSourceCode = "class " + ClassName + ": pass\n" + ClassName + " = " + ClassName + "\n";
    }

    public class Inherited : InheritanceTestBaseClassWrapper {
    }
}
