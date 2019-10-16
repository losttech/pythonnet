namespace Python.Runtime.Native {
    using System;
    using System.Runtime.InteropServices;

    unsafe struct PyBuffer {
        /// <summary>
        /// <para>A pointer to the start of the logical structure described by the buffer fields.
        /// This can be any location within the underlying physical memory block of the exporter.
        /// For example, with negative strides the value may point to the end of the memory block.
        /// </para>
        /// <para>For contiguous arrays, the value points to the beginning of the memory block.</para>
        /// </summary>
        public IntPtr Address;
        /// <summary>
        /// A new reference to the exporting object.
        /// The reference is owned by the consumer and automatically decremented and set to NULL by PyBuffer_Release().
        /// The field is the equivalent of the return value of any standard C-API function.
        /// </summary>
        public IntPtr PyObj;
        public UIntPtr ByteCount;
        [MarshalAs(UnmanagedType.I4)]
        public bool IsReadOnly;
        public UIntPtr ItemByteSize;
        public string Format;
        /// <summary>
        /// The number of dimensions the memory represents as an n-dimensional array.
        /// If it is 0, buf points to a single item representing a scalar.
        /// In this case, shape, strides and suboffsets MUST be <c>null</c>.
        /// </summary>
        public int DimensionCount;
        /// <summary>
        /// An array of Py_ssize_t of length ndim indicating the shape of the memory as an n-dimensional array.
        /// Note that shape[0] * ... * shape[ndim-1] * itemsize MUST be equal to len.
        /// </summary>
        public UIntPtr* Shape;
        /// <summary>
        /// An array of Py_ssize_t of length ndim giving the number of bytes to skip to get to a new element in each dimension.
        ///
        /// <para>Stride values can be any integer.
        /// For regular arrays, strides are usually positive,
        /// but a consumer MUST be able to handle the case strides[n] <= 0.
        /// See complex arrays for further information.</para>
        /// </summary>
        public UIntPtr* Strides;
        /// <summary>
        /// An array of Py_ssize_t of length ndim. If suboffsets[n] >= 0,
        /// the values stored along the nth dimension are pointers
        /// and the suboffset value dictates how many bytes to add
        /// to each pointer after de-referencing.
        /// A suboffset value that is negative indicates that no de-referencing should occur
        /// (striding in a contiguous memory block).
        ///
        /// <para>If all suboffsets are negative (i.e. no de-referencing is needed),
        /// then this field must be <c>null</c> (the default value).</para>
        /// </summary>
        public UIntPtr* SubOffsets;
        /// <summary>
        /// This is for use internally by the exporting object. For example,
        /// this might be re-cast as an integer by the exporter and used
        /// to store flags about whether or not the shape, strides, and suboffsets
        /// arrays must be freed when the buffer is released.
        ///
        /// <para>The consumer MUST NOT alter this value.</para>
        /// </summary>
        public IntPtr Internal;
    }
}
