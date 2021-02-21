namespace Python.Runtime.Slots
{
    interface ITypeStr
    {
        NewReference tp_str(BorrowedReference self);
    }
}
