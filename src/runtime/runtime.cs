using System;
using System.Globalization;
using System.Diagnostics.Contracts;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using Python.Runtime.Platform;

using Python.Runtime.Platforms;

namespace Python.Runtime
{
    [SuppressUnmanagedCodeSecurity]
    internal static class NativeMethods
    {
        public static IntPtr LoadLibrary(string dllToLoad) => impl.LoadLibrary(dllToLoad);
        public static IntPtr GetProcAddress(IntPtr hModule, string procedureName)
            => impl.GetProcAddress(hModule, procedureName);
        public static void FreeLibrary(IntPtr hModule) => impl.FreeLibrary(hModule);

        static readonly INativeLibraryLoader impl =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsLibraryLoader.Instance
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? LinuxLibraryLoader.Instance
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? MacLibraryLoader.Instance
            : Throw<INativeLibraryLoader>(new PlatformNotSupportedException());

        internal static T Throw<T>(Exception exception)
        {
            throw exception;
        }
    }

    /// <summary>
    /// Encapsulates the low-level Python C API. Note that it is
    /// the responsibility of the caller to have acquired the GIL
    /// before calling any of these methods.
    /// </summary>
    public static class Runtime
    {
        static Runtime() {
            lock (VerLock) {
                UpdateVersionFields();
            }
        }

        // C# compiler copies constants to the assemblies that references this library.
        // We needs to replace all public constants to static readonly fields to allow
        // binary substitution of different Python.Runtime.dll builds in a target application.

        public static int UCS { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 2 : 4;

        // C# compiler copies constants to the assemblies that references this library.
        // We needs to replace all public constants to static readonly fields to allow
        // binary substitution of different Python.Runtime.dll builds in a target application.

        public static string pyversion => _pyversion;
        /// <summary>
        /// Two-character string, describing Python version (e.g. "36" for Python 3.6)
        /// </summary>
        public static string pyver => _pyver;

        static readonly object VerLock = new object();
        static Version pythonVersion = new Version(3, 7);
        public static Version PythonVersion {
            get {
                lock (VerLock) {
                    return pythonVersion;
                }
            }
            set {
                if (value == null)
                    throw new ArgumentNullException(nameof(Version));

                lock (VerLock) {
                    pythonVersion = value;
                    UpdateVersionFields();
                }
            }
        }

        static void UpdateVersionFields() {
            _pyversion = pythonVersion.ToString(2);
            _pyver = string.Format(CultureInfo.InvariantCulture,
                "{0}{1}", pythonVersion.Major, pythonVersion.Minor);
            pyversionnumber = GetPyVersionNumber();
        }

        internal static string _pyversion { get; private set; }
        internal static string _pyver { get; private set; }

#if PYTHON_WITH_PYDEBUG
        const string dllWithPyDebug = "d";
#else
        const string dllWithPyDebug = "";
#endif

        static readonly bool IsWindowsPlatform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        static string GetDefaultDllName() {
#if PYTHON_WITHOUT_ENABLE_SHARED && !NETSTANDARD
            return "__Internal";
#else
            string dllBase = "python" + (IsWindowsPlatform ? _pyver : _pyversion);
            string dllWithPyMalloc = IsWindowsPlatform ? "" : "m";
            return dllBase + dllWithPyDebug + dllWithPyMalloc;
#endif
        }

        static string pythonDllOverride;

        public static string PythonDLL {
            get { return pythonDllOverride ?? GetDefaultDllName(); }
            set {
                if (value == null)
                    throw new ArgumentNullException(nameof(PythonDLL));

                pythonDllOverride = value;
            }
        }

        static int GetPyVersionNumber() {
            return PythonVersion.Major * 10 + PythonVersion.Minor;;
        }
        [Obsolete("Use PythonVersion instead")]
        public static int pyversionnumber = GetPyVersionNumber();

        // set to true when python is finalizing
        internal static object IsFinalizingLock = new object();
        internal static bool IsFinalizing;

        internal static bool Is32Bit => IntPtr.Size == 4;

        [Obsolete("Use IsWindowsPlatform")]
        internal static readonly bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

        static readonly Dictionary<string, OperatingSystemType> OperatingSystemTypeMapping = new Dictionary<string, OperatingSystemType>()
        {
            { "Windows", OperatingSystemType.Windows },
            { "Darwin", OperatingSystemType.Darwin },
            { "Linux", OperatingSystemType.Linux },
        };

        [Obsolete]
        public static string OperatingSystemName => OperatingSystem.ToString();

        [Obsolete]
        public static string MachineName => Machine.ToString();

        /// <summary>
        /// Gets the operating system as reported by python's platform.system().
        /// </summary>
        public static OperatingSystemType OperatingSystem { get; private set; }

        /// <summary>
        /// Map lower-case version of the python machine name to the processor
        /// type. There are aliases, e.g. x86_64 and amd64 are two names for
        /// the same thing. Make sure to lower-case the search string, because
        /// capitalization can differ.
        /// </summary>
        static readonly Dictionary<string, MachineType> MachineTypeMapping = new Dictionary<string, MachineType>()
        {
            ["i386"] = MachineType.i386,
            ["i686"] = MachineType.i386,
            ["x86"] = MachineType.i386,
            ["x86_64"] = MachineType.x86_64,
            ["amd64"] = MachineType.x86_64,
            ["x64"] = MachineType.x86_64,
            ["em64t"] = MachineType.x86_64,
            ["armv7l"] = MachineType.armv7l,
            ["armv8"] = MachineType.armv8,
            ["aarch64"] = MachineType.aarch64,
        };

        /// <summary>
        /// Gets the machine architecture as reported by python's platform.machine().
        /// </summary>
        public static MachineType Machine { get; private set; }/* set in Initialize using python's platform.machine */

        public static int MainManagedThreadId { get; private set; }

        /// <summary>
        /// Encoding to use to convert Unicode to/from Managed to Native
        /// </summary>
        internal static readonly Encoding PyEncoding = UCS == 2 ? Encoding.Unicode : Encoding.UTF32;

        private static PyReferenceCollection _pyRefs = new PyReferenceCollection();

        static long run = 0;

        internal static long GetRun() {
            long runNumber = Interlocked.Read(ref run);
            System.Diagnostics.Debug.Assert(runNumber > 0);
            return runNumber;
        }

        /// <summary>
        /// Initialize the runtime...
        /// </summary>
        internal static void Initialize(bool initSigs = false)
        {
            if (Py_IsInitialized() == 0)
            {
                Py_InitializeEx(initSigs ? 1 : 0);
                MainManagedThreadId = Thread.CurrentThread.ManagedThreadId;
            }

            if (PyEval_ThreadsInitialized() == 0)
            {
                PyEval_InitThreads();
            }

            IsFinalizing = false;

            GenericUtil.Reset();
            PyScopeManager.Reset();
            ClassManager.Reset();
            ClassDerivedObject.Reset();
            TypeManager.Reset();

            IntPtr op;
            {
                var builtins = GetBuiltins();
                SetPyMember(ref PyNotImplemented, PyObject_GetAttrString(builtins, "NotImplemented"),
                    () => PyNotImplemented = IntPtr.Zero);

                SetPyMember(ref PyBaseObjectType, PyObject_GetAttrString(builtins, "object"),
                    () => PyBaseObjectType = IntPtr.Zero);

                SetPyMember(ref PyNone, PyObject_GetAttrString(builtins, "None"),
                    () => PyNone = IntPtr.Zero);
                SetPyMember(ref PyTrue, PyObject_GetAttrString(builtins, "True"),
                    () => PyTrue = IntPtr.Zero);
                SetPyMember(ref PyFalse, PyObject_GetAttrString(builtins, "False"),
                    () => PyFalse = IntPtr.Zero);

                SetPyMember(ref PyBoolType, PyObject_Type(PyTrue),
                    () => PyBoolType = IntPtr.Zero);
                SetPyMember(ref PyNoneType, PyObject_Type(PyNone),
                    () => PyNoneType = IntPtr.Zero);
                SetPyMember(ref PyTypeType, PyObject_Type(PyNoneType),
                    () => PyTypeType = IntPtr.Zero);

                op = PyObject_GetAttrString(builtins, "len");
                SetPyMember(ref PyMethodType, PyObject_Type(op),
                    () => PyMethodType = IntPtr.Zero);
                XDecref(op);

                // For some arcane reason, builtins.__dict__.__setitem__ is *not*
                // a wrapper_descriptor, even though dict.__setitem__ is.
                //
                // object.__init__ seems safe, though.
                op = PyObject_GetAttrString(PyBaseObjectType, "__init__");
                SetPyMember(ref PyWrapperDescriptorType, PyObject_Type(op),
                    () => PyWrapperDescriptorType = IntPtr.Zero);
                XDecref(op);

                SetPyMember(ref PySuper_Type, PyObject_GetAttrString(builtins, "super"),
                    () => PySuper_Type = IntPtr.Zero);

                XDecref(builtins);
            }

            op = PyString_FromString("string");
            SetPyMember(ref PyStringType, PyObject_Type(op),
                () => PyStringType = IntPtr.Zero);
            XDecref(op);

            op = PyUnicode_FromString("unicode");
            SetPyMember(ref PyUnicodeType, PyObject_Type(op),
                () => PyUnicodeType = IntPtr.Zero);
            XDecref(op);

            op = PyBytes_FromString("bytes");
            SetPyMember(ref PyBytesType, PyObject_Type(op),
                () => PyBytesType = IntPtr.Zero);
            XDecref(op);

            op = PyTuple_New(0);
            SetPyMember(ref PyTupleType, PyObject_Type(op),
                () => PyTupleType = IntPtr.Zero);
            XDecref(op);

            op = PyList_New(0);
            SetPyMember(ref PyListType, PyObject_Type(op),
                () => PyListType = IntPtr.Zero);
            XDecref(op);

            op = PyDict_New();
            SetPyMember(ref PyDictType, PyObject_Type(op),
                () => PyDictType = IntPtr.Zero);
            XDecref(op);

            op = PyInt_FromInt32(0);
            SetPyMember(ref PyIntType, PyObject_Type(op),
                () => PyIntType = IntPtr.Zero);
            XDecref(op);

            op = PyLong_FromLong(0);
            SetPyMember(ref PyLongType, PyObject_Type(op),
                () => PyLongType = IntPtr.Zero);
            XDecref(op);

            op = PyFloat_FromDouble(0);
            SetPyMember(ref PyFloatType, PyObject_Type(op),
                () => PyFloatType = IntPtr.Zero);
            XDecref(op);

            PyClassType = IntPtr.Zero;
            PyInstanceType = IntPtr.Zero;

            Error = new IntPtr(-1);

            Interlocked.Increment(ref run);

            // Initialize data about the platform we're running on. We need
            // this for the type manager and potentially other details. Must
            // happen after caching the python types, above.
            InitializePlatformData();

            IntPtr dllLocal = IntPtr.Zero;
            var loader = LibraryLoader.Get(OperatingSystem);

            if (PythonDLL != "__Internal")
            {
                dllLocal = loader.Load(PythonDLL);
            }
            _PyObject_NextNotImplemented = loader.GetFunction(dllLocal, "_PyObject_NextNotImplemented");
            PyModuleType = loader.GetFunction(dllLocal, "PyModule_Type");

            if (dllLocal != IntPtr.Zero)
            {
                loader.Free(dllLocal);
            }

            // Initialize modules that depend on the runtime class.
            AssemblyManager.Initialize();
            PyCLRMetaType = MetaType.Initialize();
            Exceptions.Initialize();
            ImportHook.Initialize();

            // Need to add the runtime directory to sys.path so that we
            // can find built-in assemblies like System.Data, et. al.
            string rtdir = RuntimeEnvironment.GetRuntimeDirectory();
            IntPtr path = PySys_GetObject("path");
            IntPtr item = PyString_FromString(rtdir);
            PyList_Append(new BorrowedReference(path), item);
            XDecref(item);
            AssemblyManager.UpdatePath();

            inspect = GetModuleLazy("inspect");
            clrInterop = GetModuleLazy("clr.interop");
        }

        /// <summary>
        /// Initializes the data about platforms.
        ///
        /// This must be the last step when initializing the runtime:
        /// GetManagedString needs to have the cached values for types.
        /// But it must run before initializing anything outside the runtime
        /// because those rely on the platform data.
        /// </summary>
        private static void InitializePlatformData()
        {
#if !NETSTANDARD
            IntPtr op;
            IntPtr fn;
            IntPtr platformModule = PyImport_ImportModule("platform");
            IntPtr emptyTuple = PyTuple_New(0);

            fn = PyObject_GetAttrString(platformModule, "system");
            op = PyObject_Call(fn, emptyTuple, IntPtr.Zero);
            string operatingSystemName = GetManagedString(op);
            XDecref(op);
            XDecref(fn);

            fn = PyObject_GetAttrString(platformModule, "machine");
            op = PyObject_Call(fn, emptyTuple, IntPtr.Zero);
            string machineName = GetManagedString(op);
            XDecref(op);
            XDecref(fn);

            XDecref(emptyTuple);
            XDecref(platformModule);

            // Now convert the strings into enum values so we can do switch
            // statements rather than constant parsing.
            OperatingSystemType OSType;
            if (!OperatingSystemTypeMapping.TryGetValue(operatingSystemName, out OSType))
            {
                OSType = OperatingSystemType.Other;
            }
            OperatingSystem = OSType;

            MachineType MType;
            if (!MachineTypeMapping.TryGetValue(machineName.ToLower(), out MType))
            {
                MType = MachineType.Other;
            }
            Machine = MType;
#else
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                OperatingSystem = OperatingSystemType.Linux;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                OperatingSystem = OperatingSystemType.Darwin;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                OperatingSystem = OperatingSystemType.Windows;
            else
                OperatingSystem = OperatingSystemType.Other;

            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X86:
                    Machine = MachineType.i386;
                    break;
                case Architecture.X64:
                    Machine = MachineType.x86_64;
                    break;
                case Architecture.Arm:
                    Machine = MachineType.armv7l;
                    break;
                case Architecture.Arm64:
                    Machine = MachineType.aarch64;
                    break;
                default:
                    Machine = MachineType.Other;
                    break;
            }
#endif
        }

        internal static void Shutdown()
        {
            AssemblyManager.Shutdown();
            Exceptions.Shutdown();
            ImportHook.Shutdown();
            Finalizer.Shutdown();
            // TOOD: PyCLRMetaType's release operation still in #958
            PyCLRMetaType = IntPtr.Zero;
            ResetPyMembers();
            Py_Finalize();
        }

        private static Lazy<PyObject> GetModuleLazy(string moduleName)
            => moduleName is null
                ? throw new ArgumentNullException(nameof(moduleName))
                : new Lazy<PyObject>(() => PythonEngine.ImportModule(moduleName), isThreadSafe: false);

        // called *without* the GIL acquired by clr._AtExit
        internal static int AtExit()
        {
            lock (IsFinalizingLock)
            {
                IsFinalizing = true;
            }
            return 0;
        }

        private static void SetPyMember(ref IntPtr obj, IntPtr value, Action onRelease)
        {
            // XXX: For current usages, value should not be null.
            PythonException.ThrowIfIsNull(value);
            obj = value;
            _pyRefs.Add(value, onRelease);
        }

        private static void ResetPyMembers()
        {
            _pyRefs.Release();
        }

        internal static IntPtr Py_single_input = (IntPtr)256;
        internal static IntPtr Py_file_input = (IntPtr)257;
        internal static IntPtr Py_eval_input = (IntPtr)258;

        internal static IntPtr PyBaseObjectType;
        internal static IntPtr PyModuleType;
        internal static IntPtr PyClassType;
        internal static IntPtr PyInstanceType;
        internal static IntPtr PySuper_Type;
        internal static IntPtr PyCLRMetaType;
        internal static IntPtr PyMethodType;
        internal static IntPtr PyWrapperDescriptorType;

        internal static IntPtr PyUnicodeType;
        internal static IntPtr PyStringType;
        internal static IntPtr PyTupleType;
        internal static IntPtr PyListType;
        internal static IntPtr PyDictType;
        internal static IntPtr PyIntType;
        internal static IntPtr PyLongType;
        internal static IntPtr PyFloatType;
        internal static IntPtr PyBoolType;
        internal static IntPtr PyNoneType;
        internal static IntPtr PyTypeType;

        internal static IntPtr Py_NoSiteFlag;

        internal static IntPtr PyBytesType;
        internal static IntPtr _PyObject_NextNotImplemented;

        internal static IntPtr PyNotImplemented;
        internal const int Py_LT = 0;
        internal const int Py_LE = 1;
        internal const int Py_EQ = 2;
        internal const int Py_NE = 3;
        internal const int Py_GT = 4;
        internal const int Py_GE = 5;

        internal static IntPtr PyTrue;
        internal static IntPtr PyFalse;
        internal static IntPtr PyNone;
        internal static IntPtr Error;

        public static PyObject None
        {
            get
            {
                var none = Runtime.PyNone;
                Runtime.XIncref(none);
                return new PyObject(none);
            }
        }

        private static Lazy<PyObject> inspect;
        internal static PyObject InspectModule => inspect.Value;
        private static Lazy<PyObject> clrInterop;
        internal static PyObject InteropModule => clrInterop.Value;

        /// <summary>
        /// Check if any Python Exceptions occurred.
        /// If any exist throw new PythonException.
        /// </summary>
        /// <remarks>
        /// Can be used instead of `obj == IntPtr.Zero` for example.
        /// </remarks>
        internal static void CheckExceptionOccurred()
        {
            if (PyErr_Occurred() != IntPtr.Zero)
            {
                throw PythonException.ThrowLastAsClrException();
            }
        }

        internal static IntPtr ExtendTuple(IntPtr t, params IntPtr[] args)
        {
            var size = PyTuple_Size(t);
            int add = args.Length;
            IntPtr item;

            IntPtr items = PyTuple_New(size + add);
            for (var i = 0; i < size; i++)
            {
                item = PyTuple_GetItem(t, i);
                XIncref(item);
                PyTuple_SetItem(items, i, item);
            }

            for (var n = 0; n < add; n++)
            {
                item = args[n];
                XIncref(item);
                PyTuple_SetItem(items, size + n, item);
            }

            return items;
        }

        internal static Type[] PythonArgsToTypeArray(IntPtr arg)
        {
            return PythonArgsToTypeArray(arg, false);
        }

        internal static Type[] PythonArgsToTypeArray(IntPtr arg, bool mangleObjects)
        {
            // Given a PyObject * that is either a single type object or a
            // tuple of (managed or unmanaged) type objects, return a Type[]
            // containing the CLR Type objects that map to those types.
            IntPtr args = arg;
            var free = false;

            if (!PyTuple_Check(arg))
            {
                args = PyTuple_New(1);
                XIncref(arg);
                PyTuple_SetItem(args, 0, arg);
                free = true;
            }

            var n = PyTuple_Size(args);
            var types = new Type[n];
            Type t = null;

            for (var i = 0; i < n; i++)
            {
                var op = new BorrowedReference(PyTuple_GetItem(args, i));
                if (mangleObjects && (!PyType_Check(op)))
                {
                    op = PyObject_TYPE(op);
                }
                ManagedType mt = ManagedType.GetManagedObject(op);

                if (mt is ClassBase)
                {
                    t = ((ClassBase)mt).type;
                }
                else if (mt is CLRObject)
                {
                    object inst = ((CLRObject)mt).inst;
                    if (inst is Type)
                    {
                        t = inst as Type;
                    }
                }
                else
                {
                    t = Converter.GetTypeByAlias(op);
                }

                if (t == null)
                {
                    types = null;
                    break;
                }
                types[i] = t;
            }
            if (free)
            {
                XDecref(args);
            }
            return types;
        }

