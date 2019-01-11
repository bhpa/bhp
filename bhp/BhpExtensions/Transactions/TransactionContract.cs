using Bhp.BhpExtensions.Fees;
using Bhp.Ledger;
using Bhp.Network.P2P.Payloads;
using Bhp.VM;
using Bhp.Wallets;
using System.Collections.Generic;
using System.Linq;

namespace Bhp.BhpExtensions.Transactions
{
    public class TransactionContract
    {
        public TransactionAttribute MakeLockTransactionScript(uint timestamp)
        { 
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitPush(timestamp);
                sb.EmitAppCall(UInt160.Parse("0xe69a2241c0629210c44e37fb03eb786d88a0af21"));// utxo time lock hash
                return new TransactionAttribute
                {
                    Usage = TransactionAttributeUsage.SmartContractScript,
                    Data = sb.ToArray()
                };
            }
        }

        public T MakeTransaction<T>(Wallet wallet, T tx, UInt160 from = null, UInt160 change_address = null, Fixed8 fee = default(Fixed8), Fixed8 transaction_fee = default(Fixed8)) where T : Transaction
        {
            if (tx.Outputs == null) tx.Outputs = new TransactionOutput[0];
            if (tx.Attributes == null) tx.Attributes = new TransactionAttribute[0];
            fee += tx.SystemFee;
            var pay_total = (typeof(T) == typeof(IssueTransaction) ? new TransactionOutput[0] : tx.Outputs).GroupBy(p => p.AssetId, (k, g) => new
            {
                AssetId = k,
                Value = g.Sum(p => p.Value)
            }).ToDictionary(p => p.AssetId);
            if (fee > Fixed8.Zero)
            {
                if (pay_total.ContainsKey(Blockchain.UtilityToken.Hash))
                {
                    pay_total[Blockchain.UtilityToken.Hash] = new
                    {
                        AssetId = Blockchain.UtilityToken.Hash,
                        Value = pay_total[Blockchain.UtilityToken.Hash].Value + fee
                    };
                }
                else
                {
                    pay_total.Add(Blockchain.UtilityToken.Hash, new
                    {
                        AssetId = Blockchain.UtilityToken.Hash,
                        Value = fee
                    });
                }
            }
            var pay_coins = pay_total.Select(p => new
            {
                AssetId = p.Key,
                Unspents = from == null ? wallet.FindUnspentCoins(p.Key, p.Value.Value) : wallet.FindUnspentCoins(p.Key, p.Value.Value, from)
            }).ToDictionary(p => p.AssetId);
            if (pay_coins.Any(p => p.Value.Unspents == null)) return null;
            var input_sum = pay_coins.Values.ToDictionary(p => p.AssetId, p => new
            {
                p.AssetId,
                Value = p.Unspents.Sum(q => q.Output.Value)
            });
            if (change_address == null) change_address = wallet.GetChangeAddress();
            List<TransactionOutput> outputs_new = new List<TransactionOutput>(tx.Outputs);

            int n = -1;

            //添加找零地址
            foreach (UInt256 asset_id in input_sum.Keys)
            {
                if (input_sum[asset_id].Value > pay_total[asset_id].Value)
                {
                    outputs_new.Add(new TransactionOutput
                    {
                        AssetId = asset_id,
                        Value = input_sum[asset_id].Value - pay_total[asset_id].Value,
                        ScriptHash = change_address
                    });

                    n = outputs_new.Count - 1;
                }
            } 
             
            //By BHP
            for (int i = 0; i < tx.Attributes.Length; i++)
            {
                if (tx.Attributes[i].Usage == TransactionAttributeUsage.SmartContractScript)
                {  
                    using (ScriptBuilder sb = new ScriptBuilder())
                    {
                        sb.EmitPush(n);
                        sb.EmitPush(tx.Attributes[i].Data);
                        tx.Attributes[i].Data = sb.ToArray();
                    }
                }
            }           

            tx.Inputs = pay_coins.Values.SelectMany(p => p.Unspents).Select(p => p.Reference).ToArray();
            tx.Outputs = outputs_new.ToArray();

            //By BHP
            if (tx.Type == TransactionType.ContractTransaction)
            {
                Fixed8 serviceFee = ServiceFee.MinServiceFee;
                int tx_size = tx.Size - tx.Witnesses.Sum(p => p.Size);
                serviceFee = Fixed8.FromDecimal(tx_size / ServiceFee.SizeRadix + (tx_size % ServiceFee.SizeRadix == 0 ? 0 : 1)) * ServiceFee.MinServiceFee;
                serviceFee = serviceFee <= ServiceFee.MaxServceFee ? serviceFee : ServiceFee.MaxServceFee;
                TransactionOutput[] tx_changeout = tx.Outputs.Where(p => p.AssetId == Blockchain.GoverningToken.Hash && p.ScriptHash == change_address).OrderByDescending(p => p.Value).ToArray();
                //exist changeaddress
                if (tx_changeout.Count() > 0 && tx_changeout[0].Value > serviceFee)
                {
                    tx_changeout[0].Value = tx_changeout[0].Value - serviceFee;
                }
                else
                {
                    TransactionOutput[] tx_out = tx.Outputs.Where(p => p.AssetId == Blockchain.GoverningToken.Hash).OrderByDescending(p => p.Value).ToArray();
                    if (tx_out.Count() > 0)
                    {
                        if (tx_out[0].Value > serviceFee)
                        {
                            tx_out[0].Value = tx_out[0].Value - serviceFee;
                        }
                        else
                        {
                            return null;
                        }
                    }
                }
            }
            return tx;
        }
    }
}
