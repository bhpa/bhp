using Bhp.VM;
using System;

namespace Bhp.SmartContract.Enumerators
{
    internal interface IEnumerator : IDisposable
    {
        bool Next();
        StackItem Value();
    }
}
