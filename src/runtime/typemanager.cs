using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Python.Runtime.Platform;
using Python.Runtime.Slots;

namespace Python.Runtime
{
    /// <summary>
    /// The TypeManager class is responsible for building binary-compatible
    /// Python type objects that are implemented in managed code.
    /// </summary>
    internal static class TypeManager
    {
        private static BindingFlags tbFlags;
        private static Dictionary<Type, IntPtr> cache;
        private static IPythonBaseTypeProvider pythonBaseTypeProvider;

        static TypeManager()
        {
            tbFlags = BindingFlags.Public | BindingFlags.Static;
            cache = new Dictionary<Type, IntPtr>(128);
        }

        public static void Reset()
        {
            cache = new Dictionary<Type, IntPtr>(128);
            pythonBaseTypeProvider = PythonEngine.InteropConfiguration.pythonBaseTypeProviders;
        }

        /// <summary>
        /// Given a managed Type derived from ExtensionType, get the handle to
        /// a Python type object that delegates its implementation to the Type
        /// object. These Python type instances are used to implement internal
        /// descriptor and utility types like ModuleObject, PropertyObject, etc.
        /// </summary>
        internal static BorrowedReference GetTypeHandle(Type type)
        {
            // Note that these types are cached with a refcount of 1, so they
            // effectively exist until the CPython runtime is finalized.
            IntPtr handle;
            cache.TryGetValue(type, out handle);
            if (handle != IntPtr.Zero)
            {
                return new BorrowedReference(handle);
            }
            handle = CreateType(type);
            cache[type] = handle;
            return new BorrowedReference(handle);
        }


        /// <summary>
        /// Get the handle of a Python type that reflects the given CLR type.
        /// The given ManagedType instance is a managed object that implements
        /// the appropriate semantics in Python for the reflected managed type.
        /// </summary>
        internal static BorrowedReference GetTypeHandle(ManagedType obj, Type type)
        {
            IntPtr handle;
            cache.TryGetValue(type, out handle);
            if (handle != IntPtr.Zero)
            {
                return new BorrowedReference(handle);
            }
            handle = CreateType(obj, type);
            cache[type] = handle;
            return new BorrowedReference(handle);
        }


        /// <summary>
        /// The following CreateType implementations do the necessary work to
        /// create Python types to represent managed extension types, reflected
        /// types, subclasses of reflected types and the managed metatype. The
        /// dance is slightly different for each kind of type due to different
        /// behavior needed and the desire to have the existing Python runtime
        /// do as much of the allocation and initialization work as possible.
        /// </summary>
        internal static IntPtr CreateType(Type impl)
        {
            IntPtr type = AllocateTypeObject(impl.Name);
            int ob_size = ObjectOffset.Size(type);

            // Set tp_basicsize to the size of our managed instance objects.
            Marshal.WriteIntPtr(type, TypeOffset.tp_basicsize, (IntPtr)ob_size);

            var offset = (IntPtr)ObjectOffset.TypeDictOffset();
            Marshal.WriteIntPtr(type, TypeOffset.tp_dictoffset, offset);

            InitializeSlots(type, impl);

            var flags = TypeFlags.Default | TypeFlags.Managed |
                        TypeFlags.HeapType | TypeFlags.HaveGC;
            Util.WriteCLong(type, TypeOffset.tp_flags, (long)flags);

            if (Runtime.PyType_Ready(type) != 0)
                throw new PythonEngineException("Can not create type", PythonException.FromPyErr());

            IntPtr dict = Marshal.ReadIntPtr(type, TypeOffset.tp_dict);
            IntPtr mod = Runtime.PyString_FromString("CLR");
            Runtime.PyDict_SetItemString(dict, "__module__", mod);

            InitMethods(type, impl);

            return type;
        }

