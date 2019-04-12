using System.Collections.Generic;
using Bhp.Network.P2P.Payloads;

namespace Bhp.Plugins
{
    public interface IMemoryPoolTxObserverPlugin
    {
        void TransactionAdded(Transaction tx);
        void TransactionsRemoved(MemoryPoolTxRemovalReason reason, IEnumerable<Transaction> transactions);
    }
}
