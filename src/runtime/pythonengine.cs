using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

using Python.Runtime.Native;
using static Python.Runtime.Runtime;

namespace Python.Runtime
{
    /// <summary>
    /// This class provides the public interface of the Python runtime.
    /// </summary>
    public class PythonEngine : IDisposable
    {
        public static ShutdownMode ShutdownMode
        {
            get => Runtime.ShutdownMode;
            set => Runtime.ShutdownMode = value;
        }

        public static ShutdownMode DefaultShutdownMode => GetDefaultShutdownMode();

        private static DelegateManager delegateManager;
        private static bool initialized;
        private static IntPtr _pythonHome = IntPtr.Zero;
        private static IntPtr _programName = IntPtr.Zero;
        private static IntPtr _pythonPath = IntPtr.Zero;

        public PythonEngine()
        {
            Initialize();
        }

        public PythonEngine(params string[] args)
        {
            Initialize(args);
        }

        public PythonEngine(IEnumerable<string> args)
        {
            Initialize(args);
        }

        public void Dispose()
        {
            Shutdown();
        }

        public static bool IsInitialized
        {
            get { return initialized; }
        }

        internal static DelegateManager DelegateManager
        {
            get
            {
                if (delegateManager == null)
                {
                    throw new InvalidOperationException(
                        "DelegateManager has not yet been initialized using Python.Runtime.PythonEngine.Initialize().");
                }
                return delegateManager;
            }
        }

        public static string ProgramName
        {
            get
            {
                IntPtr p = Runtime.Py_GetProgramName();
                return UcsMarshaler.PtrToPy3UnicodePy2String(p) ?? "";
            }
            set
            {
                Marshal.FreeHGlobal(_programName);
                _programName = UcsMarshaler.Py3UnicodePy2StringtoPtr(value);
                Runtime.Py_SetProgramName(_programName);
            }
        }

        public static string PythonHome
        {
            get
            {
                IntPtr p = Runtime.Py_GetPythonHome();
                return UcsMarshaler.PtrToPy3UnicodePy2String(p) ?? "";
            }
            set
            {
                // this value is null in the beginning
                Marshal.FreeHGlobal(_pythonHome);
                _pythonHome = UcsMarshaler.Py3UnicodePy2StringtoPtr(value);
                Runtime.Py_SetPythonHome(_pythonHome);
            }
        }

        public static string PythonPath
        {
            get
            {
                IntPtr p = Runtime.Py_GetPath();
                return UcsMarshaler.PtrToPy3UnicodePy2String(p) ?? "";
            }
            set
            {
                Marshal.FreeHGlobal(_pythonPath);
                _pythonPath = UcsMarshaler.Py3UnicodePy2StringtoPtr(value);
                Runtime.Py_SetPath(_pythonPath);
            }
        }

        public static string Version
        {
            get { return Marshal.PtrToStringAnsi(Runtime.Py_GetVersion()); }
        }

        public static string BuildInfo
        {
            get { return Marshal.PtrToStringAnsi(Runtime.Py_GetBuildInfo()); }
        }

        public static string Platform
        {
            get { return Marshal.PtrToStringAnsi(Runtime.Py_GetPlatform()); }
        }

        public static string Copyright
        {
            get { return Marshal.PtrToStringAnsi(Runtime.Py_GetCopyright()); }
        }

        public static string Compiler
        {
            get { return Marshal.PtrToStringAnsi(Runtime.Py_GetCompiler()); }
        }

        /// <summary>
        /// Set the NoSiteFlag to disable loading the site module.
        /// Must be called before Initialize.
        /// https://docs.python.org/3/c-api/init.html#c.Py_NoSiteFlag
        /// </summary>
        public static void SetNoSiteFlag()
        {
            Runtime.SetNoSiteFlag();
        }

        public static int RunSimpleString(string code)
        {
            return Runtime.PyRun_SimpleString(code);
        }

        public static void Initialize()
        {
            Initialize(setSysArgv: true);
        }

        public static void Initialize(bool setSysArgv = true, bool initSigs = false, ShutdownMode mode = ShutdownMode.Default)
        {
            Initialize(Enumerable.Empty<string>(), setSysArgv: setSysArgv, initSigs: initSigs, mode);
        }

