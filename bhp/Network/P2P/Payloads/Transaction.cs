using Bhp.BhpExtensions.Fees;
using Bhp.BhpExtensions.Transactions;
using Bhp.Cryptography;
using Bhp.IO;
using Bhp.IO.Caching;
using Bhp.IO.Json;
using Bhp.Wallets;
using Bhp.Ledger;
using Bhp.Persistence;
using Bhp.SmartContract;
using Bhp.SmartContract.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Bhp.VM;

namespace Bhp.Network.P2P.Payloads
{
    public class Transaction : IEquatable<Transaction>, IInventory
    {
        public const int MaxTransactionSize = 102400;
        /// <summary>
        /// Maximum number of attributes that can be contained within a transaction
        /// </summary>
        private const int MaxTransactionAttributes = 16;

        /// <summary>
        /// Reflection cache for TransactionType
        /// </summary>
        private static ReflectionCache<byte> ReflectionCache = ReflectionCache<byte>.CreateFromEnum<TransactionType>();

        public readonly TransactionType Type;
        public byte Version;
        public byte[] Script;
        public UInt160 Sender;
        public long Gas;
        public long NetworkFee;
        public TransactionAttribute[] Attributes;
        public CoinReference[] Inputs;
        public TransactionOutput[] Outputs;
        public Witness[] Witnesses { get; set; }

        private Fixed8 _feePerByte = -Fixed8.Satoshi;
        /// <summary>
        /// The <c>NetworkFee</c> for the transaction divided by its <c>Size</c>.
        /// <para>Note that this property must be used with care. Getting the value of this property multiple times will return the same result. The value of this property can only be obtained after the transaction has been completely built (no longer modified).</para>
        /// </summary>
        public Fixed8 FeePerByte
        {
            get
            {
                if (_feePerByte == -Fixed8.Satoshi)
                    _feePerByte = Fixed8.Parse((NetworkFee / Size).ToString());
                return _feePerByte;
            }
        }

        private UInt256 _hash = null;
        public UInt256 Hash
        {
            get
            {
                if (_hash == null)
                {
                    _hash = new UInt256(Crypto.Default.Hash256(this.GetHashData()));
                }
                return _hash;
            }
        }

        InventoryType IInventory.InventoryType => InventoryType.TX;
        public bool IsLowPriority => NetworkFee < long.Parse(ProtocolSettings.Default.LowPriorityThreshold.ToString());

        private IReadOnlyDictionary<CoinReference, TransactionOutput> _references;
        public IReadOnlyDictionary<CoinReference, TransactionOutput> References
        {
            get
            {
                if (_references == null)
                {
                    Dictionary<CoinReference, TransactionOutput> dictionary = new Dictionary<CoinReference, TransactionOutput>();
                    foreach (var group in Inputs.GroupBy(p => p.PrevHash))
                    {
                        Transaction tx = Blockchain.Singleton.Store.GetTransaction(group.Key);
                        if (tx == null) return null;
                        foreach (var reference in group.Select(p => new
                        {
                            Input = p,
                            Output = tx.Outputs[p.PrevIndex]
                        }))
                        {
                            dictionary.Add(reference.Input, reference.Output);
                        }
                    }
                    _references = dictionary;
                }
                return _references;
            }
        }

        public virtual int Size =>
            sizeof(byte) +              //Version
            Script.GetVarSize() +       //Script
            Sender.Size +               //Sender
            sizeof(long) +              //Gas
            sizeof(long) +              //NetworkFee
            Attributes.GetVarSize() +   //Attributes
            Inputs.GetVarSize() + Outputs.GetVarSize() + Witnesses.GetVarSize();

        public virtual Fixed8 SystemFee => ProtocolSettings.Default.SystemFee.TryGetValue(Type, out Fixed8 fee) ? fee : Fixed8.Zero;

        //By BHP
        public virtual int OutputSize => Outputs.GetVarSize();

