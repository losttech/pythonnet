using System;
using System.Reflection;

namespace Python.Runtime
{
    /// <summary>
    /// Encapsulates the Python exception APIs.
    /// </summary>
    /// <remarks>
    /// Readability of the Exceptions class improvements as we look toward version 2.7 ...
    /// </remarks>
    public static class Exceptions
    {
        internal static IntPtr warnings_module;
        internal static IntPtr exceptions_module;

        /// <summary>
        /// Initialization performed on startup of the Python runtime.
        /// </summary>
        internal static void Initialize()
        {
            string exceptionsModuleName = "builtins";
            exceptions_module = Runtime.PyImport_ImportModule(exceptionsModuleName);

            Exceptions.ErrorCheck(exceptions_module);
            warnings_module = Runtime.PyImport_ImportModule("warnings");
            Exceptions.ErrorCheck(warnings_module);
            Type type = typeof(Exceptions);
            foreach (FieldInfo fi in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                IntPtr op = Runtime.PyObject_GetAttrString(exceptions_module, fi.Name);
                if (op != IntPtr.Zero)
                {
                    fi.SetValue(type, op);
                }
                else
                {
                    fi.SetValue(type, IntPtr.Zero);
                    DebugUtil.Print($"Unknown exception: {fi.Name}");
                }
            }
            Runtime.PyErr_Clear();
        }


        /// <summary>
        /// Cleanup resources upon shutdown of the Python runtime.
        /// </summary>
        internal static void Shutdown()
        {
            if (Runtime.Py_IsInitialized() == 0)
            {
                return;
            }
            Type type = typeof(Exceptions);
            foreach (FieldInfo fi in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var op = (IntPtr)fi.GetValue(type);
                if (op == IntPtr.Zero)
                {
                    continue;
                }
                Runtime.XDecref(op);
                fi.SetValue(null, IntPtr.Zero);
            }
            Runtime.Py_CLEAR(ref exceptions_module);
            Runtime.Py_CLEAR(ref warnings_module);
        }

        /// <summary>
        /// Shortcut for (pointer == NULL) -&gt; throw PythonException
        /// </summary>
        /// <param name="pointer">Pointer to a Python object</param>
        internal static void ErrorCheck(BorrowedReference pointer)
        {
            if (pointer.IsNull)
            {
                throw new PythonException();
            }
        }

        internal static void ErrorCheck(IntPtr pointer) => ErrorCheck(new BorrowedReference(pointer));

        /// <summary>
        /// Shortcut for (pointer == NULL or ErrorOccurred()) -&gt; throw PythonException
        /// </summary>
        internal static void ErrorOccurredCheck(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero || ErrorOccurred())
            {
                throw new PythonException();
            }
        }

        /// <summary>
        /// ExceptionMatches Method
        /// </summary>
        /// <remarks>
        /// Returns true if the current Python exception matches the given
        /// Python object. This is a wrapper for PyErr_ExceptionMatches.
        /// </remarks>
        public static bool ExceptionMatches(IntPtr ob)
        {
            return Runtime.PyErr_ExceptionMatches(ob) != 0;
        }

        /// <summary>
        /// ExceptionMatches Method
        /// </summary>
        /// <remarks>
        /// Returns true if the given Python exception matches the given
        /// Python object. This is a wrapper for PyErr_GivenExceptionMatches.
        /// </remarks>
        public static bool ExceptionMatches(IntPtr exc, IntPtr ob)
        {
            int i = Runtime.PyErr_GivenExceptionMatches(exc, ob);
            return i != 0;
        }

        /// <summary>
        /// SetError Method
        /// </summary>
        /// <remarks>
        /// Sets the current Python exception given a native string.
        /// This is a wrapper for the Python PyErr_SetString call.
        /// </remarks>
        public static void SetError(IntPtr ob, string value)
        {
            Runtime.PyErr_SetString(ob, value);
        }

        /// <summary>
        /// SetError Method
        /// </summary>
        /// <remarks>
        /// Sets the current Python exception given a Python object.
        /// This is a wrapper for the Python PyErr_SetObject call.
        /// </remarks>
        public static void SetError(IntPtr type, IntPtr exceptionObject)
        {
            Runtime.PyErr_SetObject(new BorrowedReference(type), new BorrowedReference(exceptionObject));
        }

        /// <summary>
        /// SetError Method
        /// </summary>
        /// <remarks>
        /// Sets the current Python exception given a CLR exception
        /// object. The CLR exception instance is wrapped as a Python
        /// object, allowing it to be handled naturally from Python.
        /// </remarks>
        public static void SetError(Exception e)
        {
            // Because delegates allow arbitrary nesting of Python calling
            // managed calling Python calling... etc. it is possible that we
            // might get a managed exception raised that is a wrapper for a
            // Python exception. In that case we'd rather have the real thing.

            var pe = e as PythonException;
            if (pe != null)
            {
                Runtime.XIncref(pe.PyType);
                Runtime.XIncref(pe.PyValue);
                Runtime.XIncref(pe.PyTB);
                Runtime.PyErr_Restore(pe.PyType, pe.PyValue, pe.PyTB);
                return;
            }

            IntPtr op = Converter.ToPython(e);
            if (op == IntPtr.Zero)
                return;

            IntPtr etype = Runtime.PyObject_GetAttr(op, PyIdentifier.__class__);
            Runtime.PyErr_SetObject(new BorrowedReference(etype), new BorrowedReference(op));
            Runtime.XDecref(etype);
            Runtime.XDecref(op);
        }

