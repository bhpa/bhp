using Bhp.Network.P2P.Payloads;
using Bhp.SmartContract;
using Bhp.VM;
using System.Linq;

namespace Bhp.Ledger
{
    partial class Blockchain
    {
        partial class ApplicationExecuted
        {
            public Transaction Transaction;
            public TriggerType Trigger { get; internal set; }
            public VMState VMState { get; internal set; }
            public long GasConsumed { get; internal set; }
            public StackItem[] Stack { get; internal set; }
            public NotifyEventArgs[] Notifications { get; internal set; }

            internal ApplicationExecuted(ApplicationEngine engine)
            {
                Transaction = engine.ScriptContainer as Transaction;
                Trigger = engine.Trigger;
                VMState = engine.State;
                GasConsumed = engine.GasConsumed;
                Stack = engine.ResultStack.ToArray();
                Notifications = engine.Notifications.ToArray();
            }
        }
    }
}