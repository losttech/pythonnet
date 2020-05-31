namespace Python.Runtime
{
    using System;
    using System.Runtime.ExceptionServices;

    static class ExceptionPolifills
    {
        public static Exception Rethrow(this Exception exception)
        {
            if (exception is null)
                throw new ArgumentNullException(nameof(exception));

#if NETSTANDARD
            ExceptionDispatchInfo.Capture(exception).Throw();
#endif
            throw exception;
        }
    }
}
