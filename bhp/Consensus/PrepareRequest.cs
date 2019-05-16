using Bhp.IO;
using Bhp.Network.P2P.Payloads;
using System;
using System.IO;
using System.Linq;

namespace Bhp.Consensus
{
    public class PrepareRequest : ConsensusMessage
    {
        public uint Timestamp;
        public UInt256[] TransactionHashes;
        public MinerTransaction MinerTransaction;

        public override int Size => base.Size
            + sizeof(uint)                      //Timestamp
            + TransactionHashes.GetVarSize()    //TransactionHashes
            + MinerTransaction.Size;            //MinerTransaction

        public PrepareRequest()
            : base(ConsensusMessageType.PrepareRequest)
        {
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            Timestamp = reader.ReadUInt32();
            TransactionHashes = reader.ReadSerializableArray<UInt256>(Block.MaxTransactionsPerBlock);
            if (TransactionHashes.Distinct().Count() != TransactionHashes.Length)
                throw new FormatException();
            MinerTransaction = reader.ReadSerializable<MinerTransaction>();
            if (MinerTransaction.Hash != TransactionHashes[0])
                throw new FormatException();
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(Timestamp);
            writer.Write(TransactionHashes);
            writer.Write(MinerTransaction);
        }
    }
}