        /// <summary>
        /// Initialize Method
        /// </summary>
        /// <remarks>
        /// Initialize the Python runtime. It is safe to call this method
        /// more than once, though initialization will only happen on the
        /// first call. It is *not* necessary to hold the Python global
        /// interpreter lock (GIL) to call this method.
        /// initSigs can be set to 1 to do default python signal configuration. This will override the way signals are handled by the application.
        /// </remarks>
        public static void Initialize(IEnumerable<string> args, bool setSysArgv = true, bool initSigs = false, ShutdownMode mode = ShutdownMode.Default)
        {
            if (initialized)
            {
                return;
            }
            // Creating the delegateManager MUST happen before Runtime.Initialize
            // is called. If it happens afterwards, DelegateManager's CodeGenerator
            // throws an exception in its ctor.  This exception is eaten somehow
            // during an initial "import clr", and the world ends shortly thereafter.
            // This is probably masking some bad mojo happening somewhere in Runtime.Initialize().
            delegateManager = new DelegateManager();
            Runtime.Initialize(initSigs, mode);
            initialized = true;
            Exceptions.Clear();

            // Make sure we clean up properly on app domain unload.
            AppDomain.CurrentDomain.DomainUnload += OnDomainUnload;

            // The global scope gets used implicitly quite early on, remember
            // to clear it out when we shut down.
            AddShutdownHandler(PyScopeManager.Global.Clear);

            if (setSysArgv)
            {
                Py.SetArgv(args);
            }

            // Load the clr.py resource into the clr module
            NewReference clr = ImportHook.GetCLRModule();
            BorrowedReference clr_dict = Runtime.PyModule_GetDict(clr);

            var locals = new PyDict();
            try
            {
                BorrowedReference module = Runtime.PyImport_AddModule("clr._extras");
                BorrowedReference module_globals = Runtime.PyModule_GetDict(module);
                BorrowedReference builtins = Runtime.PyEval_GetBuiltins();
                Runtime.PyDict_SetItemString(module_globals, "__builtins__", builtins);

                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream("clr.py"))
                using (var reader = new StreamReader(stream))
                {
                    // add the contents of clr.py to the module
                    string clr_py = reader.ReadToEnd();
                    Exec(clr_py, module_globals, locals.Reference);
                }

                // add the imported module to the clr module, and copy the API functions
                // and decorators into the main clr module.
                Runtime.PyDict_SetItemString(clr_dict, "_extras", module);
                using (var keys = locals.Keys())
                foreach (PyObject key in keys)
                {
                    if (!key.ToString().StartsWith("_") || key.ToString().Equals("__version__"))
                    {
                        PyObject value = locals[key];
                        Runtime.PyDict_SetItem(clr_dict, key.Reference, value.Reference);
                        value.Dispose();
                    }
                    key.Dispose();
                }
            }
            finally
            {
                locals.Dispose();
            }
        }

        static void OnDomainUnload(object _, EventArgs __)
        {
            Shutdown();
        }

        /// <summary>
        /// A helper to perform initialization from the context of an active
        /// CPython interpreter process - this bootstraps the managed runtime
        /// when it is imported by the CLR extension module.
        /// </summary>
        public static IntPtr InitExt()
        {
            try
            {
                Initialize(setSysArgv: false, mode: ShutdownMode.Extension);

                // Trickery - when the import hook is installed into an already
                // running Python, the standard import machinery is still in
                // control for the duration of the import that caused bootstrap.
                //
                // That is problematic because the std machinery tries to get
                // sub-names directly from the module __dict__ rather than going
                // through our module object's getattr hook. This workaround is
                // evil ;) We essentially climb up the stack looking for the
                // import that caused the bootstrap to happen, then re-execute
                // the import explicitly after our hook has been installed. By
                // doing this, the original outer import should work correctly.
                //
                // Note that this is only needed during the execution of the
                // first import that installs the CLR import hook. This hack
                // still doesn't work if you use the interactive interpreter,
                // since there is no line info to get the import line ;(

                string code =
                    "import traceback\n" +
                    "for item in traceback.extract_stack():\n" +
                    "    line = item[3]\n" +
                    "    if line is not None:\n" +
                    "        if line.startswith('import CLR') or \\\n" +
                    "           line.startswith('import clr') or \\\n" +
                    "           line.startswith('from clr') or \\\n" +
                    "           line.startswith('from CLR'):\n" +
                    "            exec(line)\n" +
                    "            break\n";

                PythonEngine.Exec(code);
            }
            catch (PythonException e)
            {
                e.Restore();
                return IntPtr.Zero;
            }

            return ImportHook.GetCLRModule()
                   .DangerousMoveToPointerOrNull();
        }

