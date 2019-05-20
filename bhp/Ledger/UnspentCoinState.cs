using Bhp.IO;
using System.IO;
using System.Linq;

namespace Bhp.Ledger
{
    public class UnspentCoinState : ICloneable<UnspentCoinState>, ISerializable
    {
        public CoinState[] Items;

        public int Size => Items.GetVarSize();

        UnspentCoinState ICloneable<UnspentCoinState>.Clone()
        {
            return new UnspentCoinState
            {
                Items = (CoinState[])Items.Clone()
            };
        }

        public void Deserialize(BinaryReader reader)
        {
            Items = reader.ReadVarBytes().Select(p => (CoinState)p).ToArray();
        }

        void ICloneable<UnspentCoinState>.FromReplica(UnspentCoinState replica)
        {
            Items = replica.Items;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.WriteVarBytes(Items.Cast<byte>().ToArray());
        }
    }
}
