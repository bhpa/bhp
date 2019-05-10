using Bhp.VM;
using Bhp.VM.Types;
using System.Numerics;

namespace Bhp.SmartContract.Native.Tokens
{
    public class Brc6AccountState
    {
        public BigInteger Balance;

        public Brc6AccountState()
        {
        }

        public Brc6AccountState(byte[] data)
        {
            FromByteArray(data);
        }

        public void FromByteArray(byte[] data)
        {
            FromStruct((Struct)data.DeserializeStackItem(16));
        }

        protected virtual void FromStruct(Struct @struct)
        {
            Balance = @struct[0].GetBigInteger();
        }

        public byte[] ToByteArray()
        {
            return ToStruct().Serialize();
        }

        protected virtual Struct ToStruct()
        {
            return new Struct(new StackItem[] { Balance });
        }
    }
}
