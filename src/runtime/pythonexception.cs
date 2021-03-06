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

        [Obsolete("Please, use FromPyErr instead")]
        public PythonException()
        {
            IntPtr gs = PythonEngine.AcquireLock();
            Runtime.PyErr_Fetch(out var type, out var value, out var traceback);
            _pyType = type.IsNull() ? IntPtr.Zero : type.DangerousMoveToPointer();
            _pyValue = value.IsNull() ? IntPtr.Zero :  value.DangerousMoveToPointer();
            _pyTB = traceback.IsNull() ? IntPtr.Zero : traceback.DangerousMoveToPointer();
            if (_pyType != IntPtr.Zero && _pyValue != IntPtr.Zero)
            {
                string message;
                Runtime.XIncref(_pyType);
                using (var pyType = new PyObject(_pyType))
                using (PyObject pyTypeName = pyType.GetAttr("__name__"))
                {
                    _pythonTypeName = pyTypeName.ToString();
                }

                Runtime.XIncref(_pyValue);
                using (var pyValue = new PyObject(_pyValue))
                {
                    message = pyValue.ToString();
                }
                _message = _pythonTypeName + " : " + message;
            }
            if (_pyTB != IntPtr.Zero)
            {
                this._traceback = TracebackHandleToString(new BorrowedReference(_pyTB));
            }
            PythonEngine.ReleaseLock(gs);
        }

        private PythonException(BorrowedReference pyTypeHandle,
                                BorrowedReference pyValueHandle,
                                BorrowedReference pyTracebackHandle,
                                string message, string pythonTypeName, string traceback,
                                Exception innerException)
            : base(message, innerException)
        {
            _pyType = pyTypeHandle.DangerousIncRefOrNull();
            _pyValue = pyValueHandle.DangerousIncRefOrNull();
            _pyTB = pyTracebackHandle.DangerousIncRefOrNull();
            _message = message;
            _pythonTypeName = pythonTypeName ?? _pythonTypeName;
            _traceback = traceback ?? _traceback;
        }

        internal static Exception FromPyErr()
        {
            Runtime.PyErr_Fetch(out var pyTypeHandle, out var pyValueHandle, out var pyTracebackHandle);
            using (pyTypeHandle)
            using (pyValueHandle)
            using (pyTracebackHandle) {
                return FromPyErr(
                    pyTypeHandle: pyTypeHandle,
                    pyValueHandle: pyValueHandle,
                    pyTracebackHandle: pyTracebackHandle);
            }
        }

        internal static Exception FromPyErrOrNull()
        {
            Runtime.PyErr_Fetch(out var pyTypeHandle, out var pyValueHandle, out var pyTracebackHandle);
            using (pyTypeHandle)
            using (pyValueHandle)
            using (pyTracebackHandle) {
                if (pyValueHandle.IsNull() && pyTypeHandle.IsNull() && pyTracebackHandle.IsNull()) {
                    return null;
                }

                var result = FromPyErr(pyTypeHandle, pyValueHandle, pyTracebackHandle);
                return result;
            }
        }

        /// <summary>
        /// Rethrows the last Python exception as corresponding CLR exception.
        /// It is recommended to call this as <code>throw ThrowLastAsClrException()</code>
        /// to assist control flow checks.
        /// </summary>
        internal static Exception ThrowLastAsClrException() {
            // prevent potential interop errors in this method
            // from crashing process with undebuggable StackOverflowException
            RuntimeHelpers.EnsureSufficientExecutionStack();

            IntPtr gs = PythonEngine.AcquireLock();
            try {
                Runtime.PyErr_Fetch(out var pyTypeHandle, out var pyValueHandle, out var pyTracebackHandle);
                using (pyTypeHandle)
                using (pyValueHandle)
                using (pyTracebackHandle) {
                    var clrObject = ManagedType.GetManagedObject(pyValueHandle) as CLRObject;
                    if (clrObject?.inst is Exception e) {
#if NETSTANDARD
                        ExceptionDispatchInfo.Capture(e).Throw();
#endif
                        throw e;
                    }

                    var result = FromPyErr(pyTypeHandle, pyValueHandle, pyTracebackHandle);
                    throw result;
                }
            } finally {
                PythonEngine.ReleaseLock(gs);
            }
        }

        /// <summary>
        /// Requires lock to be acquired elsewhere
        /// </summary>
        static Exception FromPyErr(BorrowedReference pyTypeHandle, BorrowedReference pyValueHandle, BorrowedReference pyTracebackHandle) {
            Exception inner = null;
            string pythonTypeName = null, msg = "", traceback = null;

            var clrObject = ManagedType.GetManagedObject(pyValueHandle) as CLRObject;
            if (clrObject?.inst is Exception e) {
                return e;
            }

            var errorDict = new PyDict();
            if (!pyTypeHandle.IsNull) errorDict["type"] = new PyObject(pyTypeHandle);
            if (!pyValueHandle.IsNull) errorDict["value"] = new PyObject(pyValueHandle);
            if (!pyTracebackHandle.IsNull) errorDict["traceback"] = new PyObject(pyTracebackHandle);

            object decoded;

            var pyErrType = Runtime.InteropModule.GetAttr("PyErr");
            using (PyObject pyErrInfo = pyErrType.Invoke(new PyTuple(), errorDict))
            {
                if (PyObjectConversions.TryDecode(pyErrInfo.Reference, pyErrType.Reference,
                    typeof(Exception), out decoded) && decoded is Exception decodedPyErrInfo)
                {
                    return decodedPyErrInfo;
                }
            }

            if (!pyTypeHandle.IsNull)
            {
                if (!pyValueHandle.IsNull)
                {
                    if (Runtime.PyString_Check(pyValueHandle) && pyTypeHandle != Runtime.PyStringType) {
                        using var type = new PyObject(pyTypeHandle);
                        using var message = new PyObject(pyValueHandle);
                        var instance = type.Invoke(message);
                        if (PyObjectConversions.TryDecode(instance.Reference, pyTypeHandle, typeof(Exception),
                            out decoded) && decoded is Exception decodedExceptionInstance) {
                            return decodedExceptionInstance;
                        }
                    } else if (PyObjectConversions.TryDecode(pyValueHandle, pyTypeHandle, typeof(Exception),
                                   out decoded) && decoded is Exception decodedException) {
                        return decodedException;
                    }

                    using (var pyValue = new PyObject(pyValueHandle))
                    {
                        msg = pyValue.ToString();
                        var cause = pyValue.GetAttr("__cause__", null);
                        if (cause != null && cause.Handle != Runtime.PyNone) {
                            using var innerTraceback = cause.GetAttr("__traceback__", null);
                            inner = FromPyErr(
                                pyTypeHandle: cause.GetPythonTypeHandle(),
                                pyValueHandle: cause.Reference,
                                pyTracebackHandle: innerTraceback is null ? new BorrowedReference() : innerTraceback.Reference);
                        }
                    }
                }

                using (var pyType = new PyObject(pyTypeHandle))
                using (PyObject pyTypeName = pyType.GetAttr("__name__"))
                {
                    pythonTypeName = pyTypeName.ToString();
                }

                msg = string.IsNullOrEmpty(msg) ? pythonTypeName : pythonTypeName + " : " + msg;
            }
            if (!pyTracebackHandle.IsNull)
            {
                traceback = TracebackHandleToString(pyTracebackHandle);
            }

            return new PythonException(pyTypeHandle, pyValueHandle, pyTracebackHandle,
                msg, pythonTypeName, traceback, inner);
        }

        static string TracebackHandleToString(BorrowedReference tracebackHandle) {
            if (tracebackHandle.IsNull) {
                throw new ArgumentNullException(nameof(tracebackHandle));
            }

            PyObject tracebackModule = PythonEngine.ImportModule("traceback");
            using var traceback = new PyObject(tracebackHandle);
            PyList stackLines = PyList.Wrap(tracebackModule.InvokeMethod("format_tb", traceback));
            stackLines.Reverse();
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
            if (this.IsDisposed)
            {
                return;
            }
            Finalizer.Instance.AddFinalizedObject(this);
        }

        /// <summary>
        /// Restores python error.
        /// </summary>
        public void Restore()
        {
            IntPtr gs = PythonEngine.AcquireLock();
            Runtime.PyErr_Restore(
                NewReference.DangerousMoveFromPointer(ref _pyType).Steal(),
                NewReference.DangerousMoveFromPointer(ref _pyValue).Steal(),
                NewReference.DangerousMoveFromPointer(ref _pyTB).Steal());
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
        /// Type of the exception (nullable).
        /// </summary>
        internal BorrowedReference PythonType => new BorrowedReference(_pyType);

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
        /// Exception object value (nullable).
        /// </summary>
        internal BorrowedReference Value => new BorrowedReference(_pyValue);

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
        /// Traceback (nullable).
        /// </summary>
        internal BorrowedReference Traceback => new BorrowedReference(_pyTB);

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
        /// Formats this PythonException object into a message as would be printed
        /// out via the Python console. See traceback.format_exception
        /// </summary>
        public string Format()
        {
            string res;
            IntPtr gs = PythonEngine.AcquireLock();
            try
            {
                if (_pyTB != IntPtr.Zero && _pyType != IntPtr.Zero && _pyValue != IntPtr.Zero)
                {
                    Runtime.XIncref(_pyType);
                    Runtime.XIncref(_pyValue);
                    Runtime.XIncref(_pyTB);
                    using (PyObject pyType = new PyObject(_pyType))
                    using (PyObject pyValue = new PyObject(_pyValue))
                    using (PyObject pyTB = new PyObject(_pyTB))
                    using (PyObject tb_mod = PythonEngine.ImportModule("traceback"))
                    {
                        var buffer = new StringBuilder();
                        var values = tb_mod.InvokeMethod("format_exception", pyType, pyValue, pyTB);
                        foreach (PyObject val in values)
                        {
                            buffer.Append(val.ToString());
                        }
                        res = buffer.ToString();
                    }
                }
                else
                {
                    res = StackTrace;
                }
            }
            finally
            {
                PythonEngine.ReleaseLock(gs);
            }
            return res;
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
            if (this.IsDisposed)
                return;

            if (Runtime.Py_IsInitialized() == 0)
                throw new InvalidOperationException("Python runtime must be initialized");

            if (!Runtime.IsFinalizing)
            {
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
            }

            GC.SuppressFinalize(this);
        }

        public IntPtr[] GetTrackedHandles()
        {
            return new IntPtr[] { _pyType, _pyValue, _pyTB };
        }

        bool IsDisposed => _pyType == IntPtr.Zero && _pyValue == IntPtr.Zero && _pyTB == IntPtr.Zero;

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
