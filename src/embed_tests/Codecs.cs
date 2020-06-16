namespace Python.EmbeddingTest {
    using System;
    using System.Collections.Generic;
    using System.Text;
    using NUnit.Framework;
    using Python.Runtime;
    using Python.Runtime.Codecs;
    using static Python.Runtime.PyObjectConversions;

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

        [Test]
        public void EnumEncoded() {
            var enumEncoder = new FakeEncoder<ConsoleModifiers>();
            RegisterEncoder(enumEncoder);
            ConsoleModifiers.Alt.ToPython();
            Assert.AreEqual(ConsoleModifiers.Alt, enumEncoder.LastObject);
        }

        [Test]
        public void EnumDecoded() {
            var enumDecoder = new DecoderReturningPredefinedValue<ConsoleModifiers>(
                objectType: PythonEngine.Eval("list"),
                decodeResult: ConsoleModifiers.Alt);
            RegisterDecoder(enumDecoder);
            var decoded = PythonEngine.Eval("[]").As<ConsoleModifiers>();
            Assert.AreEqual(ConsoleModifiers.Alt, decoded);
        }

        const string TestExceptionMessage = "Hello World!";
        [Test]
        public void ExceptionEncoded() {
            RegisterEncoder(new ValueErrorCodec());
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
            RegisterDecoder(new ValueErrorCodec());
            using var _ = Py.GIL();
            using var scope = Py.CreateScope();
            var error = Assert.Throws<ValueErrorWrapper>(() => PythonEngine.Exec(
                $"raise ValueError('{TestExceptionMessage}')"));
            Assert.AreEqual(TestExceptionMessage, error.Message);
        }

        [Test]
        public void ExceptionDecodedNoInstance() {
            using var _ = Py.GIL();
            RegisterDecoder(new InstancelessExceptionDecoder());
            using var scope = Py.CreateScope();
            var error = Assert.Throws<ValueErrorWrapper>(() => PythonEngine.Exec(
                $"[].__iter__().__next__()"));
            Assert.AreEqual(TestExceptionMessage, error.Message);
        }

        [Test]
        public void ExceptionStringValue() {
            RegisterDecoder(new AttributeErrorDecoder());
            using var _ = Py.GIL();
            using var scope = Py.CreateScope();
            var error = Assert.Throws<AttributeErrorWrapper>(() => "hi".ToPython().GetAttr("blah"));
            StringAssert.Contains("blah", error.Message);
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

        class AttributeErrorWrapper : Exception {
            public AttributeErrorWrapper(string message) : base(message) { }
        }

        class AttributeErrorDecoder : IPyObjectDecoder {
            public bool CanDecode(PyObject objectType, Type targetType)
                => this.SupportsTargetType(targetType)
                   && PythonReferenceComparer.Instance.Equals(objectType, PythonEngine.Eval("AttributeError"));

            bool SupportsTargetType(Type type) => type == typeof(AttributeErrorWrapper)
                                               || typeof(AttributeErrorWrapper).IsSubclassOf(type);

            public bool TryDecode<T>(PyObject pyObj, out T value) {
                var message = pyObj.GetAttr("args")[0].As<string>();
                value = (T)(object)new AttributeErrorWrapper(message);
                return true;
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
        public T LastObject { get; private set; }
        public bool CanEncode(Type type) => type == typeof(T);
        public PyObject TryEncode(object value)
        {
            this.LastObject = (T)value;
            return PyObject.FromManagedObject(this);
        }
    }

    /// <summary>
    /// "Decodes" only objects of exact type <typeparamref name="T"/>.
    /// Result is just the raw proxy to the encoder instance itself.
    /// </summary>
    class ObjectToEncoderInstanceEncoder<T> : IPyObjectEncoder
    {
        public bool CanEncode(Type type) => type == typeof(T);
        public PyObject TryEncode(object value) => PyObject.FromManagedObject(this);
    }

    abstract class SingleTypeDecoder : IPyObjectDecoder
    {
        public PyObject LastSourceType { get; private set; }
        public PyObject TheOnlySupportedSourceType { get; }

        public virtual bool CanDecode(PyObject objectType, Type targetType)
        {
            this.LastSourceType = objectType;
            return objectType.Handle == this.TheOnlySupportedSourceType.Handle;
        }

        public abstract bool TryDecode<T>(PyObject pyObj, out T value);

        protected SingleTypeDecoder(PyObject objectType) {
            this.TheOnlySupportedSourceType = objectType;
        }
    }

    /// <summary>
    /// Decodes object of specified Python type to the predefined value <see cref="DecodeResult"/>
    /// </summary>
    /// <typeparam name="TTarget">Type of the <see cref="DecodeResult"/></typeparam>
    class DecoderReturningPredefinedValue<TTarget> : SingleTypeDecoder
    {
        public TTarget DecodeResult { get; }

        public DecoderReturningPredefinedValue(PyObject objectType, TTarget decodeResult) : base(objectType)
        {
            this.DecodeResult = decodeResult;
        }

        public override bool CanDecode(PyObject objectType, Type targetType)
            => base.CanDecode(objectType, targetType) && targetType == typeof(TTarget);
        public override bool TryDecode<T>(PyObject pyObj, out T value)
        {
            if (typeof(T) != typeof(TTarget))
                throw new ArgumentException(nameof(T));
            value = (T)(object)DecodeResult;
            return true;
        }
    }
}
