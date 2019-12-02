using Bhp.Cryptography.ECC;
using Bhp.IO;
using Bhp.IO.Caching;
using Bhp.IO.Wrappers;
using Bhp.Ledger;
using Bhp.Network.P2P.Payloads;
using Bhp.SmartContract;
using Bhp.SmartContract.Native;
using Bhp.VM;
using System;
using System.Linq;
using VMArray = Bhp.VM.Types.Array;

namespace Bhp.Persistence
{
    public abstract class StoreView: IDisposable, IPersistence
    {
        public Block PersistingBlock { get; internal set; }
        public abstract DataCache<UInt256, TrimmedBlock> Blocks { get; }
        public abstract DataCache<UInt256, TransactionState> Transactions { get; }
        public abstract DataCache<UInt256, UnspentCoinState> UnspentCoins { get; }
        public abstract DataCache<UInt256, AssetState> Assets { get; }
        public abstract DataCache<UInt160, ContractState> Contracts { get; }
        public abstract DataCache<StorageKey, StorageItem> Storages { get; }
        public abstract DataCache<UInt32Wrapper, HeaderHashList> HeaderHashList { get; }
        public abstract MetaDataCache<HashIndexState> BlockHashIndex { get; }
        public abstract MetaDataCache<HashIndexState> HeaderHashIndex { get; }

        public uint Height => BlockHashIndex.Get().Index;
        public uint HeaderHeight => HeaderHashIndex.Get().Index;
        public UInt256 CurrentBlockHash => BlockHashIndex.Get().Hash;
        public UInt256 CurrentHeaderHash => HeaderHashIndex.Get().Hash;

        public StoreView Clone()
        {
            return new ClonedView(this);
        }

        public virtual void Commit()
        {
            UnspentCoins.DeleteWhere((k, v) => v.Items.All(p => p.HasFlag(CoinState.Spent)));
            Blocks.Commit();
            Transactions.Commit();
            UnspentCoins.Commit();
            Assets.Commit();
            Contracts.Commit();
            Storages.Commit();
            HeaderHashList.Commit();
            BlockHashIndex.Commit();
            HeaderHashIndex.Commit();
        }

        public virtual void Dispose()
        {
        }

        public ECPoint[] GetValidators()
        {
            byte[] script;
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitAppCall(NativeContract.Bhp.Hash, "getValidators");
                script = sb.ToArray();
            }
            using (ApplicationEngine engine = ApplicationEngine.Run(script, this, testMode: true))
            {
                return ((VMArray)engine.ResultStack.Peek()).Select(p => p.GetByteArray().AsSerializable<ECPoint>()).ToArray();
            }
        }
    }
}
