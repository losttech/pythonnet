namespace Python.Runtime
{
    using System;
    using System.Diagnostics.Contracts;

    /// <summary>
    /// Represents a reference to a Python object, that is being stolen by a C API.
    /// </summary>
    [NonCopyable]
    ref struct StealingReference
    {
        IntPtr pointer;

        /// <summary>
        /// Creates <see cref="StealingReference"/> from a raw pointer
        /// </summary>
        [Pure]
        public static StealingReference DangerousFromPointer(IntPtr pointer)
            => new StealingReference { pointer = pointer};

        public IntPtr DangerousGetAddressOrNull() => this.pointer;
    }
}