        internal static IntPtr CreateType(ManagedType impl, Type clrType)
        {
            string name = GetPythonTypeName(clrType);

            int ob_size;

            IntPtr type = AllocateTypeObject(name, typeType: Runtime.PyCLRMetaType);

            Marshal.WriteIntPtr(type, TypeOffset.ob_type, Runtime.PyCLRMetaType);
            Runtime.XIncref(Runtime.PyCLRMetaType);

            // Hide the gchandle of the implementation in a magic type slot.
            GCHandle gc = GCHandle.Alloc(impl);
            Marshal.WriteIntPtr(type, TypeOffset.magic(), (IntPtr)gc);

            // add a __len__ slot for inheritors of ICollection and ICollection<>
            if (typeof(System.Collections.ICollection).IsAssignableFrom(clrType) || clrType.GetInterfaces().Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(ICollection<>)))
            {
                InitializeSlot(type, TypeOffset.mp_length, typeof(mp_length_slot).GetMethod(nameof(mp_length_slot.mp_length)));
            }

            // we want to do this after the slot stuff above in case the class itself implements a slot method
            InitializeSlots(type, impl.GetType());

            if (typeof(IGetAttr).IsAssignableFrom(clrType)) {
                InitializeSlot(type, TypeOffset.tp_getattro, typeof(SlotOverrides).GetMethod(nameof(SlotOverrides.tp_getattro)));
            }

            int extraTypeDataOffset;
            try
            {
                using PyTuple baseTuple = GetBaseTypeTuple(clrType);
                Debug.Assert(baseTuple.Length() > 0);
                IntPtr primaryBase = baseTuple[0].Reference.DangerousIncRefOrNull();
                Marshal.WriteIntPtr(type, TypeOffset.tp_base, primaryBase);

                if (baseTuple.Length() > 1) {
                    Marshal.WriteIntPtr(type, TypeOffset.tp_bases, baseTuple.Reference.DangerousIncRefOrNull());
                }

                ob_size = checked((int)Marshal.ReadIntPtr(primaryBase, TypeOffset.tp_basicsize));
                void InheritOrAllocate(int typeField) {
                    int value = Marshal.ReadInt32(primaryBase, typeField);
                    if (value == 0) {
                        Marshal.WriteIntPtr(type, typeField, new IntPtr(ob_size));
                        ob_size += IntPtr.Size;
                    } else {
                        Marshal.WriteIntPtr(type, typeField, new IntPtr(value));
                    }
                }

                InheritOrAllocate(TypeOffset.tp_dictoffset);
                InheritOrAllocate(TypeOffset.tp_weaklistoffset);

                if (!ManagedType.IsManagedType(primaryBase)) {
                    // base type is a Python type, so we must allocate additional space for GC handle
                    extraTypeDataOffset = ob_size;
                    ObjectOffset.ClrGcHandleOffsetAssertSanity(extraTypeDataOffset);
                    ob_size += MetaType.ExtraTypeDataSize;
                } else {
                    extraTypeDataOffset = checked((int)Marshal.ReadIntPtr(primaryBase, TypeOffset.clr_gchandle_offset));
                    ObjectOffset.ClrGcHandleOffsetAssertSanity(extraTypeDataOffset);
                }
            }
            catch (Exception error)
            {
                Exceptions.SetError(error);
                return IntPtr.Zero;
            }

            ObjectOffset.ClrGcHandleOffsetAssertSanity(extraTypeDataOffset);

            Marshal.WriteIntPtr(type, TypeOffset.tp_basicsize, (IntPtr)ob_size);
            Marshal.WriteIntPtr(type, TypeOffset.tp_itemsize, IntPtr.Zero);
            Marshal.WriteIntPtr(type, TypeOffset.clr_gchandle_offset, (IntPtr)extraTypeDataOffset);

            var flags = TypeFlags.Default;
            flags |= TypeFlags.Managed;
            flags |= TypeFlags.HeapType;
            flags |= TypeFlags.BaseType;
            flags |= TypeFlags.HaveGC;
            Util.WriteCLong(type, TypeOffset.tp_flags, (long)flags);

            // Leverage followup initialization from the Python runtime. Note
            // that the type of the new type must PyType_Type at the time we
            // call this, else PyType_Ready will skip some slot initialization.

            if (Runtime.PyType_Ready(type) != 0)
            {
                gc.Free();
                return IntPtr.Zero;
            }

            Debug.Assert(extraTypeDataOffset > Marshal.ReadInt32(type, TypeOffset.tp_dictoffset));

