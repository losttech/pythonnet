namespace Python.Runtime {
    using System;
    using System.Runtime.InteropServices;
    using System.Security.Permissions;

    [SecurityPermission(SecurityAction.InheritanceDemand, UnmanagedCode = true)]
    [SecurityPermission(SecurityAction.Demand, UnmanagedCode = true)]
    class PyHandle : SafeHandle {
        public PyHandle():base(invalidHandleValue: IntPtr.Zero, ownsHandle: true) { }
        public PyHandle(bool ownsHandle) : base(IntPtr.Zero, ownsHandle) { }
        protected override bool ReleaseHandle() => throw new NotImplementedException();

        public override bool IsInvalid => this.handle == IntPtr.Zero;
    }
}
