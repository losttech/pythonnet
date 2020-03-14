namespace Python.Runtime.Platforms {
    using System;
    using System.IO;
    using System.Runtime.InteropServices;

    sealed class LinuxLibraryLoader: UnixLibraryLoader {
        public static LinuxLibraryLoader Instance { get; } = new LinuxLibraryLoader();

        const int RTLD_GLOBAL = 0x100;
        const string LinuxNativeDll = "libdl.so";
        public override IntPtr LoadLibrary(string path) {
            path = File.Exists(path) ? path : $"lib{path}.so";
            return Linux.dlopen(path, RTLD_NOW | RTLD_GLOBAL);
        }

        public override void FreeLibrary(IntPtr library) => Linux.dlclose(library);

        protected override IntPtr dlerror() => Linux.dlerror();

        protected override IntPtr dlsym(IntPtr libraryHandle, string name) => Linux.dlsym(libraryHandle, name);

        protected override IntPtr RTLD_DEFAULT => IntPtr.Zero;

        static class Linux {
            [DllImport(LinuxNativeDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            public static extern IntPtr dlopen(string fileName, int flags);

            [DllImport(LinuxNativeDll, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int dlclose(IntPtr handle);

            [DllImport(LinuxNativeDll, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr dlerror();

            [DllImport(LinuxNativeDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
            internal static extern IntPtr dlsym(IntPtr handle, string symbol);
        }

        private LinuxLibraryLoader() { }
    }
}
