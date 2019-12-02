using Bhp.Ledger;
using Bhp.Network.P2P.Payloads;
using Bhp.Persistence;
using Bhp.SmartContract;
using Bhp.VM;
using Bhp.Wallets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Bhp.BhpExtensions.Transactions
{
    /// <summary>
    /// Verify Transaction Contract
    /// </summary>
    public class VerifyTransactionContract
    {
        /*
       public static bool Verify(StoreView snapshot, Transaction tx)
       {
           return true;
       }

       public static Coin[] checkUtxo(Coin[] unspentsAsset)
       {
           return unspentsAsset;
       }
        */

        /*
        public static bool Verify(StoreView snapshot, Transaction tx)
        {
            foreach (CoinReference item in tx.Inputs)
            {
                Transaction preTx = Blockchain.Singleton.GetTransaction(item.PrevHash);
                TransactionAttribute[] attribute = preTx.Attributes;
                foreach (TransactionAttribute att in attribute)
                {
                    if (att.Usage == TransactionAttributeUsage.SmartContractScript)
                    {
                        BinaryReader OpReader = new BinaryReader(new MemoryStream(att.Data, false));
                        OpCode opcode = (OpCode)OpReader.ReadByte();
                        int n = ParseIndex(OpReader, opcode);

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

        public static Coin[] checkUtxo(Coin[] unspentsAsset)
        {
            List<Coin> unspents = unspentsAsset.ToList();
            foreach (var item in unspentsAsset)
            {
                Transaction preTx = Blockchain.Singleton.GetTransaction(item.Reference.PrevHash);
                TransactionAttribute[] attribute = preTx.Attributes;
                using (Persistence.StoreView snapshot = Blockchain.Singleton.GetSnapshot())
                {
                    foreach (TransactionAttribute att in attribute)
                    {
                        if (att.Usage == TransactionAttributeUsage.SmartContractScript)
                        {
                            BinaryReader OpReader = new BinaryReader(new MemoryStream(att.Data, false));
                            OpCode opcode = (OpCode)OpReader.ReadByte();
                            int n = ParseIndex(OpReader, opcode);

                            if (item.Reference.PrevIndex != n)
                            {
                                using (ApplicationEngine engine = new ApplicationEngine(TriggerType.Verification, null, snapshot, Fixed8.Zero))
                                {
                                    engine.LoadScript(OpReader.ReadBytes(OpReader.ReadByte()));
                                    if (!engine.Execute() || engine.ResultStack.Count != 1 || !engine.ResultStack.Pop().GetBoolean())
                                    {
                                        unspents.Remove(item);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return unspents.ToArray();

            private static int ParseIndex(BinaryReader OpReader, OpCode opcode)
            {
                int n = -1;
                if (opcode >= OpCode.PUSHBYTES1 && opcode <= OpCode.PUSHBYTES75)
                    n = BitConverter.ToInt16(OpReader.ReadBytes((byte)opcode), 0);
                else
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
                return n;
            }
        }
        */

        public static bool Verify(StoreView snapshot, Transaction tx)
        {
            foreach (CoinReference item in tx.Inputs)
            {
                Transaction preTx = Blockchain.Singleton.GetTransaction(item.PrevHash);
                TransactionAttribute[] attribute = preTx.Attributes;
                foreach (TransactionAttribute att in attribute)
                {
                    if (att.Usage == TransactionAttributeUsage.SmartContractScript)
                    {
                        BinaryReader OpReader = new BinaryReader(new MemoryStream(att.Data, false));
                        List<int> changeNum = ParseIndexList(OpReader);

                        if (!changeNum.Contains(item.PrevIndex))
                        {
                            using (ApplicationEngine engine = new ApplicationEngine(TriggerType.Verification, null, snapshot, 0))
                            {
                                engine.LoadScript(OpReader.ReadBytes(OpReader.ReadByte()));
                                engine.Execute();
                                if (engine.State.HasFlag(VMState.FAULT)) return false;
                                if (engine.ResultStack.Count != 1 || !engine.ResultStack.Pop().GetBoolean()) return false;
                            }
                        }
                    }
                }
            }
            return true;
        }

        public static Coin[] checkUtxo(Coin[] unspentsAsset)
        {
            List<Coin> unspents = unspentsAsset.ToList();
            foreach (var item in unspentsAsset)
            {
                Transaction preTx = Blockchain.Singleton.GetTransaction(item.Reference.PrevHash);
                TransactionAttribute[] attribute = preTx.Attributes;
                using (Persistence.StoreView snapshot = Blockchain.Singleton.GetSnapshot())
                {
                    foreach (TransactionAttribute att in attribute)
                    {
                        if (att.Usage == TransactionAttributeUsage.SmartContractScript)
                        {
                            BinaryReader OpReader = new BinaryReader(new MemoryStream(att.Data, false));
                            List<int> changeNum = ParseIndexList(OpReader);

                            if (!changeNum.Contains(item.Reference.PrevIndex))
                            {
                                using (ApplicationEngine engine = new ApplicationEngine(TriggerType.Verification, null, snapshot, 0))
                                {
                                    engine.LoadScript(OpReader.ReadBytes(OpReader.ReadByte()));
                                    engine.Execute();                                    
                                    if (engine.State.HasFlag(VMState.FAULT) || engine.ResultStack.Count != 1 || !engine.ResultStack.Pop().GetBoolean())
                                    {
                                        unspents.Remove(item);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return unspents.ToArray();
        }

        private static List<int> ParseIndexList(BinaryReader OpReader)
        {
            List<int> changeNum = new List<int>();
            OpCode opcode = (OpCode)OpReader.ReadByte();
            int count = ParseIndex(OpReader, opcode);
            for (int i = 0; i < count; i++)
            {
                changeNum.Add(ParseIndex(OpReader, (OpCode)OpReader.ReadByte()));
            }
            return changeNum;
        }

        private static int ParseIndex(BinaryReader OpReader, OpCode opcode)
        {
            int n = -1;
            if (opcode >= OpCode.PUSHBYTES1 && opcode <= OpCode.PUSHBYTES75)
                n = BitConverter.ToInt16(OpReader.ReadBytes((byte)opcode), 0);
            else
                switch (opcode)
                {
                    case OpCode.PUSH0:
                        n = 0;
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
            return n;
        }

    }
}
