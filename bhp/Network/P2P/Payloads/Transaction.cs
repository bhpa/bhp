using Bhp.Cryptography;
using Bhp.Cryptography.ECC;
using Bhp.IO;
using Bhp.IO.Caching;
using Bhp.IO.Json;
using Bhp.Ledger;
using Bhp.Mining;
using Bhp.Persistence;
using Bhp.SmartContract;
using Bhp.VM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Bhp.Network.P2P.Payloads
{
    public abstract class Transaction : IEquatable<Transaction>, IInventory
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
        public TransactionAttribute[] Attributes;
        public CoinReference[] Inputs;
        public TransactionOutput[] Outputs;
        public Witness[] Witnesses { get; set; }

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

        public bool IsLowPriority => Type == TransactionType.ClaimTransaction || NetworkFee < Settings.Default.LowPriorityThreshold;

        private Fixed8 _network_fee = -Fixed8.Satoshi;
        public virtual Fixed8 NetworkFee
        {
            get
            {
                if (_network_fee == -Fixed8.Satoshi)
                {
                    Fixed8 input = References.Values.Where(p => p.AssetId.Equals(Blockchain.UtilityToken.Hash)).Sum(p => p.Value);
                    Fixed8 output = Outputs.Where(p => p.AssetId.Equals(Blockchain.UtilityToken.Hash)).Sum(p => p.Value);
                    _network_fee = input - output - SystemFee;
                }
                return _network_fee;
            }
        }

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

        public virtual int Size => sizeof(TransactionType) + sizeof(byte) + Attributes.GetVarSize() + Inputs.GetVarSize() + Outputs.GetVarSize() + Witnesses.GetVarSize();

        public virtual Fixed8 SystemFee => Settings.Default.SystemFee.TryGetValue(Type, out Fixed8 fee) ? fee : Fixed8.Zero;

        protected Transaction(TransactionType type)
        {
            this.Type = type;
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
            Version = reader.ReadByte();
            DeserializeExclusiveData(reader);
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

        byte[] IScriptContainer.GetMessage()
        {
            return this.GetHashData();
        }

        public virtual UInt160[] GetScriptHashesForVerifying(Snapshot snapshot)
        {
            if (References == null) throw new InvalidOperationException();
            HashSet<UInt160> hashes = new HashSet<UInt160>(Inputs.Select(p => References[p].ScriptHash));
            hashes.UnionWith(Attributes.Where(p => p.Usage == TransactionAttributeUsage.Script).Select(p => new UInt160(p.Data)));
            foreach (var group in Outputs.GroupBy(p => p.AssetId))
            {
                AssetState asset = snapshot.Assets.TryGet(group.Key);
                if (asset == null) throw new InvalidOperationException();
                if (asset.AssetType.HasFlag(AssetType.DutyFlag))
                {
                    hashes.UnionWith(group.Select(p => p.ScriptHash));
                }
            }
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
            json["attributes"] = Attributes.Select(p => p.ToJson()).ToArray();
            json["vin"] = Inputs.Select(p => p.ToJson()).ToArray();
            json["vout"] = Outputs.Select((p, i) => p.ToJson((ushort)i)).ToArray();
            json["sys_fee"] = SystemFee.ToString();
            json["net_fee"] = NetworkFee.ToString();
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
            switch (Type)
            {
                case TransactionType.MinerTransaction:
                case TransactionType.ClaimTransaction:
                    if (results_issue.Any(p => p.AssetId != Blockchain.UtilityToken.Hash))
                        return false;
                    break;
                case TransactionType.IssueTransaction:
                    if (results_issue.Any(p => p.AssetId == Blockchain.UtilityToken.Hash))
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
            return this.VerifyWitnesses(snapshot);
        }
        */

        /*
        public virtual bool Verify(Snapshot snapshot, IEnumerable<Transaction> mempool)
        {
            //Console.WriteLine("Transaction Verify");
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
            //输入大于输出
            TransactionResult[] results_destroy = results.Where(p => p.Amount > Fixed8.Zero).ToArray();
            
            //ServiceFee By BHP
            if (Type != TransactionType.ContractTransaction)
            {
                if (results_destroy.Length > 1) return false;
                if (results_destroy.Length == 1 && results_destroy[0].AssetId != Blockchain.UtilityToken.Hash)
                    return false;
            }
            else
            {
                if (results_destroy.Length > 2) return false;
                if (results_destroy.Length == 1 && results_destroy[0].AssetId != Blockchain.UtilityToken.Hash && 
                    results_destroy[0].AssetId != Blockchain.GoverningToken.Hash)
                    return false;
            }
            
            if (SystemFee > Fixed8.Zero && (results_destroy.Length == 0 || results_destroy[0].Amount < SystemFee))
                return false;
            TransactionResult[] results_issue = results.Where(p => p.Amount < Fixed8.Zero).ToArray();
            switch (Type)
            {
                //MiningOutput
                case TransactionType.MinerTransaction:
                    if (VerifyMinerTransaction() == false)
                        return false;
                    break;
                //case TransactionType.MinerTransaction:
                case TransactionType.ClaimTransaction:
                    if (results_issue.Any(p => p.AssetId != Blockchain.UtilityToken.Hash))
                        return false;
                    break;
                case TransactionType.IssueTransaction:
                    if (results_issue.Any(p => p.AssetId == Blockchain.UtilityToken.Hash))
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
            if (!VerifyAttributes(snapshot)) return false;//by bhp contract script in attribute
            return this.VerifyWitnesses(snapshot);
        }
        */

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
            switch (Type)
            {
                case TransactionType.MinerTransaction:
                    if (VerifyMinerTransaction() == false)
                        return false;
                    break;
                case TransactionType.ClaimTransaction:
                    if (results_issue.Any(p => p.AssetId != Blockchain.UtilityToken.Hash))
                        return false;
                    break;
                case TransactionType.IssueTransaction:
                    if (results_issue.Any(p => p.AssetId == Blockchain.UtilityToken.Hash))
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
            return this.VerifyWitnesses(snapshot);
        }

        /// <summary>
        /// Verify Tx by BHP
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="mempool"></param>
        /// <returns></returns>
        public virtual string VerifyTx(Snapshot snapshot, IEnumerable<Transaction> mempool)
        {
            if (Size > MaxTransactionSize) return "The data is too long.";
            for (int i = 1; i < Inputs.Length; i++)
                for (int j = 0; j < i; j++)
                    if (Inputs[i].PrevHash == Inputs[j].PrevHash && Inputs[i].PrevIndex == Inputs[j].PrevIndex)
                        return "The transaction input is repeated.";
            if (mempool.Where(p => p != this).SelectMany(p => p.Inputs).Intersect(Inputs).Count() > 0)
                return "Transaction input already exists.";

            string res = snapshot.IsDoubleSpendByBhp(this);
            if ("success".Equals(res) == false)
            {
                return res;
            }

            foreach (var group in Outputs.GroupBy(p => p.AssetId))
            {
                AssetState asset = snapshot.Assets.TryGet(group.Key);
                if (asset == null) return "asset is null.";
                if (asset.Expiration <= snapshot.Height + 1 && asset.AssetType != AssetType.GoverningToken && asset.AssetType != AssetType.UtilityToken)
                    return "Token expiration";
                foreach (TransactionOutput output in group)
                    if (output.Value.GetData() % (long)Math.Pow(10, 8 - asset.Precision) != 0)
                        return "Transaction output value is invalid.";
            }
            TransactionResult[] results = GetTransactionResults()?.ToArray();
            if (results == null) return "TransactionResult is null.";
            TransactionResult[] results_destroy = results.Where(p => p.Amount > Fixed8.Zero).ToArray();
            if (results_destroy.Length > 1) return "Transaction input is not equal output than one.";
            if (results_destroy.Length == 1 && results_destroy[0].AssetId != Blockchain.UtilityToken.Hash)
                return "The first of transaction output is not MinerTransaction.";
            if (SystemFee > Fixed8.Zero && (results_destroy.Length == 0 || results_destroy[0].Amount < SystemFee))
                return "Transaction SystemFee is invalid.";

            //Transaction amount less than 0, because of input is zero.
            TransactionResult[] results_issue = results.Where(p => p.Amount < Fixed8.Zero).ToArray();
            switch (Type)
            {
                //MiningOutput
                case TransactionType.MinerTransaction:
                    if (VerifyMinerTransaction() == false)
                        return "MinerTransaction is invalid.";
                    break;
                //case TransactionType.MinerTransaction:
                case TransactionType.ClaimTransaction:
                    if (results_issue.Any(p => p.AssetId != Blockchain.UtilityToken.Hash))
                        return "ClaimTransaction is invalid.";
                    break;
                case TransactionType.IssueTransaction:
                    if (results_issue.Any(p => p.AssetId == Blockchain.UtilityToken.Hash))
                        return "IssueTransaction is invalid.";
                    break;
                default:
                    if (results_issue.Length > 0)
                        return "Transaction input must not be empty.";
                    break;
            }
            if (Attributes.Count(p => p.Usage == TransactionAttributeUsage.ECDH02 || p.Usage == TransactionAttributeUsage.ECDH03) > 1)
                return "ECDH02 and ECDH03 too much.";
            if (!VerifyReceivingScripts()) return "VerifyReceivingScripts is failure.";
            if (this.VerifyWitnesses(snapshot) == false) return "Verify Witnesses is failure.";
            return "success";
        }

        /// <summary>
        /// Verification of mining transactions
        /// It can only be signed by consensus nodes.
        /// </summary>
        /// <returns></returns>
        private bool VerifyMinerTransaction()
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
            if (Attributes.Count() < 1 || (Attributes[0].Usage != TransactionAttributeUsage.MinerSignature &&
                Attributes[0].Usage != TransactionAttributeUsage.Description))
            {
                return false;
            }

            //Description or MinerScript
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

            MiningOutputLedger ledger = new MiningOutputLedger
            {
                AssetId = output.AssetId,
                Value = output.Value,
                ScriptHash = output.ScriptHash
            };
            byte[] message = ledger.GetHashData();

            foreach (ECPoint publicKey in Blockchain.StandbyValidators)
            { 
                if (Crypto.Default.VerifySignature(message, signature, publicKey.ToArray()))
                {
                    //Console.WriteLine($"MinerTransaction VerifySignature Success. Miner {publicKey.ToString()}");
                    return true;
                }
            } 
            Console.WriteLine($"******MinerTransaction VerifySignature Fail.******");
            return false;
        }

        private bool VerifyReceivingScripts()
        {
            //TODO: run ApplicationEngine
            //foreach (UInt160 hash in Outputs.Select(p => p.ScriptHash).Distinct())
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

        /// <summary>
        /// verify contract script in attribute, by bhp
        /// </summary>
        /// <param name="snapshot"></param>
        /// <returns></returns>
        private bool VerifyAttributes(Snapshot snapshot)
        {
            foreach (CoinReference item in Inputs)
            {
                Transaction preTx = Blockchain.Singleton.GetTransaction(item.PrevHash);
                TransactionAttribute[] attribute = preTx.Attributes;
                foreach (TransactionAttribute att in attribute)
                {
                    if (att.Usage == TransactionAttributeUsage.SmartContractScript)
                    {
                        int n = -1;
                        BinaryReader OpReader = new BinaryReader(new MemoryStream(att.Data, false));
                        OpCode opcode = (OpCode)OpReader.ReadByte();
                        switch (opcode)
                        {
                            case OpCode.PUSH0:
                                break;
                            case OpCode.PUSHDATA1:
                                n = BitConverter.ToInt16(OpReader.ReadBytes(OpReader.ReadByte()), 0);
                                break;
                            case OpCode.PUSHDATA2:
                                n = BitConverter.ToInt16(OpReader.ReadBytes(OpReader.ReadUInt16()), 0);
                                break;
                            case OpCode.PUSHDATA4:
                                n = BitConverter.ToInt32(OpReader.ReadBytes((int)OpReader.ReadUInt32()), 0);
                                break;
                            case OpCode.PUSHM1:
                            case OpCode.PUSH1:
                            case OpCode.PUSH2:
                            case OpCode.PUSH3:
                            case OpCode.PUSH4:
                            case OpCode.PUSH5:
                            case OpCode.PUSH6:
                            case OpCode.PUSH7:
                            case OpCode.PUSH8:
                            case OpCode.PUSH9:
                            case OpCode.PUSH10:
                            case OpCode.PUSH11:
                            case OpCode.PUSH12:
                            case OpCode.PUSH13:
                            case OpCode.PUSH14:
                            case OpCode.PUSH15:
                            case OpCode.PUSH16:
                                n = (int)opcode - (int)OpCode.PUSH1 + 1;
                                break;
                        }
                        if (item.PrevIndex != n)
                        {
                            using (ApplicationEngine engine = new ApplicationEngine(TriggerType.Verification, null, snapshot, Fixed8.Zero))
                            {
                                engine.LoadScript(OpReader.ReadBytes(OpReader.ReadByte()));
                                if (!engine.Execute()) return false;
                                if (engine.ResultStack.Count != 1 || !engine.ResultStack.Pop().GetBoolean()) return false;
                            }
                        }
                    }
                }
            }
            return true;
        }

    }
}
