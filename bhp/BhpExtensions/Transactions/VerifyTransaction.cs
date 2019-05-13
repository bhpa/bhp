using Bhp.BhpExtensions.Fees;
using Bhp.Ledger;
using Bhp.Network.P2P.Payloads;
using Bhp.Persistence;
using Bhp.SmartContract;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Bhp.BhpExtensions.Transactions
{
    public class VerifyTransaction
    {
        // <summary>
        /// 
        /// </summary>
        /// <param name="persistence"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        public static string IsDoubleSpend(Snapshot snapshot, Transaction tx)
        {
            if (tx.Inputs.Length == 0) return "Input is empty.";
            foreach (var group in tx.Inputs.GroupBy(p => p.PrevHash))
            {
                UnspentCoinState state = snapshot.UnspentCoins.TryGet(group.Key);
                if (state == null) return "utxo is not exists.";
                if (group.Any(p => p.PrevIndex >= state.Items.Length || state.Items[p.PrevIndex].HasFlag(CoinState.Spent)))
                    return "utox was spent.";
            }
            return "success";
        }

        /// <summary>
        /// Verify Tx by BHP
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="mempool"></param>
        /// <returns></returns>
        public static string Verify(Snapshot snapshot, IEnumerable<Transaction> mempool,
             Transaction tx)
        {
            if (tx.Size > Transaction.MaxTransactionSize) return "The size of the free transaction must be less than 102400 bytes";
            for (int i = 1; i < tx.Inputs.Length; i++)
                for (int j = 0; j < i; j++)
                    if (tx.Inputs[i].PrevHash == tx.Inputs[j].PrevHash
                        && tx.Inputs[i].PrevIndex == tx.Inputs[j].PrevIndex)
                        return "The transaction input is repeated.";
            if (mempool.Where(p => p != tx).SelectMany(p => p.Inputs).Intersect(tx.Inputs).Count() > 0)
                return "Transaction input already exists.";

            string res = IsDoubleSpend(snapshot, tx);
            if ("success".Equals(res) == false)
            {
                return res;
            }

            foreach (var group in tx.Outputs.GroupBy(p => p.AssetId))
            {
                AssetState asset = snapshot.Assets.TryGet(group.Key);
                if (asset == null) return "asset is null.";
                if (asset.Expiration <= snapshot.Height + 1 && asset.AssetType != AssetType.GoverningToken && asset.AssetType != AssetType.UtilityToken)
                    return "Token expiration";
                foreach (TransactionOutput output in group)
                    if (output.Value.GetData() % (long)Math.Pow(10, 8 - asset.Precision) != 0)
                        return "Transaction output value is invalid.";
            }
            TransactionResult[] results = tx.GetTransactionResults()?.ToArray();
            if (results == null) return "TransactionResult is null.";
            TransactionResult[] results_destroy = results.Where(p => p.Amount > Fixed8.Zero).ToArray();
            //if (results_destroy.Length > 1) return "Transaction input is not equal output than one.";
            //if (results_destroy.Length == 1 && results_destroy[0].AssetId != Blockchain.UtilityToken.Hash)
            //    return "The first of transaction output is not MinerTransaction.";
            Fixed8 SystemFee = ProtocolSettings.Default.SystemFee.TryGetValue(tx.Type, out Fixed8 fee) ? fee : Fixed8.Zero;

            string verifyResult = Verify(tx, results_destroy, SystemFee).Trim();
            if (verifyResult != "success")
            {
                return verifyResult;
            }

            if (SystemFee > Fixed8.Zero && (results_destroy.Length == 0 || results_destroy[0].Amount < SystemFee))
                return "Transaction SystemFee is invalid.";

            //Transaction amount less than 0, because of input is zero.
            TransactionResult[] results_issue = results.Where(p => p.Amount < Fixed8.Zero).ToArray();
            switch (tx.Type)
            {
                //MiningOutput
                case TransactionType.MinerTransaction:
                    if (VerifyMiningTransaction.Verify(tx.Outputs, tx.Attributes) == false)
                        return "MinerTransaction is invalid.";
                    break;
                /*
                //case TransactionType.MinerTransaction:
                case TransactionType.ClaimTransaction:
                    if (results_issue.Any(p => p.AssetId != Blockchain.UtilityToken.Hash))
                        return "ClaimTransaction is invalid.";
                    break;
                */               
                default:
                    if (results_issue.Length > 0)
                        return "Transaction input is not equal to output.";
                    break;
            }
            if (tx.Attributes.Count(p => p.Usage == TransactionAttributeUsage.ECDH02 || p.Usage == TransactionAttributeUsage.ECDH03) > 1)
                return "ECDH02 and ECDH03 too much.";
            if (tx.VerifyWitnesses(snapshot) == false) return "Verify Witnesses is failure.";
            return "success";
        }

        //By BHP
        public static string Verify(Transaction tx, TransactionResult[] results_destroy, Fixed8 SystemFee)
        {
            /*
            if (tx.Type == TransactionType.ContractTransaction)
            {
                Fixed8 otherInput = Fixed8.Zero;
                otherInput = tx.References.Values.Where(p => p.AssetId != Blockchain.UtilityToken.Hash).Sum(p => p.Value);
                //输入存在不是gas的资产
                if (otherInput == Fixed8.Zero)
                {
                    if (results_destroy.Length == 0) return "success";
                    if (results_destroy.Length == 1 && results_destroy[0].AssetId != Blockchain.UtilityToken.Hash) return "Transaction is error";
                    if (results_destroy.Length > 1) return "Transaction is error";

                    Fixed8 amount = results_destroy.Where(p => p.AssetId == Blockchain.UtilityToken.Hash).Sum(p => p.Amount);
                    if (SystemFee > Fixed8.Zero && amount < SystemFee) return "SystemFee is not enough";

                    return "success";
                }
                else
                {
                    if (results_destroy.Length == 0)
                    {
                        return "TxFee is not enough!";
                    }
                    if (results_destroy.Length > 2)
                    {
                        return "Transaction input must equal to output!";
                    }
                    if (results_destroy.Length == 1 && results_destroy[0].AssetId != Blockchain.GoverningToken.Hash) return "Must pay TxFee for transaction!";
                    if (results_destroy.Any(p => p.AssetId != Blockchain.GoverningToken.Hash && p.AssetId != Blockchain.UtilityToken.Hash)) return "Transaction assetid error";

                    //verify gas
                    Fixed8 amount = results_destroy.Where(p => p.AssetId == Blockchain.UtilityToken.Hash).Sum(p => p.Amount);
                    if (SystemFee > Fixed8.Zero && amount < SystemFee) return "BHPgas is not enough!";

                    return CheckTxFee(tx);
                }
            }
            else
            */
            {
                if (results_destroy.Length > 1) return "Transaction input is not equal to output!";
                if (results_destroy.Length == 1 && results_destroy[0].AssetId != Blockchain.UtilityToken.Hash)
                    return "SystemFee assetid is error";
                if (SystemFee > Fixed8.Zero && (results_destroy.Length == 0 || results_destroy[0].Amount < SystemFee))
                    return "SystemFee is not enough";
                return "success";
            }
        }

        //By BHP
        public static string CheckTxFee(Transaction tx)
        {
            if (tx.References == null) return "Transaction input must not be empty";
            Fixed8 inputSum = tx.References.Values.Where(p => p.AssetId == Blockchain.GoverningToken.Hash).Sum(p => p.Value);
            Fixed8 outputSum = tx.Outputs.Where(p => p.AssetId == Blockchain.GoverningToken.Hash).Sum(p => p.Value);
            if (inputSum != Fixed8.Zero)
            {
                Fixed8 txFee = BhpTxFee.MinTxFee;
                int tx_size = tx.Size - tx.Witnesses.Sum(p => p.Size);
                txFee = Fixed8.FromDecimal(tx_size / BhpTxFee.SizeRadix + (tx_size % BhpTxFee.SizeRadix == 0 ? 0 : 1)) * BhpTxFee.MinTxFee; ;
                txFee = txFee <= BhpTxFee.MaxTxFee ? txFee : BhpTxFee.MaxTxFee;
                Fixed8 payFee = inputSum - outputSum;

                if (txFee <= payFee && payFee <= BhpTxFee.MaxTxFee)
                {
                    return "success";
                }

                if (payFee < BhpTxFee.MinTxFee)
                {
                    return "TxFee is not enough!";
                }

                if (payFee > BhpTxFee.MaxTxFee)
                {
                    return "TxFee is too much!";
                }
            }
            return "success";
        }
    }
}
