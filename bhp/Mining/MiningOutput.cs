using System.IO;
using Bhp.IO;

namespace Bhp.Mining
{
    /// <summary>
    /// sign script of mining output ledger
    /// </summary>
    public class MiningOutput : ISerializable
    {
        public UInt256 AssetId;
        public Fixed8 Value;
        public UInt160 ScriptHash;

        public int Size => AssetId.Size + Value.Size + ScriptHash.Size;

        public void Deserialize(BinaryReader reader)
        {
            AssetId = reader.ReadSerializable<UInt256>();
            Value = reader.ReadSerializable<Fixed8>();
            ScriptHash = reader.ReadSerializable<UInt160>();
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(AssetId);
            writer.Write(Value);
            writer.Write(ScriptHash);
        }

        public byte[] GetHashData()
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                Serialize(writer);
                writer.Flush();
                return ms.ToArray();
            }
        }
    }
}