        /// <summary>
        /// Shutdown Method
        /// </summary>
        /// <remarks>
        /// Shutdown and release resources held by the Python runtime. The
        /// Python runtime can no longer be used in the current process
        /// after calling the Shutdown method.
        /// </remarks>
        /// <param name="mode">The ShutdownMode to use when shutting down the Runtime</param>
        public static void Shutdown(ShutdownMode mode)
        {
            if (!initialized)
            {
                return;
            }
            // If the shutdown handlers trigger a domain unload,
            // don't call shutdown again.
            AppDomain.CurrentDomain.DomainUnload -= OnDomainUnload;

            ClearClrModules();

            ExecuteShutdownHandlers();
            Runtime.Disconnect();
            if (mode == ShutdownMode.Normal)
            {
                Runtime.Shutdown();
            }

            initialized = false;
        }

        /// <summary>
        /// Shutdown Method
        /// </summary>
        /// <remarks>
        /// Shutdown and release resources held by the Python runtime. The
        /// Python runtime can no longer be used in the current process
        /// after calling the Shutdown method.
        /// </remarks>
        public static void Shutdown()
        {
            Shutdown(Runtime.ShutdownMode);
        }

        /// <summary>
        /// Called when the engine is shut down.
        ///
        /// Shutdown handlers are run in reverse order they were added, so that
        /// resources available when running a shutdown handler are the same as
        /// what was available when it was added.
        /// </summary>
        public delegate void ShutdownHandler();

        static List<ShutdownHandler> ShutdownHandlers = new List<ShutdownHandler>();

        /// <summary>
        /// Add a function to be called when the engine is shut down.
        ///
        /// Shutdown handlers are executed in the opposite order they were
        /// added, so that you can be sure that everything that was initialized
        /// when you added the handler is still initialized when you need to shut
        /// down.
        ///
        /// If the same shutdown handler is added several times, it will be run
        /// several times.
        ///
        /// Don't add shutdown handlers while running a shutdown handler.
        /// </summary>
        public static void AddShutdownHandler(ShutdownHandler handler)
        {
            ShutdownHandlers.Add(handler);
        }

        /// <summary>
        /// Remove a shutdown handler.
        ///
        /// If the same shutdown handler is added several times, only the last
        /// one is removed.
        ///
        /// Don't remove shutdown handlers while running a shutdown handler.
        /// </summary>
        public static void RemoveShutdownHandler(ShutdownHandler handler)
        {
            for (int index = ShutdownHandlers.Count - 1; index >= 0; --index)
            {
                if (ShutdownHandlers[index] == handler)
                {
                    ShutdownHandlers.RemoveAt(index);
                    break;
                }
            }
        }

        /// <summary>
        /// Run all the shutdown handlers.
        ///
        /// They're run in opposite order they were added.
        /// </summary>
        static void ExecuteShutdownHandlers()
        {
            while(ShutdownHandlers.Count > 0)
            {
                var handler = ShutdownHandlers[ShutdownHandlers.Count - 1];
                ShutdownHandlers.RemoveAt(ShutdownHandlers.Count - 1);
                handler();
            }
        }

        /// <summary>
        /// AcquireLock Method
        /// </summary>
        /// <remarks>
        /// Acquire the Python global interpreter lock (GIL). Managed code
        /// *must* call this method before using any objects or calling any
        /// methods on objects in the Python.Runtime namespace. The only
        /// exception is PythonEngine.Initialize, which may be called without
        /// first calling AcquireLock.
        /// Each call to AcquireLock must be matched by a corresponding call
        /// to ReleaseLock, passing the token obtained from AcquireLock.
        /// For more information, see the "Extending and Embedding" section
        /// of the Python documentation on www.python.org.
        /// </remarks>
        public static IntPtr AcquireLock()
        {
            return Runtime.PyGILState_Ensure();
        }


        /// <summary>
        /// ReleaseLock Method
        /// </summary>
        /// <remarks>
        /// Release the Python global interpreter lock using a token obtained
        /// from a previous call to AcquireLock.
        /// For more information, see the "Extending and Embedding" section
        /// of the Python documentation on www.python.org.
        /// </remarks>
        public static void ReleaseLock(IntPtr gs)
        {
            Runtime.PyGILState_Release(gs);
        }


        /// <summary>
        /// BeginAllowThreads Method
        /// </summary>
        /// <remarks>
        /// Release the Python global interpreter lock to allow other threads
        /// to run. This is equivalent to the Py_BEGIN_ALLOW_THREADS macro
        /// provided by the C Python API.
        /// For more information, see the "Extending and Embedding" section
        /// of the Python documentation on www.python.org.
        /// </remarks>
        public static IntPtr BeginAllowThreads()
        {
            return Runtime.PyEval_SaveThread();
        }