        //By BHP
        public virtual Fixed8 TxFee
        {
            get
            {
                return Fixed8.Zero;
                //return Type == TransactionType.ContractTransaction ? BhpTxFee.CalcuTxFee(this) : Fixed8.Zero;
            }
        }

        protected Transaction(TransactionType type)
        {
            this.Type = type;
        }

        public void CalculateGas()
        {
            if (Sender is null) Sender = UInt160.Zero;
            if (Attributes is null) Attributes = new TransactionAttribute[0];
            if (Witnesses is null) Witnesses = new Witness[0];
            _hash = null;
            long consumed;
            using (ApplicationEngine engine = ApplicationEngine.Run(Script, this))
            {
                if (engine.State.HasFlag(VMState.FAULT))
                    throw new InvalidOperationException();
                consumed = engine.GasConsumed;
            }
            _hash = null;
            long d = (long)NativeContract.GAS.Factor;
            Gas = consumed - ApplicationEngine.GasFree;
            if (Gas <= 0)
            {
                Gas = 0;
            }
            else
            {
                long remainder = Gas % d;
                if (remainder == 0) return;
                if (remainder > 0)
                    Gas += d - remainder;
                else
                    Gas -= remainder;
            }
        }

        void ISerializable.Deserialize(BinaryReader reader)
        {
            ((IVerifiable)this).DeserializeUnsigned(reader);
            Witnesses = reader.ReadSerializableArray<Witness>();
            OnDeserialized();
        }

        protected virtual void DeserializeExclusiveData(BinaryReader reader)
        {

        }

        public static Transaction DeserializeFrom(byte[] value, int offset = 0)
        {
            using (MemoryStream ms = new MemoryStream(value, offset, value.Length - offset, false))
            using (BinaryReader reader = new BinaryReader(ms, Encoding.UTF8))
            {
                return DeserializeFrom(reader);
            }
        }

        internal static Transaction DeserializeFrom(BinaryReader reader)
        {
            // Looking for type in reflection cache
            Transaction transaction = ReflectionCache.CreateInstance<Transaction>(reader.ReadByte());
            if (transaction == null) throw new FormatException();

            transaction.DeserializeUnsignedWithoutType(reader);
            transaction.Witnesses = reader.ReadSerializableArray<Witness>();
            transaction.OnDeserialized();
            return transaction;
        }

        void IVerifiable.DeserializeUnsigned(BinaryReader reader)
        {
            if ((TransactionType)reader.ReadByte() != Type)
                throw new FormatException();
            DeserializeUnsignedWithoutType(reader);
        }

        private void DeserializeUnsignedWithoutType(BinaryReader reader)
        {
            if (Version > 0) throw new FormatException();
            Script = reader.ReadVarBytes(ushort.MaxValue);
            if (Script.Length == 0) throw new FormatException();
            Sender = reader.ReadSerializable<UInt160>();
            Gas = reader.ReadInt64();
            if (Gas < 0) throw new FormatException();
            if (Gas % NativeContract.GAS.Factor != 0) throw new FormatException();
            NetworkFee = reader.ReadInt64();
            if (NetworkFee < 0) throw new FormatException();
            if (Gas + NetworkFee < Gas) throw new FormatException();
            Attributes = reader.ReadSerializableArray<TransactionAttribute>(MaxTransactionAttributes);
            Inputs = reader.ReadSerializableArray<CoinReference>();
            Outputs = reader.ReadSerializableArray<TransactionOutput>(ushort.MaxValue + 1);
        }

        public bool Equals(Transaction other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Hash.Equals(other.Hash);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Transaction);
        }

        public override int GetHashCode()
        {
            return Hash.GetHashCode();
        }
        
        public virtual UInt160[] GetScriptHashesForVerifying(Snapshot snapshot)
        {
            HashSet<UInt160> hashes = new HashSet<UInt160> { Sender };
            hashes.UnionWith(Attributes.Where(p => p.Usage == TransactionAttributeUsage.Cosigner).Select(p => new UInt160(p.Data)));
            return hashes.OrderBy(p => p).ToArray();
        }

