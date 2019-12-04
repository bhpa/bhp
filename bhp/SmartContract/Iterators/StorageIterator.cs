using Bhp.Ledger;
using Bhp.VM;
using System.Collections.Generic;

namespace Bhp.SmartContract.Iterators
{
    internal class StorageIterator : IIterator
    {
        private readonly IEnumerator<(StorageKey Key, StorageItem Value)> enumerator;

        public StorageIterator(IEnumerator<(StorageKey, StorageItem)> enumerator)
        {
            this.enumerator = enumerator;
        }

        public void Dispose()
        {
            enumerator.Dispose();
        }

        public StackItem Key()
        {
            return enumerator.Current.Key.Key;
        }

        public bool Next()
        {
            return enumerator.MoveNext();
        }

        public StackItem Value()
        {
            return enumerator.Current.Value.Value;
        }
    }
}
