namespace Python.Runtime.Platforms {
    using System;
    using System.Runtime.InteropServices;

    class MacLibraryLoader: UnixLibraryLoader {
        public static MacLibraryLoader Instance { get; } = new MacLibraryLoader();
        const int RTLD_GLOBAL = 0x8;
        public override IntPtr LoadLibrary(string path) {
            path = string.IsNullOrEmpty(System.IO.Path.GetExtension(path)) ? $"lib{path}.dylib" : path;
            return Mac.dlopen(path, RTLD_NOW | RTLD_GLOBAL);
        }

        public override void FreeLibrary(IntPtr library) => Mac.dlclose(library);

        protected override IntPtr dlerror() => Mac.dlerror();

        protected override IntPtr dlsym(IntPtr libraryHandle, string name) => Mac.dlsym(libraryHandle, name);

        protected override IntPtr RTLD_DEFAULT => new IntPtr(-2);

        static class Mac {
            const string MacNativeDll = "/usr/lib/libSystem.dylib";
            [DllImport(MacNativeDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            public static extern IntPtr dlopen(String fileName, int flags);

            [DllImport(MacNativeDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            internal static extern IntPtr dlsym(IntPtr handle, String symbol);

            [DllImport(MacNativeDll, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int dlclose(IntPtr handle);

            [DllImport(MacNativeDll, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr dlerror();
        }

        private MacLibraryLoader() { }
    }
}