            IntPtr dict = Marshal.ReadIntPtr(type, TypeOffset.tp_dict);
            string mn = clrType.Namespace ?? "";
            IntPtr mod = Runtime.PyString_FromString(mn);
            Runtime.PyDict_SetItemString(dict, "__module__", mod);

            // Set the handle attributes on the implementing instance.
            impl.tpHandle = Runtime.PyCLRMetaType;
            impl.gcHandle = gc;
            impl.pyHandle = type;

            //DebugUtil.DumpType(type);

            return type;
        }

        static string GetPythonTypeName(Type clrType)
        {
            var result = new System.Text.StringBuilder();
            GetPythonTypeName(clrType, target: result);
            return result.ToString();
        }

        static void GetPythonTypeName(Type clrType, System.Text.StringBuilder target)
        {
            if (clrType.IsGenericType)
            {
                string fullName = clrType.GetGenericTypeDefinition().FullName;
                int argCountIndex = fullName.LastIndexOf('`');
                if (argCountIndex >= 0)
                {
                    string nonGenericFullName = fullName.Substring(0, argCountIndex);
                    string nonGenericName = CleanupFullName(nonGenericFullName);
                    target.Append(nonGenericName);

                    var arguments = clrType.GetGenericArguments();
                    target.Append('<');
                    for (int argIndex = 0; argIndex < arguments.Length; argIndex++)
                    {
                        if (argIndex != 0)
                        {
                            target.Append(',');
                        }

                        GetPythonTypeName(arguments[argIndex], target);
                    }

                    target.Append('>');
                    return;
                }
            }

            string name = CleanupFullName(clrType.FullName);
            target.Append(name);
        }

        static string CleanupFullName(string fullTypeName)
        {
            // Cleanup the type name to get rid of funny nested type names.
            string name = "CLR." + fullTypeName;
            int i = name.LastIndexOf('+');
            if (i > -1)
            {
                name = name.Substring(i + 1);
            }

            i = name.LastIndexOf('.');
            if (i > -1)
            {
                name = name.Substring(i + 1);
            }

            return name;
        }

        static PyTuple GetBaseTypeTuple(Type clrType)
        {
            var bases = pythonBaseTypeProvider
                .GetBaseTypes(clrType, new PyObject[0])
                ?.ToArray();
            if (bases is null || bases.Length == 0)
            {
                throw new InvalidOperationException("At least one base type must be specified");
            }

            if (bases.Any(@base => !PyType.IsTypeType(@base)))
            {
                throw new InvalidOperationException("Entries in base types must be Python types");
            }

            return new PyTuple(bases);
        }

