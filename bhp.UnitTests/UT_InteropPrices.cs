using Bhp.Ledger;
using Bhp.SmartContract;
using Bhp.VM;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Bhp.UnitTests
{
    class UT_InteropPrices
    {
        BhpService uut;

        [TestInitialize]
        public void TestSetup()
        {
            uut = new BhpService(TriggerType.Application, null);
        }

        [TestMethod]
        public void BhpServiceFixedPriceWithReflection()
        {
            // testing reflection with public methods too
            MethodInfo GetPrice = typeof(BhpService).GetMethod("GetPrice", BindingFlags.InvokeMethod | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(uint) }, null);
            GetPrice.Invoke(uut, new object[] { "Bhp.Runtime.CheckWitness".ToInteropMethodHash() }).Should().Be(200L);
        }

        [TestMethod]
        public void BhpServiceFixedPrices()
        {
            uut.GetPrice("Bhp.Runtime.GetTrigger".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Runtime.CheckWitness".ToInteropMethodHash()).Should().Be(200);
            uut.GetPrice("Bhp.Runtime.Notify".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Runtime.Log".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Runtime.GetTime".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Runtime.Serialize".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Runtime.Deserialize".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Blockchain.GetHeight".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Blockchain.GetHeader".ToInteropMethodHash()).Should().Be(100);
            uut.GetPrice("Bhp.Blockchain.GetBlock".ToInteropMethodHash()).Should().Be(200);
            uut.GetPrice("Bhp.Blockchain.GetTransaction".ToInteropMethodHash()).Should().Be(100);
            uut.GetPrice("Bhp.Blockchain.GetTransactionHeight".ToInteropMethodHash()).Should().Be(100);
            uut.GetPrice("Bhp.Blockchain.GetAccount".ToInteropMethodHash()).Should().Be(100);
            uut.GetPrice("Bhp.Blockchain.GetValidators".ToInteropMethodHash()).Should().Be(200);
            uut.GetPrice("Bhp.Blockchain.GetAsset".ToInteropMethodHash()).Should().Be(100);
            uut.GetPrice("Bhp.Blockchain.GetContract".ToInteropMethodHash()).Should().Be(100);
            uut.GetPrice("Bhp.Header.GetHash".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Header.GetVersion".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Header.GetPrevHash".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Header.GetMerkleRoot".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Header.GetTimestamp".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Header.GetIndex".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Header.GetConsensusData".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Header.GetNextConsensus".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Block.GetTransactionCount".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Block.GetTransactions".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Block.GetTransaction".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Transaction.GetHash".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Transaction.GetType".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Transaction.GetAttributes".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Transaction.GetInputs".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Transaction.GetOutputs".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Transaction.GetReferences".ToInteropMethodHash()).Should().Be(200);
            uut.GetPrice("Bhp.Transaction.GetUnspentCoins".ToInteropMethodHash()).Should().Be(200);
            uut.GetPrice("Bhp.Transaction.GetWitnesses".ToInteropMethodHash()).Should().Be(200);
            uut.GetPrice("Bhp.InvocationTransaction.GetScript".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Witness.GetVerificationScript".ToInteropMethodHash()).Should().Be(100);
            uut.GetPrice("Bhp.Attribute.GetUsage".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Attribute.GetData".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Input.GetHash".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Input.GetIndex".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Output.GetAssetId".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Output.GetValue".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Output.GetScriptHash".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Account.GetScriptHash".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Account.GetVotes".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Account.GetBalance".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Account.IsStandard".ToInteropMethodHash()).Should().Be(100);
            uut.GetPrice("Bhp.Asset.GetAssetId".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Asset.GetAssetType".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Asset.GetAmount".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Asset.GetAvailable".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Asset.GetPrecision".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Asset.GetOwner".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Asset.GetAdmin".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Asset.GetIssuer".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Contract.Destroy".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Contract.GetScript".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Contract.IsPayable".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Contract.GetStorageContext".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Storage.GetContext".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Storage.GetReadOnlyContext".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Storage.Get".ToInteropMethodHash()).Should().Be(100);
            uut.GetPrice("Bhp.Storage.Delete".ToInteropMethodHash()).Should().Be(100);
            uut.GetPrice("Bhp.Storage.Find".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.StorageContext.AsReadOnly".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Enumerator.Create".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Enumerator.Next".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Enumerator.Value".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Enumerator.Concat".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Iterator.Create".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Iterator.Key".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Iterator.Keys".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Iterator.Values".ToInteropMethodHash()).Should().Be(1);

            #region Aliases
            uut.GetPrice("Bhp.Iterator.Next".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("Bhp.Iterator.Value".ToInteropMethodHash()).Should().Be(1);
            #endregion 
        }

        [TestMethod]
        public void StandardServiceFixedPrices()
        {
            uut.GetPrice("System.Runtime.Platform".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Runtime.GetTrigger".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Runtime.CheckWitness".ToInteropMethodHash()).Should().Be(200);
            uut.GetPrice("System.Runtime.Notify".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Runtime.Log".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Runtime.GetTime".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Runtime.Serialize".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Runtime.Deserialize".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Blockchain.GetHeight".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Blockchain.GetHeader".ToInteropMethodHash()).Should().Be(100);
            uut.GetPrice("System.Blockchain.GetBlock".ToInteropMethodHash()).Should().Be(200);
            uut.GetPrice("System.Blockchain.GetTransaction".ToInteropMethodHash()).Should().Be(200);
            uut.GetPrice("System.Blockchain.GetTransactionHeight".ToInteropMethodHash()).Should().Be(100);
            uut.GetPrice("System.Blockchain.GetContract".ToInteropMethodHash()).Should().Be(100);
            uut.GetPrice("System.Header.GetIndex".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Header.GetHash".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Header.GetPrevHash".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Header.GetTimestamp".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Block.GetTransactionCount".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Block.GetTransactions".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Block.GetTransaction".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Transaction.GetHash".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Contract.Destroy".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Contract.GetStorageContext".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Storage.GetContext".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Storage.GetReadOnlyContext".ToInteropMethodHash()).Should().Be(1);
            uut.GetPrice("System.Storage.Get".ToInteropMethodHash()).Should().Be(100);
            uut.GetPrice("System.Storage.Delete".ToInteropMethodHash()).Should().Be(100);
            uut.GetPrice("System.StorageContext.AsReadOnly".ToInteropMethodHash()).Should().Be(1);
        }

        [TestMethod]
        public void ApplicationEngineFixedPrices()
        {
            // ApplicationEngine.GetPriceForSysCall is protected, so we will access through reflection
            MethodInfo GetPriceForSysCall = typeof(ApplicationEngine).GetMethod("GetPriceForSysCall", BindingFlags.InvokeMethod | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new Type[] { }, null);

            // System.Runtime.CheckWitness: f827ec8c (price is 200)
            byte[] SyscallSystemRuntimeCheckWitnessHash = new byte[] { 0x68, 0x04, 0xf8, 0x27, 0xec, 0x8c };
            using (ApplicationEngine ae = new ApplicationEngine(TriggerType.Application, null, null, Fixed8.Zero))
            {
                ae.LoadScript(SyscallSystemRuntimeCheckWitnessHash);
                GetPriceForSysCall.Invoke(ae, new object[] { }).Should().Be(200L);
            }

            // "System.Runtime.CheckWitness" (27 bytes -> 0x1b) - (price is 200)
            byte[] SyscallSystemRuntimeCheckWitnessString = new byte[] { 0x68, 0x1b, 0x53, 0x79, 0x73, 0x74, 0x65, 0x6d, 0x2e, 0x52, 0x75, 0x6e, 0x74, 0x69, 0x6d, 0x65, 0x2e, 0x43, 0x68, 0x65, 0x63, 0x6b, 0x57, 0x69, 0x74, 0x6e, 0x65, 0x73, 0x73 };
            using (ApplicationEngine ae = new ApplicationEngine(TriggerType.Application, null, null, Fixed8.Zero))
            {
                ae.LoadScript(SyscallSystemRuntimeCheckWitnessString);
                GetPriceForSysCall.Invoke(ae, new object[] { }).Should().Be(200L);
            }

            // System.Storage.GetContext: 9bf667ce (price is 1)
            byte[] SyscallSystemStorageGetContextHash = new byte[] { 0x68, 0x04, 0x9b, 0xf6, 0x67, 0xce };
            using (ApplicationEngine ae = new ApplicationEngine(TriggerType.Application, null, null, Fixed8.Zero))
            {
                ae.LoadScript(SyscallSystemStorageGetContextHash);
                GetPriceForSysCall.Invoke(ae, new object[] { }).Should().Be(1L);
            }

            // System.Storage.Get: 925de831 (price is 100)
            byte[] SyscallSystemStorageGetHash = new byte[] { 0x68, 0x04, 0x92, 0x5d, 0xe8, 0x31 };
            using (ApplicationEngine ae = new ApplicationEngine(TriggerType.Application, null, null, Fixed8.Zero))
            {
                ae.LoadScript(SyscallSystemStorageGetHash);
                GetPriceForSysCall.Invoke(ae, new object[] { }).Should().Be(100L);
            }
        }

        [TestMethod]
        public void ApplicationEngineVariablePrices()
        {
            // ApplicationEngine.GetPriceForSysCall is protected, so we will access through reflection
            MethodInfo GetPriceForSysCall = typeof(ApplicationEngine).GetMethod("GetPriceForSysCall", BindingFlags.InvokeMethod | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new Type[] { }, null);

            // Bhp.Asset.Create: 83c5c61f
            byte[] SyscallAssetCreateHash = new byte[] { 0x68, 0x04, 0x83, 0xc5, 0xc6, 0x1f };
            using (ApplicationEngine ae = new ApplicationEngine(TriggerType.Application, null, null, Fixed8.Zero))
            {
                ae.LoadScript(SyscallAssetCreateHash);
                GetPriceForSysCall.Invoke(ae, new object[] { }).Should().Be(5000L * 100000000L / 100000); // assuming private ae.ratio = 100000
            }

            // Bhp.Asset.Renew: 78849071 (requires push 09 push 09 before)
            byte[] SyscallAssetRenewHash = new byte[] { 0x59, 0x59, 0x68, 0x04, 0x78, 0x84, 0x90, 0x71 };
            using (ApplicationEngine ae = new ApplicationEngine(TriggerType.Application, null, null, Fixed8.Zero))
            {
                ae.LoadScript(SyscallAssetRenewHash);
                ae.StepInto(); // push 9
                ae.StepInto(); // push 9
                GetPriceForSysCall.Invoke(ae, new object[] { }).Should().Be(9L * 5000L * 100000000L / 100000); // assuming private ae.ratio = 100000
            }

            // Bhp.Contract.Create: f66ca56e (requires push properties on fourth position)
            byte[] SyscallContractCreateHash00 = new byte[] { (byte)ContractPropertyState.NoProperty, 0x00, 0x00, 0x00, 0x68, 0x04, 0xf6, 0x6c, 0xa5, 0x6e };
            using (ApplicationEngine ae = new ApplicationEngine(TriggerType.Application, null, null, Fixed8.Zero))
            {
                ae.LoadScript(SyscallContractCreateHash00);
                ae.StepInto(); // push 0 - ContractPropertyState.NoProperty
                ae.StepInto(); // push 0
                ae.StepInto(); // push 0
                ae.StepInto(); // push 0
                GetPriceForSysCall.Invoke(ae, new object[] { }).Should().Be(100L * 100000000L / 100000); // assuming private ae.ratio = 100000
            }

            // Bhp.Contract.Create: f66ca56e (requires push properties on fourth position)
            byte[] SyscallContractCreateHash01 = new byte[] { 0x51, 0x00, 0x00, 0x00, 0x68, 0x04, 0xf6, 0x6c, 0xa5, 0x6e };
            using (ApplicationEngine ae = new ApplicationEngine(TriggerType.Application, null, null, Fixed8.Zero))
            {
                ae.LoadScript(SyscallContractCreateHash01);
                ae.StepInto(); // push 01 - ContractPropertyState.HasStorage
                ae.StepInto(); // push 0
                ae.StepInto(); // push 0
                ae.StepInto(); // push 0
                GetPriceForSysCall.Invoke(ae, new object[] { }).Should().Be(500L * 100000000L / 100000); // assuming private ae.ratio = 100000
            }

            // Bhp.Contract.Create: f66ca56e (requires push properties on fourth position)
            byte[] SyscallContractCreateHash02 = new byte[] { 0x52, 0x00, 0x00, 0x00, 0x68, 0x04, 0xf6, 0x6c, 0xa5, 0x6e };
            using (ApplicationEngine ae = new ApplicationEngine(TriggerType.Application, null, null, Fixed8.Zero))
            {
                ae.LoadScript(SyscallContractCreateHash02);
                ae.StepInto(); // push 02 - ContractPropertyState.HasDynamicInvoke
                ae.StepInto(); // push 0
                ae.StepInto(); // push 0
                ae.StepInto(); // push 0
                GetPriceForSysCall.Invoke(ae, new object[] { }).Should().Be(600L * 100000000L / 100000); // assuming private ae.ratio = 100000
            }

            // Bhp.Contract.Create: f66ca56e (requires push properties on fourth position)
            byte[] SyscallContractCreateHash03 = new byte[] { 0x53, 0x00, 0x00, 0x00, 0x68, 0x04, 0xf6, 0x6c, 0xa5, 0x6e };
            using (ApplicationEngine ae = new ApplicationEngine(TriggerType.Application, null, null, Fixed8.Zero))
            {
                ae.LoadScript(SyscallContractCreateHash03);
                ae.StepInto(); // push 03 - HasStorage and HasDynamicInvoke
                ae.StepInto(); // push 0
                ae.StepInto(); // push 0
                ae.StepInto(); // push 0
                GetPriceForSysCall.Invoke(ae, new object[] { }).Should().Be(1000L * 100000000L / 100000); // assuming private ae.ratio = 100000
            }

            // Bhp.Contract.Migrate: 471b6290 (requires push properties on fourth position)
            byte[] SyscallContractMigrateHash00 = new byte[] { (byte)ContractPropertyState.NoProperty, 0x00, 0x00, 0x00, 0x68, 0x04, 0x47, 0x1b, 0x62, 0x90 };
            using (ApplicationEngine ae = new ApplicationEngine(TriggerType.Application, null, null, Fixed8.Zero))
            {
                ae.LoadScript(SyscallContractMigrateHash00);
                ae.StepInto(); // push 0 - ContractPropertyState.NoProperty
                ae.StepInto(); // push 0
                ae.StepInto(); // push 0
                ae.StepInto(); // push 0
                GetPriceForSysCall.Invoke(ae, new object[] { }).Should().Be(100L * 100000000L / 100000); // assuming private ae.ratio = 100000
            }

            // System.Storage.Put: e63f1884 (requires push key and value)
            byte[] SyscallStoragePutHash = new byte[] { 0x53, 0x53, 0x00, 0x68, 0x04, 0xe6, 0x3f, 0x18, 0x84 };
            using (ApplicationEngine ae = new ApplicationEngine(TriggerType.Application, null, null, Fixed8.Zero))
            {
                ae.LoadScript(SyscallStoragePutHash);
                ae.StepInto(); // push 03 (length 1)
                ae.StepInto(); // push 03 (length 1)
                ae.StepInto(); // push 00
                GetPriceForSysCall.Invoke(ae, new object[] { }).Should().Be(1000L); //((1+1-1) / 1024 + 1) * 1000);
            }

            // System.Storage.PutEx: 73e19b3a (requires push key and value)
            byte[] SyscallStoragePutExHash = new byte[] { 0x53, 0x53, 0x00, 0x68, 0x04, 0x73, 0xe1, 0x9b, 0x3a };
            using (ApplicationEngine ae = new ApplicationEngine(TriggerType.Application, null, null, Fixed8.Zero))
            {
                ae.LoadScript(SyscallStoragePutExHash);
                ae.StepInto(); // push 03 (length 1)
                ae.StepInto(); // push 03 (length 1)
                ae.StepInto(); // push 00
                GetPriceForSysCall.Invoke(ae, new object[] { }).Should().Be(1000L); //((1+1-1) / 1024 + 1) * 1000);
            }
        }
    }

}
