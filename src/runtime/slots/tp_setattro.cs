namespace Python.Runtime.Slots
{
    interface ITypeSetAttrO
    {
        int tp_setattro(BorrowedReference self, BorrowedReference attr, BorrowedReference value);
    }
}