        internal static BorrowedReference CreateSubType(IntPtr py_name, IntPtr py_base_type, IntPtr py_dict)
        {
            // Utility to create a subtype of a managed type with the ability for the
            // a python subtype able to override the managed implementation
            string name = Runtime.GetManagedString(py_name);

            // the derived class can have class attributes __assembly__ and __module__ which
            // control the name of the assembly and module the new type is created in.
            object assembly = null;
            object namespaceStr = null;

            var disposeList = new List<PyObject>();
            try
            {
                var assemblyKey = new PyObject(Converter.ToPython("__assembly__"));
                disposeList.Add(assemblyKey);
                if (0 != Runtime.PyMapping_HasKey(py_dict, assemblyKey.Handle))
                {
                    var pyAssembly = new PyObject(Runtime.PyDict_GetItem(py_dict, assemblyKey.Handle));
                    Runtime.XIncref(pyAssembly.Handle);
                    disposeList.Add(pyAssembly);
                    if (!Converter.ToManagedValue(pyAssembly.Handle, typeof(string), out assembly, false))
                    {
                        throw new InvalidCastException("Couldn't convert __assembly__ value to string");
                    }
                }

                var namespaceKey = new PyObject(Converter.ToPython("__namespace__"));
                disposeList.Add(namespaceKey);
                if (0 != Runtime.PyMapping_HasKey(py_dict, namespaceKey.Handle))
                {
                    var pyNamespace = new PyObject(Runtime.PyDict_GetItem(py_dict, namespaceKey.Handle));
                    Runtime.XIncref(pyNamespace.Handle);
                    disposeList.Add(pyNamespace);
                    if (!Converter.ToManagedValue(pyNamespace.Handle, typeof(string), out namespaceStr, false))
                    {
                        throw new InvalidCastException("Couldn't convert __namespace__ value to string");
                    }
                }
            }
            finally
            {
                foreach (PyObject o in disposeList)
                {
                    o.Dispose();
                }
            }

            // create the new managed type subclassing the base managed type
            var baseClass = ManagedType.GetManagedObject(py_base_type) as ClassBase;
            if (null == baseClass)
            {
                return new BorrowedReference(Exceptions.RaiseTypeError("invalid base class, expected CLR class type"));
            }

            try
            {
                Type subType = ClassDerivedObject.CreateDerivedType(name,
                    baseClass.type,
                    py_dict,
                    (string)namespaceStr,
                    (string)assembly);

                // create the new ManagedType and python type
                ClassBase subClass = ClassManager.GetClass(subType);
                BorrowedReference py_type = GetTypeHandle(subClass, subType);
                if (py_type.IsNull)
                {
                    return new BorrowedReference();
                }

                // by default the class dict will have all the C# methods in it, but as this is a
                // derived class we want the python overrides in there instead if they exist.
                IntPtr cls_dict = Marshal.ReadIntPtr(py_type.DangerousGetAddress(), TypeOffset.tp_dict);
                Runtime.PyDict_Update(cls_dict, py_dict);

                // Update the __classcell__ if it exists
                var cell = new BorrowedReference(Runtime.PyDict_GetItemString(cls_dict, "__classcell__"));
                if (!cell.IsNull)
                {
                    int r = Runtime.PyCell_Set(cell, py_type);
                    if (r == 0) return new BorrowedReference();
                    r = Runtime.PyDict_DelItemString(cls_dict, "__classcell__");
                    if (r == 0) return new BorrowedReference();
                }

                return py_type;
            }
            catch (Exception e)
            {
                return new BorrowedReference(Exceptions.RaiseTypeError(e.Message));
            }
        }

        internal static IntPtr WriteMethodDef(IntPtr mdef, IntPtr name, IntPtr func, int flags, IntPtr doc)
        {
            Marshal.WriteIntPtr(mdef, name);
            Marshal.WriteIntPtr(mdef, 1 * IntPtr.Size, func);
            Marshal.WriteInt32(mdef, 2 * IntPtr.Size, flags);
            Marshal.WriteIntPtr(mdef, 3 * IntPtr.Size, doc);
            return mdef + 4 * IntPtr.Size;
        }

        internal static IntPtr WriteMethodDef(IntPtr mdef, string name, IntPtr func, int flags = 0x0001,
            string doc = null)
        {
            IntPtr namePtr = Marshal.StringToHGlobalAnsi(name);
            IntPtr docPtr = doc != null ? Marshal.StringToHGlobalAnsi(doc) : IntPtr.Zero;

            return WriteMethodDef(mdef, namePtr, func, flags, docPtr);
        }

