using System;
using System.Collections.Generic;
using System.Text;

namespace Bhp.SmartContract.Native.Tokens
{
    public sealed class GasToken : Brc6Token<Brc6AccountState>
    {
        public override string ServiceName => "Bhp.Native.Tokens.GAS";
        public override string Name => "GAS";
        public override string Symbol => "gas";
        public override int Decimals => 8;

        internal GasToken()
        {
        }
    }
}
