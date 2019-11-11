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
using Bhp.VM.Types;

namespace Bhp.Network.P2P.Payloads
{
    public class Transaction : IEquatable<Transaction>, IInventory, IInteroperable
    {
        public const int MaxTransactionSize = 102400;
        public const uint MaxValidUntilBlockIncrement = 2102400;
        /// <summary>
        /// Maximum number of attributes that can be contained within a transaction
        /// </summary>
        private const int MaxTransactionAttributes = 16;
        /// <summary>
        /// Maximum number of cosigners that can be contained within a transaction
        /// </summary>
        private const int MaxCosigners = 16;
        /// <summary>
        /// Reflection cache for TransactionType
        /// </summary>
        private static ReflectionCache<byte> ReflectionCache = ReflectionCache<byte>.CreateFromEnum<TransactionType>();

        public readonly TransactionType Type;
        public byte Version;
        public uint Nonce;
        public UInt160 Sender;
        /// <summary>
        /// Distributed to BHP holders.
        /// </summary>
        public long SystemFee;
        /// <summary>
        /// Distributed to consensus nodes.
        /// </summary>
        public long NetworkFee;
        public uint ValidUntilBlock;
        public TransactionAttribute[] Attributes;
        public Cosigner[] Cosigners { get; set; }
        public CoinReference[] Inputs;
        public TransactionOutput[] Outputs;
        public byte[] Script;
        public Witness[] Witnesses { get; set; }

        private Fixed8 _feePerByte = -Fixed8.Satoshi;
        /// <summary>
        /// The <c>NetworkFee</c> for the transaction divided by its <c>Size</c>.
        /// <para>Note that this property must be used with care. Getting the value of this property multiple times will return the same result. The value of this property can only be obtained after the transaction has been completely built (no longer modified).</para>
        /// </summary>
        public long FeePerByte => NetworkFee / Size;

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

        public const int HeaderSize =
            sizeof(byte) +  //Version
            sizeof(uint) +  //Nonce
            20 +            //Sender
            sizeof(long) +  //SystemFee
            sizeof(long) +  //NetworkFee
            sizeof(uint);   //ValidUntilBlock

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

        public int Size => HeaderSize +
                   Attributes.GetVarSize() +   //Attributes
                   Cosigners.GetVarSize() +    //Cosigners
                   Script.GetVarSize() +       //Script
                   Witnesses.GetVarSize();     //Witnesses

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
            Nonce = reader.ReadUInt32();
            Sender = reader.ReadSerializable<UInt160>();
            SystemFee = reader.ReadInt64();
            if (SystemFee < 0) throw new FormatException();
            if (SystemFee % NativeContract.GAS.Factor != 0) throw new FormatException();
            NetworkFee = reader.ReadInt64();
            if (NetworkFee < 0) throw new FormatException();
            if (SystemFee + NetworkFee < SystemFee) throw new FormatException();
            ValidUntilBlock = reader.ReadUInt32();
            Attributes = reader.ReadSerializableArray<TransactionAttribute>(MaxTransactionAttributes);
            Cosigners = reader.ReadSerializableArray<Cosigner>(MaxCosigners);
            if (Cosigners.Select(u => u.Account).Distinct().Count() != Cosigners.Length) throw new FormatException();
            Inputs = reader.ReadSerializableArray<CoinReference>();
            Outputs = reader.ReadSerializableArray<TransactionOutput>(ushort.MaxValue + 1);
            var cosigners = Attributes.Where(p => p.Usage == TransactionAttributeUsage.Cosigner).Select(p => new UInt160(p.Data)).ToArray();
            if (cosigners.Distinct().Count() != cosigners.Length) throw new FormatException();
            Script = reader.ReadVarBytes(ushort.MaxValue);
            if (Script.Length == 0) throw new FormatException();
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

        public UInt160[] GetScriptHashesForVerifying(Snapshot snapshot)
        {
            var hashes = new HashSet<UInt160> { Sender };
            hashes.UnionWith(Cosigners.Select(p => p.Account));
            return hashes.OrderBy(p => p).ToArray();
        }

        public virtual bool Reverify(Snapshot snapshot, IEnumerable<Transaction> mempool)
        {
            if (ValidUntilBlock <= snapshot.Height || ValidUntilBlock > snapshot.Height + MaxValidUntilBlockIncrement)
                return false;
            if (NativeContract.Policy.GetBlockedAccounts(snapshot).Intersect(GetScriptHashesForVerifying(snapshot)).Count() > 0)
                return false;
            BigInteger balance = NativeContract.GAS.BalanceOf(snapshot, Sender);
            BigInteger fee = SystemFee + NetworkFee;
            if (balance < fee) return false;
            fee += mempool.Where(p => p != this && p.Sender.Equals(Sender)).Select(p => (BigInteger)(p.SystemFee + p.NetworkFee)).Sum();
            if (balance < fee) return false;
            UInt160[] hashes = GetScriptHashesForVerifying(snapshot);
            if (hashes.Length != Witnesses.Length) return false;
            for (int i = 0; i < hashes.Length; i++)
            {
                if (Witnesses[i].VerificationScript.Length > 0) continue;
                if (snapshot.Contracts.TryGet(hashes[i]) is null) return false;
            }
            return true;
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
            writer.Write(Nonce);
            writer.Write(Sender);
            writer.Write(ValidUntilBlock);
            SerializeExclusiveData(writer);
            writer.Write(Attributes);
            writer.Write(Cosigners);
            writer.Write(Inputs);
            writer.Write(Outputs);
        }

