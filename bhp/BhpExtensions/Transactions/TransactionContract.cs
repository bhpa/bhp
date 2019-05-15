using Bhp.BhpExtensions.Fees;
using Bhp.Ledger;
using Bhp.Network.P2P.Payloads;
using Bhp.SmartContract;
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
                sb.EmitPush(UInt160.Parse("0xe69a2241c0629210c44e37fb03eb786d88a0af21"));// utxo time lock hash
                sb.EmitSysCall(InteropService.System_Contract_Call);
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
            var pay_total = tx.Outputs.GroupBy(p => p.AssetId, (k, g) => new
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
            /*
            var pay_coins = pay_total.Select(p => new
            {
                AssetId = p.Key,
                Unspents = from == null ? wallet.FindUnspentCoins(p.Key, p.Value.Value) : wallet.FindUnspentCoins(p.Key, p.Value.Value, from)
            }).ToDictionary(p => p.AssetId);
            */

            //By BHP
            //When transferring money, finding UTXO requires additional transaction fees
            var pay_coins = pay_total.Select(p => new
            {
                AssetId = p.Key,
                Unspents = from == null ? wallet.FindUnspentCoins(p.Key, p.Value.Value + BhpTxFee.EstimateTxFee(tx, p.Key)) :
                                          wallet.FindUnspentCoins(p.Key, p.Value.Value + BhpTxFee.EstimateTxFee(tx, p.Key), from)
            }).ToDictionary(p => p.AssetId);

            if (pay_coins.Any(p => p.Value.Unspents == null)) return null;
            var input_sum = pay_coins.Values.ToDictionary(p => p.AssetId, p => new
            {
                p.AssetId,
                Value = p.Unspents.Sum(q => q.Output.Value)
            });
            if (change_address == null) change_address = wallet.GetChangeAddress();
            List<TransactionOutput> outputs_new = new List<TransactionOutput>(tx.Outputs);

            /*
            int n = -1;
            //添加找零地址  By BHP        
            foreach (UInt256 asset_id in input_sum.Keys)
            {
                Fixed8 txFee = BhpTxFee.EstimateTxFee(tx, asset_id);
                if (input_sum[asset_id].Value > (pay_total[asset_id].Value + txFee))
                {
                    outputs_new.Add(new TransactionOutput
                    {
                        AssetId = asset_id,
                        Value = input_sum[asset_id].Value - pay_total[asset_id].Value - txFee,
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
            */

            List<int> changeNum = new List<int>();
            //添加找零地址  By BHP        
            foreach (UInt256 asset_id in input_sum.Keys)
            {
                Fixed8 txFee = BhpTxFee.EstimateTxFee(tx, asset_id);
                if (input_sum[asset_id].Value > (pay_total[asset_id].Value + txFee))
                {
                    outputs_new.Add(new TransactionOutput
                    {
                        AssetId = asset_id,
                        Value = input_sum[asset_id].Value - pay_total[asset_id].Value - txFee,
                        ScriptHash = change_address
                    });
                    changeNum.Add(outputs_new.Count - 1);
                }
            }

            //By BHP
            for (int i = 0; i < tx.Attributes.Length; i++)
            {
                if (tx.Attributes[i].Usage == TransactionAttributeUsage.SmartContractScript)
                {
                    using (ScriptBuilder sb = new ScriptBuilder())
                    {
                        sb.EmitPush(changeNum.Count);
                        if (changeNum.Count > 0)
                        {
                            for (int j = changeNum.Count - 1; j >= 0; j--)
                                sb.EmitPush(changeNum[j]);
                        }
                        sb.EmitPush(tx.Attributes[i].Data);
                        tx.Attributes[i].Data = sb.ToArray();
                    }
                }
            }

            tx.Inputs = pay_coins.Values.SelectMany(p => p.Unspents).Select(p => p.Reference).ToArray();
            tx.Outputs = outputs_new.ToArray();

            return tx;
        }

        public static Fixed8 CalcuAmount(Transaction tx)
        {

            Fixed8 amount = Fixed8.Zero;
            List<string> inAddress = new List<string>();
            foreach (TransactionOutput output in tx.References.Values)
            {
                inAddress.Add(output.ScriptHash.ToAddress());
            }

            bool hasOther = false;

            foreach (TransactionOutput output in tx.Outputs)
            {
                bool found = false;
                foreach (string address in inAddress)
                {
                    if (output.ScriptHash.ToAddress().Equals(address))
                    {
                        found = true;
                        break;
                    }
                    else
                    {
                        //有其它地址认为不是自已给自己转
                        hasOther = true;
                    }
                }
                if (found == false)
                {
                    amount += output.Value;
                }
            }

            if (hasOther)
            {
                return amount;
            }
            else
            {
                return tx.Outputs.Sum(p => p.Value);
            }
        }

        public static Coin[] FindUnspentCoins(IEnumerable<Coin> unspents, UInt160 to_address)
        {
            Coin[] unspents_asset = unspents.Where(p => p.Output.AssetId == Blockchain.GoverningToken.Hash).ToArray();
            unspents_asset = VerifyTransactionContract.checkUtxo(unspents_asset);//By BHP
            return unspents_asset;
        }
    }
}