        internal static IntPtr WriteMethodDefSentinel(IntPtr mdef)
        {
            return WriteMethodDef(mdef, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        }

        internal static IntPtr CreateMetaType(Type impl)
        {
            // The managed metatype is functionally little different than the
            // standard Python metatype (PyType_Type). It overrides certain of
            // the standard type slots, and has to subclass PyType_Type for
            // certain functions in the C runtime to work correctly with it.

            IntPtr type = AllocateTypeObject("CLR Metatype");
            IntPtr py_type = Runtime.PyTypeType;

            Marshal.WriteIntPtr(type, TypeOffset.tp_base, py_type);
            Runtime.XIncref(py_type);

            TypeOffset.clr_gchandle_offset = checked((int)Marshal.ReadIntPtr(py_type, TypeOffset.tp_basicsize));
            if (TypeOffset.clr_gchandle_offset <= 0)
                throw new PythonEngineException("CLR Metatype initialization failed: unable to read tp_basicsize correctly");
            ObjectOffset.ClrGcHandleOffsetAssertSanity(TypeOffset.clr_gchandle_offset);
            int structSize = TypeOffset.clr_gchandle_offset + MetaType.ExtraTypeDataSize;
            Marshal.WriteIntPtr(type, TypeOffset.tp_basicsize, new IntPtr(structSize));

            // Slots will inherit from TypeType, it's not neccesary for setting them.
            // Inheried slots:
            // tp_basicsize, tp_itemsize,
            // tp_dictoffset, tp_weaklistoffset,
            // tp_traverse, tp_clear, tp_is_gc, etc.

            // Override type slots with those of the managed implementation.

            InitializeSlots(type, impl);

            var flags = TypeFlags.Default;
            flags |= TypeFlags.Managed;
            flags |= TypeFlags.HeapType;
            flags |= TypeFlags.HaveGC;
            Util.WriteCLong(type, TypeOffset.tp_flags, (long)flags);

            // We need space for 3 PyMethodDef structs, each of them
            // 4 int-ptrs in size.
            IntPtr mdef = Runtime.PyMem_Malloc(3 * 4 * IntPtr.Size);
            IntPtr mdefStart = mdef;
            ThunkInfo thunkInfo = Interop.GetThunk(typeof(MetaType).GetMethod("__instancecheck__"), "BinaryFunc");
            mdef = WriteMethodDef(
                mdef,
                "__instancecheck__",
                thunkInfo.Address
            );

            thunkInfo = Interop.GetThunk(typeof(MetaType).GetMethod("__subclasscheck__"), "BinaryFunc");
            mdef = WriteMethodDef(
                mdef,
                "__subclasscheck__",
                thunkInfo.Address
            );

            // FIXME: mdef is not used
            mdef = WriteMethodDefSentinel(mdef);

            Marshal.WriteIntPtr(type, TypeOffset.tp_methods, mdefStart);

            if (Runtime.PyType_Ready(type) != 0)
                throw new PythonEngineException("Failed to create CLR Metatype", PythonException.FromPyErr());

            IntPtr dict = Marshal.ReadIntPtr(type, TypeOffset.tp_dict);
            IntPtr mod = Runtime.PyString_FromString("CLR");
            Runtime.PyDict_SetItemString(dict, "__module__", mod);

            //DebugUtil.DumpType(type);

            return type;
        }

        /// <summary>
        /// Utility method to allocate a type object &amp; do basic initialization.
        /// </summary>
        internal static IntPtr AllocateTypeObject(string name)
            => AllocateTypeObject(name, Runtime.PyTypeType);
        /// <summary>
        /// Utility method to allocate a type object &amp; do basic initialization.
        /// </summary>
        internal static IntPtr AllocateTypeObject(string name, IntPtr typeType)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (typeType == IntPtr.Zero) throw new ArgumentNullException(nameof(typeType));

            IntPtr type = Runtime.PyType_GenericAlloc(typeType, 0);

            // Cheat a little: we'll set tp_name to the internal char * of
            // the Python version of the type name - otherwise we'd have to
            // allocate the tp_name and would have no way to free it.
            IntPtr temp = Runtime.PyUnicode_FromString(name);
            IntPtr raw = Runtime.PyUnicode_AsUTF8(temp);
            Marshal.WriteIntPtr(type, TypeOffset.tp_name, raw);
            Marshal.WriteIntPtr(type, TypeOffset.name, temp);
            Runtime.XIncref(temp);
            Marshal.WriteIntPtr(type, TypeOffset.qualname, temp);

            long ptr = type.ToInt64(); // 64-bit safe

            temp = new IntPtr(ptr + TypeOffset.nb_add);
            Marshal.WriteIntPtr(type, TypeOffset.tp_as_number, temp);

            temp = new IntPtr(ptr + TypeOffset.sq_length);
            Marshal.WriteIntPtr(type, TypeOffset.tp_as_sequence, temp);

            temp = new IntPtr(ptr + TypeOffset.mp_length);
            Marshal.WriteIntPtr(type, TypeOffset.tp_as_mapping, temp);

            temp = new IntPtr(ptr + TypeOffset.bf_getbuffer);
            Marshal.WriteIntPtr(type, TypeOffset.tp_as_buffer, temp);
            return type;
        }


