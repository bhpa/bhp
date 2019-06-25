using Bhp.IO;
using Bhp.Persistence;
using System.IO;

namespace Bhp.Network.P2P.Payloads
{
    public interface IVerifiable : ISerializable
    {
        Witness[] Witnesses { get; set; }

        void DeserializeUnsigned(BinaryReader reader);

        UInt160[] GetScriptHashesForVerifying(Snapshot snapshot);

        void SerializeUnsigned(BinaryWriter writer);
    }
}
