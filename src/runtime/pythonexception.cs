using System;

namespace Python.Runtime
{
    using System.Text;

    /// <summary>
    /// Provides a managed interface to exceptions thrown by the Python
    /// runtime.
    /// </summary>
    public class PythonException : System.Exception
    {
        private IntPtr _pyType = IntPtr.Zero;
        private IntPtr _pyValue = IntPtr.Zero;
        private IntPtr _pyTB = IntPtr.Zero;
        private string _traceback = "";
        private string _message = "";
        private string _pythonTypeName = "";
        private bool disposed = false;

        [Obsolete("Please, use FromPyErr instead")]
        public PythonException()
        {
            IntPtr gs = PythonEngine.AcquireLock();
            Runtime.PyErr_Fetch(ref _pyType, ref _pyValue, ref _pyTB);
            Runtime.XIncref(_pyType);
            Runtime.XIncref(_pyValue);
            Runtime.XIncref(_pyTB);
            if (_pyType != IntPtr.Zero && _pyValue != IntPtr.Zero)
            {
                string type;
                string message;
                Runtime.XIncref(_pyType);
                using (var pyType = new PyObject(_pyType))
                using (PyObject pyTypeName = pyType.GetAttr("__name__"))
                {
                    type = pyTypeName.ToString();
                }

                _pythonTypeName = type;

                Runtime.XIncref(_pyValue);
                using (var pyValue = new PyObject(_pyValue))
                {
                    message = pyValue.ToString();
                }
                _message = type + " : " + message;
            }
            if (_pyTB != IntPtr.Zero)
            {
                this._traceback = TracebackHandleToString(_pyTB);
            }
            PythonEngine.ReleaseLock(gs);
        }

        private PythonException(IntPtr pyTypeHandle, IntPtr pyValueHandle, IntPtr pyTracebackHandle,
                                string message, string pythonTypeName, string traceback,
                                Exception innerException)
            : base(message, innerException)
        {
            _pyType = pyTypeHandle;
            _pyValue = pyValueHandle;
            _pyTB = pyTracebackHandle;
            _message = message;
            _pythonTypeName = pythonTypeName ?? _pythonTypeName;
            _traceback = traceback ?? _traceback;
        }

        internal static PythonException FromPyErr() {
            IntPtr gs = PythonEngine.AcquireLock();
            IntPtr pyTypeHandle = IntPtr.Zero, pyValueHandle = IntPtr.Zero, pyTracebackHandle = IntPtr.Zero;
            Runtime.PyErr_Fetch(ref pyTypeHandle, ref pyValueHandle, ref pyTracebackHandle);
            Runtime.XIncref(pyTypeHandle);
            Runtime.XIncref(pyValueHandle);
            Runtime.XIncref(pyTracebackHandle);
            var result = FromPyErr(pyTypeHandle, pyValueHandle, pyTracebackHandle);
            PythonEngine.ReleaseLock(gs);
            return result;
        }

        /// <summary>
        /// Requires lock to be acquired eslewhere
        /// </summary>
        static PythonException FromPyErr(IntPtr pyTypeHandle, IntPtr pyValueHandle, IntPtr pyTracebackHandle) {
            Exception inner = null;
            string pythonTypeName = null, msg = "", traceback = null;
            if (pyTypeHandle != IntPtr.Zero && pyValueHandle != IntPtr.Zero)
            {
                string type;
                string message;
                Runtime.XIncref(pyTypeHandle);
                using (var pyType = new PyObject(pyTypeHandle))
                using (PyObject pyTypeName = pyType.GetAttr("__name__"))
                {
                    type = pyTypeName.ToString();
                }

                pythonTypeName = type;

                Runtime.XIncref(pyValueHandle);
                using (var pyValue = new PyObject(pyValueHandle))
                {
                    message = pyValue.ToString();
                    var cause = pyValue.GetAttr("__cause__", null);
                    if (cause != null && cause.Handle != Runtime.PyNone) {
                        IntPtr innerTraceback = cause.GetAttr("__traceback__", null)?.Handle ?? IntPtr.Zero;
                        Runtime.XIncref(innerTraceback);
                        inner = FromPyErr(cause.GetPythonTypeHandle(), cause.obj, innerTraceback);
                        Runtime.XDecref(innerTraceback);
                    }
                }
                msg = type + " : " + message;
            }
            if (pyTracebackHandle != IntPtr.Zero)
            {
                traceback = TracebackHandleToString(pyTracebackHandle);
            }

            return new PythonException(pyTypeHandle, pyValueHandle, pyTracebackHandle,
                msg, pythonTypeName, traceback, inner);
        }

