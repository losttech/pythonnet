namespace Python.Runtime
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    using Python.Runtime.Codecs;

    /// <summary>
    /// Defines <see cref="PyObject"/> conversion to CLR types (unmarshalling)
    /// </summary>
    public interface IPyObjectDecoder
    {
        /// <summary>
        /// Checks if this decoder can decode from <paramref name="objectType"/> to <paramref name="targetType"/>
        /// </summary>
        bool CanDecode(PyObject objectType, Type targetType);
        /// <summary>
        /// Attempts do decode <paramref name="pyObj"/> into a variable of specified type
        /// </summary>
        /// <typeparam name="T">CLR type to decode into</typeparam>
        /// <param name="pyObj">Object to decode</param>
        /// <param name="value">The variable, that will receive decoding result</param>
        /// <returns></returns>
        bool TryDecode<T>(PyObject pyObj, out T value);
    }

    /// <summary>
    /// Defines conversion from CLR objects into Python objects (e.g. <see cref="PyObject"/>) (marshalling)
    /// </summary>
    public interface IPyObjectEncoder
    {
        /// <summary>
        /// Checks if encoder can encode CLR objects of specified type
        /// </summary>
        bool CanEncode(Type type);
        /// <summary>
        /// Attempts to encode CLR object <paramref name="value"/> into Python object
        /// </summary>
        PyObject TryEncode(object value);
    }

    /// <summary>
    /// This class allows to register additional marshalling codecs.
    /// <para>Python.NET will pick suitable encoder/decoder registered first</para>
    /// </summary>
    public static class PyObjectConversions
    {
        static readonly DecoderGroup decoders = new DecoderGroup();
        static readonly EncoderGroup encoders = new EncoderGroup();

        /// <summary>
        /// Registers specified encoder (marshaller)
        /// <para>Python.NET will pick suitable encoder/decoder registered first</para>
        /// </summary>
        public static void RegisterEncoder(IPyObjectEncoder encoder)
        {
            if (encoder == null) throw new ArgumentNullException(nameof(encoder));

            lock (encoders)
            {
                encoders.Add(encoder);
            }
        }

        /// <summary>
        /// Registers specified decoder (unmarshaller)
        /// <para>Python.NET will pick suitable encoder/decoder registered first</para>
        /// </summary>
        public static void RegisterDecoder(IPyObjectDecoder decoder)
        {
            if (decoder == null) throw new ArgumentNullException(nameof(decoder));

            lock (decoders)
            {
                decoders.Add(decoder);
            }
        }

        #region Encoding
        internal static PyObject TryEncode(object obj, Type type)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (type == null) throw new ArgumentNullException(nameof(type));

            foreach (var encoder in clrToPython.GetOrAdd(type, GetEncoders))
            {
                var result = encoder.TryEncode(obj);
                if (result != null) return result;
            }

            return null;
        }

        static readonly ConcurrentDictionary<Type, IPyObjectEncoder[]>
            clrToPython = new ConcurrentDictionary<Type, IPyObjectEncoder[]>();
        static IPyObjectEncoder[] GetEncoders(Type type)
        {
            lock (encoders)
            {
                return encoders.GetEncoders(type).ToArray();
            }
        }
        #endregion

        #region Decoding
        static readonly ConcurrentDictionary<TypePair, Converter.TryConvertFromPythonDelegate>
            pythonToClr = new ConcurrentDictionary<TypePair, Converter.TryConvertFromPythonDelegate>();
        internal static bool TryDecode(BorrowedReference obj, BorrowedReference objType, Type targetType, out object result)
        {
            if (obj.IsNull) throw new ArgumentNullException(nameof(obj));
            if (objType.IsNull) throw new ArgumentNullException(nameof(objType));
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));

            var decoder = pythonToClr.GetOrAdd(new TypePair(objType.DangerousGetAddress(), targetType), pair => GetDecoder(pair.PyType, pair.ClrType));
            result = null;
            if (decoder == null) return false;
            try
            {
                return decoder.Invoke(obj, out result);
            } catch (TargetInvocationException invocationException)
            {
                throw invocationException.InnerException.Rethrow();
            }
        }

        static Converter.TryConvertFromPythonDelegate GetDecoder(IntPtr sourceType, Type targetType)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            IPyObjectDecoder decoder;
            var pyType = new PyObject(Runtime.SelfIncRef(sourceType));
            lock (decoders)
            {
                decoder = decoders.GetDecoder(pyType, targetType);
                if (decoder == null) return null;
            }

            var decode = genericDecode.MakeGenericMethod(targetType);

            bool TryDecode(BorrowedReference pyHandle, out object result)
            {
                var pyObj = new PyObject(pyHandle);
                var @params = new object[] { pyObj, null };
                bool success = false;
                try
                {
                    success = (bool)decode.Invoke(decoder, @params);
                } catch (TargetInvocationException invocationException)
                {
                    throw invocationException.InnerException.Rethrow();
                }

                if (!success)
                {
                    pyObj.Dispose();
                }

                result = @params[1];
                return success;
            }

            return TryDecode;
        }

        static readonly MethodInfo genericDecode = typeof(IPyObjectDecoder).GetMethod(nameof(IPyObjectDecoder.TryDecode));

        #endregion

        internal static void Reset()
        {
            lock (encoders)
                lock (decoders)
                {
                    clrToPython.Clear();
                    pythonToClr.Clear();
                    encoders.Clear();
                    decoders.Clear();
                }
        }

        struct TypePair : IEquatable<TypePair>
        {
            internal readonly IntPtr PyType;
            internal readonly Type ClrType;

            public TypePair(IntPtr pyType, Type clrType)
            {
                this.PyType = pyType;
                this.ClrType = clrType;
            }

            public override int GetHashCode()
                => this.ClrType.GetHashCode() ^ this.PyType.GetHashCode();

            public bool Equals(TypePair other)
                => this.PyType == other.PyType && this.ClrType == other.ClrType;

            public override bool Equals(object obj) => obj is TypePair other && this.Equals(other);
        }
    }
}
