using Bhp.Network.P2P;
using Bhp.Network.P2P.Payloads;

namespace Bhp.Plugins
{
    public interface IP2PPlugin
    {
        bool OnP2PMessage(Message message);
        bool OnConsensusMessage(ConsensusPayload payload);
    }
}