        static string TracebackHandleToString(IntPtr tracebackHandle) {
            if (tracebackHandle == IntPtr.Zero) {
                throw new ArgumentNullException(nameof(tracebackHandle));
            }

            PyObject tracebackModule = PythonEngine.ImportModule("traceback");
            PyObject traceback = new PyObject(tracebackHandle);
            PyList stackLines = new PyList(tracebackModule.InvokeMethod("format_tb", traceback));
            var result = new StringBuilder();
            foreach (object stackLine in stackLines) {
                result.Append(stackLine);
            }
            return result.ToString();
        }

        // Ensure that encapsulated Python objects are decref'ed appropriately
        // when the managed exception wrapper is garbage-collected.

        ~PythonException()
        {
            // We needs to disable Finalizers until it's valid implementation.
            // Current implementation can produce low probability floating bugs.
            return;

            Dispose();
        }

        /// <summary>
        /// Restores python error.
        /// </summary>
        public void Restore()
        {
            IntPtr gs = PythonEngine.AcquireLock();
            Runtime.PyErr_Restore(_pyType, _pyValue, _pyTB);
            _pyType = IntPtr.Zero;
            _pyValue = IntPtr.Zero;
            _pyTB = IntPtr.Zero;
            PythonEngine.ReleaseLock(gs);
        }

        /// <summary>
        /// PyType Property
        /// </summary>
        /// <remarks>
        /// Returns the exception type as a Python object.
        /// </remarks>
        public IntPtr PyType
        {
            get { return _pyType; }
        }

        /// <summary>
        /// PyValue Property
        /// </summary>
        /// <remarks>
        /// Returns the exception value as a Python object.
        /// </remarks>
        public IntPtr PyValue
        {
            get { return _pyValue; }
        }

        /// <summary>
        /// PyTB Property
        /// </summary>
        /// <remarks>
        /// Returns the TraceBack as a Python object.
        /// </remarks>
        public IntPtr PyTB
        {
            get { return _pyTB; }
        }

        /// <summary>
        /// Message Property
        /// </summary>
        /// <remarks>
        /// A string representing the python exception message.
        /// </remarks>
        public override string Message
        {
            get { return _message; }
        }

        /// <summary>
        /// StackTrace Property
        /// </summary>
        /// <remarks>
        /// A string representing the python exception stack trace.
        /// </remarks>
        public override string StackTrace
        {
            get { return this._traceback + base.StackTrace; }
        }

        /// <summary>
        /// Python error type name.
        /// </summary>
        public string PythonTypeName
        {
            get { return _pythonTypeName; }
        }

        /// <summary>
        /// Dispose Method
        /// </summary>
        /// <remarks>
        /// The Dispose method provides a way to explicitly release the
        /// Python objects represented by a PythonException.
        /// If object not properly disposed can cause AppDomain unload issue.
        /// See GH#397 and GH#400.
        /// </remarks>
        public void Dispose()
        {
            if (!disposed)
            {
                if (Runtime.Py_IsInitialized() > 0 && !Runtime.IsFinalizing)
                {
                    IntPtr gs = PythonEngine.AcquireLock();
                    Runtime.XDecref(_pyType);
                    Runtime.XDecref(_pyValue);
                    // XXX Do we ever get TraceBack? //
                    if (_pyTB != IntPtr.Zero)
                    {
                        Runtime.XDecref(_pyTB);
                    }
                    PythonEngine.ReleaseLock(gs);
                }
                GC.SuppressFinalize(this);
                disposed = true;
            }
        }

        /// <summary>
        /// Matches Method
        /// </summary>
        /// <remarks>
        /// Returns true if the Python exception type represented by the
        /// PythonException instance matches the given exception type.
        /// </remarks>
        public static bool Matches(IntPtr ob)
        {
            return Runtime.PyErr_ExceptionMatches(ob) != 0;
        }
    }
}