        /// <summary>
        /// Raise an exception when a refcount of Python object exceeds this limit.
        /// Only affects debug builds.
        /// </summary>
        public static long RefCountSanityLimit { get; set; } = Int32.MaxValue;

        /// <summary>
        /// Managed exports of the Python C API. Where appropriate, we do
        /// some optimization to avoid managed &lt;--&gt; unmanaged transitions
        /// (mostly for heavily used methods).
        /// </summary>
        internal static unsafe void XIncref(IntPtr op)
        {
            DebugUtil.EnsureGIL();
            if (op == IntPtr.Zero)
                throw new ArgumentNullException(nameof(op));
#if DEBUG
            long refcount = Refcount(op);
            if (refcount < 0 || refcount > RefCountSanityLimit)
                throw new ArgumentOutOfRangeException(
                    message: "Reference count is insane",
                    paramName: nameof(op),
                    actualValue: refcount);
#endif

#if PYTHON_WITH_PYDEBUG || NETSTANDARD
            Py_IncRef(op);
            return;
#else
            var p = (void*)op;
            if ((void*)0 != p)
            {
                if (Is32Bit)
                {
                    (*(int*)p)++;
                }
                else
                {
                    (*(long*)p)++;
                }
            }
#endif
        }

        /// <summary>
        /// Increase Python's ref counter for the given object, and get the object back.
        /// </summary>
        internal static IntPtr SelfIncRef(IntPtr op)
        {
            XIncref(op);
            return op;
        }

        internal static unsafe void XDecref(IntPtr op)
        {
            DebugUtil.EnsureGIL();
#if DEBUG
            if (op == IntPtr.Zero)
                throw new ArgumentNullException(nameof(op));
            long refcount = Refcount(op);
            if (refcount <= 0 || refcount > RefCountSanityLimit)
                throw new ArgumentOutOfRangeException(
                    paramName: nameof(refcount),
                    actualValue: refcount,
                    message: "Reference count is insane");
#endif

#if PYTHON_WITH_PYDEBUG || NETSTANDARD
            Py_DecRef(op);
            return;
#else
            var p = (void*)op;
            if ((void*)0 != p)
            {
                if (Is32Bit)
                {
                    --(*(int*)p);
                }
                else
                {
                    --(*(long*)p);
                }
                if ((*(int*)p) == 0)
                {
                    // PyObject_HEAD: struct _typeobject *ob_type
                    void* t = Is32Bit
                        ? (void*)(*((uint*)p + 1))
                        : (void*)(*((ulong*)p + 1));
                    // PyTypeObject: destructor tp_dealloc
                    void* f = Is32Bit
                        ? (void*)(*((uint*)t + 6))
                        : (void*)(*((ulong*)t + 6));
                    if ((void*)0 == f)
                    {
                        return;
                    }
                    NativeCall.Impl.Void_Call_1(new IntPtr(f), op);
                }
            }
#endif
        }

        internal static void XDecrefIgnoreNull(IntPtr op)
        {
            if (op != IntPtr.Zero) { XDecref(op); }
        }

        [Pure]
        internal static unsafe long Refcount(IntPtr op)
        {
            var p = (void*)op;
            if ((void*)0 == p)
            {
                return 0;
            }
            return Is32Bit ? (*(int*)p) : (*(long*)p);
        }

        /// <summary>
        /// Export of Macro Py_XIncRef. Use XIncref instead.
        /// Limit this function usage for Testing and Py_Debug builds
        /// </summary>
        /// <param name="ob">PyObject Ptr</param>
        
        internal static void Py_IncRef(IntPtr ob) => Delegates.Py_IncRef(ob);

        /// <summary>
        /// Export of Macro Py_XDecRef. Use XDecref instead.
        /// Limit this function usage for Testing and Py_Debug builds
        /// </summary>
        /// <param name="ob">PyObject Ptr</param>
        
        internal static void Py_DecRef(IntPtr ob) => Delegates.Py_DecRef(ob);

        
        internal static void Py_Initialize() => Delegates.Py_Initialize();

        
        internal static void Py_InitializeEx(int initsigs) => Delegates.Py_InitializeEx(initsigs);

        
        internal static int Py_IsInitialized() => Delegates.Py_IsInitialized();

        
        internal static void Py_Finalize() => Delegates.Py_Finalize();

        
        internal static IntPtr Py_NewInterpreter() => Delegates.Py_NewInterpreter();

        
        internal static void Py_EndInterpreter(IntPtr threadState) => Delegates.Py_EndInterpreter(threadState);

        
        internal static IntPtr PyThreadState_New(IntPtr istate) => Delegates.PyThreadState_New(istate);

        
        internal static IntPtr PyThreadState_Get() => Delegates.PyThreadState_Get();

        
        internal static IntPtr PyThread_get_key_value(IntPtr key) => Delegates.PyThread_get_key_value(key);

        
        internal static int PyThread_get_thread_ident() => Delegates.PyThread_get_thread_ident();

        
        internal static int PyThread_set_key_value(IntPtr key, IntPtr value) => Delegates.PyThread_set_key_value(key, value);

        
        internal static IntPtr PyThreadState_Swap(IntPtr key) => Delegates.PyThreadState_Swap(key);

        
        internal static IntPtr PyGILState_Ensure() => Delegates.PyGILState_Ensure();

        
        internal static void PyGILState_Release(IntPtr gs) => Delegates.PyGILState_Release(gs);


        
        internal static IntPtr PyGILState_GetThisThreadState() => Delegates.PyGILState_GetThisThreadState();

        internal static int PyGILState_Check() => Delegates.PyGILState_Check();

