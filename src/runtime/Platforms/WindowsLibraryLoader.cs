namespace Python.Runtime.Platforms {
    using System;
    using System.Runtime.InteropServices;

    class WindowsLibraryLoader : INativeLibraryLoader {
        public static WindowsLibraryLoader Instance { get; } = new WindowsLibraryLoader();
        public void FreeLibrary(IntPtr library) => WinFreeLibrary(library);
        public IntPtr GetProcAddress(IntPtr library, string functionName) => WinGetProcAddress(library, functionName);
        public IntPtr LoadLibrary(string path) => WinLoadLibrary(path);

        private const string WinNativeDll = "kernel32.dll";

        [DllImport(WinNativeDll, EntryPoint = "LoadLibrary")]
        public static extern IntPtr WinLoadLibrary(string dllToLoad);
        [DllImport(WinNativeDll, EntryPoint = "GetProcAddress")]
        public static extern IntPtr WinGetProcAddress(IntPtr hModule, string procedureName);
        [DllImport(WinNativeDll, EntryPoint = "FreeLibrary")]
        public static extern bool WinFreeLibrary(IntPtr hModule);

        private WindowsLibraryLoader() { }
    }
}
