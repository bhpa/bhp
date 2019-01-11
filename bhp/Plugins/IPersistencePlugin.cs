using Bhp.Persistence;
using System.Collections.Generic;
using static Bhp.Ledger.Blockchain;

namespace Bhp.Plugins
{
    public interface IPersistencePlugin
    {
        void OnPersist(Snapshot snapshot, IReadOnlyList<ApplicationExecuted> applicationExecutedList);
    }
}