        public virtual JObject ToJson()
        {
            JObject json = new JObject();
            json["hash"] = Hash.ToString();
            json["size"] = Size;
            json["type"] = Type;
            json["version"] = Version;
            json["nonce"] = Nonce;
            json["sender"] = Sender.ToAddress();
            json["attributes"] = Attributes.Select(p => p.ToJson()).ToArray();
            json["cosigners"] = Cosigners.Select(p => p.ToJson()).ToArray();
            json["vin"] = Inputs.Select(p => p.ToJson()).ToArray();
            json["vout"] = Outputs.Select((p, i) => p.ToJson((ushort)i)).ToArray();
            json["sys_fee"] = SystemFee.ToString();
            json["net_fee"] = NetworkFee.ToString();
            json["valid_until_block"] = ValidUntilBlock;
            json["tx_fee"] = TxFee.ToString();
            json["script"] = Convert.ToBase64String(Script);
            json["witnesses"] = Witnesses.Select(p => p.ToJson()).ToArray();
            return json;
        }

        bool IInventory.Verify(Snapshot snapshot)
        {
            return Verify(snapshot, Enumerable.Empty<Transaction>());
        }

        public virtual bool Reverify(Snapshot snapshot, BigInteger totalSenderFeeFromPool)
        {
            if (ValidUntilBlock <= snapshot.Height || ValidUntilBlock > snapshot.Height + MaxValidUntilBlockIncrement)
                return false;
            if (NativeContract.Policy.GetBlockedAccounts(snapshot).Intersect(GetScriptHashesForVerifying(snapshot)).Count() > 0)
                return false;
            BigInteger balance = NativeContract.GAS.BalanceOf(snapshot, Sender);
            BigInteger fee = SystemFee + NetworkFee + totalSenderFeeFromPool;
            if (balance < fee) return false;
            UInt160[] hashes = GetScriptHashesForVerifying(snapshot);
            if (hashes.Length != Witnesses.Length) return false;
            for (int i = 0; i < hashes.Length; i++)
            {
                if (Witnesses[i].VerificationScript.Length > 0) continue;
                if (snapshot.Contracts.TryGet(hashes[i]) is null) return false;
            }
            return true;
        }

        public virtual bool Verify(Snapshot snapshot, BigInteger totalSenderFeeFromPool)
        {
            if (!Reverify(snapshot, totalSenderFeeFromPool)) return false;
            int size = Size;
            if (size > MaxTransactionSize) return false;
            long net_fee = NetworkFee - size * NativeContract.Policy.GetFeePerByte(snapshot);
            if (net_fee < 0) return false;
            return this.VerifyWitnesses(snapshot, net_fee);
        }

        public virtual bool Verify(Snapshot snapshot, IEnumerable<Transaction> mempool)
        {
            if (!Reverify(snapshot, mempool)) return false;
            int size = Size;
            if (size > MaxTransactionSize) return false;
            long net_fee = NetworkFee - size * NativeContract.Policy.GetFeePerByte(snapshot);
            if (net_fee < 0) return false;
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
            if (BhpTxFee.Verify(this, results_destroy, Fixed8.Parse(SystemFee.ToString())) == false) return false;

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
            return this.VerifyWitnesses(snapshot, net_fee);
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

        public static Transaction FromJson(JObject json)
        {
            Transaction tx = new Transaction();
            tx.Version = byte.Parse(json["version"].AsString());
            tx.Nonce = uint.Parse(json["nonce"].AsString());
            tx.Sender = json["sender"].AsString().ToScriptHash();
            tx.SystemFee = long.Parse(json["sys_fee"].AsString());
            tx.NetworkFee = long.Parse(json["net_fee"].AsString());
            tx.ValidUntilBlock = uint.Parse(json["valid_until_block"].AsString());
            tx.Attributes = ((JArray)json["attributes"]).Select(p => TransactionAttribute.FromJson(p)).ToArray();
            tx.Cosigners = ((JArray)json["cosigners"]).Select(p => Cosigner.FromJson(p)).ToArray();
            tx.Script = Convert.FromBase64String(json["script"].AsString());
            tx.Witnesses = ((JArray)json["witnesses"]).Select(p => Witness.FromJson(p)).ToArray();
            return tx;
        }

        public StackItem ToStackItem()
        {
            return new VM.Types.Array
            (
                new StackItem[]
                {
                    // Computed properties
                    new ByteArray(Hash.ToArray()),

                    // Transaction properties
                    new Integer(Version),
                    new Integer(Nonce),
                    new ByteArray(Sender.ToArray()),
                    new Integer(SystemFee),
                    new Integer(NetworkFee),
                    new Integer(ValidUntilBlock),
                    // Attributes
                    // Cosigners
                    new ByteArray(Script),
                    // Witnesses
                }
            );
        }
    }
}
