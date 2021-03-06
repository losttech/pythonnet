using System;
using System.Collections.Generic;

using NUnit.Framework;
using Python.Runtime;

namespace Python.EmbeddingTest {
    public class Inspect {
        [OneTimeSetUp]
        public void SetUp() {
            PythonEngine.Initialize();
        }

        [OneTimeTearDown]
        public void Dispose() {
            PythonEngine.Shutdown();
        }

        [Test]
        public void BoundMethodsAreInspectable() {
            var obj = new Class();
            using (var scope = Py.CreateScope()) {
                scope.Import("inspect");
                scope.Set(nameof(obj), obj);
                var spec = scope.Eval($"inspect.getfullargspec({nameof(obj)}.{nameof(Class.Method)})");
            }
        }

        [Test]
        public void InstancePropertiesVisibleOnClass() {
            var uri = new Uri("http://example.org").ToPython();
            var uriClass = uri.GetPythonType();
            var property = uriClass.GetAttr(nameof(Uri.AbsoluteUri));
            var pyProp = ExtensionType.GetManagedObject<PropertyObject>(property.Reference);
            Assert.AreEqual(nameof(Uri.AbsoluteUri), pyProp.info.Name);
        }

        class Class {
            public void Method(int a, int b = 10) { }
            public void Method(int a, object b) { }
        }
    }
}