        /// <summary>
        /// When called after SetError, sets the cause of the error.
        /// </summary>
        /// <param name="cause">The cause of the current error</param>
        public static void SetCause(PythonException cause)
        {
            var currentException = new PythonException();
            currentException.Normalize();
            cause.Normalize();
            Runtime.XIncref(cause.PyValue);
            Runtime.PyException_SetCause(currentException.PyValue, cause.PyValue);
            currentException.Restore();
        }

        /// <summary>
        /// ErrorOccurred Method
        /// </summary>
        /// <remarks>
        /// Returns true if an exception occurred in the Python runtime.
        /// This is a wrapper for the Python PyErr_Occurred call.
        /// </remarks>
        public static bool ErrorOccurred()
        {
            return Runtime.PyErr_Occurred() != IntPtr.Zero;
        }

        /// <summary>
        /// Clear Method
        /// </summary>
        /// <remarks>
        /// Clear any exception that has been set in the Python runtime.
        /// </remarks>
        public static void Clear()
        {
            Runtime.PyErr_Clear();
        }

        //====================================================================
        // helper methods for raising warnings
        //====================================================================

        /// <summary>
        /// Alias for Python's warnings.warn() function.
        /// </summary>
        public static void warn(string message, IntPtr exception, int stacklevel)
        {
            if (exception == IntPtr.Zero ||
                (Runtime.PyObject_IsSubclass(exception, Exceptions.Warning) != 1))
            {
                Exceptions.RaiseTypeError("Invalid exception");
            }

            Runtime.XIncref(warnings_module);
            IntPtr warn = Runtime.PyObject_GetAttrString(warnings_module, "warn");
            Runtime.XDecref(warnings_module);
            Exceptions.ErrorCheck(warn);

            IntPtr args = Runtime.PyTuple_New(3);
            IntPtr msg = Runtime.PyString_FromString(message);
            Runtime.XIncref(exception); // PyTuple_SetItem steals a reference
            IntPtr level = Runtime.PyInt_FromInt32(stacklevel);
            Runtime.PyTuple_SetItem(args, 0, msg);
            Runtime.PyTuple_SetItem(args, 1, exception);
            Runtime.PyTuple_SetItem(args, 2, level);

            IntPtr result = Runtime.PyObject_CallObject(warn, args);
            Exceptions.ErrorCheck(result);

            Runtime.XDecref(warn);
            Runtime.XDecref(result);
            Runtime.XDecref(args);
        }

        public static void warn(string message, IntPtr exception)
        {
            warn(message, exception, 1);
        }

        public static void deprecation(string message, int stacklevel)
        {
            warn(message, Exceptions.DeprecationWarning, stacklevel);
        }

        public static void deprecation(string message)
        {
            deprecation(message, 1);
        }

        //====================================================================
        // Internal helper methods for common error handling scenarios.
        //====================================================================

        /// <summary>
        /// Raises a TypeError exception and attaches any existing exception as its cause.
        /// </summary>
        /// <param name="message">The exception message</param>
        /// <returns><c>IntPtr.Zero</c></returns>
        internal static IntPtr RaiseTypeError(string message)
        {
            PythonException previousException = null;
            if (ErrorOccurred())
            {
                previousException = new PythonException();
            }
            Exceptions.SetError(Exceptions.TypeError, message);
            if (previousException != null)
            {
                SetCause(previousException);
            }
            return IntPtr.Zero;
        }

        // 2010-11-16: Arranged in python (2.6 & 2.7) source header file order
        /* Predefined exceptions are
           public static variables on the Exceptions class filled in from
           the python class using reflection in Initialize() looked up by
		   name, not position. */
        public static IntPtr BaseException;
        public static IntPtr Exception;
        public static IntPtr StopIteration;
        public static IntPtr GeneratorExit;
        public static IntPtr ArithmeticError;
        public static IntPtr LookupError;

        public static IntPtr AssertionError;
        public static IntPtr AttributeError;
        public static IntPtr EOFError;
        public static IntPtr FloatingPointError;
        public static IntPtr EnvironmentError;
        public static IntPtr IOError;
        public static IntPtr OSError;
        public static IntPtr ImportError;
        public static IntPtr IndexError;
        public static IntPtr KeyError;
        public static IntPtr KeyboardInterrupt;
        public static IntPtr MemoryError;
        public static IntPtr NameError;
        public static IntPtr OverflowError;
        public static IntPtr RuntimeError;
        public static IntPtr NotImplementedError;
        public static IntPtr SyntaxError;
        public static IntPtr IndentationError;
        public static IntPtr TabError;
        public static IntPtr ReferenceError;
        public static IntPtr SystemError;
        public static IntPtr SystemExit;
        public static IntPtr TypeError;
        public static IntPtr UnboundLocalError;
        public static IntPtr UnicodeError;
        public static IntPtr UnicodeEncodeError;
        public static IntPtr UnicodeDecodeError;
        public static IntPtr UnicodeTranslateError;
        public static IntPtr ValueError;
        public static IntPtr ZeroDivisionError;
        //#ifdef MS_WINDOWS
        //public static IntPtr WindowsError;
        //#endif
        //#ifdef __VMS
        //public static IntPtr VMSError;
        //#endif

        //PyAPI_DATA(PyObject *) PyExc_BufferError;

        //PyAPI_DATA(PyObject *) PyExc_MemoryErrorInst;
        //PyAPI_DATA(PyObject *) PyExc_RecursionErrorInst;


        /* Predefined warning categories */
        public static IntPtr Warning;
        public static IntPtr UserWarning;
        public static IntPtr DeprecationWarning;
        public static IntPtr PendingDeprecationWarning;
        public static IntPtr SyntaxWarning;
        public static IntPtr RuntimeWarning;
        public static IntPtr FutureWarning;
        public static IntPtr ImportWarning;
        public static IntPtr UnicodeWarning;
        //PyAPI_DATA(PyObject *) PyExc_BytesWarning;
    }
}
