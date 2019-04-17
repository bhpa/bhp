using Bhp.Ledger;
using Bhp.Network.P2P.Payloads;
using Bhp.Persistence;
using Bhp.VM;
using Bhp.VM.Types;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Bhp.SmartContract
{
    public class ApplicationEngine : ExecutionEngine
    {
        private const long ratio = 100000;
        private const long gas_free = 10 * 100000000;
        private readonly long gas_amount;
        private long gas_consumed = 0;
        private readonly bool testMode;
        private readonly Snapshot snapshot;

        public Fixed8 GasConsumed => new Fixed8(gas_consumed);
        public new BhpService Service => (BhpService)base.Service;

        public ApplicationEngine(TriggerType trigger, IScriptContainer container, Snapshot snapshot, Fixed8 gas, bool testMode = false)
            : base(container, Cryptography.Crypto.Default, snapshot, new BhpService(trigger, snapshot))
        {
            this.gas_amount = gas_free + gas.GetData();
            this.testMode = testMode;
            this.snapshot = snapshot;
        }

        private bool CheckArraySize(OpCode nextInstruction)
        {
            int size;
            switch (nextInstruction)
            {
                case OpCode.PACK:
                case OpCode.NEWARRAY:
                case OpCode.NEWSTRUCT:
                    {
                        if (CurrentContext.EvaluationStack.Count == 0) return false;
                        size = (int)CurrentContext.EvaluationStack.Peek().GetBigInteger();
                    }
                    break;
                case OpCode.SETITEM:
                    {
                        if (CurrentContext.EvaluationStack.Count < 3) return false;
                        if (!(CurrentContext.EvaluationStack.Peek(2) is Map map)) return true;
                        StackItem key = CurrentContext.EvaluationStack.Peek(1);
                        if (key is ICollection) return false;
                        if (map.ContainsKey(key)) return true;
                        size = map.Count + 1;
                    }
                    break;
                case OpCode.APPEND:
                    {
                        if (CurrentContext.EvaluationStack.Count < 2) return false;
                        if (!(CurrentContext.EvaluationStack.Peek(1) is Array array)) return false;
                        size = array.Count + 1;
                    }
                    break;
                default:
                    return true;
            }
            return size <= MaxArraySize;
        }

        private bool CheckInvocationStack(OpCode nextInstruction)
        {
            switch (nextInstruction)
            {
                case OpCode.CALL:
                case OpCode.APPCALL:
                case OpCode.CALL_I:
                case OpCode.CALL_E:
                case OpCode.CALL_ED:
                    if (InvocationStack.Count >= MaxInvocationStackSize) return false;
                    return true;
                default:
                    return true;
            }
        }
        
        /// <summary>
        /// Check if the BigInteger is allowed for numeric operations
        /// </summary>
        private bool CheckBigIntegers(OpCode nextInstruction)
        {
            switch (nextInstruction)
            {
                case OpCode.SHL:
                    {
                        BigInteger ishift = CurrentContext.EvaluationStack.Peek(0).GetBigInteger();

                        if ((ishift > Max_SHL_SHR || ishift < Min_SHL_SHR))
                            return false;

                        BigInteger x = CurrentContext.EvaluationStack.Peek(1).GetBigInteger();

                        if (!CheckBigInteger(x << (int)ishift))
                            return false;

                        break;
                    }
                case OpCode.SHR:
                    {
                        BigInteger ishift = CurrentContext.EvaluationStack.Peek(0).GetBigInteger();

                        if ((ishift > Max_SHL_SHR || ishift < Min_SHL_SHR))
                            return false;

                        BigInteger x = CurrentContext.EvaluationStack.Peek(1).GetBigInteger();

                        if (!CheckBigInteger(x >> (int)ishift))
                            return false;

                        break;
                    }
                case OpCode.INC:
                    {
                        BigInteger x = CurrentContext.EvaluationStack.Peek().GetBigInteger();

                        if (!CheckBigInteger(x) || !CheckBigInteger(x + 1))
                            return false;

                        break;
                    }
                case OpCode.DEC:
                    {
                        BigInteger x = CurrentContext.EvaluationStack.Peek().GetBigInteger();

                        if (!CheckBigInteger(x) || (x.Sign <= 0 && !CheckBigInteger(x - 1)))
                            return false;

                        break;
                    }
                case OpCode.ADD:
                    {
                        BigInteger x2 = CurrentContext.EvaluationStack.Peek().GetBigInteger();
                        BigInteger x1 = CurrentContext.EvaluationStack.Peek(1).GetBigInteger();

                        if (!CheckBigInteger(x2) || !CheckBigInteger(x1) || !CheckBigInteger(x1 + x2))
                            return false;

                        break;
                    }
                case OpCode.SUB:
                    {
                        BigInteger x2 = CurrentContext.EvaluationStack.Peek().GetBigInteger();
                        BigInteger x1 = CurrentContext.EvaluationStack.Peek(1).GetBigInteger();

                        if (!CheckBigInteger(x2) || !CheckBigInteger(x1) || !CheckBigInteger(x1 - x2))
                            return false;

                        break;
                    }
                case OpCode.MUL:
                    {
                        BigInteger x2 = CurrentContext.EvaluationStack.Peek().GetBigInteger();
                        BigInteger x1 = CurrentContext.EvaluationStack.Peek(1).GetBigInteger();

                        int lx1 = x1.ToByteArray().Length;

                        if (lx1 > MaxSizeForBigInteger)
                            return false;

                        int lx2 = x2.ToByteArray().Length;

                        if ((lx1 + lx2) > MaxSizeForBigInteger)
                            return false;

                        break;
                    }
                case OpCode.DIV:
                    {
                        BigInteger x2 = CurrentContext.EvaluationStack.Peek().GetBigInteger();
                        BigInteger x1 = CurrentContext.EvaluationStack.Peek(1).GetBigInteger();

                        if (!CheckBigInteger(x2) || !CheckBigInteger(x1))
                            return false;

                        break;
                    }
                case OpCode.MOD:
                    {
                        BigInteger x2 = CurrentContext.EvaluationStack.Peek().GetBigInteger();
                        BigInteger x1 = CurrentContext.EvaluationStack.Peek(1).GetBigInteger();

                        if (!CheckBigInteger(x2) || !CheckBigInteger(x1))
                            return false;

                        break;
                    }
            }

            return true;
        }

        private bool CheckDynamicInvoke(OpCode nextInstruction)
        {
            switch (nextInstruction)
            {
                case OpCode.APPCALL:
                case OpCode.TAILCALL:
                    for (int i = CurrentContext.InstructionPointer + 1; i < CurrentContext.InstructionPointer + 21; i++)
                    {
                        if (CurrentContext.Script[i] != 0) return true;
                    }
                    // if we get this far it is a dynamic call
                    // now look at the current executing script
                    // to determine if it can do dynamic calls
                    return snapshot.Contracts[new UInt160(CurrentContext.ScriptHash)].HasDynamicInvoke;
                case OpCode.CALL_ED:
                case OpCode.CALL_EDT:
                    return snapshot.Contracts[new UInt160(CurrentContext.ScriptHash)].HasDynamicInvoke;
                default:
                    return true;
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            Service.Dispose();
        }

        private static int GetItemCount(IEnumerable<StackItem> items)
        {
            Queue<StackItem> queue = new Queue<StackItem>(items);
            List<StackItem> counted = new List<StackItem>();
            int count = 0;
            while (queue.Count > 0)
            {
                StackItem item = queue.Dequeue();
                count++;
                switch (item)
                {
                    case Array array:
                        if (counted.Any(p => ReferenceEquals(p, array)))
                            continue;
                        counted.Add(array);
                        foreach (StackItem subitem in array)
                            queue.Enqueue(subitem);
                        break;
                    case Map map:
                        if (counted.Any(p => ReferenceEquals(p, map)))
                            continue;
                        counted.Add(map);
                        foreach (StackItem subitem in map.Values)
                            queue.Enqueue(subitem);
                        break;
                }
            }
            return count;
        }

        protected virtual long GetPrice(OpCode nextInstruction)
        {
            if (nextInstruction <= OpCode.NOP) return 0;
            switch (nextInstruction)
            {
                case OpCode.APPCALL:
                case OpCode.TAILCALL:
                    return 10;
                case OpCode.SYSCALL:
                    return GetPriceForSysCall();
                case OpCode.SHA1:
                case OpCode.SHA256:
                    return 10;
                case OpCode.HASH160:
                case OpCode.HASH256:
                    return 20;
                case OpCode.CHECKSIG:
                case OpCode.VERIFY:
                    return 100;
                case OpCode.CHECKMULTISIG:
                    {
                        if (CurrentContext.EvaluationStack.Count == 0) return 1;

                        var item = CurrentContext.EvaluationStack.Peek();

                        int n;
                        if (item is Array array) n = array.Count;
                        else n = (int)item.GetBigInteger();

                        if (n < 1) return 1;
                        return 100 * n;
                    }
                default: return 1;
            }
        }

        protected virtual long GetPriceForSysCall()
        {
            Instruction instruction = CurrentContext.CurrentInstruction;
            uint api_hash = instruction.Operand.Length == 4
                ? instruction.TokenU32
                : instruction.TokenString.ToInteropMethodHash();
            long price = Service.GetPrice(api_hash);
            if (price > 0) return price;
            if (api_hash == "Bhp.Asset.Create".ToInteropMethodHash() ||
               api_hash == "AntShares.Asset.Create".ToInteropMethodHash())
                return 5000L * 100000000L / ratio;
            if (api_hash == "Bhp.Asset.Renew".ToInteropMethodHash() ||
                api_hash == "AntShares.Asset.Renew".ToInteropMethodHash())
                return (byte)CurrentContext.EvaluationStack.Peek(1).GetBigInteger() * 5000L * 100000000L / ratio;
            if (api_hash == "Bhp.Contract.Create".ToInteropMethodHash() ||
                api_hash == "Bhp.Contract.Migrate".ToInteropMethodHash() ||
                api_hash == "AntShares.Contract.Create".ToInteropMethodHash() ||
                api_hash == "AntShares.Contract.Migrate".ToInteropMethodHash())
            {
                long fee = 100L;

                ContractPropertyState contract_properties = (ContractPropertyState)(byte)CurrentContext.EvaluationStack.Peek(3).GetBigInteger();

                if (contract_properties.HasFlag(ContractPropertyState.HasStorage))
                {
                    fee += 400L;
                }
                if (contract_properties.HasFlag(ContractPropertyState.HasDynamicInvoke))
                {
                    fee += 500L;
                }
                return fee * 100000000L / ratio;
            }
            if (api_hash == "System.Storage.Put".ToInteropMethodHash() ||
                api_hash == "System.Storage.PutEx".ToInteropMethodHash() ||
                api_hash == "Bhp.Storage.Put".ToInteropMethodHash() ||
                api_hash == "AntShares.Storage.Put".ToInteropMethodHash())
                return ((CurrentContext.EvaluationStack.Peek(1).GetByteArray().Length + CurrentContext.EvaluationStack.Peek(2).GetByteArray().Length - 1) / 1024 + 1) * 1000;
            return 1;
        }
        
        public static ApplicationEngine Run(byte[] script, Snapshot snapshot,
            IScriptContainer container = null, Block persistingBlock = null, bool testMode = false, Fixed8 extraGAS = default(Fixed8))
        {
            snapshot.PersistingBlock = persistingBlock ?? snapshot.PersistingBlock ?? new Block
            {
                Version = 0,
                PrevHash = snapshot.CurrentBlockHash,
                MerkleRoot = new UInt256(),
                Timestamp = snapshot.Blocks[snapshot.CurrentBlockHash].TrimmedBlock.Timestamp + Blockchain.SecondsPerBlock,
                Index = snapshot.Height + 1,
                ConsensusData = 0,
                NextConsensus = snapshot.Blocks[snapshot.CurrentBlockHash].TrimmedBlock.NextConsensus,
                Witness = new Witness
                {
                    InvocationScript = new byte[0],
                    VerificationScript = new byte[0]
                },
                Transactions = new Transaction[0]
            };
            ApplicationEngine engine = new ApplicationEngine(TriggerType.Application, container, snapshot, extraGAS, testMode);
            engine.LoadScript(script);
            engine.Execute();
            return engine;
        }

        public static ApplicationEngine Run(byte[] script, IScriptContainer container = null, Block persistingBlock = null, bool testMode = false, Fixed8 extraGAS = default(Fixed8))
        {
            using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot())
            {
                return Run(script, snapshot, container, persistingBlock, testMode, extraGAS);
            }
        }
    }
}