        #region Native Code Page
        /// <summary>
        /// Initialized by InitializeNativeCodePage.
        ///
        /// This points to a page of memory allocated using mmap or VirtualAlloc
        /// (depending on the system), and marked read and execute (not write).
        /// Very much on purpose, the page is *not* released on a shutdown and
        /// is instead leaked. See the TestDomainReload test case.
        ///
        /// The contents of the page are two native functions: one that returns 0,
        /// one that returns 1.
        ///
        /// If python didn't keep its gc list through a Py_Finalize we could remove
        /// this entire section.
        /// </summary>
        internal static IntPtr NativeCodePage = IntPtr.Zero;

        /// <summary>
        /// Structure to describe native code.
        ///
        /// Use NativeCode.Active to get the native code for the current platform.
        ///
        /// Generate the code by creating the following C code:
        /// <code>
        /// int Return0() { return 0; }
        /// int Return1() { return 1; }
        /// </code>
        /// Then compiling on the target platform, e.g. with gcc or clang:
        /// <code>cc -c -fomit-frame-pointer -O2 foo.c</code>
        /// And then analyzing the resulting functions with a hex editor, e.g.:
        /// <code>objdump -disassemble foo.o</code>
        /// </summary>
        internal class NativeCode
        {
            /// <summary>
            /// The code, as a string of bytes.
            /// </summary>
            public byte[] Code { get; private set; }

            /// <summary>
            /// Where does the "return 0" function start?
            /// </summary>
            public int Return0 { get; private set; }

            /// <summary>
            /// Where does the "return 1" function start?
            /// </summary>
            public int Return1 { get; private set; }

            public static NativeCode Active
            {
                get
                {
                    switch (Runtime.Machine)
                    {
                        case MachineType.i386:
                            return I386;
                        case MachineType.x86_64:
                            return X86_64;
                        default:
                            return null;
                    }
                }
            }

            /// <summary>
            /// Code for x86_64. See the class comment for how it was generated.
            /// </summary>
            public static readonly NativeCode X86_64 = new NativeCode()
            {
                Return0 = 0x10,
                Return1 = 0,
                Code = new byte[]
                {
                    // First Return1:
                    0xb8, 0x01, 0x00, 0x00, 0x00, // movl $1, %eax
                    0xc3, // ret

                    // Now some padding so that Return0 can be 16-byte-aligned.
                    // I put Return1 first so there's not as much padding to type in.
                    0x66, 0x2e, 0x0f, 0x1f, 0x84, 0x00, 0x00, 0x00, 0x00, 0x00, // nop

                    // Now Return0.
                    0x31, 0xc0, // xorl %eax, %eax
                    0xc3, // ret
                }
            };

            /// <summary>
            /// Code for X86.
            ///
            /// It's bitwise identical to X86_64, so we just point to it.
            /// <see cref="NativeCode.X86_64"/>
            /// </summary>
            public static readonly NativeCode I386 = X86_64;
        }

        /// <summary>
        /// Platform-dependent mmap and mprotect.
        /// </summary>
        internal interface IMemoryMapper
        {
            /// <summary>
            /// Map at least numBytes of memory. Mark the page read-write (but not exec).
            /// </summary>
            IntPtr MapWriteable(int numBytes);

            /// <summary>
            /// Sets the mapped memory to be read-exec (but not write).
            /// </summary>
            void SetReadExec(IntPtr mappedMemory, int numBytes);
        }

        class WindowsMemoryMapper : IMemoryMapper
        {
            const UInt32 MEM_COMMIT = 0x1000;
            const UInt32 MEM_RESERVE = 0x2000;
            const UInt32 PAGE_READWRITE = 0x04;
            const UInt32 PAGE_EXECUTE_READ = 0x20;

            [DllImport("kernel32.dll")]
            static extern IntPtr VirtualAlloc(IntPtr lpAddress, IntPtr dwSize, UInt32 flAllocationType, UInt32 flProtect);

