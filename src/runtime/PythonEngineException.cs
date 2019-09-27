namespace Python.Runtime {
    using System;
    using System.Runtime.Serialization;

    [Serializable]
    public class PythonEngineException: Exception {
        public PythonEngineException() : base() { }

        public PythonEngineException(string message) : base(message) { }

        public PythonEngineException(string message, Exception innerException) : base(message, innerException) { }

        protected PythonEngineException(SerializationInfo info, StreamingContext context):base(info, context) { }
    }
}
