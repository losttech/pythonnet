namespace Python.Runtime.Slots
{
    interface ITypeGetAttrO
    {
        NewReference tp_getattro(BorrowedReference self, BorrowedReference attr);
    }
}
