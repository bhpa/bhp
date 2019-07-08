using Bhp.VM;
using System;
using VMArray = Bhp.VM.Types.Array;

namespace Bhp.SmartContract.Native
{
    internal class ContractMethodMetadata
    {
        public Func<ApplicationEngine, VMArray, StackItem> Delegate;
        public long Price;
    }
}