        /// <summary>
        /// EndAllowThreads Method
        /// </summary>
        /// <remarks>
        /// Re-aquire the Python global interpreter lock for the current
        /// thread. This is equivalent to the Py_END_ALLOW_THREADS macro
        /// provided by the C Python API.
        /// For more information, see the "Extending and Embedding" section
        /// of the Python documentation on www.python.org.
        /// </remarks>
        public static void EndAllowThreads(IntPtr ts)
        {
            Runtime.PyEval_RestoreThread(ts);
        }

        /// <summary>
        /// Eval Method
        /// </summary>
        /// <remarks>
        /// Evaluate a Python expression and returns the result.
        /// It's a subset of Python eval function.
        /// </remarks>
        public static PyObject Eval(string code, IntPtr? globals = null, IntPtr? locals = null)
        {
            var globalsRef = new BorrowedReference(globals.GetValueOrDefault());
            var localsRef = new BorrowedReference(locals.GetValueOrDefault());
            PyObject result = RunString(code, globalsRef, localsRef, RunFlagType.Eval);
            return result;
        }


        /// <summary>
        /// Exec Method
        /// </summary>
        /// <remarks>
        /// Run a string containing Python code.
        /// It's a subset of Python exec function.
        /// </remarks>
        public static void Exec(string code, IntPtr? globals = null, IntPtr? locals = null)
        {
            var globalsRef = new BorrowedReference(globals.GetValueOrDefault());
            var localsRef = new BorrowedReference(locals.GetValueOrDefault());
            using PyObject result = RunString(code, globalsRef, localsRef, RunFlagType.File);
            if (result.obj != Runtime.PyNone)
            {
                throw new PythonException();
            }
        }
        /// <summary>
        /// Exec Method
        /// </summary>
        /// <remarks>
        /// Run a string containing Python code.
        /// It's a subset of Python exec function.
        /// </remarks>
        internal static void Exec(string code, BorrowedReference globals, BorrowedReference locals = default)
        {
            using PyObject result = RunString(code, globals: globals, locals: locals, RunFlagType.File);
            if (result.obj != Runtime.PyNone)
            {
                throw new PythonException();
            }
        }

        /// <summary>
        /// Gets the Python thread ID.
        /// </summary>
        /// <returns>The Python thread ID.</returns>
        public static ulong GetPythonThreadID()
        {
            dynamic threading = Py.Import("threading");
            return threading.InvokeMethod("get_ident");
        }

