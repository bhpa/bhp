using Bhp.Ledger;
using Bhp.Network.P2P.Payloads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bhp.BhpExtensions.Fees
{

    /// <summary>
    /// ServiceFee of transaction
    /// </summary>
    public class ServiceFee
    {
        /// <summary>
        /// 手续费收取字节基数
        /// </summary>
        public const int SizeRadix = 512;

        /// <summary>
        /// 最小手续费
        /// </summary>
        public static readonly Fixed8 MinServiceFee = Fixed8.FromDecimal(0.0001m);

        /// <summary>
        /// 最大手续费
        /// </summary>
        public static readonly Fixed8 MaxServceFee = Fixed8.FromDecimal(0.0005m);

        /*
        public static Fixed8 CalcuServiceFee(List<Transaction> transactions)
        {
            return Fixed8.Zero;
        }

        public static bool Verify(Transaction tx, TransactionResult[] results_destroy, Fixed8 systemFee)
        {
            if (results_destroy.Length > 1) return false;
            if (results_destroy.Length == 1 && results_destroy[0].AssetId != Blockchain.UtilityToken.Hash)
                return false;
            return true;
        }
        */

        public static Fixed8 CalcuServiceFee(List<Transaction> transactions)
        { 
            Transaction[] ts = transactions.Where(p => p.Type == TransactionType.ContractTransaction).ToArray();
            Fixed8 inputsum = Fixed8.Zero;
            Fixed8 outputsum = Fixed8.Zero;
            foreach (Transaction tr in ts)
            {
                foreach (CoinReference coin in tr.Inputs)
                {
                    if (Blockchain.Singleton.GetTransaction(coin.PrevHash).Outputs[coin.PrevIndex].AssetId == Blockchain.GoverningToken.Hash)
                    {
                       inputsum += Blockchain.Singleton.GetTransaction(coin.PrevHash).Outputs[coin.PrevIndex].Value;
                    }
                }
                foreach (TransactionOutput output in tr.Outputs.Where(p => p.AssetId == Blockchain.GoverningToken.Hash))
                {
                    outputsum += output.Value; 
                }
            }
            return inputsum - outputsum;
        }

        public static bool Verify(Transaction tx, TransactionResult[] results_destroy, Fixed8 SystemFee)
        {
            if (tx.Type == TransactionType.ContractTransaction)
            {
                Fixed8 otherInput = Fixed8.Zero;
                otherInput = tx.References.Values.Where(p => p.AssetId != Blockchain.UtilityToken.Hash).Sum(p => p.Value);
                //输入存在不是gas的资产
                if (otherInput == Fixed8.Zero)
                {
                    if (results_destroy.Length == 0) return true;
                    if (results_destroy.Length == 1 && results_destroy[0].AssetId != Blockchain.UtilityToken.Hash) return false;
                    if (results_destroy.Length > 1) return false;

                    Fixed8 amount = results_destroy.Where(p => p.AssetId == Blockchain.UtilityToken.Hash).Sum(p => p.Amount);
                    if (SystemFee > Fixed8.Zero && amount < SystemFee) return false;
                    
                    return true;
                }
                else
                {
                    if (results_destroy.Length == 0 || results_destroy.Length > 2) return false;
                    if (results_destroy.Length == 1 && results_destroy[0].AssetId != Blockchain.GoverningToken.Hash) return false;
                    if (results_destroy.Any(p => p.AssetId != Blockchain.GoverningToken.Hash && p.AssetId != Blockchain.UtilityToken.Hash)) return false;

                    //verify gas
                    Fixed8 amount = results_destroy.Where(p => p.AssetId == Blockchain.UtilityToken.Hash).Sum(p => p.Amount);
                    if (SystemFee > Fixed8.Zero && amount < SystemFee) return false;
                    return CheckServiceFee(tx);
                }
            }
            else
            {
                if (results_destroy.Length > 1) return false;
                if (results_destroy.Length == 1 && results_destroy[0].AssetId != Blockchain.UtilityToken.Hash)
                    return false;
                if (SystemFee > Fixed8.Zero && (results_destroy.Length == 0 || results_destroy[0].Amount < SystemFee))
                    return false;
                return true;
            }
        } 
      
        public static bool CheckServiceFee(Transaction tx)
        {   
            if (tx.References == null) return false;
            Fixed8 inputSum = tx.References.Values.Where(p => p.AssetId == Blockchain.GoverningToken.Hash).Sum(p => p.Value);
            Fixed8 outputSum = tx.Outputs.Where(p => p.AssetId == Blockchain.GoverningToken.Hash).Sum(p => p.Value);
            if (inputSum != Fixed8.Zero)
            {
                Fixed8 serviceFee = MinServiceFee;
                int tx_size = tx.Size - tx.Witnesses.Sum(p => p.Size);
                serviceFee = Fixed8.FromDecimal(tx_size / SizeRadix + (tx_size % SizeRadix == 0 ? 0:1)) * MinServiceFee; ;
                serviceFee = serviceFee <= MaxServceFee ? serviceFee : MaxServceFee ;
                Fixed8 payFee = inputSum - outputSum;

                return serviceFee <= payFee && payFee <= MaxServceFee;
            }
            return true;
        }
        
    }
}
