using Bhp.IO;
using Bhp.Network.P2P.Payloads;
using System.IO;

namespace Bhp.Ledger
{
    public class TransactionState : ICloneable<TransactionState>, ISerializable
    {
        public uint BlockIndex;
        public Transaction Transaction;

        int ISerializable.Size => sizeof(uint) + Transaction.Size;

        TransactionState ICloneable<TransactionState>.Clone()
        {
            return new TransactionState
            {
                BlockIndex = BlockIndex,
                Transaction = Transaction
            };
        }

        void ISerializable.Deserialize(BinaryReader reader)
        {
            BlockIndex = reader.ReadUInt32();
            Transaction = Transaction.DeserializeFrom(reader);
        }

        void ICloneable<TransactionState>.FromReplica(TransactionState replica)
        {
            BlockIndex = replica.BlockIndex;
            Transaction = replica.Transaction;
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.Write(BlockIndex);
            writer.Write(Transaction);
        }
    }
}
