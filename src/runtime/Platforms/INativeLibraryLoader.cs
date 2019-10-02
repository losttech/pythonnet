namespace Python.Runtime.Platforms {
    using System;

    interface INativeLibraryLoader {
        IntPtr LoadLibrary(string path);
        IntPtr GetProcAddress(IntPtr library, string functionName);
        void FreeLibrary(IntPtr library);
    }
}