        public IEnumerable<TransactionResult> GetTransactionResults()
        {
            if (References == null) return null;
            return References.Values.Select(p => new
            {
                p.AssetId,
                p.Value
            }).Concat(Outputs.Select(p => new
            {
                p.AssetId,
                Value = -p.Value
            })).GroupBy(p => p.AssetId, (k, g) => new TransactionResult
            {
                AssetId = k,
                Amount = g.Sum(p => p.Value)
            }).Where(p => p.Amount != Fixed8.Zero);
        }

        protected virtual void OnDeserialized()
        {
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            ((IVerifiable)this).SerializeUnsigned(writer);
            writer.Write(Witnesses);
        }

        protected virtual void SerializeExclusiveData(BinaryWriter writer)
        {
        }

        void IVerifiable.SerializeUnsigned(BinaryWriter writer)
        {
            writer.Write((byte)Type);
            writer.Write(Version);
            writer.Write(Sender);
            SerializeExclusiveData(writer);
            writer.Write(Attributes);
            writer.Write(Inputs);
            writer.Write(Outputs);
        }

        public virtual JObject ToJson()
        {
            JObject json = new JObject();
            json["txid"] = Hash.ToString();
            json["size"] = Size;
            json["type"] = Type;
            json["version"] = Version;
            json["sender"] = Sender.ToAddress();
            json["attributes"] = Attributes.Select(p => p.ToJson()).ToArray();
            json["vin"] = Inputs.Select(p => p.ToJson()).ToArray();
            json["vout"] = Outputs.Select((p, i) => p.ToJson((ushort)i)).ToArray();
            json["sys_fee"] = SystemFee.ToString();
            json["net_fee"] = NetworkFee.ToString();
            json["tx_fee"] = TxFee.ToString();
            json["scripts"] = Witnesses.Select(p => p.ToJson()).ToArray();
            return json;
        }

        bool IInventory.Verify(Snapshot snapshot)
        {
            return Verify(snapshot, Enumerable.Empty<Transaction>());
        }

        /*
        public virtual bool Verify(Snapshot snapshot, IEnumerable<Transaction> mempool)
        {
            if (Size > MaxTransactionSize) return false;
            for (int i = 1; i < Inputs.Length; i++)
                for (int j = 0; j < i; j++)
                    if (Inputs[i].PrevHash == Inputs[j].PrevHash && Inputs[i].PrevIndex == Inputs[j].PrevIndex)
                        return false;
            if (mempool.Where(p => p != this).SelectMany(p => p.Inputs).Intersect(Inputs).Count() > 0)
                return false;
            if (snapshot.IsDoubleSpend(this))
                return false;
            foreach (var group in Outputs.GroupBy(p => p.AssetId))
            {
                AssetState asset = snapshot.Assets.TryGet(group.Key);
                if (asset == null) return false;
                if (asset.Expiration <= snapshot.Height + 1 && asset.AssetType != AssetType.GoverningToken && asset.AssetType != AssetType.UtilityToken)
                    return false;
                foreach (TransactionOutput output in group)
                    if (output.Value.GetData() % (long)Math.Pow(10, 8 - asset.Precision) != 0)
                        return false;
            }
            TransactionResult[] results = GetTransactionResults()?.ToArray();
            if (results == null) return false;
            TransactionResult[] results_destroy = results.Where(p => p.Amount > Fixed8.Zero).ToArray();
            if (results_destroy.Length > 1) return false;
            if (results_destroy.Length == 1 && results_destroy[0].AssetId != Blockchain.UtilityToken.Hash)
                return false;
            if (SystemFee > Fixed8.Zero && (results_destroy.Length == 0 || results_destroy[0].Amount < SystemFee))
                return false;
            TransactionResult[] results_issue = results.Where(p => p.Amount < Fixed8.Zero).ToArray();
            if (Type == TransactionType.MinerTransaction)
            {                
                if (results_issue.Any(p => p.AssetId != Blockchain.UtilityToken.Hash))
                    return false;
            }
            else
            {
                if (results_issue.Length > 0)
                    return false;
            }
            if (Attributes.Count(p => p.Usage == TransactionAttributeUsage.ECDH02 || p.Usage == TransactionAttributeUsage.ECDH03) > 1)
                return false;
            if (!VerifyReceivingScripts()) return false;
            return this.VerifyWitnesses(snapshot);
        }
        */