        /// <summary>
        /// Interrupts the execution of a thread.
        /// </summary>
        /// <param name="pythonThreadID">The Python thread ID.</param>
        /// <returns>The number of thread states modified; this is normally one, but will be zero if the thread id isn’t found.</returns>
        public static int Interrupt(ulong pythonThreadID)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Runtime.PyThreadState_SetAsyncExcLLP64((uint)pythonThreadID, Exceptions.KeyboardInterrupt);
            }

            return Runtime.PyThreadState_SetAsyncExcLP64(pythonThreadID, Exceptions.KeyboardInterrupt);
        }

        /// <summary>
        /// RunString Method. Function has been deprecated and will be removed.
        /// Use Exec/Eval/RunSimpleString instead.
        /// </summary>
        [Obsolete("RunString is deprecated and will be removed. Use Exec/Eval/RunSimpleString instead.")]
        public static PyObject RunString(string code, IntPtr? globals = null, IntPtr? locals = null)
        {
            return RunString(code, new BorrowedReference(globals.GetValueOrDefault()), new BorrowedReference(locals.GetValueOrDefault()), RunFlagType.File);
        }

        /// <summary>
        /// Internal RunString Method.
        /// </summary>
        /// <remarks>
        /// Run a string containing Python code. Returns the result of
        /// executing the code string as a PyObject instance, or null if
        /// an exception was raised.
        /// </remarks>
        internal static PyObject RunString(string code, BorrowedReference globals, BorrowedReference locals, RunFlagType flag)
        {
            NewReference tempGlobals = default;
            if (globals.IsNull)
            {
                globals = Runtime.PyEval_GetGlobals();
                if (globals.IsNull)
                {
                    globals = tempGlobals = NewReference.DangerousFromPointer(Runtime.PyDict_New());
                    Runtime.PyDict_SetItem(
                        globals, PyIdentifier.__builtins__,
                        Runtime.PyEval_GetBuiltins()
                    );
                }
            }

            if (locals == null)
            {
                locals = globals;
            }

            try
            {
                NewReference result = Runtime.PyRun_String(
                    code, flag, globals, locals
                );
                PythonException.ThrowIfIsNull(result);
                return result.MoveToPyObject();
            }
            finally
            {
                tempGlobals.Dispose();
            }
        }

        public static void MoveClrInstancesOwnershipToPython()
        {
            var objs = ManagedType.GetManagedObjects();
            var copyObjs = objs.ToArray();
            foreach (var entry in copyObjs)
            {
                ManagedType obj = entry.Key;
                if (!objs.ContainsKey(obj))
                {
                    Debug.Assert(obj.gcHandle == default);
                    continue;
                }
                if (entry.Value == ManagedType.TrackTypes.Extension)
                {
                    obj.CallTypeClear();
                    // obj's tp_type will degenerate to a pure Python type after TypeManager.RemoveTypes(),
                    // thus just be safe to give it back to GC chain.
                    if (!Runtime._PyObject_GC_IS_TRACKED(obj.ObjectReference))
                    {
                        Runtime.PyObject_GC_Track(obj.pyHandle);
                    }
                }
                if (obj.gcHandle.IsAllocated)
                {
                    obj.gcHandle.Free();
                }
                obj.gcHandle = default;
            }
            ManagedType.ClearTrackedObjects();
        }

        internal static ShutdownMode GetDefaultShutdownMode()
        {
            string modeEvn = Environment.GetEnvironmentVariable("PYTHONNET_SHUTDOWN_MODE");
            if (modeEvn == null)
            {
                return ShutdownMode.Normal;
            }
            ShutdownMode mode;
            if (Enum.TryParse(modeEvn, true, out mode))
            {
                return mode;
            }
            return ShutdownMode.Normal;
        }

        private static void ClearClrModules()
        {
            var modules = PyImport_GetModuleDict();
            var items = PyDict_Items(modules);
            long length = PyList_Size(items);
            for (long i = 0; i < length; i++)
            {
                var item = PyList_GetItem(items, i);
                var name = PyTuple_GetItem(item, 0);
                var module = PyTuple_GetItem(item, 1);
                if (ManagedType.IsManagedType(module))
                {
                    PyDict_DelItem(modules, name);
                }
            }
            items.Dispose();
        }
    }

    public static class Py
    {
        public static GILState GIL()
        {
            if (!PythonEngine.IsInitialized)
            {
                PythonEngine.Initialize();
            }

            return new GILState();
        }

        public static PyScope CreateScope()
        {
            var scope = PyScopeManager.Global.Create();
            return scope;
        }

        public static PyScope CreateScope(string name)
        {
            var scope = PyScopeManager.Global.Create(name);
            return scope;
        }

        public class GILState : IDisposable
        {
            private readonly IntPtr state;
            private bool isDisposed;

            internal GILState()
            {
                state = PythonEngine.AcquireLock();
            }

            public void Dispose()
            {
                if (this.isDisposed) return;

                PythonEngine.ReleaseLock(state);
                GC.SuppressFinalize(this);
                this.isDisposed = true;
            }

            ~GILState()
            {
                Dispose();
            }
        }

        public static PyObject Import(string name)
        {
            return PyModule.Import(name);
        }

        public static void SetArgv()
        {
            IEnumerable<string> args;
            try
            {
                args = Environment.GetCommandLineArgs();
            }
            catch (NotSupportedException)
            {
                args = Enumerable.Empty<string>();
            }

            SetArgv(
                new[] { "" }.Concat(
                    Environment.GetCommandLineArgs().Skip(1)
                )
            );
        }

        public static void SetArgv(params string[] argv)
        {
            SetArgv(argv as IEnumerable<string>);
        }

        public static void SetArgv(IEnumerable<string> argv)
        {
            using (GIL())
            {
                string[] arr = argv.ToArray();
                Runtime.PySys_SetArgvEx(arr.Length, arr, 0);
                Runtime.CheckExceptionOccurred();
            }
        }

        public static void With(PyObject obj, Action<dynamic> Body)
        {
            // Behavior described here:
            // https://docs.python.org/2/reference/datamodel.html#with-statement-context-managers

            IntPtr type = Runtime.PyNone;
            IntPtr val = Runtime.PyNone;
            IntPtr traceBack = Runtime.PyNone;
            PythonException ex = null;

            try
            {
                PyObject enterResult = obj.InvokeMethod("__enter__");

                Body(enterResult);
            }
            catch (PythonException e)
            {
                ex = e;
                type = ex.PyType.Coalesce(type);
                val = ex.PyValue.Coalesce(val);
                traceBack = ex.PyTB.Coalesce(traceBack);
            }

            Runtime.XIncref(type);
            Runtime.XIncref(val);
            Runtime.XIncref(traceBack);
            var exitResult = obj.InvokeMethod("__exit__", new PyObject(type), new PyObject(val), new PyObject(traceBack));

            if (ex != null && !exitResult.IsTrue()) throw ex;
        }
    }
}
