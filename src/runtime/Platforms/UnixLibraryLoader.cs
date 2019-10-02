namespace Python.Runtime.Platforms {
    using System;
    using System.Runtime.InteropServices;

    abstract class UnixLibraryLoader: INativeLibraryLoader {
        protected const int RTLD_NOW = 0x2;
        public abstract IntPtr LoadLibrary(string path);

        public IntPtr GetProcAddress(IntPtr libraryHandle, string functionName) {
            // look in the exe if dllHandle is NULL
            if (libraryHandle == IntPtr.Zero) {
                libraryHandle = RTLD_DEFAULT;
            }

            // clear previous errors if any
            IntPtr res, errPtr;
            this.dlerror();
            res = this.dlsym(libraryHandle, functionName);
            errPtr = this.dlerror();

            if (errPtr != IntPtr.Zero) {
                throw new Exception("dlsym: " + Marshal.PtrToStringAnsi(errPtr));
            }
            return res;
        }

        protected abstract IntPtr dlerror();
        protected abstract IntPtr dlsym(IntPtr libraryHandle, string name);
        protected abstract IntPtr RTLD_DEFAULT { get; }

        public abstract void FreeLibrary(IntPtr library);
    }
}
