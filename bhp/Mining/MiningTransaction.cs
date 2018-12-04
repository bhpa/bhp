using Bhp.Cryptography;
using Bhp.Ledger;
using Bhp.Network.P2P.Payloads;
using Bhp.Wallets;
using System.Linq;

namespace Bhp.Mining
{
    /// <summary>
    /// Mining transaction output (block output)
    /// </summary>
    public class MiningTransaction
    {
        private TransactionOutput[] outputs = null;
        private byte[] signatureOfMining = null;
        private MiningOutputLedger miningOutput = null;
        private Fixed8 amount_netfee = Fixed8.Zero;
        private Fixed8 transaction_fee = Fixed8.Zero;

        //public MiningTransaction(Fixed8 amount_netfee, Fixed8 transaction_fee)
        public MiningTransaction(Fixed8 amount_netfee)
        {
            this.amount_netfee = amount_netfee;
            //this.transaction_fee = transaction_fee;
        }

        //public MiningTransaction(Fixed8 amount_netfee)
        //{
        //    this.amount_netfee = amount_netfee;
        //}

        public MinerTransaction GetMinerTransaction(ulong nonce, uint blockIndex, Wallet wallet)
        {
            if (blockIndex < 1)
            {
                return new MinerTransaction
                {
                    Nonce = (uint)(nonce % (uint.MaxValue + 1ul)),
                    Attributes = new TransactionAttribute[0],
                    Inputs = new CoinReference[0],
                    Outputs = amount_netfee == Fixed8.Zero ? new TransactionOutput[0] : new TransactionOutput[] { NetFeeOutput(wallet, amount_netfee) },
                    Witnesses = new Witness[0]
                };
            }

            if (outputs == null)
            {
                MakeTransactionOutputs(blockIndex, wallet, amount_netfee);
                byte[] hashDataOfMining = GetHashData();
                if (hashDataOfMining != null)
                {
                    //Signature 
                    WalletAccount account = wallet.GetAccount(wallet.GetChangeAddress());
                    if (account?.HasKey == true)
                    {
                        KeyPair key = account.GetKey();
                        signatureOfMining = Crypto.Default.Sign(hashDataOfMining, key.PrivateKey, key.PublicKey.EncodePoint(false).Skip(1).ToArray());
                    }
                }
            }

            MinerTransaction tx = new MinerTransaction
            {
                Nonce = (uint)(nonce % (uint.MaxValue + 1ul)),
                Attributes = signatureOfMining == null ? new TransactionAttribute[0] :
                        new TransactionAttribute[]
                        {
                                new TransactionAttribute
                                {
                                    Usage = TransactionAttributeUsage.MinerSignature,
                                    Data = signatureOfMining
                                }
                        },
                Inputs = new CoinReference[0],
                Outputs = outputs,
                Witnesses = new Witness[0]
            };
            return tx;
        }
         
        private byte[] GetHashData()
        {
            return miningOutput == null ? null : miningOutput.GetHashData();
        }

        //by BHP
        //private TransactionOutput[] MakeTransactionOutputs(uint blockIndex, Wallet wallet, Fixed8 amount_netfee, Fixed8 transaction_fee)
        private TransactionOutput[] MakeTransactionOutputs(uint blockIndex, Wallet wallet, Fixed8 amount_netfee)
        {
            if (amount_netfee == Fixed8.Zero )
            {
                //If the network fee is 0, only a mining transaction will be constructed
                outputs = new TransactionOutput[] { MiningOutput(blockIndex, wallet) };
            }
            else
            {
                outputs = new TransactionOutput[] { MiningOutput(blockIndex, wallet), NetFeeOutput(wallet, amount_netfee) };               
            }
            return outputs;
        }

        /// <summary>
        /// Mining output
        /// </summary>
        /// <returns></returns>
        private TransactionOutput MiningOutput(uint blockIndex, Wallet wallet)
        {
            miningOutput = new MiningOutputLedger
            {
                AssetId = Blockchain.GoverningToken.Hash,
                Value = MiningSubsidy.GetMiningSubsidy(blockIndex),
                ScriptHash = MiningParams.PoSAddressOfMainNet.Length > 0 ? MiningParams.PoSAddressOfMainNet[blockIndex % (uint)MiningParams.PoSAddressOfMainNet.Length].ToScriptHash() : wallet.GetChangeAddress()
                //ScriptHash = MiningParams.PoSAddressOfTestNet.Length > 0 ? MiningParams.PoSAddressOfTestNet[blockIndex % (uint)MiningParams.PoSAddressOfTestNet.Length].ToScriptHash() : wallet.GetChangeAddress()
            };

            return new TransactionOutput
            {
                AssetId = miningOutput.AssetId,
                Value = miningOutput.Value,
                ScriptHash = miningOutput.ScriptHash
            };
        }

        /// <summary>
        /// Network Fee
        /// </summary>
        /// <param name="wallet"></param>
        /// <param name="amount_netfee"></param>
        /// <returns></returns>
        private TransactionOutput NetFeeOutput(Wallet wallet, Fixed8 amount_netfee)
        {
            return new TransactionOutput
            {
                AssetId = Blockchain.UtilityToken.Hash,
                Value = amount_netfee,
                ScriptHash = wallet.GetChangeAddress()
            };
        }

        /// <summary>
        /// Transaction Fee
        /// </summary>
        /// <param name="wallet"></param>
        /// <param name="blockIndex"></param>
        /// <param name="transaction_fee"></param>
        /// <returns></returns>
        private TransactionOutput TransactionFeeOutput(Wallet wallet, uint blockIndex, Fixed8 transaction_fee)
        {
            return new TransactionOutput
            {
                AssetId = Blockchain.GoverningToken.Hash,
                Value = transaction_fee,
                ScriptHash = MiningParams.ServiceChargeAddressOfMainNet.ToScriptHash()
            };
        }
    }
}