            [DllImport("kernel32.dll")]
            static extern bool VirtualProtect(IntPtr lpAddress, IntPtr dwSize, UInt32 flNewProtect, out UInt32 lpflOldProtect);

            public IntPtr MapWriteable(int numBytes)
            {
                return VirtualAlloc(IntPtr.Zero, new IntPtr(numBytes),
                                    MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
            }

            public void SetReadExec(IntPtr mappedMemory, int numBytes)
            {
                UInt32 _;
                VirtualProtect(mappedMemory, new IntPtr(numBytes), PAGE_EXECUTE_READ, out _);
            }
        }

        class UnixMemoryMapper : IMemoryMapper
        {
            const int PROT_READ = 0x1;
            const int PROT_WRITE = 0x2;
            const int PROT_EXEC = 0x4;

            const int MAP_PRIVATE = 0x2;
            int MAP_ANONYMOUS
            {
                get
                {
                    switch (Runtime.OperatingSystem)
                    {
                        case OperatingSystemType.Darwin:
                            return 0x1000;
                        case OperatingSystemType.Linux:
                            return 0x20;
                        default:
                            throw new NotImplementedException(
                                $"mmap is not supported on {Runtime.OperatingSystem}"
                            );
                    }
                }
            }

            [DllImport("libc")]
            static extern IntPtr mmap(IntPtr addr, IntPtr len, int prot, int flags, int fd, IntPtr offset);

            [DllImport("libc")]
            static extern int mprotect(IntPtr addr, IntPtr len, int prot);

            public IntPtr MapWriteable(int numBytes)
            {
                // MAP_PRIVATE must be set on linux, even though MAP_ANON implies it.
                // It doesn't hurt on darwin, so just do it.
                return mmap(IntPtr.Zero, new IntPtr(numBytes), PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS, -1, IntPtr.Zero);
            }

            public void SetReadExec(IntPtr mappedMemory, int numBytes)
            {
                mprotect(mappedMemory, new IntPtr(numBytes), PROT_READ | PROT_EXEC);
            }
        }

        internal static IMemoryMapper CreateMemoryMapper()
        {
            switch (Runtime.OperatingSystem)
            {
                case OperatingSystemType.Darwin:
                case OperatingSystemType.Linux:
                    return new UnixMemoryMapper();
                case OperatingSystemType.Windows:
                    return new WindowsMemoryMapper();
                default:
                    throw new NotImplementedException(
                        $"No support for {Runtime.OperatingSystem}"
                    );
            }
        }

        /// <summary>
        /// Initializes the native code page.
        ///
        /// Safe to call if we already initialized (this function is idempotent).
        /// <see cref="NativeCodePage"/>
        /// </summary>
        internal static void InitializeNativeCodePage()
        {
            // Do nothing if we already initialized.
            if (NativeCodePage != IntPtr.Zero)
            {
                return;
            }

            // Allocate the page, write the native code into it, then set it
            // to be executable.
            IMemoryMapper mapper = CreateMemoryMapper();
            int codeLength = NativeCode.Active.Code.Length;
            NativeCodePage = mapper.MapWriteable(codeLength);
            Marshal.Copy(NativeCode.Active.Code, 0, NativeCodePage, codeLength);
            mapper.SetReadExec(NativeCodePage, codeLength);
        }
        #endregion