        public virtual bool Verify(Snapshot snapshot, IEnumerable<Transaction> mempool)
        {
            if (Size > MaxTransactionSize) return false;
            BigInteger balance = NativeContract.GAS.BalanceOf(snapshot, Sender);
            BigInteger fee = Gas + NetworkFee;
            if (balance < fee) return false;
            fee += mempool.Where(p => p != this && p.Sender.Equals(Sender)).Sum(p => p.Gas + p.NetworkFee);
            if (balance < fee) return false;
            for (int i = 1; i < Inputs.Length; i++)
                for (int j = 0; j < i; j++)
                    if (Inputs[i].PrevHash == Inputs[j].PrevHash && Inputs[i].PrevIndex == Inputs[j].PrevIndex)
                        return false;
            if (mempool.Where(p => p != this).SelectMany(p => p.Inputs).Intersect(Inputs).Count() > 0)
                return false;
            if (snapshot.IsDoubleSpend(this))
                return false;
            foreach (var group in Outputs.GroupBy(p => p.AssetId))
            {
                AssetState asset = snapshot.Assets.TryGet(group.Key);
                if (asset == null) return false;
                if (asset.Expiration <= snapshot.Height + 1 && asset.AssetType != AssetType.GoverningToken && asset.AssetType != AssetType.UtilityToken)
                    return false;
                foreach (TransactionOutput output in group)
                    if (output.Value.GetData() % (long)Math.Pow(10, 8 - asset.Precision) != 0)
                        return false;
            }
            TransactionResult[] results = GetTransactionResults()?.ToArray();
            if (results == null) return false;
            TransactionResult[] results_destroy = results.Where(p => p.Amount > Fixed8.Zero).ToArray();

            //By BHP
            if (BhpTxFee.Verify(this, results_destroy, SystemFee) == false) return false;

            TransactionResult[] results_issue = results.Where(p => p.Amount < Fixed8.Zero).ToArray();
            switch (Type)
            {
                //By BHP
                case TransactionType.MinerTransaction:
                    if (VerifyMiningTransaction.Verify(Outputs, Attributes) == false)
                        return false;
                    break;              
                default:
                    if (results_issue.Length > 0)
                        return false;
                    break;
            }
            if (Attributes.Count(p => p.Usage == TransactionAttributeUsage.ECDH02 || p.Usage == TransactionAttributeUsage.ECDH03) > 1)
                return false;
            if (!VerifyReceivingScripts()) return false;
            //By BHP
            if (VerifyTransactionContract.Verify(snapshot, this) == false) return false;
            return this.VerifyWitnesses(snapshot);
        }

        private bool VerifyReceivingScripts()
        {
            //TODO: run ApplicationEngine
            //foreach (UInt160 hash in Outputs.Select(p => p.Hash).Distinct())
            //{
            //    ContractState contract = Blockchain.Default.GetContract(hash);
            //    if (contract == null) continue;
            //    if (!contract.Payable) return false;
            //    using (StateReader service = new StateReader())
            //    {
            //        ApplicationEngine engine = new ApplicationEngine(TriggerType.VerificationR, this, Blockchain.Default, service, Fixed8.Zero);
            //        engine.LoadScript(contract.Script, false);
            //        using (ScriptBuilder sb = new ScriptBuilder())
            //        {
            //            sb.EmitPush(0);
            //            sb.Emit(OpCode.PACK);
            //            sb.EmitPush("receiving");
            //            engine.LoadScript(sb.ToArray(), false);
            //        }
            //        if (!engine.Execute()) return false;
            //        if (engine.EvaluationStack.Count != 1 || !engine.EvaluationStack.Pop().GetBoolean()) return false;
            //    }
            //}
            return true;
        }
    }
}
