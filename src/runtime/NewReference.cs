namespace Python.Runtime
{
    using System;
    using System.Diagnostics.Contracts;

    /// <summary>
    /// Represents a reference to a Python object, that is tracked by Python's reference counting.
    /// </summary>
    [NonCopyable]
    ref struct NewReference
    {
        IntPtr pointer;

        [Pure]
        public static implicit operator BorrowedReference(in NewReference reference)
            => new BorrowedReference(reference.pointer);

        /// <summary>
        /// Creates <see cref="NewReference"/> from a nullable <see cref="BorrowedReference"/>.
        /// Increments the reference count accordingly.
        /// </summary>
        [Pure]
        public static NewReference FromNullable(BorrowedReference reference)
        {
            if (reference.IsNull) return default;

            IntPtr address = reference.DangerousGetAddress();
            Runtime.XIncref(address);
            return DangerousFromPointer(address);
        }

        /// <summary>
        /// Returns <see cref="PyObject"/> wrapper around this reference, which now owns
        /// the pointer. Sets the original reference to <c>null</c>, as it no longer owns it.
        /// </summary>
        public PyObject MoveToPyObject()
        {
            if (this.IsNull()) throw new NullReferenceException();

            var result = new PyObject(this.pointer);
            this.pointer = IntPtr.Zero;
            return result;
        }
        /// <summary>
        /// Removes this reference to a Python object, and sets it to <c>null</c>.
        /// </summary>
        public void Dispose()
        {
            if (!this.IsNull())
                Runtime.XDecref(this.pointer);
            this.pointer = IntPtr.Zero;
        }

        /// <summary>
        /// Creates <see cref="NewReference"/> from a raw pointer
        /// </summary>
        [Pure]
        public static NewReference DangerousFromPointer(IntPtr pointer)
            => new NewReference {pointer = pointer};

        /// <summary>
        /// Creates <see cref="NewReference"/> from a raw pointer
        /// and writes <c>null</c> to the original location.
        /// </summary>
        [Pure]
        public static NewReference DangerousMoveFromPointer(ref IntPtr pointer)
        {
            var pointerValue = pointer;
            pointer = IntPtr.Zero;
            return DangerousFromPointer(pointerValue);
        }

        public IntPtr DangerousMoveToPointer()
        {
            if (this.IsNull()) throw new NullReferenceException();

            var result = this.pointer;
            this.pointer = IntPtr.Zero;
            return result;
        }

        [Pure]
        internal static IntPtr DangerousGetAddress(in NewReference reference)
            => IsNull(reference) ? throw new NullReferenceException() : reference.pointer;
        [Pure]
        internal static bool IsNull(in NewReference reference)
            => reference.pointer == IntPtr.Zero;

        // TODO: return some static type
        internal StealingReference Steal()
        {
            var result = this.pointer;
            this.pointer = IntPtr.Zero;
            return StealingReference.DangerousFromPointer(result);
        }
    }

    /// <summary>
    /// These members can not be directly in <see cref="NewReference"/> type,
    /// because <c>this</c> is always passed by value, which we need to avoid.
    /// (note <code>this in NewReference</code> vs the usual <code>this NewReference</code>)
    /// </summary>
    static class NewReferenceExtensions
    {
        /// <summary>Gets a raw pointer to the Python object</summary>
        [Pure]
        public static IntPtr DangerousGetAddress(this in NewReference reference)
            => NewReference.DangerousGetAddress(reference);
        [Pure]
        public static bool IsNull(this in NewReference reference)
            => NewReference.IsNull(reference);
    }
}
