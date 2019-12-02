using Bhp.Persistence;
using System;
using System.Collections.Generic;
using static Bhp.Ledger.Blockchain;

namespace Bhp.Plugins
{
    public interface IPersistencePlugin
    {
        void OnPersist(StoreView snapshot, IReadOnlyList<ApplicationExecuted> applicationExecutedList);
        void OnCommit(StoreView snapshot);
        bool ShouldThrowExceptionFromCommit(Exception ex);
    }
}
