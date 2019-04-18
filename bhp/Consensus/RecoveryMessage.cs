using Bhp.IO;
using Bhp.Ledger;
using Bhp.Network.P2P.Payloads;
using Bhp.SmartContract;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Bhp.Consensus
{
    public partial class RecoveryMessage : ConsensusMessage
    {
        public RecoveryMessage() : base(ConsensusMessageType.RecoveryMessage)
        {
        }
    }
}
