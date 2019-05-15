using Bhp.Ledger;
using Bhp.Network.P2P.Payloads;
using Bhp.Persistence;
using Bhp.SmartContract.Native;
using Bhp.VM;
using System;
using System.Collections.Generic;
using VMArray = Bhp.VM.Types.Array;

namespace Bhp.SmartContract
{
    public class ApplicationEngine : ExecutionEngine
    {
        public static event EventHandler<NotifyEventArgs> Notify;
        public static event EventHandler<LogEventArgs> Log;

        private const long ratio = 100000;
        private static readonly long gas_free = 10 * (long)NativeContract.GAS.Factor;
        private readonly long gas_amount;
        private readonly bool testMode;
        private readonly RandomAccessStack<UInt160> hashes = new RandomAccessStack<UInt160>();
        private readonly List<NotifyEventArgs> notifications = new List<NotifyEventArgs>();
        private readonly List<IDisposable> disposables = new List<IDisposable>();

        public TriggerType Trigger { get; }
        public new IVerifiable ScriptContainer { get; }
        public Snapshot Snapshot { get; }
        public long GasConsumed { get; private set; } = 0;
        public UInt160 CurrentScriptHash => hashes.Count > 0 ? hashes.Peek() : null;
        public UInt160 CallingScriptHash => hashes.Count > 1 ? hashes.Peek(1) : null;
        public new UInt160 EntryScriptHash => hashes.Count > 0 ? hashes.Peek(hashes.Count - 1) : null;
        public IReadOnlyList<NotifyEventArgs> Notifications => notifications;

        public ApplicationEngine(TriggerType trigger, IVerifiable container, Snapshot snapshot, long gas, bool testMode = false)             
        {
            this.gas_amount = gas_free + gas;
            this.testMode = testMode;
            this.Trigger = trigger;
            this.ScriptContainer = container;
            this.Snapshot = snapshot;
            ContextLoaded += ApplicationEngine_ContextLoaded;
            ContextUnloaded += ApplicationEngine_ContextUnloaded;
        }

        internal T AddDisposable<T>(T disposable) where T : IDisposable
        {
            disposables.Add(disposable);
            return disposable;
        }

        private void ApplicationEngine_ContextLoaded(object sender, ExecutionContext e)
        {
            hashes.Push(((byte[])e.Script).ToScriptHash());
        }

        private void ApplicationEngine_ContextUnloaded(object sender, ExecutionContext e)
        {
            hashes.Pop();
        }

        public override void Dispose()
        {
            foreach (IDisposable disposable in disposables)
                disposable.Dispose();
            disposables.Clear();
            base.Dispose();
        }

        protected virtual long GetPrice()
        {
            Instruction instruction = CurrentContext.CurrentInstruction;
            if (instruction.OpCode <= OpCode.NOP) return 0;
            switch (instruction.OpCode)
            {
                case OpCode.SYSCALL:
                    return GetPriceForSysCall();
                case OpCode.SHA1:
                case OpCode.SHA256:
                    return 10;
                default: return 1;
            }
        }

        protected virtual long GetPriceForSysCall()
        {
            Instruction instruction = CurrentContext.CurrentInstruction;
            uint method = instruction.TokenU32;
            long price = InteropService.GetPrice(method);
            if (price > 0) return price;
            if (method == InteropService.Bhp_Crypto_CheckMultiSig)
            {
                if (CurrentContext.EvaluationStack.Count == 0) return 1;

                var item = CurrentContext.EvaluationStack.Peek();

                int n;
                if (item is VMArray array) n = array.Count;
                else n = (int)item.GetBigInteger();

                if (n < 1) return 1;
                return 100 * n;
            }
            if (method == InteropService.Bhp_Contract_Create ||
                method == InteropService.Bhp_Contract_Migrate)
            {
                long fee = 100L;

                ContractPropertyState contract_properties = (ContractPropertyState)(byte)CurrentContext.EvaluationStack.Peek(3).GetBigInteger();

                if (contract_properties.HasFlag(ContractPropertyState.HasStorage))
                {
                    fee += 400L;
                }
                return fee * (long)NativeContract.GAS.Factor / ratio;
            }
            if (method == InteropService.System_Storage_Put ||
                method == InteropService.System_Storage_PutEx)
                return ((CurrentContext.EvaluationStack.Peek(1).GetByteArray().Length + CurrentContext.EvaluationStack.Peek(2).GetByteArray().Length - 1) / 1024 + 1) * 1000;
            return 1;
        }

        protected override bool OnSysCall(uint method)
        {
            return InteropService.Invoke(this, method);
        }

        protected override bool PreExecuteInstruction()
        {
            if (CurrentContext.InstructionPointer >= CurrentContext.Script.Length)
                return true;
            GasConsumed = checked(GasConsumed + GetPrice() * ratio);
            if (!testMode && GasConsumed > gas_amount) return false;
            return true;
        }

        public static ApplicationEngine Run(byte[] script, Snapshot snapshot,
            IVerifiable container = null, Block persistingBlock = null, bool testMode = false, long extraGAS = default)
        {
            snapshot.PersistingBlock = persistingBlock ?? snapshot.PersistingBlock ?? new Block
            {
                Version = 0,
                PrevHash = snapshot.CurrentBlockHash,
                MerkleRoot = new UInt256(),
                Timestamp = snapshot.Blocks[snapshot.CurrentBlockHash].TrimmedBlock.Timestamp + Blockchain.SecondsPerBlock,
                Index = snapshot.Height + 1,
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

        public static ApplicationEngine Run(byte[] script, IVerifiable container = null, Block persistingBlock = null, bool testMode = false, long extraGAS = default)
        {
            using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot())
            {
                return Run(script, snapshot, container, persistingBlock, testMode, extraGAS);
            }
        }

        internal void SendLog(UInt160 script_hash, string message)
        {
            LogEventArgs log = new LogEventArgs(ScriptContainer, script_hash, message);
            Log?.Invoke(this, log);
        }

        internal void SendNotification(UInt160 script_hash, StackItem state)
        {
            NotifyEventArgs notification = new NotifyEventArgs(ScriptContainer, script_hash, state);
            Notify?.Invoke(this, notification);
            notifications.Add(notification);
        }
    }
}