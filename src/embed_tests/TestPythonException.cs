using System;
using NUnit.Framework;
using Python.Runtime;

namespace Python.EmbeddingTest
{
    public class TestPythonException
    {
        private IntPtr _gs;

        [SetUp]
        public void SetUp()
        {
            PythonEngine.Initialize();
            _gs = PythonEngine.AcquireLock();
        }

        [TearDown]
        public void Dispose()
        {
            PythonEngine.ReleaseLock(_gs);
            PythonEngine.Shutdown();
        }

        [Test]
        public void TestMessage()
        {
            var list = new PyList();
            PyObject foo = null;

            var ex = Assert.Throws<PythonException>(() => foo = list[0]);

            Assert.AreEqual("IndexError : list index out of range", ex.Message);
            Assert.IsNull(foo);
        }

        [Test]
        public void TestNoError()
        {
            var e = new PythonException(); // There is no PyErr to fetch
            Assert.AreEqual("", e.Message);
        }

        [Test]
        public void TestPythonErrorTypeName()
        {
            try
            {
                var module = (PyObject)PyModule.Import("really____unknown___module");
                Assert.Fail("Unknown module should not be loaded");
            }
            catch (PythonException ex)
            {
                Assert.That(ex.PythonTypeName, Is.EqualTo("ModuleNotFoundError").Or.EqualTo("ImportError"));
            }
        }

        [Test]
        public void TestPythonExceptionFormat()
        {
            try
            {
                PythonEngine.Exec("raise ValueError('Error!')");
                Assert.Fail("Exception should have been raised");
            }
            catch (PythonException ex)
            {
                // Console.WriteLine($"Format: {ex.Format()}");
                // Console.WriteLine($"Stacktrace: {ex.StackTrace}");
                Assert.That(
                    ex.Format(),
                    Does.Contain("Traceback")
                    .And.Contains("(most recent call last):")
                    .And.Contains("ValueError: Error!")
                );

                // Check that the stacktrace is properly formatted
                Assert.That(
                    ex.StackTrace,
                    Does.Not.StartWith("[")
                    .And.Not.Contain("\\n")
                );
            }
        }

        [Test]
        public void TestPythonExceptionFormatNoError()
        {
            var ex = new PythonException();
            Assert.AreEqual(ex.StackTrace, ex.Format());
        }

        [Test]
        public void TestPythonExceptionFormatNoTraceback()
        {
            try
            {
                var module = (PyObject)PyModule.Import("really____unknown___module");
                Assert.Fail("Unknown module should not be loaded");
            }
            catch (PythonException ex)
            {
                // ImportError/ModuleNotFoundError do not have a traceback when not running in a script
                Assert.AreEqual(ex.StackTrace, ex.Format());
            }
        }

        [Test]
        public void TestPythonExceptionFormatNormalized()
        {
            try
            {
                PythonEngine.Exec("a=b\n");
                Assert.Fail("Exception should have been raised");
            }
            catch (PythonException ex)
            {
                Assert.AreEqual("Traceback (most recent call last):\n  File \"<string>\", line 1, in <module>\nNameError: name 'b' is not defined\n", ex.Format());
            }
        }

        [Test]
        public void TestPythonException_PyErr_NormalizeException()
        {
            using (var scope = Py.CreateScope())
            {
                scope.Exec(@"
class TestException(NameError):
    def __init__(self, val):
        super().__init__(val)
        x = int(val)");
                Assert.IsTrue(scope.TryGet("TestException", out PyObject type));

                PyObject str = "dummy string".ToPython();
                IntPtr typePtr = type.Handle;
                IntPtr strPtr = str.Handle;
                IntPtr tbPtr = Runtime.Runtime.None.Handle;
                Runtime.Runtime.XIncref(typePtr);
                Runtime.Runtime.XIncref(strPtr);
                Runtime.Runtime.XIncref(tbPtr);
                Runtime.Runtime.PyErr_NormalizeException(ref typePtr, ref strPtr, ref tbPtr);

                using (PyObject typeObj = new PyObject(typePtr), strObj = new PyObject(strPtr), tbObj = new PyObject(tbPtr))
                {
                    // the type returned from PyErr_NormalizeException should not be the same type since a new
                    // exception was raised by initializing the exception
                    Assert.AreNotEqual(type.Handle, typePtr);
                    // the message should now be the string from the throw exception during normalization
                    Assert.AreEqual("invalid literal for int() with base 10: 'dummy string'", strObj.ToString());
                }
            }
        }

        [Test]
        public void TestPythonException_Normalize_ThrowsWhenErrorSet()
        {
            Exceptions.SetError(Exceptions.TypeError, "Error!");
            var pythonException = new PythonException();
            Exceptions.SetError(Exceptions.TypeError, "Another error");
            Assert.Throws<InvalidOperationException>(() => pythonException.Normalize());
        }
    }
}