        public static int Py_Main(
            int argc,
            [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StrArrayMarshaler))] string[] argv
        ) => Delegates.Py_Main(argc, argv
);


        
        internal static void PyEval_InitThreads() => Delegates.PyEval_InitThreads();

        
        internal static int PyEval_ThreadsInitialized() => Delegates.PyEval_ThreadsInitialized();

        
        internal static void PyEval_AcquireLock() => Delegates.PyEval_AcquireLock();

        
        internal static void PyEval_ReleaseLock() => Delegates.PyEval_ReleaseLock();

        
        internal static void PyEval_AcquireThread(IntPtr tstate) => Delegates.PyEval_AcquireThread(tstate);

        
        internal static void PyEval_ReleaseThread(IntPtr tstate) => Delegates.PyEval_ReleaseThread(tstate);

        
        internal static IntPtr PyEval_SaveThread() => Delegates.PyEval_SaveThread();

        
        internal static void PyEval_RestoreThread(IntPtr tstate) => Delegates.PyEval_RestoreThread(tstate);

        
        internal static BorrowedReference PyEval_GetBuiltins() => Delegates.PyEval_GetBuiltins();

        
        internal static IntPtr PyEval_GetGlobals() => Delegates.PyEval_GetGlobals();

        
        internal static IntPtr PyEval_GetLocals() => Delegates.PyEval_GetLocals();

        
        internal static IntPtr Py_GetProgramName() => Delegates.Py_GetProgramName();

        
        internal static void Py_SetProgramName(IntPtr name) => Delegates.Py_SetProgramName(name);

        
        internal static IntPtr Py_GetPythonHome() => Delegates.Py_GetPythonHome();

        
        internal static void Py_SetPythonHome(IntPtr home) => Delegates.Py_SetPythonHome(home);

        
        internal static IntPtr Py_GetPath() => Delegates.Py_GetPath();

        
        internal static void Py_SetPath(IntPtr home) => Delegates.Py_SetPath(home);

        
        internal static IntPtr Py_GetVersion() => Delegates.Py_GetVersion();

        
        internal static IntPtr Py_GetPlatform() => Delegates.Py_GetPlatform();

        
        internal static IntPtr Py_GetCopyright() => Delegates.Py_GetCopyright();

        
        internal static IntPtr Py_GetCompiler() => Delegates.Py_GetCompiler();

        
        internal static IntPtr Py_GetBuildInfo() => Delegates.Py_GetBuildInfo();

        
        internal static int PyRun_SimpleString(string code) => Delegates.PyRun_SimpleString(code);

        
        internal static NewReference PyRun_String(string code, IntPtr st, IntPtr globals, IntPtr locals) => Delegates.PyRun_String(code, st, globals, locals);

        
        internal static IntPtr PyEval_EvalCode(IntPtr co, IntPtr globals, IntPtr locals) => Delegates.PyEval_EvalCode(co, globals, locals);

        /// <summary>
        /// Return value: New reference.
        /// This is a simplified interface to Py_CompileStringFlags() below, leaving flags set to NULL.
        /// </summary>
        internal static IntPtr Py_CompileString(string str, string file, int start)
        {
            return Py_CompileStringFlags(str, file, start, IntPtr.Zero);
        }

        /// <summary>
        /// Return value: New reference.
        /// This is a simplified interface to Py_CompileStringExFlags() below, with optimize set to -1.
        /// </summary>
        internal static IntPtr Py_CompileStringFlags(string str, string file, int start, IntPtr flags)
        {
            return Py_CompileStringExFlags(str, file, start, flags, -1);
        }

        /// <summary>
        /// Return value: New reference.
        /// Like Py_CompileStringObject(), but filename is a byte string decoded from the filesystem encoding(os.fsdecode()).
        /// </summary>
        internal static IntPtr Py_CompileStringExFlags(string str, string file, int start, IntPtr flags, int optimize) => Delegates.Py_CompileStringExFlags(str, file, start, flags, optimize);

        
        internal static IntPtr PyImport_ExecCodeModule(string name, IntPtr code) => Delegates.PyImport_ExecCodeModule(name, code);

        
        internal static IntPtr PyCFunction_NewEx(IntPtr ml, IntPtr self, IntPtr mod) => Delegates.PyCFunction_NewEx(ml, self, mod);

        
        internal static IntPtr PyCFunction_Call(IntPtr func, IntPtr args, IntPtr kw) => Delegates.PyCFunction_Call(func, args, kw);


        //====================================================================
        // Python abstract object API
        //====================================================================

        /// <summary>
        /// A macro-like method to get the type of a Python object. This is
        /// designed to be lean and mean in IL &amp; avoid managed &lt;-&gt; unmanaged
        /// transitions. Note that this does not incref the type object.
        /// </summary>
        internal static unsafe IntPtr PyObject_TYPE(IntPtr op)
        {
            var p = (void*)op;
            if ((void*)0 == p)
            {
                return IntPtr.Zero;
            }
#if PYTHON_WITH_PYDEBUG
            var n = 3;
#else
            var n = 1;
#endif
            return Is32Bit
                ? new IntPtr((void*)(*((uint*)p + n)))
                : new IntPtr((void*)(*((ulong*)p + n)));
        }

        internal static BorrowedReference PyObject_TYPE(BorrowedReference reference) {
            return reference.IsNull
                ? throw new ArgumentNullException(nameof(reference))
                : new BorrowedReference(PyObject_TYPE(reference.DangerousGetAddress()));
        }

        /// <summary>
        /// Managed version of the standard Python C API PyObject_Type call.
        /// This version avoids a managed  &lt;-&gt; unmanaged transition.
        /// This one does incref the returned type object.
        /// </summary>
        internal static IntPtr PyObject_Type(IntPtr op)
        {
            IntPtr tp = PyObject_TYPE(op);
            XIncref(tp);
            return tp;
        }

        internal static string PyObject_GetTypeName(IntPtr op)
        {
            IntPtr pyType = Marshal.ReadIntPtr(op, ObjectOffset.ob_type);
            IntPtr ppName = Marshal.ReadIntPtr(pyType, TypeOffset.tp_name);
            return Marshal.PtrToStringAnsi(ppName);
        }

        /// <summary>
        /// Test whether the Python object is an iterable.
        /// </summary>
        internal static bool PyObject_IsIterable(BorrowedReference pointer)
        {
            var ob_type = Marshal.ReadIntPtr(pointer.DangerousGetAddress(), ObjectOffset.ob_type);
            IntPtr tp_iter = Marshal.ReadIntPtr(ob_type, TypeOffset.tp_iter);
            return tp_iter != IntPtr.Zero;
        }

        
        internal static int PyObject_HasAttrString(IntPtr pointer, string name) => Delegates.PyObject_HasAttrString(pointer, name);

        internal static NewReference PyObject_GetAttrString(BorrowedReference pointer, string name)
            => NewReference.DangerousFromPointer(PyObject_GetAttrString(pointer.DangerousGetAddress(), name));
        internal static IntPtr PyObject_GetAttrString(IntPtr pointer, string name) => Delegates.PyObject_GetAttrString(pointer, name);

        
        internal static int PyObject_SetAttrString(IntPtr pointer, string name, IntPtr value) => Delegates.PyObject_SetAttrString(pointer, name, value);

        
        internal static int PyObject_HasAttr(IntPtr pointer, IntPtr name) => Delegates.PyObject_HasAttr(pointer, name);

        
        internal static IntPtr PyObject_GetAttr(IntPtr pointer, IntPtr name) => Delegates.PyObject_GetAttr(pointer, name);

        
        internal static int PyObject_SetAttr(IntPtr pointer, IntPtr name, IntPtr value) => Delegates.PyObject_SetAttr(pointer, name, value);

        
        internal static NewReference PyObject_GetItem(BorrowedReference pointer, BorrowedReference key) => Delegates.PyObject_GetItem(pointer, key);

        
        internal static int PyObject_SetItem(IntPtr pointer, IntPtr key, IntPtr value) => Delegates.PyObject_SetItem(pointer, key, value);

        
        internal static int PyObject_DelItem(IntPtr pointer, IntPtr key) => Delegates.PyObject_DelItem(pointer, key);

        
        internal static NewReference PyObject_GetIter(BorrowedReference op) => Delegates.PyObject_GetIter(op);

        
        internal static IntPtr PyObject_Call(IntPtr pointer, IntPtr args, IntPtr kw) => Delegates.PyObject_Call(pointer, args, kw);

        
        internal static IntPtr PyObject_CallObject(IntPtr pointer, IntPtr args) => Delegates.PyObject_CallObject(pointer, args);

        internal static int PyObject_RichCompareBool(IntPtr value1, IntPtr value2, int opid) => Delegates.PyObject_RichCompareBool(value1, value2, opid);

        internal static int PyObject_Compare(IntPtr value1, IntPtr value2)
        {
            int res;
            res = PyObject_RichCompareBool(value1, value2, Py_LT);
            if (-1 == res)
                return -1;
            else if (1 == res)
                return -1;

            res = PyObject_RichCompareBool(value1, value2, Py_EQ);
            if (-1 == res)
                return -1;
            else if (1 == res)
                return 0;

            res = PyObject_RichCompareBool(value1, value2, Py_GT);
            if (-1 == res)
                return -1;
            else if (1 == res)
                return 1;

            Exceptions.SetError(Exceptions.SystemError, "Error comparing objects");
            return -1;
        }

        
        internal static int PyObject_IsInstance(IntPtr ob, IntPtr type) => Delegates.PyObject_IsInstance(ob, type);

        
        internal static int PyObject_IsSubclass(IntPtr ob, IntPtr type) => Delegates.PyObject_IsSubclass(ob, type);

        
        internal static int PyCallable_Check(IntPtr pointer) => Delegates.PyCallable_Check(pointer);

        
        internal static int PyObject_IsTrue(IntPtr pointer) => Delegates.PyObject_IsTrue(pointer);

        
        internal static int PyObject_Not(IntPtr pointer) => Delegates.PyObject_Not(pointer);

        internal static long PyObject_Size(IntPtr pointer)
        {
            return (long)_PyObject_Size(pointer);
        }

        
        private static IntPtr _PyObject_Size(IntPtr pointer) => Delegates._PyObject_Size(pointer);

        
        internal static IntPtr PyObject_Hash(IntPtr op) => Delegates.PyObject_Hash(op);

        
        internal static IntPtr PyObject_Repr(IntPtr pointer) => Delegates.PyObject_Repr(pointer);

        
        internal static IntPtr PyObject_Str(IntPtr pointer) => Delegates.PyObject_Str(pointer);

        
        internal static IntPtr PyObject_Unicode(IntPtr pointer) => Delegates.PyObject_Unicode(pointer);

        
        internal static IntPtr PyObject_Dir(IntPtr pointer) => Delegates.PyObject_Dir(pointer);

        //====================================================================
        // Python buffer API
        //====================================================================


        internal static bool PyObject_CheckBuffer(BorrowedReference obj)
        {
            var type = PyObject_TYPE(obj);
            var bufferProcs = Marshal.ReadIntPtr(type.DangerousGetAddress(), TypeOffset.tp_as_buffer);
            if (bufferProcs == IntPtr.Zero) return false;
            var getBuffer = Marshal.ReadIntPtr(bufferProcs, 0);
            return getBuffer != IntPtr.Zero;
        }

        
        internal static int PyObject_GetBuffer(BorrowedReference exporter, out Py_buffer view, PyBUF flags) => Delegates.PyObject_GetBuffer(exporter, out view, flags);

        
        internal static void PyBuffer_Release(ref Py_buffer view) => Delegates.PyBuffer_Release(ref view);

        
        internal static IntPtr PyBuffer_SizeFromFormat([MarshalAs(UnmanagedType.LPStr)] string format) => Delegates.PyBuffer_SizeFromFormat(format);

        
        internal static int PyBuffer_IsContiguous(ref Py_buffer view, BufferOrderStyle order) => Delegates.PyBuffer_IsContiguous(ref view, order);

        
        internal static IntPtr PyBuffer_GetPointer(ref Py_buffer view, IntPtr[] indices) => Delegates.PyBuffer_GetPointer(ref view, indices);

        
        internal static int PyBuffer_FromContiguous(ref Py_buffer view, IntPtr buf, IntPtr len, BufferOrderStyle fort) => Delegates.PyBuffer_FromContiguous(ref view, buf, len, fort);

        
        internal static int PyBuffer_ToContiguous(IntPtr buf, ref Py_buffer src, IntPtr len, BufferOrderStyle order) => Delegates.PyBuffer_ToContiguous(buf, ref src, len, order);

        
        internal static void PyBuffer_FillContiguousStrides(int ndims, IntPtr[] shape, IntPtr[] strides, int itemsize, BufferOrderStyle order) => Delegates.PyBuffer_FillContiguousStrides(ndims, shape, strides, itemsize, order);

        
        internal static int PyBuffer_FillInfo(ref Py_buffer view, BorrowedReference exporter, IntPtr buf, IntPtr len, bool @readonly, PyBUF flags) => Delegates.PyBuffer_FillInfo(ref view, exporter, buf, len, @readonly, flags);

        //====================================================================
        // Python number API
        //====================================================================

        
        internal static IntPtr PyNumber_Int(IntPtr ob) => Delegates.PyNumber_Int(ob);

        
        internal static IntPtr PyNumber_Long(IntPtr ob) => Delegates.PyNumber_Long(ob);

        
        internal static IntPtr PyNumber_Float(IntPtr ob) => Delegates.PyNumber_Float(ob);

        
        internal static bool PyNumber_Check(IntPtr ob) => Delegates.PyNumber_Check(ob);

        internal static bool PyInt_Check(IntPtr ob)
        {
            return PyObject_TypeCheck(ob, PyIntType);
        }

        internal static bool PyBool_Check(IntPtr ob)
        {
            return PyObject_TypeCheck(ob, PyBoolType);
        }

        internal static IntPtr PyInt_FromInt32(int value)
        {
            var v = new IntPtr(value);
            return PyInt_FromLong(v);
        }

        internal static IntPtr PyInt_FromInt64(long value)
        {
            var v = new IntPtr(value);
            return PyInt_FromLong(v);
        }

        [Obsolete("Should not be used due to the size of long not being guaranteed")]
        private static IntPtr PyInt_FromLong(IntPtr value) => Delegates.PyInt_FromLong(value);

        internal static int PyInt_AsLong(IntPtr value) => Delegates.PyInt_AsLong(value);

        internal static IntPtr PyInt_FromString(string value, IntPtr end, int radix) => Delegates.PyInt_FromString(value, end, radix);


        internal static bool PyLong_Check(IntPtr ob)
        {
            return PyObject_TYPE(ob) == PyLongType;
        }

        [Obsolete("Should not be used due to the size of long not being guaranteed")]
        internal static IntPtr PyLong_FromLong(long value) => Delegates.PyLong_FromLong(value);

        internal static IntPtr PyLong_FromUnsignedLong32(uint value) => Delegates.PyLong_FromUnsignedLong32(value);
        internal static IntPtr PyLong_FromUnsignedLong64(ulong value) => Delegates.PyLong_FromUnsignedLong64(value);

        internal static IntPtr PyLong_FromUnsignedLong(object value)
        {
            if (Is32Bit || IsWindows)
                return PyLong_FromUnsignedLong32(Convert.ToUInt32(value));
            else
                return PyLong_FromUnsignedLong64(Convert.ToUInt64(value));
        }

        
        internal static IntPtr PyLong_FromDouble(double value) => Delegates.PyLong_FromDouble(value);

        [Obsolete("Should not be used due to the size of long not being guaranteed")]
        internal static IntPtr PyLong_FromLongLong(long value) => Delegates.PyLong_FromLongLong(value);

        [Obsolete("Should not be used due to the size of long not being guaranteed")]
        internal static IntPtr PyLong_FromUnsignedLongLong(ulong value) => Delegates.PyLong_FromUnsignedLongLong(value);

        
        internal static IntPtr PyLong_FromString(string value, IntPtr end, int radix) => Delegates.PyLong_FromString(value, end, radix);

        internal static int PyLong_AsLong(IntPtr value) => Delegates.PyLong_AsLong(value);

        internal static uint PyLong_AsUnsignedLong32(IntPtr value) => Delegates.PyLong_AsUnsignedLong32(value);
        internal static ulong PyLong_AsUnsignedLong64(IntPtr value) => Delegates.PyLong_AsUnsignedLong64(value);

        internal static object PyLong_AsUnsignedLong(IntPtr value)
        {
            if (Is32Bit || IsWindows)
                return PyLong_AsUnsignedLong32(value);
            else
                return PyLong_AsUnsignedLong64(value);
        }

        [Obsolete("Should not be used due to the size of long not being guaranteed")]
        internal static long PyLong_AsLongLong(IntPtr value) => Delegates.PyLong_AsLongLong(value);

        [Obsolete("Should not be used due to the size of long not being guaranteed")]
        internal static ulong PyLong_AsUnsignedLongLong(IntPtr value) => Delegates.PyLong_AsUnsignedLongLong(value);

        internal static bool PyFloat_Check(IntPtr ob)
        {
            return PyObject_TYPE(ob) == PyFloatType;
        }

        
        internal static IntPtr PyFloat_FromDouble(double value) => Delegates.PyFloat_FromDouble(value);

        
        internal static IntPtr PyFloat_FromString(IntPtr value, IntPtr junk) => Delegates.PyFloat_FromString(value, junk);

        
        internal static double PyFloat_AsDouble(IntPtr ob) => Delegates.PyFloat_AsDouble(ob);

        
        internal static IntPtr PyNumber_Add(IntPtr o1, IntPtr o2) => Delegates.PyNumber_Add(o1, o2);

        
        internal static IntPtr PyNumber_Subtract(IntPtr o1, IntPtr o2) => Delegates.PyNumber_Subtract(o1, o2);

        
        internal static IntPtr PyNumber_Multiply(IntPtr o1, IntPtr o2) => Delegates.PyNumber_Multiply(o1, o2);

        
        internal static IntPtr PyNumber_TrueDivide(IntPtr o1, IntPtr o2) => Delegates.PyNumber_TrueDivide(o1, o2);

        
        internal static IntPtr PyNumber_And(IntPtr o1, IntPtr o2) => Delegates.PyNumber_And(o1, o2);

        
        internal static IntPtr PyNumber_Xor(IntPtr o1, IntPtr o2) => Delegates.PyNumber_Xor(o1, o2);

        
        internal static IntPtr PyNumber_Or(IntPtr o1, IntPtr o2) => Delegates.PyNumber_Or(o1, o2);

        
        internal static IntPtr PyNumber_Lshift(IntPtr o1, IntPtr o2) => Delegates.PyNumber_Lshift(o1, o2);

        
        internal static IntPtr PyNumber_Rshift(IntPtr o1, IntPtr o2) => Delegates.PyNumber_Rshift(o1, o2);

        
        internal static IntPtr PyNumber_Power(IntPtr o1, IntPtr o2) => Delegates.PyNumber_Power(o1, o2);

        
        internal static IntPtr PyNumber_Remainder(IntPtr o1, IntPtr o2) => Delegates.PyNumber_Remainder(o1, o2);

        
        internal static IntPtr PyNumber_InPlaceAdd(IntPtr o1, IntPtr o2) => Delegates.PyNumber_InPlaceAdd(o1, o2);

        
        internal static IntPtr PyNumber_InPlaceSubtract(IntPtr o1, IntPtr o2) => Delegates.PyNumber_InPlaceSubtract(o1, o2);

        
        internal static IntPtr PyNumber_InPlaceMultiply(IntPtr o1, IntPtr o2) => Delegates.PyNumber_InPlaceMultiply(o1, o2);

        
        internal static IntPtr PyNumber_InPlaceTrueDivide(IntPtr o1, IntPtr o2) => Delegates.PyNumber_InPlaceTrueDivide(o1, o2);

        
        internal static IntPtr PyNumber_InPlaceAnd(IntPtr o1, IntPtr o2) => Delegates.PyNumber_InPlaceAnd(o1, o2);

        
        internal static IntPtr PyNumber_InPlaceXor(IntPtr o1, IntPtr o2) => Delegates.PyNumber_InPlaceXor(o1, o2);

        
        internal static IntPtr PyNumber_InPlaceOr(IntPtr o1, IntPtr o2) => Delegates.PyNumber_InPlaceOr(o1, o2);

        
        internal static IntPtr PyNumber_InPlaceLshift(IntPtr o1, IntPtr o2) => Delegates.PyNumber_InPlaceLshift(o1, o2);

        
        internal static IntPtr PyNumber_InPlaceRshift(IntPtr o1, IntPtr o2) => Delegates.PyNumber_InPlaceRshift(o1, o2);

        
        internal static IntPtr PyNumber_InPlacePower(IntPtr o1, IntPtr o2) => Delegates.PyNumber_InPlacePower(o1, o2);

        
        internal static IntPtr PyNumber_InPlaceRemainder(IntPtr o1, IntPtr o2) => Delegates.PyNumber_InPlaceRemainder(o1, o2);

        
        internal static IntPtr PyNumber_Negative(IntPtr o1) => Delegates.PyNumber_Negative(o1);

        
        internal static IntPtr PyNumber_Positive(IntPtr o1) => Delegates.PyNumber_Positive(o1);

        
        internal static IntPtr PyNumber_Invert(IntPtr o1) => Delegates.PyNumber_Invert(o1);


        //====================================================================
        // Python sequence API
        //====================================================================

        
        internal static bool PySequence_Check(IntPtr pointer) => Delegates.PySequence_Check(pointer);

        internal static IntPtr PySequence_GetItem(IntPtr pointer, long index)
        {
            return PySequence_GetItem(pointer, new IntPtr(index));
        }

        
        private static IntPtr PySequence_GetItem(IntPtr pointer, IntPtr index) => Delegates.PySequence_GetItem(pointer, index);

        internal static int PySequence_SetItem(IntPtr pointer, long index, IntPtr value)
        {
            return PySequence_SetItem(pointer, new IntPtr(index), value);
        }

        
        private static int PySequence_SetItem(IntPtr pointer, IntPtr index, IntPtr value) => Delegates.PySequence_SetItem(pointer, index, value);

        internal static int PySequence_DelItem(IntPtr pointer, long index)
        {
            return PySequence_DelItem(pointer, new IntPtr(index));
        }

        
        private static int PySequence_DelItem(IntPtr pointer, IntPtr index) => Delegates.PySequence_DelItem(pointer, index);

        internal static IntPtr PySequence_GetSlice(IntPtr pointer, long i1, long i2)
        {
            return PySequence_GetSlice(pointer, new IntPtr(i1), new IntPtr(i2));
        }

        
        private static IntPtr PySequence_GetSlice(IntPtr pointer, IntPtr i1, IntPtr i2) => Delegates.PySequence_GetSlice(pointer, i1, i2);

        internal static int PySequence_SetSlice(IntPtr pointer, long i1, long i2, IntPtr v)
        {
            return PySequence_SetSlice(pointer, new IntPtr(i1), new IntPtr(i2), v);
        }

        
        private static int PySequence_SetSlice(IntPtr pointer, IntPtr i1, IntPtr i2, IntPtr v) => Delegates.PySequence_SetSlice(pointer, i1, i2, v);

        internal static int PySequence_DelSlice(IntPtr pointer, long i1, long i2)
        {
            return PySequence_DelSlice(pointer, new IntPtr(i1), new IntPtr(i2));
        }

        
        private static int PySequence_DelSlice(IntPtr pointer, IntPtr i1, IntPtr i2) => Delegates.PySequence_DelSlice(pointer, i1, i2);

        internal static long PySequence_Size(IntPtr pointer)
        {
            return (long)_PySequence_Size(pointer);
        }

        
        private static IntPtr _PySequence_Size(IntPtr pointer) => Delegates._PySequence_Size(pointer);

        
        internal static int PySequence_Contains(IntPtr pointer, IntPtr item) => Delegates.PySequence_Contains(pointer, item);

        
        internal static IntPtr PySequence_Concat(IntPtr pointer, IntPtr other) => Delegates.PySequence_Concat(pointer, other);

        internal static IntPtr PySequence_Repeat(IntPtr pointer, long count)
        {
            return PySequence_Repeat(pointer, new IntPtr(count));
        }

        
        private static IntPtr PySequence_Repeat(IntPtr pointer, IntPtr count) => Delegates.PySequence_Repeat(pointer, count);

        
        internal static int PySequence_Index(IntPtr pointer, IntPtr item) => Delegates.PySequence_Index(pointer, item);

        internal static long PySequence_Count(IntPtr pointer, IntPtr value)
        {
            return (long)_PySequence_Count(pointer, value);
        }

        
        private static IntPtr _PySequence_Count(IntPtr pointer, IntPtr value) => Delegates._PySequence_Count(pointer, value);

        
        internal static IntPtr PySequence_Tuple(IntPtr pointer) => Delegates.PySequence_Tuple(pointer);

        
        internal static IntPtr PySequence_List(IntPtr pointer) => Delegates.PySequence_List(pointer);


        //====================================================================
        // Python string API
        //====================================================================

        internal static bool IsStringType(IntPtr op)
        {
            IntPtr t = PyObject_TYPE(op);
            return (t == PyStringType) || (t == PyUnicodeType);
        }

        [Obsolete(Util.UseOverloadWithReferenceTypes)]
        internal static bool PyString_Check(IntPtr ob)
        {
            return PyObject_TYPE(ob) == PyStringType;
        }
        internal static bool PyString_Check(BorrowedReference ob)
            => PyObject_TYPE(ob) == PyStringType;

        internal static IntPtr PyString_FromString(string value)
        {
            return PyUnicode_FromKindAndData(UCS, value, value.Length);
        }

        internal static IntPtr PyBytes_FromString(string op) => Delegates.PyBytes_FromString(op);

        internal static long PyBytes_Size(IntPtr op)
        {
            return (long)_PyBytes_Size(op);
        }

        private static IntPtr _PyBytes_Size(IntPtr op) => Delegates._PyBytes_Size(op);

        internal static IntPtr PyBytes_AS_STRING(IntPtr ob)
        {
            return ob + BytesOffset.ob_sval;
        }

        internal static IntPtr PyString_FromStringAndSize(string value, long size)
        {
            return _PyString_FromStringAndSize(value, new IntPtr(size));
        }

        internal static IntPtr _PyString_FromStringAndSize(
            [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(Utf8Marshaler))] string value,
            IntPtr size
        ) => Delegates._PyString_FromStringAndSize(value, size);

        internal static IntPtr PyUnicode_FromStringAndSize(IntPtr value, long size)
        {
            return PyUnicode_FromStringAndSize(value, new IntPtr(size));
        }

        private static IntPtr PyUnicode_FromStringAndSize(IntPtr value, IntPtr size) => Delegates.PyUnicode_FromStringAndSize(value, size);

        internal static IntPtr PyUnicode_AsUTF8(IntPtr unicode) => Delegates.PyUnicode_AsUTF8(unicode);

        internal static bool PyUnicode_Check(IntPtr ob)
        {
            return PyObject_TYPE(ob) == PyUnicodeType;
        }

        internal static IntPtr PyUnicode_FromObject(IntPtr ob) => Delegates.PyUnicode_FromObject(ob);

        internal static IntPtr PyUnicode_FromEncodedObject(IntPtr ob, IntPtr enc, IntPtr err) => Delegates.PyUnicode_FromEncodedObject(ob, enc, err);

        internal static IntPtr PyUnicode_FromKindAndData(int kind, string s, long size)
        {
            return PyUnicode_FromKindAndData(kind, s, new IntPtr(size));
        }

        private static IntPtr PyUnicode_FromKindAndData(
            int kind,
            [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(UcsMarshaler))] string s,
            IntPtr size
        ) => Delegates.PyUnicode_FromKindAndData(kind, s, size
);

        internal static IntPtr PyUnicode_FromUnicode(string s, long size)
        {
            return PyUnicode_FromKindAndData(UCS, s, size);
        }

        internal static long PyUnicode_GetSize(IntPtr ob)
        {
            return (long)_PyUnicode_GetSize(ob);
        }

        private static IntPtr _PyUnicode_GetSize(IntPtr ob) => Delegates._PyUnicode_GetSize(ob);

        internal static IntPtr PyUnicode_AsUnicode(IntPtr ob) => Delegates.PyUnicode_AsUnicode(ob);

        internal static IntPtr PyUnicode_FromOrdinal(int c) => Delegates.PyUnicode_FromOrdinal(c);

        internal static IntPtr PyUnicode_FromString(string s)
        {
            return PyUnicode_FromUnicode(s, s.Length);
        }

        internal static string GetManagedString(in BorrowedReference borrowedReference)
            => GetManagedString(borrowedReference.DangerousGetAddress());
        /// <summary>
        /// Function to access the internal PyUnicode/PyString object and
        /// convert it to a managed string with the correct encoding.
        /// </summary>
        /// <remarks>
        /// We can't easily do this through through the CustomMarshaler's on
        /// the returns because will have access to the IntPtr but not size.
        /// <para />
        /// For PyUnicodeType, we can't convert with Marshal.PtrToStringUni
        /// since it only works for UCS2.
        /// </remarks>
        /// <param name="op">PyStringType or PyUnicodeType object to convert</param>
        /// <returns>Managed String</returns>
        internal static string GetManagedString(IntPtr op)
        {
            IntPtr type = PyObject_TYPE(op);

            if (type == PyUnicodeType)
            {
                IntPtr p = PyUnicode_AsUnicode(op);
                Exceptions.ErrorCheck(p);
                int length = checked((int)PyUnicode_GetSize(op));

                int size = checked(length * UCS);
                var buffer = new byte[size];
                Marshal.Copy(p, buffer, 0, size);
                return PyEncoding.GetString(buffer, 0, size);
            }

            return null;
        }


        //====================================================================
        // Python dictionary API
        //====================================================================

        internal static bool PyDict_Check(IntPtr ob)
        {
            return PyObject_TYPE(ob) == PyDictType;
        }

        
        internal static IntPtr PyDict_New() => Delegates.PyDict_New();

        
        internal static IntPtr PyDictProxy_New(IntPtr dict) => Delegates.PyDictProxy_New(dict);

        
        internal static IntPtr PyDict_GetItem(IntPtr pointer, IntPtr key) => Delegates.PyDict_GetItem(pointer, key);

        
        internal static IntPtr PyDict_GetItemString(IntPtr pointer, string key) => Delegates.PyDict_GetItemString(pointer, key);

        
        internal static int PyDict_SetItem(BorrowedReference pointer, BorrowedReference key, BorrowedReference value) => Delegates.PyDict_SetItem(pointer, key, value);

        
        internal static int PyDict_SetItemString(IntPtr pointer, string key, IntPtr value) => Delegates.PyDict_SetItemString(pointer, key, value);
        internal static int PyDict_SetItemString(BorrowedReference pointer, string key, BorrowedReference value)
            => PyDict_SetItemString(pointer.DangerousGetAddress(), key, value.DangerousGetAddress());


        internal static int PyDict_DelItem(IntPtr pointer, IntPtr key) => Delegates.PyDict_DelItem(pointer, key);

        
        internal static int PyDict_DelItemString(IntPtr pointer, string key) => Delegates.PyDict_DelItemString(pointer, key);

        
        internal static int PyMapping_HasKey(IntPtr pointer, IntPtr key) => Delegates.PyMapping_HasKey(pointer, key);

        
        internal static IntPtr PyDict_Keys(IntPtr pointer) => Delegates.PyDict_Keys(pointer);

        
        internal static IntPtr PyDict_Values(IntPtr pointer) => Delegates.PyDict_Values(pointer);

        
        internal static NewReference PyDict_Items(IntPtr pointer) => Delegates.PyDict_Items(pointer);

        
        internal static IntPtr PyDict_Copy(IntPtr pointer) => Delegates.PyDict_Copy(pointer);

        
        internal static int PyDict_Update(IntPtr pointer, IntPtr other) => Delegates.PyDict_Update(pointer, other);

        
        internal static void PyDict_Clear(IntPtr pointer) => Delegates.PyDict_Clear(pointer);

        internal static long PyDict_Size(IntPtr pointer)
        {
            return (long)_PyDict_Size(pointer);
        }

        
        internal static IntPtr _PyDict_Size(IntPtr pointer) => Delegates._PyDict_Size(pointer);


        //====================================================================
        // Python list API
        //====================================================================

        internal static bool PyList_Check(IntPtr ob)
        {
            return PyObject_TYPE(ob) == PyListType;
        }

        internal static IntPtr PyList_New(long size)
        {
            return PyList_New(new IntPtr(size));
        }

        
        private static IntPtr PyList_New(IntPtr size) => Delegates.PyList_New(size);

        
        internal static IntPtr PyList_AsTuple(IntPtr pointer) => Delegates.PyList_AsTuple(pointer);

        internal static BorrowedReference PyList_GetItem(IntPtr pointer, long index)
        {
            return PyList_GetItem(pointer, new IntPtr(index));
        }

        
        private static BorrowedReference PyList_GetItem(IntPtr pointer, IntPtr index) => Delegates.PyList_GetItem(pointer, index);

        internal static int PyList_SetItem(IntPtr pointer, long index, IntPtr value)
        {
            return PyList_SetItem(pointer, new IntPtr(index), value);
        }

        
        private static int PyList_SetItem(IntPtr pointer, IntPtr index, IntPtr value) => Delegates.PyList_SetItem(pointer, index, value);

        internal static int PyList_Insert(BorrowedReference pointer, long index, IntPtr value)
        {
            return PyList_Insert(pointer, new IntPtr(index), value);
        }

        
        private static int PyList_Insert(BorrowedReference pointer, IntPtr index, IntPtr value) => Delegates.PyList_Insert(pointer, index, value);

        
        internal static int PyList_Append(BorrowedReference pointer, IntPtr value) => Delegates.PyList_Append(pointer, value);

        
        internal static int PyList_Reverse(BorrowedReference pointer) => Delegates.PyList_Reverse(pointer);

        
        internal static int PyList_Sort(BorrowedReference pointer) => Delegates.PyList_Sort(pointer);

        internal static IntPtr PyList_GetSlice(IntPtr pointer, long start, long end)
        {
            return PyList_GetSlice(pointer, new IntPtr(start), new IntPtr(end));
        }

        
        private static IntPtr PyList_GetSlice(IntPtr pointer, IntPtr start, IntPtr end) => Delegates.PyList_GetSlice(pointer, start, end);

        internal static int PyList_SetSlice(IntPtr pointer, long start, long end, IntPtr value)
        {
            return PyList_SetSlice(pointer, new IntPtr(start), new IntPtr(end), value);
        }

        
        private static int PyList_SetSlice(IntPtr pointer, IntPtr start, IntPtr end, IntPtr value) => Delegates.PyList_SetSlice(pointer, start, end, value);

        internal static long PyList_Size(IntPtr pointer)
        {
            return (long)_PyList_Size(pointer);
        }

        
        private static IntPtr _PyList_Size(IntPtr pointer) => Delegates._PyList_Size(pointer);

        //====================================================================
        // Python tuple API
        //====================================================================

        internal static bool PyTuple_Check(IntPtr ob)
        {
            return PyObject_TYPE(ob) == PyTupleType;
        }

        internal static IntPtr PyTuple_New(long size)
        {
            return PyTuple_New(new IntPtr(size));
        }

        
        private static IntPtr PyTuple_New(IntPtr size) => Delegates.PyTuple_New(size);

        internal static IntPtr PyTuple_GetItem(IntPtr pointer, long index)
        {
            return PyTuple_GetItem(pointer, new IntPtr(index));
        }

        
        private static IntPtr PyTuple_GetItem(IntPtr pointer, IntPtr index) => Delegates.PyTuple_GetItem(pointer, index);

        internal static int PyTuple_SetItem(IntPtr pointer, long index, IntPtr value)
        {
            return PyTuple_SetItem(pointer, new IntPtr(index), value);
        }

        
        private static int PyTuple_SetItem(IntPtr pointer, IntPtr index, IntPtr value) => Delegates.PyTuple_SetItem(pointer, index, value);

        internal static IntPtr PyTuple_GetSlice(IntPtr pointer, long start, long end)
        {
            return PyTuple_GetSlice(pointer, new IntPtr(start), new IntPtr(end));
        }

        
        private static IntPtr PyTuple_GetSlice(IntPtr pointer, IntPtr start, IntPtr end) => Delegates.PyTuple_GetSlice(pointer, start, end);

        internal static long PyTuple_Size(IntPtr pointer)
        {
            return (long)_PyTuple_Size(pointer);
        }

        
        private static IntPtr _PyTuple_Size(IntPtr pointer) => Delegates._PyTuple_Size(pointer);


        //====================================================================
        // Python iterator API
        //====================================================================

        internal static bool PyIter_Check(BorrowedReference pointer)
        {
            var ob_type = Marshal.ReadIntPtr(pointer.DangerousGetAddress(), ObjectOffset.ob_type);
            IntPtr tp_iternext = Marshal.ReadIntPtr(ob_type, TypeOffset.tp_iternext);
            return tp_iternext != IntPtr.Zero && tp_iternext != _PyObject_NextNotImplemented;
        }

        
        internal static NewReference PyIter_Next(BorrowedReference pointer) => Delegates.PyIter_Next(pointer);


        //====================================================================
        // Python module API
        //====================================================================

        
        internal static IntPtr PyModule_New(string name) => Delegates.PyModule_New(name);

        
        internal static string PyModule_GetName(IntPtr module) => Delegates.PyModule_GetName(module);


        [Obsolete(Util.UseOverloadWithReferenceTypes)]
        internal static IntPtr PyModule_GetDict(IntPtr module) => Delegates.PyModule_GetDict(module);
        internal static BorrowedReference PyModule_GetDict(BorrowedReference module)
            => new BorrowedReference(PyModule_GetDict(module.DangerousGetAddress()));


        internal static string PyModule_GetFilename(IntPtr module) => Delegates.PyModule_GetFilename(module);

        internal static IntPtr PyModule_Create2(IntPtr module, int apiver) => Delegates.PyModule_Create2(module, apiver);

        internal static IntPtr PyImport_Import(IntPtr name) => Delegates.PyImport_Import(name);

        internal static IntPtr PyImport_ImportModule(string name) => Delegates.PyImport_ImportModule(name);

        internal static IntPtr PyImport_ReloadModule(IntPtr module) => Delegates.PyImport_ReloadModule(module);

        internal static BorrowedReference PyImport_AddModule(string name) => Delegates.PyImport_AddModule(name);

        internal static IntPtr PyImport_GetModuleDict() => Delegates.PyImport_GetModuleDict();

        internal static void PySys_SetArgvEx(
            int argc,
            [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StrArrayMarshaler))] string[] argv,
            int updatepath
        ) => Delegates.PySys_SetArgvEx(argc, argv, updatepath
);

        internal static IntPtr PySys_GetObject(string name) => Delegates.PySys_GetObject(name);

        internal static int PySys_SetObject(string name, IntPtr ob) => Delegates.PySys_SetObject(name, ob);

        //====================================================================
        // Python type object API
        //====================================================================

        internal static bool PyType_Check(BorrowedReference ob)
        {
            // fast path using raw memory access
            BorrowedReference type = PyObject_TYPE(ob);
            if (type == PyTypeType) return true;
            return PyType_FastSubclass(type, TypeFlags.TypeSubclass);
        }

        internal static bool PyType_FastSubclass(BorrowedReference type, TypeFlags baseType)
        {
            var flags = (TypeFlags)Util.ReadCLong(type.DangerousGetAddress(), TypeOffset.tp_flags);
            return (flags & baseType) != 0;
        }

        internal static void PyType_Modified(IntPtr type) => Delegates.PyType_Modified(type);

        internal static bool PyType_IsSubtype(IntPtr t1, IntPtr t2) => Delegates.PyType_IsSubtype(t1, t2);

        internal static bool PyObject_TypeCheck(IntPtr ob, IntPtr tp)
        {
            IntPtr t = PyObject_TYPE(ob);
            return (t == tp) || PyType_IsSubtype(t, tp);
        }

        internal static bool PyType_IsSameAsOrSubtype(IntPtr type, IntPtr ofType)
        {
            return (type == ofType) || PyType_IsSubtype(type, ofType);
        }

        internal static IntPtr PyType_GenericNew(IntPtr type, IntPtr args, IntPtr kw) => Delegates.PyType_GenericNew(type, args, kw);

        internal static IntPtr PyType_GenericAlloc(IntPtr type, long n)
        {
            return PyType_GenericAlloc(type, new IntPtr(n));
        }
        internal static NewReference PyType_GenericAlloc(BorrowedReference type, long n)
            => NewReference.DangerousFromPointer(PyType_GenericAlloc(type.DangerousGetAddress(), n));

        private static IntPtr PyType_GenericAlloc(IntPtr type, IntPtr n) => Delegates.PyType_GenericAlloc(type, n);

        internal static int PyType_Ready(IntPtr type) => Delegates.PyType_Ready(type);

        internal static IntPtr _PyType_Lookup(IntPtr type, IntPtr name) => Delegates._PyType_Lookup(type, name);

        internal static IntPtr PyObject_GenericGetAttr(IntPtr obj, IntPtr name) => Delegates.PyObject_GenericGetAttr(obj, name);

        internal static int PyObject_GenericSetAttr(IntPtr obj, IntPtr name, IntPtr value) => Delegates.PyObject_GenericSetAttr(obj, name, value);

        internal static IntPtr _PyObject_GetDictPtr(IntPtr obj) => Delegates._PyObject_GetDictPtr(obj);

        internal static void PyObject_GC_Del(IntPtr tp) => Delegates.PyObject_GC_Del(tp);

        internal static void PyObject_GC_Track(IntPtr tp) => Delegates.PyObject_GC_Track(tp);

        internal static void PyObject_GC_UnTrack(IntPtr tp) => Delegates.PyObject_GC_UnTrack(tp);

        //====================================================================
        // Python memory API
        //====================================================================

        internal static IntPtr PyMem_Malloc(long size)
        {
            return PyMem_Malloc(new IntPtr(size));
        }

        private static IntPtr PyMem_Malloc(IntPtr size) => Delegates.PyMem_Malloc(size);

        internal static IntPtr PyMem_Realloc(IntPtr ptr, long size)
        {
            return PyMem_Realloc(ptr, new IntPtr(size));
        }

        private static IntPtr PyMem_Realloc(IntPtr ptr, IntPtr size) => Delegates.PyMem_Realloc(ptr, size);

        internal static void PyMem_Free(IntPtr ptr) => Delegates.PyMem_Free(ptr);

        //====================================================================
        // Python exception API
        //====================================================================

        internal static void PyErr_SetString(IntPtr ob, string message) => Delegates.PyErr_SetString(ob, message);

        internal static void PyErr_SetObject(IntPtr ob, IntPtr message) => Delegates.PyErr_SetObject(ob, message);

        internal static IntPtr PyErr_SetFromErrno(IntPtr ob) => Delegates.PyErr_SetFromErrno(ob);

        internal static void PyErr_SetNone(IntPtr ob) => Delegates.PyErr_SetNone(ob);

        internal static int PyErr_ExceptionMatches(IntPtr exception) => Delegates.PyErr_ExceptionMatches(exception);

        internal static int PyErr_GivenExceptionMatches(IntPtr ob, IntPtr val) => Delegates.PyErr_GivenExceptionMatches(ob, val);

        internal static void PyErr_NormalizeException(IntPtr ob, IntPtr val, IntPtr tb) => Delegates.PyErr_NormalizeException(ob, val, tb);

        internal static IntPtr PyErr_Occurred() => Delegates.PyErr_Occurred();

        internal static void PyErr_Fetch(out NewReference type, out NewReference value, out NewReference traceback)
            => Delegates.PyErr_Fetch(out type, out value, out traceback);

        internal static void PyErr_Restore(StealingReference ob, StealingReference val, StealingReference tb)
            => Delegates.PyErr_Restore(
                ob.DangerousGetAddressOrNull(),
                val.DangerousGetAddressOrNull(),
                tb.DangerousGetAddressOrNull());

        internal static void PyErr_Clear() => Delegates.PyErr_Clear();

        internal static void PyErr_Print() => Delegates.PyErr_Print();

        //====================================================================
        // Cell API
        //====================================================================

        internal static NewReference PyCell_Get(BorrowedReference cell) => Delegates.PyCell_Get(cell);
        internal static int PyCell_Set(BorrowedReference cell, BorrowedReference value) => Delegates.PyCell_Set(cell, value);

        //====================================================================
        // Miscellaneous
        //====================================================================

        internal static IntPtr PyMethod_Self(IntPtr ob) => Delegates.PyMethod_Self(ob);

        internal static IntPtr PyMethod_Function(IntPtr ob) => Delegates.PyMethod_Function(ob);

        internal static IntPtr PyMethod_New(IntPtr func, IntPtr self) => Delegates.PyMethod_New(func, self);

        internal static int Py_AddPendingCall(IntPtr func, IntPtr arg) => Delegates.Py_AddPendingCall(func, arg);

        internal static int Py_MakePendingCalls() => Delegates.Py_MakePendingCalls();

        internal static void SetNoSiteFlag() {
            var loader = LibraryLoader.Get(OperatingSystem);

            IntPtr dllLocal = PythonDLL != "__Internal"
                ? loader.Load(PythonDLL)
                : IntPtr.Zero;

            try {
                Py_NoSiteFlag = loader.GetFunction(dllLocal, "Py_NoSiteFlag");
                Marshal.WriteInt32(Py_NoSiteFlag, 1);
            } finally {
                if (dllLocal != IntPtr.Zero) {
                    loader.Free(dllLocal);
                }
            }
        }

        /// <summary>
        /// Return value: New reference.
        /// </summary>
        internal static IntPtr GetBuiltins() => PyImport_ImportModule("builtins");

        public static class Delegates
        {
            static Delegates()
            {
                Py_IncRef = GetDelegateForFunctionPointer<Py_IncRefDelegate>(GetFunctionByName(nameof(Py_IncRef), GetUnmanagedDll(PythonDLL)));
                Py_DecRef = GetDelegateForFunctionPointer<Py_DecRefDelegate>(GetFunctionByName(nameof(Py_DecRef), GetUnmanagedDll(PythonDLL)));
                Py_Initialize = GetDelegateForFunctionPointer<Py_InitializeDelegate>(GetFunctionByName(nameof(Py_Initialize), GetUnmanagedDll(PythonDLL)));
                Py_InitializeEx = GetDelegateForFunctionPointer<Py_InitializeExDelegate>(GetFunctionByName(nameof(Py_InitializeEx), GetUnmanagedDll(PythonDLL)));
                Py_IsInitialized = GetDelegateForFunctionPointer<Py_IsInitializedDelegate>(GetFunctionByName(nameof(Py_IsInitialized), GetUnmanagedDll(PythonDLL)));
                Py_Finalize = GetDelegateForFunctionPointer<Py_FinalizeDelegate>(GetFunctionByName(nameof(Py_Finalize), GetUnmanagedDll(PythonDLL)));
                Py_NewInterpreter = GetDelegateForFunctionPointer<Py_NewInterpreterDelegate>(GetFunctionByName(nameof(Py_NewInterpreter), GetUnmanagedDll(PythonDLL)));
                Py_EndInterpreter = GetDelegateForFunctionPointer<Py_EndInterpreterDelegate>(GetFunctionByName(nameof(Py_EndInterpreter), GetUnmanagedDll(PythonDLL)));
                PyThreadState_New = GetDelegateForFunctionPointer<PyThreadState_NewDelegate>(GetFunctionByName(nameof(PyThreadState_New), GetUnmanagedDll(PythonDLL)));
                PyThreadState_Get = GetDelegateForFunctionPointer<PyThreadState_GetDelegate>(GetFunctionByName(nameof(PyThreadState_Get), GetUnmanagedDll(PythonDLL)));
                PyThread_get_key_value = GetDelegateForFunctionPointer<PyThread_get_key_valueDelegate>(GetFunctionByName(nameof(PyThread_get_key_value), GetUnmanagedDll(PythonDLL)));
                PyThread_get_thread_ident = GetDelegateForFunctionPointer<PyThread_get_thread_identDelegate>(GetFunctionByName(nameof(PyThread_get_thread_ident), GetUnmanagedDll(PythonDLL)));
                PyThread_set_key_value = GetDelegateForFunctionPointer<PyThread_set_key_valueDelegate>(GetFunctionByName(nameof(PyThread_set_key_value), GetUnmanagedDll(PythonDLL)));
                PyThreadState_Swap = GetDelegateForFunctionPointer<PyThreadState_SwapDelegate>(GetFunctionByName(nameof(PyThreadState_Swap), GetUnmanagedDll(PythonDLL)));
                PyGILState_Ensure = GetDelegateForFunctionPointer<PyGILState_EnsureDelegate>(GetFunctionByName(nameof(PyGILState_Ensure), GetUnmanagedDll(PythonDLL)));
                PyGILState_Release = GetDelegateForFunctionPointer<PyGILState_ReleaseDelegate>(GetFunctionByName(nameof(PyGILState_Release), GetUnmanagedDll(PythonDLL)));
                PyGILState_GetThisThreadState = GetDelegateForFunctionPointer<PyGILState_GetThisThreadStateDelegate>(GetFunctionByName(nameof(PyGILState_GetThisThreadState), GetUnmanagedDll(PythonDLL)));
                if (PythonVersion >= new Version(3, 4)) {
                    PyGILState_Check = GetDelegateForFunctionPointer<PyGILState_CheckDelegate>(GetFunctionByName(nameof(PyGILState_Check), GetUnmanagedDll(PythonDLL)));
                }
                Py_Main = GetDelegateForFunctionPointer<Py_MainDelegate>(GetFunctionByName(nameof(Py_Main), GetUnmanagedDll(PythonDLL)));
                PyEval_InitThreads = GetDelegateForFunctionPointer<PyEval_InitThreadsDelegate>(GetFunctionByName(nameof(PyEval_InitThreads), GetUnmanagedDll(PythonDLL)));
                PyEval_ThreadsInitialized = GetDelegateForFunctionPointer<PyEval_ThreadsInitializedDelegate>(GetFunctionByName(nameof(PyEval_ThreadsInitialized), GetUnmanagedDll(PythonDLL)));
                PyEval_AcquireLock = GetDelegateForFunctionPointer<PyEval_AcquireLockDelegate>(GetFunctionByName(nameof(PyEval_AcquireLock), GetUnmanagedDll(PythonDLL)));
                PyEval_ReleaseLock = GetDelegateForFunctionPointer<PyEval_ReleaseLockDelegate>(GetFunctionByName(nameof(PyEval_ReleaseLock), GetUnmanagedDll(PythonDLL)));
                PyEval_AcquireThread = GetDelegateForFunctionPointer<PyEval_AcquireThreadDelegate>(GetFunctionByName(nameof(PyEval_AcquireThread), GetUnmanagedDll(PythonDLL)));
                PyEval_ReleaseThread = GetDelegateForFunctionPointer<PyEval_ReleaseThreadDelegate>(GetFunctionByName(nameof(PyEval_ReleaseThread), GetUnmanagedDll(PythonDLL)));
                PyEval_SaveThread = GetDelegateForFunctionPointer<PyEval_SaveThreadDelegate>(GetFunctionByName(nameof(PyEval_SaveThread), GetUnmanagedDll(PythonDLL)));
                PyEval_RestoreThread = GetDelegateForFunctionPointer<PyEval_RestoreThreadDelegate>(GetFunctionByName(nameof(PyEval_RestoreThread), GetUnmanagedDll(PythonDLL)));
                PyEval_GetBuiltins = GetDelegateForFunctionPointer<PyEval_GetBuiltinsDelegate>(GetFunctionByName(nameof(PyEval_GetBuiltins), GetUnmanagedDll(PythonDLL)));
                PyEval_GetGlobals = GetDelegateForFunctionPointer<PyEval_GetGlobalsDelegate>(GetFunctionByName(nameof(PyEval_GetGlobals), GetUnmanagedDll(PythonDLL)));
                PyEval_GetLocals = GetDelegateForFunctionPointer<PyEval_GetLocalsDelegate>(GetFunctionByName(nameof(PyEval_GetLocals), GetUnmanagedDll(PythonDLL)));
                Py_GetProgramName = GetDelegateForFunctionPointer<Py_GetProgramNameDelegate>(GetFunctionByName(nameof(Py_GetProgramName), GetUnmanagedDll(PythonDLL)));
                Py_SetProgramName = GetDelegateForFunctionPointer<Py_SetProgramNameDelegate>(GetFunctionByName(nameof(Py_SetProgramName), GetUnmanagedDll(PythonDLL)));
                Py_GetPythonHome = GetDelegateForFunctionPointer<Py_GetPythonHomeDelegate>(GetFunctionByName(nameof(Py_GetPythonHome), GetUnmanagedDll(PythonDLL)));
                Py_SetPythonHome = GetDelegateForFunctionPointer<Py_SetPythonHomeDelegate>(GetFunctionByName(nameof(Py_SetPythonHome), GetUnmanagedDll(PythonDLL)));
                Py_GetPath = GetDelegateForFunctionPointer<Py_GetPathDelegate>(GetFunctionByName(nameof(Py_GetPath), GetUnmanagedDll(PythonDLL)));
                Py_SetPath = GetDelegateForFunctionPointer<Py_SetPathDelegate>(GetFunctionByName(nameof(Py_SetPath), GetUnmanagedDll(PythonDLL)));
                Py_GetVersion = GetDelegateForFunctionPointer<Py_GetVersionDelegate>(GetFunctionByName(nameof(Py_GetVersion), GetUnmanagedDll(PythonDLL)));
                Py_GetPlatform = GetDelegateForFunctionPointer<Py_GetPlatformDelegate>(GetFunctionByName(nameof(Py_GetPlatform), GetUnmanagedDll(PythonDLL)));
                Py_GetCopyright = GetDelegateForFunctionPointer<Py_GetCopyrightDelegate>(GetFunctionByName(nameof(Py_GetCopyright), GetUnmanagedDll(PythonDLL)));
                Py_GetCompiler = GetDelegateForFunctionPointer<Py_GetCompilerDelegate>(GetFunctionByName(nameof(Py_GetCompiler), GetUnmanagedDll(PythonDLL)));
                Py_GetBuildInfo = GetDelegateForFunctionPointer<Py_GetBuildInfoDelegate>(GetFunctionByName(nameof(Py_GetBuildInfo), GetUnmanagedDll(PythonDLL)));
                PyRun_SimpleString = GetDelegateForFunctionPointer<PyRun_SimpleStringDelegate>(GetFunctionByName(nameof(PyRun_SimpleString), GetUnmanagedDll(PythonDLL)));
                PyRun_String = GetDelegateForFunctionPointer<PyRun_StringDelegate>(GetFunctionByName(nameof(PyRun_String), GetUnmanagedDll(PythonDLL)));
                PyEval_EvalCode = GetDelegateForFunctionPointer<PyEval_EvalCodeDelegate>(GetFunctionByName(nameof(PyEval_EvalCode), GetUnmanagedDll(PythonDLL)));
                Py_CompileString = GetDelegateForFunctionPointer<Py_CompileStringDelegate>(GetFunctionByName(nameof(Py_CompileString), GetUnmanagedDll(PythonDLL)));
                Py_CompileStringExFlags = GetDelegateForFunctionPointer<Py_CompileStringExFlagsDelegate>(GetFunctionByName(nameof(Py_CompileStringExFlags), GetUnmanagedDll(PythonDLL)));
                PyImport_ExecCodeModule = GetDelegateForFunctionPointer<PyImport_ExecCodeModuleDelegate>(GetFunctionByName(nameof(PyImport_ExecCodeModule), GetUnmanagedDll(PythonDLL)));
                PyCFunction_NewEx = GetDelegateForFunctionPointer<PyCFunction_NewExDelegate>(GetFunctionByName(nameof(PyCFunction_NewEx), GetUnmanagedDll(PythonDLL)));
                PyCFunction_Call = GetDelegateForFunctionPointer<PyCFunction_CallDelegate>(GetFunctionByName(nameof(PyCFunction_Call), GetUnmanagedDll(PythonDLL)));
                PyObject_HasAttrString = GetDelegateForFunctionPointer<PyObject_HasAttrStringDelegate>(GetFunctionByName(nameof(PyObject_HasAttrString), GetUnmanagedDll(PythonDLL)));
                PyObject_GetAttrString = GetDelegateForFunctionPointer<PyObject_GetAttrStringDelegate>(GetFunctionByName(nameof(PyObject_GetAttrString), GetUnmanagedDll(PythonDLL)));
                PyObject_SetAttrString = GetDelegateForFunctionPointer<PyObject_SetAttrStringDelegate>(GetFunctionByName(nameof(PyObject_SetAttrString), GetUnmanagedDll(PythonDLL)));
                PyObject_HasAttr = GetDelegateForFunctionPointer<PyObject_HasAttrDelegate>(GetFunctionByName(nameof(PyObject_HasAttr), GetUnmanagedDll(PythonDLL)));
                PyObject_GetAttr = GetDelegateForFunctionPointer<PyObject_GetAttrDelegate>(GetFunctionByName(nameof(PyObject_GetAttr), GetUnmanagedDll(PythonDLL)));
                PyObject_SetAttr = GetDelegateForFunctionPointer<PyObject_SetAttrDelegate>(GetFunctionByName(nameof(PyObject_SetAttr), GetUnmanagedDll(PythonDLL)));
                PyObject_GetItem = GetDelegateForFunctionPointer<PyObject_GetItemDelegate>(GetFunctionByName(nameof(PyObject_GetItem), GetUnmanagedDll(PythonDLL)));
                PyObject_SetItem = GetDelegateForFunctionPointer<PyObject_SetItemDelegate>(GetFunctionByName(nameof(PyObject_SetItem), GetUnmanagedDll(PythonDLL)));
                PyObject_DelItem = GetDelegateForFunctionPointer<PyObject_DelItemDelegate>(GetFunctionByName(nameof(PyObject_DelItem), GetUnmanagedDll(PythonDLL)));
                PyObject_GetIter = GetDelegateForFunctionPointer<PyObject_GetIterDelegate>(GetFunctionByName(nameof(PyObject_GetIter), GetUnmanagedDll(PythonDLL)));
                PyObject_Call = GetDelegateForFunctionPointer<PyObject_CallDelegate>(GetFunctionByName(nameof(PyObject_Call), GetUnmanagedDll(PythonDLL)));
                PyObject_CallObject = GetDelegateForFunctionPointer<PyObject_CallObjectDelegate>(GetFunctionByName(nameof(PyObject_CallObject), GetUnmanagedDll(PythonDLL)));
                PyObject_RichCompareBool = GetDelegateForFunctionPointer<PyObject_RichCompareBoolDelegate>(GetFunctionByName(nameof(PyObject_RichCompareBool), GetUnmanagedDll(PythonDLL)));
                PyObject_IsInstance = GetDelegateForFunctionPointer<PyObject_IsInstanceDelegate>(GetFunctionByName(nameof(PyObject_IsInstance), GetUnmanagedDll(PythonDLL)));
                PyObject_IsSubclass = GetDelegateForFunctionPointer<PyObject_IsSubclassDelegate>(GetFunctionByName(nameof(PyObject_IsSubclass), GetUnmanagedDll(PythonDLL)));
                PyCallable_Check = GetDelegateForFunctionPointer<PyCallable_CheckDelegate>(GetFunctionByName(nameof(PyCallable_Check), GetUnmanagedDll(PythonDLL)));
                PyObject_IsTrue = GetDelegateForFunctionPointer<PyObject_IsTrueDelegate>(GetFunctionByName(nameof(PyObject_IsTrue), GetUnmanagedDll(PythonDLL)));
                PyObject_Not = GetDelegateForFunctionPointer<PyObject_NotDelegate>(GetFunctionByName(nameof(PyObject_Not), GetUnmanagedDll(PythonDLL)));
                _PyObject_Size = GetDelegateForFunctionPointer<_PyObject_SizeDelegate>(GetFunctionByName("PyObject_Size", GetUnmanagedDll(PythonDLL)));
                PyObject_Hash = GetDelegateForFunctionPointer<PyObject_HashDelegate>(GetFunctionByName(nameof(PyObject_Hash), GetUnmanagedDll(PythonDLL)));
                PyObject_Repr = GetDelegateForFunctionPointer<PyObject_ReprDelegate>(GetFunctionByName(nameof(PyObject_Repr), GetUnmanagedDll(PythonDLL)));
                PyObject_Str = GetDelegateForFunctionPointer<PyObject_StrDelegate>(GetFunctionByName(nameof(PyObject_Str), GetUnmanagedDll(PythonDLL)));
                PyObject_Unicode = GetDelegateForFunctionPointer<PyObject_UnicodeDelegate>(GetFunctionByName("PyObject_Str", GetUnmanagedDll(PythonDLL)));
                PyObject_Dir = GetDelegateForFunctionPointer<PyObject_DirDelegate>(GetFunctionByName(nameof(PyObject_Dir), GetUnmanagedDll(PythonDLL)));
                PyNumber_Int = GetDelegateForFunctionPointer<PyNumber_IntDelegate>(GetFunctionByName("PyNumber_Long", GetUnmanagedDll(PythonDLL)));
                PyNumber_Long = GetDelegateForFunctionPointer<PyNumber_LongDelegate>(GetFunctionByName(nameof(PyNumber_Long), GetUnmanagedDll(PythonDLL)));
                PyNumber_Float = GetDelegateForFunctionPointer<PyNumber_FloatDelegate>(GetFunctionByName(nameof(PyNumber_Float), GetUnmanagedDll(PythonDLL)));
                PyNumber_Check = GetDelegateForFunctionPointer<PyNumber_CheckDelegate>(GetFunctionByName(nameof(PyNumber_Check), GetUnmanagedDll(PythonDLL)));
                PyInt_FromLong = GetDelegateForFunctionPointer<PyInt_FromLongDelegate>(GetFunctionByName("PyLong_FromLong", GetUnmanagedDll(PythonDLL)));
                PyInt_AsLong = GetDelegateForFunctionPointer<PyInt_AsLongDelegate>(GetFunctionByName("PyLong_AsLong", GetUnmanagedDll(PythonDLL)));
                PyInt_FromString = GetDelegateForFunctionPointer<PyInt_FromStringDelegate>(GetFunctionByName("PyLong_FromString", GetUnmanagedDll(PythonDLL)));
                PyLong_FromLong = GetDelegateForFunctionPointer<PyLong_FromLongDelegate>(GetFunctionByName(nameof(PyLong_FromLong), GetUnmanagedDll(PythonDLL)));
                PyLong_FromUnsignedLong32 = GetDelegateForFunctionPointer<PyLong_FromUnsignedLong32Delegate>(GetFunctionByName("PyLong_FromUnsignedLong", GetUnmanagedDll(PythonDLL)));
                PyLong_FromUnsignedLong64 = GetDelegateForFunctionPointer<PyLong_FromUnsignedLong64Delegate>(GetFunctionByName("PyLong_FromUnsignedLong", GetUnmanagedDll(PythonDLL)));
                PyLong_FromUnsignedLong = GetDelegateForFunctionPointer<PyLong_FromUnsignedLongDelegate>(GetFunctionByName(nameof(PyLong_FromUnsignedLong), GetUnmanagedDll(PythonDLL)));
                PyLong_FromDouble = GetDelegateForFunctionPointer<PyLong_FromDoubleDelegate>(GetFunctionByName(nameof(PyLong_FromDouble), GetUnmanagedDll(PythonDLL)));
                PyLong_FromLongLong = GetDelegateForFunctionPointer<PyLong_FromLongLongDelegate>(GetFunctionByName(nameof(PyLong_FromLongLong), GetUnmanagedDll(PythonDLL)));
                PyLong_FromUnsignedLongLong = GetDelegateForFunctionPointer<PyLong_FromUnsignedLongLongDelegate>(GetFunctionByName(nameof(PyLong_FromUnsignedLongLong), GetUnmanagedDll(PythonDLL)));
                PyLong_FromString = GetDelegateForFunctionPointer<PyLong_FromStringDelegate>(GetFunctionByName(nameof(PyLong_FromString), GetUnmanagedDll(PythonDLL)));
                PyLong_AsLong = GetDelegateForFunctionPointer<PyLong_AsLongDelegate>(GetFunctionByName(nameof(PyLong_AsLong), GetUnmanagedDll(PythonDLL)));
                PyLong_AsUnsignedLong32 = GetDelegateForFunctionPointer<PyLong_AsUnsignedLong32Delegate>(GetFunctionByName("PyLong_AsUnsignedLong", GetUnmanagedDll(PythonDLL)));
                PyLong_AsUnsignedLong64 = GetDelegateForFunctionPointer<PyLong_AsUnsignedLong64Delegate>(GetFunctionByName("PyLong_AsUnsignedLong", GetUnmanagedDll(PythonDLL)));
                PyLong_AsUnsignedLong = GetDelegateForFunctionPointer<PyLong_AsUnsignedLongDelegate>(GetFunctionByName(nameof(PyLong_AsUnsignedLong), GetUnmanagedDll(PythonDLL)));
                PyLong_AsLongLong = GetDelegateForFunctionPointer<PyLong_AsLongLongDelegate>(GetFunctionByName(nameof(PyLong_AsLongLong), GetUnmanagedDll(PythonDLL)));
                PyLong_AsUnsignedLongLong = GetDelegateForFunctionPointer<PyLong_AsUnsignedLongLongDelegate>(GetFunctionByName(nameof(PyLong_AsUnsignedLongLong), GetUnmanagedDll(PythonDLL)));
                PyFloat_FromDouble = GetDelegateForFunctionPointer<PyFloat_FromDoubleDelegate>(GetFunctionByName(nameof(PyFloat_FromDouble), GetUnmanagedDll(PythonDLL)));
                PyFloat_FromString = GetDelegateForFunctionPointer<PyFloat_FromStringDelegate>(GetFunctionByName(nameof(PyFloat_FromString), GetUnmanagedDll(PythonDLL)));
                PyFloat_AsDouble = GetDelegateForFunctionPointer<PyFloat_AsDoubleDelegate>(GetFunctionByName(nameof(PyFloat_AsDouble), GetUnmanagedDll(PythonDLL)));
                PyNumber_Add = GetDelegateForFunctionPointer<PyNumber_AddDelegate>(GetFunctionByName(nameof(PyNumber_Add), GetUnmanagedDll(PythonDLL)));
                PyNumber_Subtract = GetDelegateForFunctionPointer<PyNumber_SubtractDelegate>(GetFunctionByName(nameof(PyNumber_Subtract), GetUnmanagedDll(PythonDLL)));
                PyNumber_Multiply = GetDelegateForFunctionPointer<PyNumber_MultiplyDelegate>(GetFunctionByName(nameof(PyNumber_Multiply), GetUnmanagedDll(PythonDLL)));
                PyNumber_TrueDivide = GetDelegateForFunctionPointer<PyNumber_TrueDivideDelegate>(GetFunctionByName(nameof(PyNumber_TrueDivide), GetUnmanagedDll(PythonDLL)));
                PyNumber_And = GetDelegateForFunctionPointer<PyNumber_AndDelegate>(GetFunctionByName(nameof(PyNumber_And), GetUnmanagedDll(PythonDLL)));
                PyNumber_Xor = GetDelegateForFunctionPointer<PyNumber_XorDelegate>(GetFunctionByName(nameof(PyNumber_Xor), GetUnmanagedDll(PythonDLL)));
                PyNumber_Or = GetDelegateForFunctionPointer<PyNumber_OrDelegate>(GetFunctionByName(nameof(PyNumber_Or), GetUnmanagedDll(PythonDLL)));
                PyNumber_Lshift = GetDelegateForFunctionPointer<PyNumber_LshiftDelegate>(GetFunctionByName(nameof(PyNumber_Lshift), GetUnmanagedDll(PythonDLL)));
                PyNumber_Rshift = GetDelegateForFunctionPointer<PyNumber_RshiftDelegate>(GetFunctionByName(nameof(PyNumber_Rshift), GetUnmanagedDll(PythonDLL)));
                PyNumber_Power = GetDelegateForFunctionPointer<PyNumber_PowerDelegate>(GetFunctionByName(nameof(PyNumber_Power), GetUnmanagedDll(PythonDLL)));
                PyNumber_Remainder = GetDelegateForFunctionPointer<PyNumber_RemainderDelegate>(GetFunctionByName(nameof(PyNumber_Remainder), GetUnmanagedDll(PythonDLL)));
                PyNumber_InPlaceAdd = GetDelegateForFunctionPointer<PyNumber_InPlaceAddDelegate>(GetFunctionByName(nameof(PyNumber_InPlaceAdd), GetUnmanagedDll(PythonDLL)));
                PyNumber_InPlaceSubtract = GetDelegateForFunctionPointer<PyNumber_InPlaceSubtractDelegate>(GetFunctionByName(nameof(PyNumber_InPlaceSubtract), GetUnmanagedDll(PythonDLL)));
                PyNumber_InPlaceMultiply = GetDelegateForFunctionPointer<PyNumber_InPlaceMultiplyDelegate>(GetFunctionByName(nameof(PyNumber_InPlaceMultiply), GetUnmanagedDll(PythonDLL)));
                PyNumber_InPlaceTrueDivide = GetDelegateForFunctionPointer<PyNumber_InPlaceTrueDivideDelegate>(GetFunctionByName(nameof(PyNumber_InPlaceTrueDivide), GetUnmanagedDll(PythonDLL)));
                PyNumber_InPlaceAnd = GetDelegateForFunctionPointer<PyNumber_InPlaceAndDelegate>(GetFunctionByName(nameof(PyNumber_InPlaceAnd), GetUnmanagedDll(PythonDLL)));
                PyNumber_InPlaceXor = GetDelegateForFunctionPointer<PyNumber_InPlaceXorDelegate>(GetFunctionByName(nameof(PyNumber_InPlaceXor), GetUnmanagedDll(PythonDLL)));
                PyNumber_InPlaceOr = GetDelegateForFunctionPointer<PyNumber_InPlaceOrDelegate>(GetFunctionByName(nameof(PyNumber_InPlaceOr), GetUnmanagedDll(PythonDLL)));
                PyNumber_InPlaceLshift = GetDelegateForFunctionPointer<PyNumber_InPlaceLshiftDelegate>(GetFunctionByName(nameof(PyNumber_InPlaceLshift), GetUnmanagedDll(PythonDLL)));
                PyNumber_InPlaceRshift = GetDelegateForFunctionPointer<PyNumber_InPlaceRshiftDelegate>(GetFunctionByName(nameof(PyNumber_InPlaceRshift), GetUnmanagedDll(PythonDLL)));
                PyNumber_InPlacePower = GetDelegateForFunctionPointer<PyNumber_InPlacePowerDelegate>(GetFunctionByName(nameof(PyNumber_InPlacePower), GetUnmanagedDll(PythonDLL)));
                PyNumber_InPlaceRemainder = GetDelegateForFunctionPointer<PyNumber_InPlaceRemainderDelegate>(GetFunctionByName(nameof(PyNumber_InPlaceRemainder), GetUnmanagedDll(PythonDLL)));
                PyNumber_Negative = GetDelegateForFunctionPointer<PyNumber_NegativeDelegate>(GetFunctionByName(nameof(PyNumber_Negative), GetUnmanagedDll(PythonDLL)));
                PyNumber_Positive = GetDelegateForFunctionPointer<PyNumber_PositiveDelegate>(GetFunctionByName(nameof(PyNumber_Positive), GetUnmanagedDll(PythonDLL)));
                PyNumber_Invert = GetDelegateForFunctionPointer<PyNumber_InvertDelegate>(GetFunctionByName(nameof(PyNumber_Invert), GetUnmanagedDll(PythonDLL)));
                PySequence_Check = GetDelegateForFunctionPointer<PySequence_CheckDelegate>(GetFunctionByName(nameof(PySequence_Check), GetUnmanagedDll(PythonDLL)));
                PySequence_GetItem = GetDelegateForFunctionPointer<PySequence_GetItemDelegate>(GetFunctionByName(nameof(PySequence_GetItem), GetUnmanagedDll(PythonDLL)));
                PySequence_SetItem = GetDelegateForFunctionPointer<PySequence_SetItemDelegate>(GetFunctionByName(nameof(PySequence_SetItem), GetUnmanagedDll(PythonDLL)));
                PySequence_DelItem = GetDelegateForFunctionPointer<PySequence_DelItemDelegate>(GetFunctionByName(nameof(PySequence_DelItem), GetUnmanagedDll(PythonDLL)));
                PySequence_GetSlice = GetDelegateForFunctionPointer<PySequence_GetSliceDelegate>(GetFunctionByName(nameof(PySequence_GetSlice), GetUnmanagedDll(PythonDLL)));
                PySequence_SetSlice = GetDelegateForFunctionPointer<PySequence_SetSliceDelegate>(GetFunctionByName(nameof(PySequence_SetSlice), GetUnmanagedDll(PythonDLL)));
                PySequence_DelSlice = GetDelegateForFunctionPointer<PySequence_DelSliceDelegate>(GetFunctionByName(nameof(PySequence_DelSlice), GetUnmanagedDll(PythonDLL)));
                _PySequence_Size = GetDelegateForFunctionPointer<_PySequence_SizeDelegate>(GetFunctionByName("PySequence_Size", GetUnmanagedDll(PythonDLL)));
                PySequence_Contains = GetDelegateForFunctionPointer<PySequence_ContainsDelegate>(GetFunctionByName(nameof(PySequence_Contains), GetUnmanagedDll(PythonDLL)));
                PySequence_Concat = GetDelegateForFunctionPointer<PySequence_ConcatDelegate>(GetFunctionByName(nameof(PySequence_Concat), GetUnmanagedDll(PythonDLL)));
                PySequence_Repeat = GetDelegateForFunctionPointer<PySequence_RepeatDelegate>(GetFunctionByName(nameof(PySequence_Repeat), GetUnmanagedDll(PythonDLL)));
                PySequence_Index = GetDelegateForFunctionPointer<PySequence_IndexDelegate>(GetFunctionByName(nameof(PySequence_Index), GetUnmanagedDll(PythonDLL)));
                _PySequence_Count = GetDelegateForFunctionPointer<_PySequence_CountDelegate>(GetFunctionByName("PySequence_Count", GetUnmanagedDll(PythonDLL)));
                PySequence_Tuple = GetDelegateForFunctionPointer<PySequence_TupleDelegate>(GetFunctionByName(nameof(PySequence_Tuple), GetUnmanagedDll(PythonDLL)));
                PySequence_List = GetDelegateForFunctionPointer<PySequence_ListDelegate>(GetFunctionByName(nameof(PySequence_List), GetUnmanagedDll(PythonDLL)));
                PyBytes_FromString = GetDelegateForFunctionPointer<PyBytes_FromStringDelegate>(GetFunctionByName(nameof(PyBytes_FromString), GetUnmanagedDll(PythonDLL)));
                _PyBytes_Size = GetDelegateForFunctionPointer<_PyBytes_SizeDelegate>(GetFunctionByName("PyBytes_Size", GetUnmanagedDll(PythonDLL)));
                _PyString_FromStringAndSize = GetDelegateForFunctionPointer<_PyString_FromStringAndSizeDelegate>(GetFunctionByName("PyUnicode_FromStringAndSize", GetUnmanagedDll(PythonDLL)));
                PyUnicode_FromStringAndSize = GetDelegateForFunctionPointer<PyUnicode_FromStringAndSizeDelegate>(GetFunctionByName(nameof(PyUnicode_FromStringAndSize), GetUnmanagedDll(PythonDLL)));
                PyUnicode_AsUTF8 = GetDelegateForFunctionPointer<PyUnicode_AsUTF8Delegate>(GetFunctionByName(nameof(PyUnicode_AsUTF8), GetUnmanagedDll(PythonDLL)));
                PyUnicode_FromObject = GetDelegateForFunctionPointer<PyUnicode_FromObjectDelegate>(GetFunctionByName(nameof(PyUnicode_FromObject), GetUnmanagedDll(PythonDLL)));
                PyUnicode_FromEncodedObject = GetDelegateForFunctionPointer<PyUnicode_FromEncodedObjectDelegate>(GetFunctionByName(nameof(PyUnicode_FromEncodedObject), GetUnmanagedDll(PythonDLL)));
                PyUnicode_FromKindAndData = GetDelegateForFunctionPointer<PyUnicode_FromKindAndDataDelegate>(GetFunctionByName(nameof(PyUnicode_FromKindAndData), GetUnmanagedDll(PythonDLL)));
                _PyUnicode_GetSize = GetDelegateForFunctionPointer<_PyUnicode_GetSizeDelegate>(GetFunctionByName("PyUnicode_GetSize", GetUnmanagedDll(PythonDLL)));
                PyUnicode_AsUnicode = GetDelegateForFunctionPointer<PyUnicode_AsUnicodeDelegate>(GetFunctionByName(nameof(PyUnicode_AsUnicode), GetUnmanagedDll(PythonDLL)));
                PyUnicode_FromOrdinal = GetDelegateForFunctionPointer<PyUnicode_FromOrdinalDelegate>(GetFunctionByName(nameof(PyUnicode_FromOrdinal), GetUnmanagedDll(PythonDLL)));
                PyDict_New = GetDelegateForFunctionPointer<PyDict_NewDelegate>(GetFunctionByName(nameof(PyDict_New), GetUnmanagedDll(PythonDLL)));
                PyDictProxy_New = GetDelegateForFunctionPointer<PyDictProxy_NewDelegate>(GetFunctionByName(nameof(PyDictProxy_New), GetUnmanagedDll(PythonDLL)));
                PyDict_GetItem = GetDelegateForFunctionPointer<PyDict_GetItemDelegate>(GetFunctionByName(nameof(PyDict_GetItem), GetUnmanagedDll(PythonDLL)));
                PyDict_GetItemString = GetDelegateForFunctionPointer<PyDict_GetItemStringDelegate>(GetFunctionByName(nameof(PyDict_GetItemString), GetUnmanagedDll(PythonDLL)));
                PyDict_SetItem = GetDelegateForFunctionPointer<PyDict_SetItemDelegate>(GetFunctionByName(nameof(PyDict_SetItem), GetUnmanagedDll(PythonDLL)));
                PyDict_SetItemString = GetDelegateForFunctionPointer<PyDict_SetItemStringDelegate>(GetFunctionByName(nameof(PyDict_SetItemString), GetUnmanagedDll(PythonDLL)));
                PyDict_DelItem = GetDelegateForFunctionPointer<PyDict_DelItemDelegate>(GetFunctionByName(nameof(PyDict_DelItem), GetUnmanagedDll(PythonDLL)));
                PyDict_DelItemString = GetDelegateForFunctionPointer<PyDict_DelItemStringDelegate>(GetFunctionByName(nameof(PyDict_DelItemString), GetUnmanagedDll(PythonDLL)));
                PyMapping_HasKey = GetDelegateForFunctionPointer<PyMapping_HasKeyDelegate>(GetFunctionByName(nameof(PyMapping_HasKey), GetUnmanagedDll(PythonDLL)));
                PyDict_Keys = GetDelegateForFunctionPointer<PyDict_KeysDelegate>(GetFunctionByName(nameof(PyDict_Keys), GetUnmanagedDll(PythonDLL)));
                PyDict_Values = GetDelegateForFunctionPointer<PyDict_ValuesDelegate>(GetFunctionByName(nameof(PyDict_Values), GetUnmanagedDll(PythonDLL)));
                PyDict_Items = GetDelegateForFunctionPointer<PyDict_ItemsDelegate>(GetFunctionByName(nameof(PyDict_Items), GetUnmanagedDll(PythonDLL)));
                PyDict_Copy = GetDelegateForFunctionPointer<PyDict_CopyDelegate>(GetFunctionByName(nameof(PyDict_Copy), GetUnmanagedDll(PythonDLL)));
                PyDict_Update = GetDelegateForFunctionPointer<PyDict_UpdateDelegate>(GetFunctionByName(nameof(PyDict_Update), GetUnmanagedDll(PythonDLL)));
                PyDict_Clear = GetDelegateForFunctionPointer<PyDict_ClearDelegate>(GetFunctionByName(nameof(PyDict_Clear), GetUnmanagedDll(PythonDLL)));
                _PyDict_Size = GetDelegateForFunctionPointer<_PyDict_SizeDelegate>(GetFunctionByName("PyDict_Size", GetUnmanagedDll(PythonDLL)));
                PyList_New = GetDelegateForFunctionPointer<PyList_NewDelegate>(GetFunctionByName(nameof(PyList_New), GetUnmanagedDll(PythonDLL)));
                PyList_AsTuple = GetDelegateForFunctionPointer<PyList_AsTupleDelegate>(GetFunctionByName(nameof(PyList_AsTuple), GetUnmanagedDll(PythonDLL)));
                PyList_GetItem = GetDelegateForFunctionPointer<PyList_GetItemDelegate>(GetFunctionByName(nameof(PyList_GetItem), GetUnmanagedDll(PythonDLL)));
                PyList_SetItem = GetDelegateForFunctionPointer<PyList_SetItemDelegate>(GetFunctionByName(nameof(PyList_SetItem), GetUnmanagedDll(PythonDLL)));
                PyList_Insert = GetDelegateForFunctionPointer<PyList_InsertDelegate>(GetFunctionByName(nameof(PyList_Insert), GetUnmanagedDll(PythonDLL)));
                PyList_Append = GetDelegateForFunctionPointer<PyList_AppendDelegate>(GetFunctionByName(nameof(PyList_Append), GetUnmanagedDll(PythonDLL)));
                PyList_Reverse = GetDelegateForFunctionPointer<PyList_ReverseDelegate>(GetFunctionByName(nameof(PyList_Reverse), GetUnmanagedDll(PythonDLL)));
                PyList_Sort = GetDelegateForFunctionPointer<PyList_SortDelegate>(GetFunctionByName(nameof(PyList_Sort), GetUnmanagedDll(PythonDLL)));
                PyList_GetSlice = GetDelegateForFunctionPointer<PyList_GetSliceDelegate>(GetFunctionByName(nameof(PyList_GetSlice), GetUnmanagedDll(PythonDLL)));
                PyList_SetSlice = GetDelegateForFunctionPointer<PyList_SetSliceDelegate>(GetFunctionByName(nameof(PyList_SetSlice), GetUnmanagedDll(PythonDLL)));
                _PyList_Size = GetDelegateForFunctionPointer<_PyList_SizeDelegate>(GetFunctionByName("PyList_Size", GetUnmanagedDll(PythonDLL)));
                PyTuple_New = GetDelegateForFunctionPointer<PyTuple_NewDelegate>(GetFunctionByName(nameof(PyTuple_New), GetUnmanagedDll(PythonDLL)));
                PyTuple_GetItem = GetDelegateForFunctionPointer<PyTuple_GetItemDelegate>(GetFunctionByName(nameof(PyTuple_GetItem), GetUnmanagedDll(PythonDLL)));
                PyTuple_SetItem = GetDelegateForFunctionPointer<PyTuple_SetItemDelegate>(GetFunctionByName(nameof(PyTuple_SetItem), GetUnmanagedDll(PythonDLL)));
                PyTuple_GetSlice = GetDelegateForFunctionPointer<PyTuple_GetSliceDelegate>(GetFunctionByName(nameof(PyTuple_GetSlice), GetUnmanagedDll(PythonDLL)));
                _PyTuple_Size = GetDelegateForFunctionPointer<_PyTuple_SizeDelegate>(GetFunctionByName("PyTuple_Size", GetUnmanagedDll(PythonDLL)));
                PyIter_Next = GetDelegateForFunctionPointer<PyIter_NextDelegate>(GetFunctionByName(nameof(PyIter_Next), GetUnmanagedDll(PythonDLL)));
                PyModule_New = GetDelegateForFunctionPointer<PyModule_NewDelegate>(GetFunctionByName(nameof(PyModule_New), GetUnmanagedDll(PythonDLL)));
                PyModule_GetName = GetDelegateForFunctionPointer<PyModule_GetNameDelegate>(GetFunctionByName(nameof(PyModule_GetName), GetUnmanagedDll(PythonDLL)));
                PyModule_GetDict = GetDelegateForFunctionPointer<PyModule_GetDictDelegate>(GetFunctionByName(nameof(PyModule_GetDict), GetUnmanagedDll(PythonDLL)));
                PyModule_GetFilename = GetDelegateForFunctionPointer<PyModule_GetFilenameDelegate>(GetFunctionByName(nameof(PyModule_GetFilename), GetUnmanagedDll(PythonDLL)));
                PyModule_Create2 = GetDelegateForFunctionPointer<PyModule_Create2Delegate>(GetFunctionByName(nameof(PyModule_Create2), GetUnmanagedDll(PythonDLL)));
                PyImport_Import = GetDelegateForFunctionPointer<PyImport_ImportDelegate>(GetFunctionByName(nameof(PyImport_Import), GetUnmanagedDll(PythonDLL)));
                PyImport_ImportModule = GetDelegateForFunctionPointer<PyImport_ImportModuleDelegate>(GetFunctionByName(nameof(PyImport_ImportModule), GetUnmanagedDll(PythonDLL)));
                PyImport_ReloadModule = GetDelegateForFunctionPointer<PyImport_ReloadModuleDelegate>(GetFunctionByName(nameof(PyImport_ReloadModule), GetUnmanagedDll(PythonDLL)));
                PyImport_AddModule = GetDelegateForFunctionPointer<PyImport_AddModuleDelegate>(GetFunctionByName(nameof(PyImport_AddModule), GetUnmanagedDll(PythonDLL)));
                PyImport_GetModuleDict = GetDelegateForFunctionPointer<PyImport_GetModuleDictDelegate>(GetFunctionByName(nameof(PyImport_GetModuleDict), GetUnmanagedDll(PythonDLL)));
                PySys_SetArgvEx = GetDelegateForFunctionPointer<PySys_SetArgvExDelegate>(GetFunctionByName(nameof(PySys_SetArgvEx), GetUnmanagedDll(PythonDLL)));
                PySys_GetObject = GetDelegateForFunctionPointer<PySys_GetObjectDelegate>(GetFunctionByName(nameof(PySys_GetObject), GetUnmanagedDll(PythonDLL)));
                PySys_SetObject = GetDelegateForFunctionPointer<PySys_SetObjectDelegate>(GetFunctionByName(nameof(PySys_SetObject), GetUnmanagedDll(PythonDLL)));
                PyType_Modified = GetDelegateForFunctionPointer<PyType_ModifiedDelegate>(GetFunctionByName(nameof(PyType_Modified), GetUnmanagedDll(PythonDLL)));
                PyType_IsSubtype = GetDelegateForFunctionPointer<PyType_IsSubtypeDelegate>(GetFunctionByName(nameof(PyType_IsSubtype), GetUnmanagedDll(PythonDLL)));
                PyType_GenericNew = GetDelegateForFunctionPointer<PyType_GenericNewDelegate>(GetFunctionByName(nameof(PyType_GenericNew), GetUnmanagedDll(PythonDLL)));
                PyType_GenericAlloc = GetDelegateForFunctionPointer<PyType_GenericAllocDelegate>(GetFunctionByName(nameof(PyType_GenericAlloc), GetUnmanagedDll(PythonDLL)));
                PyType_Ready = GetDelegateForFunctionPointer<PyType_ReadyDelegate>(GetFunctionByName(nameof(PyType_Ready), GetUnmanagedDll(PythonDLL)));
                _PyType_Lookup = GetDelegateForFunctionPointer<_PyType_LookupDelegate>(GetFunctionByName(nameof(_PyType_Lookup), GetUnmanagedDll(PythonDLL)));
                PyObject_GenericGetAttr = GetDelegateForFunctionPointer<PyObject_GenericGetAttrDelegate>(GetFunctionByName(nameof(PyObject_GenericGetAttr), GetUnmanagedDll(PythonDLL)));
                PyObject_GenericSetAttr = GetDelegateForFunctionPointer<PyObject_GenericSetAttrDelegate>(GetFunctionByName(nameof(PyObject_GenericSetAttr), GetUnmanagedDll(PythonDLL)));
                _PyObject_GetDictPtr = GetDelegateForFunctionPointer<_PyObject_GetDictPtrDelegate>(GetFunctionByName(nameof(_PyObject_GetDictPtr), GetUnmanagedDll(PythonDLL)));
                PyObject_GC_Del = GetDelegateForFunctionPointer<PyObject_GC_DelDelegate>(GetFunctionByName(nameof(PyObject_GC_Del), GetUnmanagedDll(PythonDLL)));
                PyObject_GC_Track = GetDelegateForFunctionPointer<PyObject_GC_TrackDelegate>(GetFunctionByName(nameof(PyObject_GC_Track), GetUnmanagedDll(PythonDLL)));
                PyObject_GC_UnTrack = GetDelegateForFunctionPointer<PyObject_GC_UnTrackDelegate>(GetFunctionByName(nameof(PyObject_GC_UnTrack), GetUnmanagedDll(PythonDLL)));
                PyMem_Malloc = GetDelegateForFunctionPointer<PyMem_MallocDelegate>(GetFunctionByName(nameof(PyMem_Malloc), GetUnmanagedDll(PythonDLL)));
                PyMem_Realloc = GetDelegateForFunctionPointer<PyMem_ReallocDelegate>(GetFunctionByName(nameof(PyMem_Realloc), GetUnmanagedDll(PythonDLL)));
                PyMem_Free = GetDelegateForFunctionPointer<PyMem_FreeDelegate>(GetFunctionByName(nameof(PyMem_Free), GetUnmanagedDll(PythonDLL)));
                PyErr_SetString = GetDelegateForFunctionPointer<PyErr_SetStringDelegate>(GetFunctionByName(nameof(PyErr_SetString), GetUnmanagedDll(PythonDLL)));
                PyErr_SetObject = GetDelegateForFunctionPointer<PyErr_SetObjectDelegate>(GetFunctionByName(nameof(PyErr_SetObject), GetUnmanagedDll(PythonDLL)));
                PyErr_SetFromErrno = GetDelegateForFunctionPointer<PyErr_SetFromErrnoDelegate>(GetFunctionByName(nameof(PyErr_SetFromErrno), GetUnmanagedDll(PythonDLL)));
                PyErr_SetNone = GetDelegateForFunctionPointer<PyErr_SetNoneDelegate>(GetFunctionByName(nameof(PyErr_SetNone), GetUnmanagedDll(PythonDLL)));
                PyErr_ExceptionMatches = GetDelegateForFunctionPointer<PyErr_ExceptionMatchesDelegate>(GetFunctionByName(nameof(PyErr_ExceptionMatches), GetUnmanagedDll(PythonDLL)));
                PyErr_GivenExceptionMatches = GetDelegateForFunctionPointer<PyErr_GivenExceptionMatchesDelegate>(GetFunctionByName(nameof(PyErr_GivenExceptionMatches), GetUnmanagedDll(PythonDLL)));
                PyErr_NormalizeException = GetDelegateForFunctionPointer<PyErr_NormalizeExceptionDelegate>(GetFunctionByName(nameof(PyErr_NormalizeException), GetUnmanagedDll(PythonDLL)));
                PyErr_Occurred = GetDelegateForFunctionPointer<PyErr_OccurredDelegate>(GetFunctionByName(nameof(PyErr_Occurred), GetUnmanagedDll(PythonDLL)));
                PyErr_Fetch = GetDelegateForFunctionPointer<PyErr_FetchDelegate>(GetFunctionByName(nameof(PyErr_Fetch), GetUnmanagedDll(PythonDLL)));
                PyErr_Restore = GetDelegateForFunctionPointer<PyErr_RestoreDelegate>(GetFunctionByName(nameof(PyErr_Restore), GetUnmanagedDll(PythonDLL)));
                PyErr_Clear = GetDelegateForFunctionPointer<PyErr_ClearDelegate>(GetFunctionByName(nameof(PyErr_Clear), GetUnmanagedDll(PythonDLL)));
                PyErr_Print = GetDelegateForFunctionPointer<PyErr_PrintDelegate>(GetFunctionByName(nameof(PyErr_Print), GetUnmanagedDll(PythonDLL)));
                PyCell_Get = GetDelegateForFunctionPointer<PyCell_GetDelegate>(GetFunctionByName(nameof(PyCell_Get), GetUnmanagedDll(PythonDLL)));
                PyCell_Set = GetDelegateForFunctionPointer<PyCell_SetDelegate>(GetFunctionByName(nameof(PyCell_Set), GetUnmanagedDll(PythonDLL)));
                PyMethod_Self = GetDelegateForFunctionPointer<PyMethod_SelfDelegate>(GetFunctionByName(nameof(PyMethod_Self), GetUnmanagedDll(PythonDLL)));
                PyMethod_Function = GetDelegateForFunctionPointer<PyMethod_FunctionDelegate>(GetFunctionByName(nameof(PyMethod_Function), GetUnmanagedDll(PythonDLL)));
                PyMethod_New = GetDelegateForFunctionPointer<PyMethod_NewDelegate>(GetFunctionByName(nameof(PyMethod_New), GetUnmanagedDll(PythonDLL)));
                Py_AddPendingCall = GetDelegateForFunctionPointer<Py_AddPendingCallDelegate>(GetFunctionByName(nameof(Py_AddPendingCall), GetUnmanagedDll(PythonDLL)));
                Py_MakePendingCalls = GetDelegateForFunctionPointer<Py_MakePendingCallsDelegate>(GetFunctionByName(nameof(Py_MakePendingCalls), GetUnmanagedDll(PythonDLL)));
                PyObject_GetBuffer = GetDelegateForFunctionPointer<PyObject_GetBufferDelegate>(GetFunctionByName(nameof(PyObject_GetBuffer), GetUnmanagedDll(PythonDLL)));
                PyBuffer_Release = GetDelegateForFunctionPointer<PyBuffer_ReleaseDelegate>(GetFunctionByName(nameof(PyBuffer_Release), GetUnmanagedDll(PythonDLL)));
                if (Runtime.PythonVersion >= new Version(3,9))
                    PyBuffer_SizeFromFormat = GetDelegateForFunctionPointer<PyBuffer_SizeFromFormatDelegate>(GetFunctionByName(nameof(PyBuffer_SizeFromFormat), GetUnmanagedDll(PythonDLL)));
                PyBuffer_IsContiguous = GetDelegateForFunctionPointer<PyBuffer_IsContiguousDelegate>(GetFunctionByName(nameof(PyBuffer_IsContiguous), GetUnmanagedDll(PythonDLL)));
                PyBuffer_GetPointer = GetDelegateForFunctionPointer<PyBuffer_GetPointerDelegate>(GetFunctionByName(nameof(PyBuffer_GetPointer), GetUnmanagedDll(PythonDLL)));
                PyBuffer_FromContiguous = GetDelegateForFunctionPointer<PyBuffer_FromContiguousDelegate>(GetFunctionByName(nameof(PyBuffer_FromContiguous), GetUnmanagedDll(PythonDLL)));
                PyBuffer_ToContiguous = GetDelegateForFunctionPointer<PyBuffer_ToContiguousDelegate>(GetFunctionByName(nameof(PyBuffer_ToContiguous), GetUnmanagedDll(PythonDLL)));
                PyBuffer_FillContiguousStrides = GetDelegateForFunctionPointer<PyBuffer_FillContiguousStridesDelegate>(GetFunctionByName(nameof(PyBuffer_FillContiguousStrides), GetUnmanagedDll(PythonDLL)));
                PyBuffer_FillInfo = GetDelegateForFunctionPointer<PyBuffer_FillInfoDelegate>(GetFunctionByName(nameof(PyBuffer_FillInfo), GetUnmanagedDll(PythonDLL)));
            }

            static T GetDelegateForFunctionPointer<T>(IntPtr functionPointer) {
#if NETFX
                return (T)(object)Marshal.GetDelegateForFunctionPointer(functionPointer, typeof(T));
#else
                return Marshal.GetDelegateForFunctionPointer<T>(functionPointer);
#endif
            }

            static global::System.IntPtr GetUnmanagedDll(string libraryName) {
                if (string.IsNullOrEmpty(Path.GetExtension(libraryName))) {
                    libraryName =
                        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? libraryName + ".dll"
                        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? $"lib{libraryName}.so"
                        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? $"lib{libraryName}.dylib"
                        : NativeMethods.Throw<string>(new PlatformNotSupportedException());
                }
                IntPtr handle = NativeMethods.LoadLibrary(libraryName);
                if (handle == IntPtr.Zero)
                    throw new FileLoadException("Could not load " + libraryName);
                return handle;
            }

            static global::System.IntPtr GetFunctionByName(string functionName, global::System.IntPtr libraryHandle) {
                IntPtr functionPointer = NativeMethods.GetProcAddress(libraryHandle, functionName);
                if (functionPointer == IntPtr.Zero)
                    throw new EntryPointNotFoundException($"Function {functionName} was not found");
                return functionPointer;
            }

            internal static Py_IncRefDelegate Py_IncRef { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void Py_IncRefDelegate(IntPtr ob);

            internal static Py_DecRefDelegate Py_DecRef { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void Py_DecRefDelegate(IntPtr ob);

            internal static Py_InitializeDelegate Py_Initialize { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void Py_InitializeDelegate();

            internal static Py_InitializeExDelegate Py_InitializeEx { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void Py_InitializeExDelegate(int initsigs);

            internal static Py_IsInitializedDelegate Py_IsInitialized { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int Py_IsInitializedDelegate();

            internal static Py_FinalizeDelegate Py_Finalize { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void Py_FinalizeDelegate();

            internal static Py_NewInterpreterDelegate Py_NewInterpreter { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr Py_NewInterpreterDelegate();

            internal static Py_EndInterpreterDelegate Py_EndInterpreter { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void Py_EndInterpreterDelegate(IntPtr threadState);

            internal static PyThreadState_NewDelegate PyThreadState_New { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyThreadState_NewDelegate(IntPtr istate);

            internal static PyThreadState_GetDelegate PyThreadState_Get { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyThreadState_GetDelegate();

            internal static PyThread_get_key_valueDelegate PyThread_get_key_value { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyThread_get_key_valueDelegate(IntPtr key);

            internal static PyThread_get_thread_identDelegate PyThread_get_thread_ident { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyThread_get_thread_identDelegate();

            internal static PyThread_set_key_valueDelegate PyThread_set_key_value { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyThread_set_key_valueDelegate(IntPtr key, IntPtr value);

            internal static PyThreadState_SwapDelegate PyThreadState_Swap { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyThreadState_SwapDelegate(IntPtr key);

            internal static PyGILState_EnsureDelegate PyGILState_Ensure { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyGILState_EnsureDelegate();

            internal static PyGILState_ReleaseDelegate PyGILState_Release { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyGILState_ReleaseDelegate(IntPtr gs);

            internal static PyGILState_GetThisThreadStateDelegate PyGILState_GetThisThreadState { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyGILState_GetThisThreadStateDelegate();

            internal static PyGILState_CheckDelegate PyGILState_Check { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyGILState_CheckDelegate();

            internal static Py_MainDelegate Py_Main { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int Py_MainDelegate(
            int argc,
            [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StrArrayMarshaler))] string[] argv
        );

            internal static PyEval_InitThreadsDelegate PyEval_InitThreads { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyEval_InitThreadsDelegate();

            internal static PyEval_ThreadsInitializedDelegate PyEval_ThreadsInitialized { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyEval_ThreadsInitializedDelegate();

            internal static PyEval_AcquireLockDelegate PyEval_AcquireLock { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyEval_AcquireLockDelegate();

            internal static PyEval_ReleaseLockDelegate PyEval_ReleaseLock { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyEval_ReleaseLockDelegate();

            internal static PyEval_AcquireThreadDelegate PyEval_AcquireThread { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyEval_AcquireThreadDelegate(IntPtr tstate);

            internal static PyEval_ReleaseThreadDelegate PyEval_ReleaseThread { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyEval_ReleaseThreadDelegate(IntPtr tstate);

            internal static PyEval_SaveThreadDelegate PyEval_SaveThread { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyEval_SaveThreadDelegate();

            internal static PyEval_RestoreThreadDelegate PyEval_RestoreThread { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyEval_RestoreThreadDelegate(IntPtr tstate);

            internal static PyEval_GetBuiltinsDelegate PyEval_GetBuiltins { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate BorrowedReference PyEval_GetBuiltinsDelegate();

            internal static PyEval_GetGlobalsDelegate PyEval_GetGlobals { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyEval_GetGlobalsDelegate();

            internal static PyEval_GetLocalsDelegate PyEval_GetLocals { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyEval_GetLocalsDelegate();

            internal static Py_GetProgramNameDelegate Py_GetProgramName { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr Py_GetProgramNameDelegate();

            internal static Py_SetProgramNameDelegate Py_SetProgramName { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void Py_SetProgramNameDelegate(IntPtr name);

            internal static Py_GetPythonHomeDelegate Py_GetPythonHome { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr Py_GetPythonHomeDelegate();

            internal static Py_SetPythonHomeDelegate Py_SetPythonHome { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void Py_SetPythonHomeDelegate(IntPtr home);

            internal static Py_GetPathDelegate Py_GetPath { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr Py_GetPathDelegate();

            internal static Py_SetPathDelegate Py_SetPath { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void Py_SetPathDelegate(IntPtr home);

            internal static Py_GetVersionDelegate Py_GetVersion { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr Py_GetVersionDelegate();

            internal static Py_GetPlatformDelegate Py_GetPlatform { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr Py_GetPlatformDelegate();

            internal static Py_GetCopyrightDelegate Py_GetCopyright { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr Py_GetCopyrightDelegate();

            internal static Py_GetCompilerDelegate Py_GetCompiler { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr Py_GetCompilerDelegate();

            internal static Py_GetBuildInfoDelegate Py_GetBuildInfo { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr Py_GetBuildInfoDelegate();

            internal static PyRun_SimpleStringDelegate PyRun_SimpleString { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyRun_SimpleStringDelegate(string code);

            internal static PyRun_StringDelegate PyRun_String { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate NewReference PyRun_StringDelegate([MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(Utf8Marshaler))] string code, IntPtr st, IntPtr globals, IntPtr locals);

            internal static PyEval_EvalCodeDelegate PyEval_EvalCode { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyEval_EvalCodeDelegate(IntPtr co, IntPtr globals, IntPtr locals);

            internal static Py_CompileStringDelegate Py_CompileString { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr Py_CompileStringDelegate(string code, string file, IntPtr tok);

            internal static Py_CompileStringExFlagsDelegate Py_CompileStringExFlags { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr Py_CompileStringExFlagsDelegate(string str, string file, int start, IntPtr flags, int optimize);

            internal static PyImport_ExecCodeModuleDelegate PyImport_ExecCodeModule { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyImport_ExecCodeModuleDelegate(string name, IntPtr code);

            internal static PyCFunction_NewExDelegate PyCFunction_NewEx { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyCFunction_NewExDelegate(IntPtr ml, IntPtr self, IntPtr mod);

            internal static PyCFunction_CallDelegate PyCFunction_Call { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyCFunction_CallDelegate(IntPtr func, IntPtr args, IntPtr kw);

            internal static PyObject_HasAttrStringDelegate PyObject_HasAttrString { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyObject_HasAttrStringDelegate(IntPtr pointer, string name);

            internal static PyObject_GetAttrStringDelegate PyObject_GetAttrString { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyObject_GetAttrStringDelegate(IntPtr pointer, string name);

            internal static PyObject_SetAttrStringDelegate PyObject_SetAttrString { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyObject_SetAttrStringDelegate(IntPtr pointer, string name, IntPtr value);

            internal static PyObject_HasAttrDelegate PyObject_HasAttr { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyObject_HasAttrDelegate(IntPtr pointer, IntPtr name);

            internal static PyObject_GetAttrDelegate PyObject_GetAttr { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyObject_GetAttrDelegate(IntPtr pointer, IntPtr name);

            internal static PyObject_SetAttrDelegate PyObject_SetAttr { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyObject_SetAttrDelegate(IntPtr pointer, IntPtr name, IntPtr value);

            internal static PyObject_GetItemDelegate PyObject_GetItem { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate NewReference PyObject_GetItemDelegate(BorrowedReference pointer, BorrowedReference key);

            internal static PyObject_SetItemDelegate PyObject_SetItem { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyObject_SetItemDelegate(IntPtr pointer, IntPtr key, IntPtr value);

            internal static PyObject_DelItemDelegate PyObject_DelItem { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyObject_DelItemDelegate(IntPtr pointer, IntPtr key);

            internal static PyObject_GetIterDelegate PyObject_GetIter { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate NewReference PyObject_GetIterDelegate(BorrowedReference op);

            internal static PyObject_CallDelegate PyObject_Call { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyObject_CallDelegate(IntPtr pointer, IntPtr args, IntPtr kw);

            internal static PyObject_CallObjectDelegate PyObject_CallObject { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyObject_CallObjectDelegate(IntPtr pointer, IntPtr args);

            internal static PyObject_RichCompareBoolDelegate PyObject_RichCompareBool { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyObject_RichCompareBoolDelegate(IntPtr value1, IntPtr value2, int opid);

            internal static PyObject_IsInstanceDelegate PyObject_IsInstance { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyObject_IsInstanceDelegate(IntPtr ob, IntPtr type);

            internal static PyObject_IsSubclassDelegate PyObject_IsSubclass { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyObject_IsSubclassDelegate(IntPtr ob, IntPtr type);

            internal static PyCallable_CheckDelegate PyCallable_Check { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyCallable_CheckDelegate(IntPtr pointer);

            internal static PyObject_IsTrueDelegate PyObject_IsTrue { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyObject_IsTrueDelegate(IntPtr pointer);

            internal static PyObject_NotDelegate PyObject_Not { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyObject_NotDelegate(IntPtr pointer);

            internal static _PyObject_SizeDelegate _PyObject_Size { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr _PyObject_SizeDelegate(IntPtr pointer);

            internal static PyObject_HashDelegate PyObject_Hash { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyObject_HashDelegate(IntPtr op);

            internal static PyObject_ReprDelegate PyObject_Repr { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyObject_ReprDelegate(IntPtr pointer);

            internal static PyObject_StrDelegate PyObject_Str { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyObject_StrDelegate(IntPtr pointer);

            internal static PyObject_UnicodeDelegate PyObject_Unicode { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyObject_UnicodeDelegate(IntPtr pointer);

            internal static PyObject_DirDelegate PyObject_Dir { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyObject_DirDelegate(IntPtr pointer);

            internal static PyNumber_IntDelegate PyNumber_Int { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_IntDelegate(IntPtr ob);

            internal static PyNumber_LongDelegate PyNumber_Long { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_LongDelegate(IntPtr ob);

            internal static PyNumber_FloatDelegate PyNumber_Float { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_FloatDelegate(IntPtr ob);

            internal static PyNumber_CheckDelegate PyNumber_Check { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate bool PyNumber_CheckDelegate(IntPtr ob);

            internal static PyInt_FromLongDelegate PyInt_FromLong { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyInt_FromLongDelegate(IntPtr value);

            internal static PyInt_AsLongDelegate PyInt_AsLong { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyInt_AsLongDelegate(IntPtr value);

            internal static PyInt_FromStringDelegate PyInt_FromString { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyInt_FromStringDelegate(string value, IntPtr end, int radix);

            internal static PyLong_FromLongDelegate PyLong_FromLong { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyLong_FromLongDelegate(long value);

            internal static PyLong_FromUnsignedLong32Delegate PyLong_FromUnsignedLong32 { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyLong_FromUnsignedLong32Delegate(uint value);

            internal static PyLong_FromUnsignedLong64Delegate PyLong_FromUnsignedLong64 { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyLong_FromUnsignedLong64Delegate(ulong value);

            internal static PyLong_FromUnsignedLongDelegate PyLong_FromUnsignedLong { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyLong_FromUnsignedLongDelegate(uint value);

            internal static PyLong_FromDoubleDelegate PyLong_FromDouble { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyLong_FromDoubleDelegate(double value);

            internal static PyLong_FromLongLongDelegate PyLong_FromLongLong { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyLong_FromLongLongDelegate(long value);

            internal static PyLong_FromUnsignedLongLongDelegate PyLong_FromUnsignedLongLong { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyLong_FromUnsignedLongLongDelegate(ulong value);

            internal static PyLong_FromStringDelegate PyLong_FromString { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyLong_FromStringDelegate(string value, IntPtr end, int radix);

            internal static PyLong_AsLongDelegate PyLong_AsLong { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyLong_AsLongDelegate(IntPtr value);

            internal static PyLong_AsUnsignedLong32Delegate PyLong_AsUnsignedLong32 { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate uint PyLong_AsUnsignedLong32Delegate(IntPtr value);

            internal static PyLong_AsUnsignedLong64Delegate PyLong_AsUnsignedLong64 { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate ulong PyLong_AsUnsignedLong64Delegate(IntPtr value);

            internal static PyLong_AsUnsignedLongDelegate PyLong_AsUnsignedLong { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate uint PyLong_AsUnsignedLongDelegate(IntPtr value);

            internal static PyLong_AsLongLongDelegate PyLong_AsLongLong { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate long PyLong_AsLongLongDelegate(IntPtr value);

            internal static PyLong_AsUnsignedLongLongDelegate PyLong_AsUnsignedLongLong { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate ulong PyLong_AsUnsignedLongLongDelegate(IntPtr value);

            internal static PyFloat_FromDoubleDelegate PyFloat_FromDouble { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyFloat_FromDoubleDelegate(double value);

            internal static PyFloat_FromStringDelegate PyFloat_FromString { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyFloat_FromStringDelegate(IntPtr value, IntPtr junk);

            internal static PyFloat_AsDoubleDelegate PyFloat_AsDouble { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate double PyFloat_AsDoubleDelegate(IntPtr ob);

            internal static PyNumber_AddDelegate PyNumber_Add { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_AddDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_SubtractDelegate PyNumber_Subtract { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_SubtractDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_MultiplyDelegate PyNumber_Multiply { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_MultiplyDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_TrueDivideDelegate PyNumber_TrueDivide { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_TrueDivideDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_AndDelegate PyNumber_And { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_AndDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_XorDelegate PyNumber_Xor { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_XorDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_OrDelegate PyNumber_Or { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_OrDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_LshiftDelegate PyNumber_Lshift { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_LshiftDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_RshiftDelegate PyNumber_Rshift { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_RshiftDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_PowerDelegate PyNumber_Power { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_PowerDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_RemainderDelegate PyNumber_Remainder { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_RemainderDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_InPlaceAddDelegate PyNumber_InPlaceAdd { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_InPlaceAddDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_InPlaceSubtractDelegate PyNumber_InPlaceSubtract { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_InPlaceSubtractDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_InPlaceMultiplyDelegate PyNumber_InPlaceMultiply { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_InPlaceMultiplyDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_InPlaceTrueDivideDelegate PyNumber_InPlaceTrueDivide { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_InPlaceTrueDivideDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_InPlaceAndDelegate PyNumber_InPlaceAnd { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_InPlaceAndDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_InPlaceXorDelegate PyNumber_InPlaceXor { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_InPlaceXorDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_InPlaceOrDelegate PyNumber_InPlaceOr { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_InPlaceOrDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_InPlaceLshiftDelegate PyNumber_InPlaceLshift { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_InPlaceLshiftDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_InPlaceRshiftDelegate PyNumber_InPlaceRshift { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_InPlaceRshiftDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_InPlacePowerDelegate PyNumber_InPlacePower { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_InPlacePowerDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_InPlaceRemainderDelegate PyNumber_InPlaceRemainder { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_InPlaceRemainderDelegate(IntPtr o1, IntPtr o2);

            internal static PyNumber_NegativeDelegate PyNumber_Negative { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_NegativeDelegate(IntPtr o1);

            internal static PyNumber_PositiveDelegate PyNumber_Positive { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_PositiveDelegate(IntPtr o1);

            internal static PyNumber_InvertDelegate PyNumber_Invert { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyNumber_InvertDelegate(IntPtr o1);

            internal static PySequence_CheckDelegate PySequence_Check { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate bool PySequence_CheckDelegate(IntPtr pointer);

            internal static PySequence_GetItemDelegate PySequence_GetItem { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PySequence_GetItemDelegate(IntPtr pointer, IntPtr index);

            internal static PySequence_SetItemDelegate PySequence_SetItem { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PySequence_SetItemDelegate(IntPtr pointer, IntPtr index, IntPtr value);

            internal static PySequence_DelItemDelegate PySequence_DelItem { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PySequence_DelItemDelegate(IntPtr pointer, IntPtr index);

            internal static PySequence_GetSliceDelegate PySequence_GetSlice { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PySequence_GetSliceDelegate(IntPtr pointer, IntPtr i1, IntPtr i2);

            internal static PySequence_SetSliceDelegate PySequence_SetSlice { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PySequence_SetSliceDelegate(IntPtr pointer, IntPtr i1, IntPtr i2, IntPtr v);

            internal static PySequence_DelSliceDelegate PySequence_DelSlice { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PySequence_DelSliceDelegate(IntPtr pointer, IntPtr i1, IntPtr i2);

            internal static _PySequence_SizeDelegate _PySequence_Size { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr _PySequence_SizeDelegate(IntPtr pointer);

            internal static PySequence_ContainsDelegate PySequence_Contains { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PySequence_ContainsDelegate(IntPtr pointer, IntPtr item);

            internal static PySequence_ConcatDelegate PySequence_Concat { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PySequence_ConcatDelegate(IntPtr pointer, IntPtr other);

            internal static PySequence_RepeatDelegate PySequence_Repeat { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PySequence_RepeatDelegate(IntPtr pointer, IntPtr count);

            internal static PySequence_IndexDelegate PySequence_Index { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PySequence_IndexDelegate(IntPtr pointer, IntPtr item);

            internal static _PySequence_CountDelegate _PySequence_Count { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr _PySequence_CountDelegate(IntPtr pointer, IntPtr value);

            internal static PySequence_TupleDelegate PySequence_Tuple { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PySequence_TupleDelegate(IntPtr pointer);

            internal static PySequence_ListDelegate PySequence_List { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PySequence_ListDelegate(IntPtr pointer);

            internal static PyBytes_FromStringDelegate PyBytes_FromString { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyBytes_FromStringDelegate(string op);

            internal static _PyBytes_SizeDelegate _PyBytes_Size { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr _PyBytes_SizeDelegate(IntPtr op);

            internal static _PyString_FromStringAndSizeDelegate _PyString_FromStringAndSize { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr _PyString_FromStringAndSizeDelegate(
            [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(Utf8Marshaler))] string value,
            IntPtr size
        );

            internal static PyUnicode_FromStringAndSizeDelegate PyUnicode_FromStringAndSize { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyUnicode_FromStringAndSizeDelegate(IntPtr value, IntPtr size);

            internal static PyUnicode_AsUTF8Delegate PyUnicode_AsUTF8 { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyUnicode_AsUTF8Delegate(IntPtr unicode);

            internal static PyUnicode_FromObjectDelegate PyUnicode_FromObject { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyUnicode_FromObjectDelegate(IntPtr ob);

            internal static PyUnicode_FromEncodedObjectDelegate PyUnicode_FromEncodedObject { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyUnicode_FromEncodedObjectDelegate(IntPtr ob, IntPtr enc, IntPtr err);

            internal static PyUnicode_FromKindAndDataDelegate PyUnicode_FromKindAndData { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyUnicode_FromKindAndDataDelegate(
            int kind,
            [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(UcsMarshaler))] string s,
            IntPtr size
        );

            internal static _PyUnicode_GetSizeDelegate _PyUnicode_GetSize { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr _PyUnicode_GetSizeDelegate(IntPtr ob);

            internal static PyUnicode_AsUnicodeDelegate PyUnicode_AsUnicode { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyUnicode_AsUnicodeDelegate(IntPtr ob);

            internal static PyUnicode_FromOrdinalDelegate PyUnicode_FromOrdinal { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyUnicode_FromOrdinalDelegate(int c);

            internal static PyDict_NewDelegate PyDict_New { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyDict_NewDelegate();

            internal static PyDictProxy_NewDelegate PyDictProxy_New { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyDictProxy_NewDelegate(IntPtr dict);

            internal static PyDict_GetItemDelegate PyDict_GetItem { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyDict_GetItemDelegate(IntPtr pointer, IntPtr key);

            internal static PyDict_GetItemStringDelegate PyDict_GetItemString { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyDict_GetItemStringDelegate(IntPtr pointer, string key);

            internal static PyDict_SetItemDelegate PyDict_SetItem { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyDict_SetItemDelegate(BorrowedReference pointer, BorrowedReference key, BorrowedReference value);

            internal static PyDict_SetItemStringDelegate PyDict_SetItemString { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyDict_SetItemStringDelegate(IntPtr pointer, string key, IntPtr value);

            internal static PyDict_DelItemDelegate PyDict_DelItem { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyDict_DelItemDelegate(IntPtr pointer, IntPtr key);

            internal static PyDict_DelItemStringDelegate PyDict_DelItemString { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyDict_DelItemStringDelegate(IntPtr pointer, string key);

            internal static PyMapping_HasKeyDelegate PyMapping_HasKey { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyMapping_HasKeyDelegate(IntPtr pointer, IntPtr key);

            internal static PyDict_KeysDelegate PyDict_Keys { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyDict_KeysDelegate(IntPtr pointer);

            internal static PyDict_ValuesDelegate PyDict_Values { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyDict_ValuesDelegate(IntPtr pointer);

            internal static PyDict_ItemsDelegate PyDict_Items { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate NewReference PyDict_ItemsDelegate(IntPtr pointer);

            internal static PyDict_CopyDelegate PyDict_Copy { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyDict_CopyDelegate(IntPtr pointer);

            internal static PyDict_UpdateDelegate PyDict_Update { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyDict_UpdateDelegate(IntPtr pointer, IntPtr other);

            internal static PyDict_ClearDelegate PyDict_Clear { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyDict_ClearDelegate(IntPtr pointer);

            internal static _PyDict_SizeDelegate _PyDict_Size { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr _PyDict_SizeDelegate(IntPtr pointer);

            internal static PyList_NewDelegate PyList_New { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyList_NewDelegate(IntPtr size);

            internal static PyList_AsTupleDelegate PyList_AsTuple { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyList_AsTupleDelegate(IntPtr pointer);

            internal static PyList_GetItemDelegate PyList_GetItem { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate BorrowedReference PyList_GetItemDelegate(IntPtr pointer, IntPtr index);

            internal static PyList_SetItemDelegate PyList_SetItem { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyList_SetItemDelegate(IntPtr pointer, IntPtr index, IntPtr value);

            internal static PyList_InsertDelegate PyList_Insert { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyList_InsertDelegate(BorrowedReference pointer, IntPtr index, IntPtr value);

            internal static PyList_AppendDelegate PyList_Append { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyList_AppendDelegate(BorrowedReference pointer, IntPtr value);

            internal static PyList_ReverseDelegate PyList_Reverse { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyList_ReverseDelegate(BorrowedReference pointer);

            internal static PyList_SortDelegate PyList_Sort { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyList_SortDelegate(BorrowedReference pointer);

            internal static PyList_GetSliceDelegate PyList_GetSlice { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyList_GetSliceDelegate(IntPtr pointer, IntPtr start, IntPtr end);

            internal static PyList_SetSliceDelegate PyList_SetSlice { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyList_SetSliceDelegate(IntPtr pointer, IntPtr start, IntPtr end, IntPtr value);

            internal static _PyList_SizeDelegate _PyList_Size { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr _PyList_SizeDelegate(IntPtr pointer);

            internal static PyTuple_NewDelegate PyTuple_New { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyTuple_NewDelegate(IntPtr size);

            internal static PyTuple_GetItemDelegate PyTuple_GetItem { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyTuple_GetItemDelegate(IntPtr pointer, IntPtr index);

            internal static PyTuple_SetItemDelegate PyTuple_SetItem { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyTuple_SetItemDelegate(IntPtr pointer, IntPtr index, IntPtr value);

            internal static PyTuple_GetSliceDelegate PyTuple_GetSlice { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyTuple_GetSliceDelegate(IntPtr pointer, IntPtr start, IntPtr end);

            internal static _PyTuple_SizeDelegate _PyTuple_Size { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr _PyTuple_SizeDelegate(IntPtr pointer);

            internal static PyIter_NextDelegate PyIter_Next { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate NewReference PyIter_NextDelegate(BorrowedReference pointer);

            internal static PyModule_NewDelegate PyModule_New { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyModule_NewDelegate(string name);

            internal static PyModule_GetNameDelegate PyModule_GetName { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate string PyModule_GetNameDelegate(IntPtr module);

            internal static PyModule_GetDictDelegate PyModule_GetDict { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyModule_GetDictDelegate(IntPtr module);

            internal static PyModule_GetFilenameDelegate PyModule_GetFilename { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate string PyModule_GetFilenameDelegate(IntPtr module);

            internal static PyModule_Create2Delegate PyModule_Create2 { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyModule_Create2Delegate(IntPtr module, int apiver);

            internal static PyImport_ImportDelegate PyImport_Import { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyImport_ImportDelegate(IntPtr name);

            internal static PyImport_ImportModuleDelegate PyImport_ImportModule { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyImport_ImportModuleDelegate(string name);

            internal static PyImport_ReloadModuleDelegate PyImport_ReloadModule { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyImport_ReloadModuleDelegate(IntPtr module);

            internal static PyImport_AddModuleDelegate PyImport_AddModule { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate BorrowedReference PyImport_AddModuleDelegate(string name);

            internal static PyImport_GetModuleDictDelegate PyImport_GetModuleDict { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyImport_GetModuleDictDelegate();

            internal static PySys_SetArgvExDelegate PySys_SetArgvEx { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PySys_SetArgvExDelegate(
            int argc,
            [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StrArrayMarshaler))] string[] argv,
            int updatepath
        );

            internal static PySys_GetObjectDelegate PySys_GetObject { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PySys_GetObjectDelegate(string name);

            internal static PySys_SetObjectDelegate PySys_SetObject { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PySys_SetObjectDelegate(string name, IntPtr ob);

            internal static PyType_ModifiedDelegate PyType_Modified { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyType_ModifiedDelegate(IntPtr type);

            internal static PyType_IsSubtypeDelegate PyType_IsSubtype { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate bool PyType_IsSubtypeDelegate(IntPtr t1, IntPtr t2);

            internal static PyType_GenericNewDelegate PyType_GenericNew { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyType_GenericNewDelegate(IntPtr type, IntPtr args, IntPtr kw);

            internal static PyType_GenericAllocDelegate PyType_GenericAlloc { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyType_GenericAllocDelegate(IntPtr type, IntPtr n);

            internal static PyType_ReadyDelegate PyType_Ready { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyType_ReadyDelegate(IntPtr type);

            internal static _PyType_LookupDelegate _PyType_Lookup { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr _PyType_LookupDelegate(IntPtr type, IntPtr name);

            internal static PyObject_GenericGetAttrDelegate PyObject_GenericGetAttr { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyObject_GenericGetAttrDelegate(IntPtr obj, IntPtr name);

            internal static PyObject_GenericSetAttrDelegate PyObject_GenericSetAttr { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyObject_GenericSetAttrDelegate(IntPtr obj, IntPtr name, IntPtr value);

            internal static _PyObject_GetDictPtrDelegate _PyObject_GetDictPtr { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr _PyObject_GetDictPtrDelegate(IntPtr obj);

            internal static PyObject_GC_DelDelegate PyObject_GC_Del { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyObject_GC_DelDelegate(IntPtr tp);

            internal static PyObject_GC_TrackDelegate PyObject_GC_Track { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyObject_GC_TrackDelegate(IntPtr tp);

            internal static PyObject_GC_UnTrackDelegate PyObject_GC_UnTrack { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyObject_GC_UnTrackDelegate(IntPtr tp);

            internal static PyMem_MallocDelegate PyMem_Malloc { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyMem_MallocDelegate(IntPtr size);

            internal static PyMem_ReallocDelegate PyMem_Realloc { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyMem_ReallocDelegate(IntPtr ptr, IntPtr size);

            internal static PyMem_FreeDelegate PyMem_Free { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyMem_FreeDelegate(IntPtr ptr);

            internal static PyErr_SetStringDelegate PyErr_SetString { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyErr_SetStringDelegate(IntPtr ob, string message);

            internal static PyErr_SetObjectDelegate PyErr_SetObject { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyErr_SetObjectDelegate(IntPtr ob, IntPtr message);

            internal static PyErr_SetFromErrnoDelegate PyErr_SetFromErrno { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyErr_SetFromErrnoDelegate(IntPtr ob);

            internal static PyErr_SetNoneDelegate PyErr_SetNone { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyErr_SetNoneDelegate(IntPtr ob);

            internal static PyErr_ExceptionMatchesDelegate PyErr_ExceptionMatches { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyErr_ExceptionMatchesDelegate(IntPtr exception);

            internal static PyErr_GivenExceptionMatchesDelegate PyErr_GivenExceptionMatches { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyErr_GivenExceptionMatchesDelegate(IntPtr ob, IntPtr val);

            internal static PyErr_NormalizeExceptionDelegate PyErr_NormalizeException { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyErr_NormalizeExceptionDelegate(IntPtr ob, IntPtr val, IntPtr tb);

            internal static PyErr_OccurredDelegate PyErr_Occurred { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyErr_OccurredDelegate();

            internal static PyErr_FetchDelegate PyErr_Fetch { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyErr_FetchDelegate(out NewReference ob, out NewReference val, out NewReference tb);

            internal static PyErr_RestoreDelegate PyErr_Restore { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyErr_RestoreDelegate(IntPtr ob, IntPtr val, IntPtr tb);

            internal static PyErr_ClearDelegate PyErr_Clear { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyErr_ClearDelegate();

            internal static PyErr_PrintDelegate PyErr_Print { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyErr_PrintDelegate();

            //====================================================================
            // Cell API
            //====================================================================
            internal static PyCell_GetDelegate PyCell_Get { get; }
            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate NewReference PyCell_GetDelegate(BorrowedReference cell);

            internal static PyCell_SetDelegate PyCell_Set { get; }
            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyCell_SetDelegate(BorrowedReference cell, BorrowedReference value);

            internal static PyMethod_SelfDelegate PyMethod_Self { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyMethod_SelfDelegate(IntPtr ob);

            internal static PyMethod_FunctionDelegate PyMethod_Function { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyMethod_FunctionDelegate(IntPtr ob);

            internal static PyMethod_NewDelegate PyMethod_New { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyMethod_NewDelegate(IntPtr func, IntPtr self);

            internal static Py_AddPendingCallDelegate Py_AddPendingCall { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int Py_AddPendingCallDelegate(IntPtr func, IntPtr arg);

            internal static Py_MakePendingCallsDelegate Py_MakePendingCalls { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int Py_MakePendingCallsDelegate();
            // end of PY3

            enum Py2 { }

            internal static PyObject_GetBufferDelegate PyObject_GetBuffer { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyObject_GetBufferDelegate(BorrowedReference exporter, out Py_buffer view, PyBUF flags);

            internal static PyBuffer_ReleaseDelegate PyBuffer_Release { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate void PyBuffer_ReleaseDelegate(ref Py_buffer view);

            internal static PyBuffer_SizeFromFormatDelegate PyBuffer_SizeFromFormat { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            internal delegate IntPtr PyBuffer_SizeFromFormatDelegate([MarshalAs(UnmanagedType.LPStr)] string format);

            internal static PyBuffer_IsContiguousDelegate PyBuffer_IsContiguous { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyBuffer_IsContiguousDelegate(ref Py_buffer view, BufferOrderStyle order);

            internal static PyBuffer_GetPointerDelegate PyBuffer_GetPointer { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate IntPtr PyBuffer_GetPointerDelegate(ref Py_buffer view, IntPtr[] indices);

            internal static PyBuffer_FromContiguousDelegate PyBuffer_FromContiguous { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyBuffer_FromContiguousDelegate(ref Py_buffer view, IntPtr buf, IntPtr len, BufferOrderStyle fort);

            internal static PyBuffer_ToContiguousDelegate PyBuffer_ToContiguous { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyBuffer_ToContiguousDelegate(IntPtr buf, ref Py_buffer src, IntPtr len, BufferOrderStyle order);

            internal static PyBuffer_FillContiguousStridesDelegate PyBuffer_FillContiguousStrides { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            public delegate void PyBuffer_FillContiguousStridesDelegate(int ndims, IntPtr[] shape, IntPtr[] strides, int itemsize, BufferOrderStyle order);

            internal static PyBuffer_FillInfoDelegate PyBuffer_FillInfo { get; }

            [global::System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute(CallingConvention.Cdecl)]
            internal delegate int PyBuffer_FillInfoDelegate(ref Py_buffer view, BorrowedReference exporter, IntPtr buf, IntPtr len, bool @readonly, PyBUF flags);
        }
    }


    class PyReferenceCollection
    {
        private List<KeyValuePair<IntPtr, Action>> _actions = new List<KeyValuePair<IntPtr, Action>>();

        /// <summary>
        /// Record obj's address to release the obj in the future,
        /// obj must alive before calling Release.
        /// </summary>
        public void Add(IntPtr ob, Action onRelease)
        {
            _actions.Add(new KeyValuePair<IntPtr, Action>(ob, onRelease));
        }

        public void Release()
        {
            foreach (var item in _actions)
            {
                Runtime.XDecref(item.Key);
                item.Value?.Invoke();
            }
            _actions.Clear();
        }
    }
#if NETFX
    public static class RuntimeInformation {
      public static OSPlatform OSPlatform { get; set; } = Environment.OSVersion.Platform == PlatformID.Unix ? OSPlatform.Linux : OSPlatform.Windows;
      public static bool IsOSPlatform(OSPlatform platform){return platform == OSPlatform;}
    }

    public enum OSPlatform{ Windows, Linux, OSX }
#endif
}
