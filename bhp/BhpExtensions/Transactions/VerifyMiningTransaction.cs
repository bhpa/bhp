using Bhp.Cryptography;
using Bhp.Cryptography.ECC;
using Bhp.IO;
using Bhp.Ledger;
using Bhp.Mining;
using Bhp.Network.P2P.Payloads;
using System; 
using System.Linq; 

namespace Bhp.BhpExtensions.Transactions
{
    public class VerifyMiningTransaction
    {
        /// <summary>
        /// Verification of mining transactions
        /// It can only be signed by consensus nodes.
        /// </summary>
        /// <returns></returns>
        public static bool Verify(TransactionOutput[] Outputs, TransactionAttribute[] Attributes)
        {
            //Console.WriteLine("VerifyMinerTransaction");
            //No transaction output
            if (Outputs.Count() < 1)
            {
                return true;
            }

            //Asset can only be GoverningToken or UtilityToken
            if (Outputs.Any(p => p.AssetId != Blockchain.GoverningToken.Hash && p.AssetId != Blockchain.UtilityToken.Hash))
            {
                return false;
            }

            //It can only be signed in the first attribute.
            if (Attributes.Count() < 1 || Attributes[0].Usage != TransactionAttributeUsage.MinerSignature)
            {
                return false;
            }

            //MinerScript
            byte[] signature = Attributes[0].Data;

            //There is only one governing asset in mining transactions.            
            //if (Outputs.Select(p => p.AssetId == Blockchain.GoverningToken.Hash).Count() > 1)
            //{
            //    return false;
            //} 

            //It can only be in first transaction output.
            TransactionOutput output = Outputs[0];
            if (output.AssetId != Blockchain.GoverningToken.Hash)
            {
                return false;
            }

            MiningOutput miningOutput = new MiningOutput
            {
                AssetId = output.AssetId,
                Value = output.Value,
                ScriptHash = output.ScriptHash
            };
            byte[] message = miningOutput.GetHashData();

            foreach (ECPoint publicKey in Blockchain.StandbyValidators)
            {
                if (Crypto.Default.VerifySignature(message, signature, publicKey.EncodePoint(false)))
                {
                    //Console.WriteLine($"MinerTransaction VerifySignature Success. Miner {publicKey.ToString()}");
                    return true;
                }
            }
            Console.WriteLine("*** MinerTransaction VerifySignature Fail.***");
            return false;
        }
    }
}
