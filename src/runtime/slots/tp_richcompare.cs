namespace Python.Runtime.Slots
{
    enum CompareOp
    {
        Py_LT = 0,
        Py_LE = 1,
        Py_EQ = 2,
        Py_NE = 3,
        Py_GT = 4,
        Py_GE = 5,
    }

    interface ITypeRichCompare
    {
        NewReference tp_richcompare(BorrowedReference self, BorrowedReference other, CompareOp op);
    }
}
