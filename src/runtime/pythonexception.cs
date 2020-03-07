using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Python.Runtime
{
    /// <summary>
    /// Provides a managed interface to exceptions thrown by the Python
    /// runtime.
    /// </summary>
    public class PythonException : System.Exception, IPyDisposable
    {
        private IntPtr _pyType = IntPtr.Zero;
        private IntPtr _pyValue = IntPtr.Zero;
        private IntPtr _pyTB = IntPtr.Zero;
        private string _traceback = "";
        private string _message = "";
        private string _pythonTypeName = "";
        private bool disposed = false;
        private bool _finalized = false;

        [Obsolete("Please, use FromPyErr instead")]
        public PythonException()
        {
            IntPtr gs = PythonEngine.AcquireLock();
            Runtime.PyErr_Fetch(out _pyType, out _pyValue, out _pyTB);
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

        internal static Exception FromPyErr() {
            IntPtr gs = PythonEngine.AcquireLock();
            Runtime.PyErr_Fetch(out var pyTypeHandle, out var pyValueHandle, out var pyTracebackHandle);
            var result = FromPyErr(pyTypeHandle, pyValueHandle, pyTracebackHandle);
            PythonEngine.ReleaseLock(gs);
            return result;
        }

        internal static Exception FromPyErrOrNull()
        {
            IntPtr gs = PythonEngine.AcquireLock();
            Runtime.PyErr_Fetch(out var pyTypeHandle, out var pyValueHandle, out var pyTracebackHandle);
            if (pyValueHandle == IntPtr.Zero && pyTypeHandle == IntPtr.Zero && pyTracebackHandle == IntPtr.Zero)
            {
                return null;
            }
            var result = FromPyErr(pyTypeHandle, pyValueHandle, pyTracebackHandle);
            PythonEngine.ReleaseLock(gs);
            return result;
        }

        /// <summary>
        /// Rethrows the last Python exception as corresponding CLR exception.
        /// It is recommended to call this as <code>throw ThrowLastAsClrException()</code>
        /// to assist control flow checks.
        /// </summary>
        internal static Exception ThrowLastAsClrException() {
            IntPtr gs = PythonEngine.AcquireLock();
            try {
                Runtime.PyErr_Fetch(out var pyTypeHandle, out var pyValueHandle, out var pyTracebackHandle);
                var clrObject = ManagedType.GetManagedObject(pyValueHandle) as CLRObject;
                if (clrObject?.inst is Exception e) {
#if NETSTANDARD
                    ExceptionDispatchInfo.Capture(e).Throw();
#endif
                    throw e;
                }
                var result = FromPyErr(pyTypeHandle, pyValueHandle, pyTracebackHandle);
                throw result;
            } finally {
                PythonEngine.ReleaseLock(gs);
            }
        }

        /// <summary>
        /// Requires lock to be acquired eslewhere
        /// </summary>
        static Exception FromPyErr(IntPtr pyTypeHandle, IntPtr pyValueHandle, IntPtr pyTracebackHandle) {
            Exception inner = null;
            string pythonTypeName = null, msg = "", traceback = null;

            var clrObject = ManagedType.GetManagedObject(pyValueHandle) as CLRObject;
            if (clrObject?.inst is Exception e) {
                return e;
            }

            if (pyTypeHandle != IntPtr.Zero && pyValueHandle != IntPtr.Zero)
            {
                if (PyObjectConversions.TryDecode(pyValueHandle, pyTypeHandle, typeof(Exception),
                    out object decoded) && decoded is Exception decodedException) {
                    return decodedException;
                }

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
            using var traceback = new PyObject(Runtime.SelfIncRef(tracebackHandle));
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
            if (_finalized || disposed)
            {
                return;
            }
            _finalized = true;
            Finalizer.Instance.AddFinalizedObject(this);
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
                    if (_pyType != IntPtr.Zero)
                    {
                        Runtime.XDecref(_pyType);
                        _pyType= IntPtr.Zero;
                    }

                    if (_pyValue != IntPtr.Zero)
                    {
                        Runtime.XDecref(_pyValue);
                        _pyValue = IntPtr.Zero;
                    }

                    // XXX Do we ever get TraceBack? //
                    if (_pyTB != IntPtr.Zero)
                    {
                        Runtime.XDecref(_pyTB);
                        _pyTB = IntPtr.Zero;
                    }
                    PythonEngine.ReleaseLock(gs);
                }
                GC.SuppressFinalize(this);
                disposed = true;
            }
        }

        public IntPtr[] GetTrackedHandles()
        {
            return new IntPtr[] { _pyType, _pyValue, _pyTB };
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

        public static IntPtr ThrowIfIsNull(IntPtr ob)
        {
            if (ob == IntPtr.Zero)
            {
                throw PythonException.ThrowLastAsClrException();
            }

            return ob;
        }

        public static void ThrowIfIsNotZero(int value)
        {
            if (value != 0)
            {
                throw PythonException.ThrowLastAsClrException();
            }
        }
    }
}
