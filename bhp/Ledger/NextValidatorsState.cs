using Bhp.Cryptography.ECC;
using Bhp.IO;
using System.IO;

namespace Bhp.Ledger
{
    public class NextValidatorsState : StateBase, ICloneable<NextValidatorsState>
    {
        public ECPoint[] Validators = Blockchain.StandbyValidators;

        public override int Size => base.Size + Validators.GetVarSize();

        NextValidatorsState ICloneable<NextValidatorsState>.Clone()
        {
            return new NextValidatorsState
            {
                Validators = (ECPoint[])Validators.Clone()
            };
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            Validators = reader.ReadSerializableArray<ECPoint>();
        }

        void ICloneable<NextValidatorsState>.FromReplica(NextValidatorsState replica)
        {
            Validators = replica.Validators;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(Validators);
        }
    }
}