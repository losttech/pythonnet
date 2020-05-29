namespace Python.EmbeddingTest {
    using System;
    using System.Collections.Generic;
    using System.Text;
    using NUnit.Framework;
    using Python.Runtime;
    using Python.Runtime.Codecs;

    public class Codecs {
        [SetUp]
        public void SetUp() {
            PythonEngine.Initialize();
        }

        [TearDown]
        public void Dispose() {
            PythonEngine.Shutdown();
        }

        [Test]
        public void ConversionsGeneric() {
            ConversionsGeneric<ValueTuple<int, string, object>, ValueTuple>();
        }

        static void ConversionsGeneric<T, TTuple>() {
            TupleCodec<TTuple>.Register();
            var tuple = Activator.CreateInstance(typeof(T), 42, "42", new object());
            T restored = default;
            using (Py.GIL())
            using (var scope = Py.CreateScope()) {
                void Accept(T value) => restored = value;
                var accept = new Action<T>(Accept).ToPython();
                scope.Set(nameof(tuple), tuple);
                scope.Set(nameof(accept), accept);
                scope.Exec($"{nameof(accept)}({nameof(tuple)})");
                Assert.AreEqual(expected: tuple, actual: restored);
            }
        }

        [Test]
        public void ConversionsObject() {
            ConversionsObject<ValueTuple<int, string, object>, ValueTuple>();
        }
        static void ConversionsObject<T, TTuple>() {
            TupleCodec<TTuple>.Register();
            var tuple = Activator.CreateInstance(typeof(T), 42, "42", new object());
            T restored = default;
            using (Py.GIL())
            using (var scope = Py.CreateScope()) {
                void Accept(object value) => restored = (T)value;
                var accept = new Action<object>(Accept).ToPython();
                scope.Set(nameof(tuple), tuple);
                scope.Set(nameof(accept), accept);
                scope.Exec($"{nameof(accept)}({nameof(tuple)})");
                Assert.AreEqual(expected: tuple, actual: restored);
            }
        }

        [Test]
        public void TupleRoundtripObject() {
            TupleRoundtripObject<ValueTuple<int, string, object>, ValueTuple>();
        }
        static void TupleRoundtripObject<T, TTuple>() {
            var tuple = Activator.CreateInstance(typeof(T), 42, "42", new object());
            using (Py.GIL()) {
                var pyTuple = TupleCodec<TTuple>.Instance.TryEncode(tuple);
                Assert.IsTrue(TupleCodec<TTuple>.Instance.TryDecode(pyTuple, out object restored));
                Assert.AreEqual(expected: tuple, actual: restored);
            }
        }

        [Test]
        public void TupleRoundtripGeneric() {
            TupleRoundtripGeneric<ValueTuple<int, string, object>, ValueTuple>();
        }

        static void TupleRoundtripGeneric<T, TTuple>() {
            var tuple = Activator.CreateInstance(typeof(T), 42, "42", new object());
            using (Py.GIL()) {
                var pyTuple = TupleCodec<TTuple>.Instance.TryEncode(tuple);
                Assert.IsTrue(TupleCodec<TTuple>.Instance.TryDecode(pyTuple, out T restored));
                Assert.AreEqual(expected: tuple, actual: restored);
            }
        }

        const string TestExceptionMessage = "Hello World!";
        [Test]
        public void ExceptionEncoded() {
            PyObjectConversions.RegisterEncoder(new ValueErrorCodec());
            void CallMe() => throw new ValueErrorWrapper(TestExceptionMessage);
            var callMeAction = new Action(CallMe);
            using var _ = Py.GIL();
            using var scope = Py.CreateScope();
            scope.Exec(@"
def call(func):
  try:
    func()
  except ValueError as e:
    return str(e)
");
            var callFunc = scope.Get("call");
            string message = callFunc.Invoke(callMeAction.ToPython()).As<string>();
            Assert.AreEqual(TestExceptionMessage, message);
        }

        [Test]
        public void ExceptionDecoded() {
            PyObjectConversions.RegisterDecoder(new ValueErrorCodec());
            using var _ = Py.GIL();
            using var scope = Py.CreateScope();
            var error = Assert.Throws<ValueErrorWrapper>(() => PythonEngine.Exec(
                $"raise ValueError('{TestExceptionMessage}')"));
            Assert.AreEqual(TestExceptionMessage, error.Message);
        }

        [Test]
        public void ExceptionDecodedNoInstance() {
            PyObjectConversions.RegisterDecoder(new InstancelessExceptionDecoder());
            using var _ = Py.GIL();
            using var scope = Py.CreateScope();
            var error = Assert.Throws<ValueErrorWrapper>(() => PythonEngine.Exec(
                $"[].__iter__().__next__()"));
            Assert.AreEqual(TestExceptionMessage, error.Message);
        }

        class ValueErrorWrapper : Exception {
            public ValueErrorWrapper(string message) : base(message) { }
        }

        class ValueErrorCodec : IPyObjectEncoder, IPyObjectDecoder {
            public bool CanDecode(PyObject objectType, Type targetType)
                => this.CanEncode(targetType)
                   && PythonReferenceComparer.Instance.Equals(objectType, PythonEngine.Eval("ValueError"));

            public bool CanEncode(Type type) => type == typeof(ValueErrorWrapper)
                                                || typeof(ValueErrorWrapper).IsSubclassOf(type);

            public bool TryDecode<T>(PyObject pyObj, out T value) {
                var message = pyObj.GetAttr("args")[0].As<string>();
                value = (T)(object)new ValueErrorWrapper(message);
                return true;
            }

            public PyObject TryEncode(object value) {
                var error = (ValueErrorWrapper)value;
                return PythonEngine.Eval("ValueError").Invoke(error.Message.ToPython());
            }
        }

        class InstancelessExceptionDecoder : IPyObjectDecoder
        {
            readonly PyObject PyErr = Py.Import("clr.interop").GetAttr("PyErr");

            public bool CanDecode(PyObject objectType, Type targetType)
                => PythonReferenceComparer.Instance.Equals(PyErr, objectType);

            public bool TryDecode<T>(PyObject pyObj, out T value)
            {
                if (pyObj.HasAttr("value"))
                {
                    value = default;
                    return false;
                }

                value = (T)(object)new ValueErrorWrapper(TestExceptionMessage);
                return true;
            }
        }
    }

    class FakeEncoder<T> : IPyObjectEncoder
    {
        public bool CanEncode(Type type) => type == typeof(T);
        public PyObject TryEncode(object value) => this.GetRawPythonProxy();
    }

    class FakeDecoder<TTarget> : IPyObjectDecoder
    {
        public PyObject TheOnlySupportedSourceType { get; }
        public TTarget DecodeResult { get; }

        public FakeDecoder(PyObject objectType, TTarget decodeResult)
        {
            this.TheOnlySupportedSourceType = objectType;
            this.DecodeResult = decodeResult;
        }

        public bool CanDecode(PyObject objectType, Type targetType)
            => objectType.Handle == TheOnlySupportedSourceType.Handle
               && targetType == typeof(TTarget);
        public bool TryDecode<T>(PyObject pyObj, out T value)
        {
            if (typeof(T) != typeof(TTarget))
                throw new ArgumentException(nameof(T));
            value = (T)(object)DecodeResult;
            return true;
        }
    }

    /// <summary>
    /// "Decodes" only objects of exact type <typeparamref name="T"/>.
    /// Result is just a raw Python object proxy.
    /// </summary>
    class ObjectToRawProxyEncoder<T> : IPyObjectEncoder
    {
        public bool CanEncode(Type type) => type == typeof(T);
        public PyObject TryEncode(object value) => this.GetRawPythonProxy();
    }

    /// <summary>
    /// Decodes object of specified Python type to the predefined value <see cref="DecodeResult"/>
    /// </summary>
    /// <typeparam name="TTarget">Type of the <see cref="DecodeResult"/></typeparam>
    class DecoderReturningPredefinedValue<TTarget> : IPyObjectDecoder
    {
        public PyObject TheOnlySupportedSourceType { get; }
        public TTarget DecodeResult { get; }

        public DecoderReturningPredefinedValue(PyObject objectType, TTarget decodeResult)
        {
            this.TheOnlySupportedSourceType = objectType;
            this.DecodeResult = decodeResult;
        }

        public bool CanDecode(PyObject objectType, Type targetType)
            => objectType.Handle == TheOnlySupportedSourceType.Handle
               && targetType == typeof(TTarget);
        public bool TryDecode<T>(PyObject pyObj, out T value)
        {
            if (typeof(T) != typeof(TTarget))
                throw new ArgumentException(nameof(T));
            value = (T)(object)DecodeResult;
            return true;
        }
    }
}
