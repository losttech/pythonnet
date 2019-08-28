namespace Python.Runtime
{
    /// <summary>
    /// Implement this interface to change how instances
    /// of your class are presented to Python interpreter.
    /// </summary>
    public interface IConvertibleToPython
    {
        /// <summary>
        /// Converts current object to <see cref="PyObject"/>,
        /// which can be passed to the Python interpreter.
        ///
        /// <para>Returns <c>null</c> if conversion should be skipped</para>
        /// </summary>
        PyObject TryConvertToPython();
    }
}