        /// <summary>
        /// Given a newly allocated Python type object and a managed Type that
        /// provides the implementation for the type, connect the type slots of
        /// the Python object to the managed methods of the implementing Type.
        /// </summary>
        internal static void InitializeSlots(IntPtr type, Type impl)
        {
            // We work from the most-derived class up; make sure to get
            // the most-derived slot and not to override it with a base
            // class's slot.
            var seen = new HashSet<string>();

            while (impl != null)
            {
                MethodInfo[] methods = impl.GetMethods(tbFlags);
                foreach (MethodInfo method in methods)
                {
                    string name = method.Name;
                    if (!(name.StartsWith("tp_") ||
                          name.StartsWith("nb_") ||
                          name.StartsWith("sq_") ||
                          name.StartsWith("mp_") ||
                          name.StartsWith("bf_")
                    ))
                    {
                        continue;
                    }

                    if (seen.Contains(name))
                    {
                        continue;
                    }

                    var thunkInfo = Interop.GetThunk(method);
                    InitializeSlot(type, thunkInfo.Address, name);

                    seen.Add(name);
                }

                var initSlot = impl.GetMethod("InitializeSlots", BindingFlags.Static | BindingFlags.Public);
                initSlot?.Invoke(null, parameters: new object[] {type, seen});

                impl = impl.BaseType;
            }

            var native = NativeCode.Active;

            // The garbage collection related slots always have to return 1 or 0
            // since .NET objects don't take part in Python's gc:
            //   tp_traverse (returns 0)
            //   tp_clear    (returns 0)
            //   tp_is_gc    (returns 1)
            // These have to be defined, though, so by default we fill these with
            // static C# functions from this class.

            var ret0 = Interop.GetThunk(((Func<IntPtr, int>)Return0).Method).Address;
            var ret1 = Interop.GetThunk(((Func<IntPtr, int>)Return1).Method).Address;

            if (native != null)
            {
                // If we want to support domain reload, the C# implementation
                // cannot be used as the assembly may get released before
                // CPython calls these functions. Instead, for amd64 and x86 we
                // load them into a separate code page that is leaked
                // intentionally.
                InitializeNativeCodePage();
                ret1 = NativeCodePage + native.Return1;
                ret0 = NativeCodePage + native.Return0;
            }

            InitializeSlot(type, ret0, "tp_traverse");
            InitializeSlot(type, ret0, "tp_clear");
            InitializeSlot(type, ret1, "tp_is_gc");
        }

        static int Return1(IntPtr _) => 1;

        static int Return0(IntPtr _) => 0;

        /// <summary>
        /// Helper for InitializeSlots.
        ///
        /// Initializes one slot to point to a function pointer.
        /// The function pointer might be a thunk for C#, or it may be
        /// an address in the NativeCodePage.
        /// </summary>
        /// <param name="type">Type being initialized.</param>
        /// <param name="slot">Function pointer.</param>
        /// <param name="name">Name of the method.</param>
        internal static void InitializeSlot(IntPtr type, IntPtr slot, string name)
        {
            Type typeOffset = typeof(TypeOffset);
            FieldInfo fi = typeOffset.GetField(name);
            var offset = (int)fi.GetValue(typeOffset);

            Marshal.WriteIntPtr(type, offset, slot);
        }

        static void InitializeSlot(IntPtr type, int slotOffset, MethodInfo method)
        {
            var thunk = Interop.GetThunk(method);
            Marshal.WriteIntPtr(type, slotOffset, thunk.Address);
        }

        /// <summary>
        /// Given a newly allocated Python type object and a managed Type that
        /// implements it, initialize any methods defined by the Type that need
        /// to appear in the Python type __dict__ (based on custom attribute).
        /// </summary>
        private static void InitMethods(IntPtr pytype, Type type)
        {
            IntPtr dict = Marshal.ReadIntPtr(pytype, TypeOffset.tp_dict);
            Type marker = typeof(PythonMethodAttribute);

            BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            var addedMethods = new HashSet<string>();

            while (type != null)
            {
                MethodInfo[] methods = type.GetMethods(flags);
                foreach (MethodInfo method in methods)
                {
                    if (!addedMethods.Contains(method.Name))
                    {
                        object[] attrs = method.GetCustomAttributes(marker, false);
                        if (attrs.Length > 0)
                        {
                            string method_name = method.Name;
                            var mi = new MethodInfo[1];
                            mi[0] = method;
                            MethodObject m = new TypeMethod(type, method_name, mi);
                            Runtime.PyDict_SetItemString(dict, method_name, m.pyHandle);
                            addedMethods.Add(method_name);
                        }
                    }
                }
                type = type.BaseType;
            }
        }


        /// <summary>
        /// Utility method to copy slots from a given type to another type.
        /// </summary>
        internal static void CopySlot(IntPtr from, IntPtr to, int offset)
        {
            IntPtr fp = Marshal.ReadIntPtr(from, offset);
            Marshal.WriteIntPtr(to, offset, fp);
        }
    }
}
