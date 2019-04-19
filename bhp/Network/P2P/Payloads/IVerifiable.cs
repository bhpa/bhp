using Bhp.IO;
using Bhp.Persistence;
using Bhp.VM;
using System.IO;

namespace Bhp.Network.P2P.Payloads
{
    public interface IVerifiable : ISerializable, IScriptContainer
    {
        Witness[] Witnesses { get; }

        void DeserializeUnsigned(BinaryReader reader);

        UInt160[] GetScriptHashesForVerifying(Snapshot snapshot);

        void SerializeUnsigned(BinaryWriter writer);
    }
}
