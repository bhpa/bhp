using Bhp.BhpExtensions.Transactions;
using Bhp.Cryptography;
using Bhp.Ledger;
using Bhp.Network.P2P.Payloads;
using Bhp.Persistence;
using Bhp.SmartContract;
using Bhp.SmartContract.Native;
using Bhp.VM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ECPoint = Bhp.Cryptography.ECC.ECPoint;

namespace Bhp.Wallets
{
    public abstract class Wallet : IDisposable
    {
        public abstract event EventHandler<WalletTransactionEventArgs> WalletTransaction;

        private static readonly Random rand = new Random();

        //By BHP
        TransactionContract transactionContract = new TransactionContract();

        public abstract string Name { get; }
        public abstract Version Version { get; }
        public abstract uint WalletHeight { get; }

        public abstract bool Contains(UInt160 scriptHash);
        public abstract WalletAccount CreateAccount(byte[] privateKey);
        public abstract WalletAccount CreateAccount(Contract contract, KeyPair key = null);
        public abstract WalletAccount CreateAccount(UInt160 scriptHash);
        public abstract bool DeleteAccount(UInt160 scriptHash);
        public abstract WalletAccount GetAccount(UInt160 scriptHash);
        public abstract IEnumerable<WalletAccount> GetAccounts();
        public abstract IEnumerable<Coin> GetCoins(IEnumerable<UInt160> accounts);
        public abstract IEnumerable<UInt256> GetTransactions();

        public WalletAccount CreateAccount()
        {
            byte[] privateKey = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            WalletAccount account = CreateAccount(privateKey);
            Array.Clear(privateKey, 0, privateKey.Length);
            return account;
        }

        public WalletAccount CreateAccount(Contract contract, byte[] privateKey)
        {
            if (privateKey == null) return CreateAccount(contract);
            return CreateAccount(contract, new KeyPair(privateKey));
        }

        public virtual void Dispose()
        {
        }

        public void FillTransaction(Transaction tx, UInt160 sender = null)
        {
            if (tx.Nonce == 0)
                tx.Nonce = (uint)rand.Next();
            if (tx.ValidUntilBlock == 0)
                using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot())
                    tx.ValidUntilBlock = snapshot.Height + Transaction.MaxValidUntilBlockIncrement;
            tx.CalculateFees();
            UInt160[] accounts = sender is null ? GetAccounts().Where(p => !p.Lock && !p.WatchOnly).Select(p => p.ScriptHash).ToArray() : new[] { sender };
            BigInteger fee = tx.Gas + tx.NetworkFee;
            using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot())
                foreach (UInt160 account in accounts)
                {
                    BigInteger balance = NativeContract.GAS.BalanceOf(snapshot, account);
                    if (balance >= fee)
                    {
                        tx.Sender = account;
                        return;
                    }
                }
            throw new InvalidOperationException();
        }

        public IEnumerable<Coin> FindUnspentCoins(params UInt160[] from)
        {
            IEnumerable<UInt160> accounts = from.Length > 0 ? from : GetAccounts().Where(p => !p.Lock && !p.WatchOnly).Select(p => p.ScriptHash);
            return GetCoins(accounts).Where(p => p.State.HasFlag(CoinState.Confirmed) && !p.State.HasFlag(CoinState.Spent) && !p.State.HasFlag(CoinState.Frozen));
        }

        public virtual Coin[] FindUnspentCoins(UInt256 asset_id, Fixed8 amount, params UInt160[] from)
        {
            return FindUnspentCoins(FindUnspentCoins(from), asset_id, amount);
        }

        protected static Coin[] FindUnspentCoins(IEnumerable<Coin> unspents, UInt256 asset_id, Fixed8 amount)
        {
            Coin[] unspents_asset = unspents.Where(p => p.Output.AssetId == asset_id).ToArray();
            unspents_asset = VerifyTransactionContract.checkUtxo(unspents_asset);//By BHP
            Fixed8 sum = unspents_asset.Sum(p => p.Output.Value);
            if (sum < amount) return null;
            if (sum == amount) return unspents_asset;
            Coin[] unspents_ordered = unspents_asset.OrderByDescending(p => p.Output.Value).ToArray();
            int i = 0;
            while (unspents_ordered[i].Output.Value <= amount)
                amount -= unspents_ordered[i++].Output.Value;
            if (amount == Fixed8.Zero)
                return unspents_ordered.Take(i).ToArray();
            else
                return unspents_ordered.Take(i).Concat(new[] { unspents_ordered.Last(p => p.Output.Value >= amount) }).ToArray();
        }

        public WalletAccount GetAccount(ECPoint pubkey)
        {
            return GetAccount(Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash());
        }


        public BigDecimal GetAvailable(UInt160 asset_id)
        {
            UInt160[] accounts = GetAccounts().Where(p => !p.WatchOnly).Select(p => p.ScriptHash).ToArray();
            return GetBalance(asset_id, accounts);
        }

        public BigDecimal GetBalance(UInt160 asset_id, params UInt160[] accounts)
        {
            byte[] script;
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitPush(0);
                foreach (UInt160 account in accounts)
                {
                    sb.EmitAppCall(asset_id, "balanceOf", account);
                    sb.Emit(OpCode.ADD);
                }
                sb.EmitAppCall(asset_id, "decimals");
                script = sb.ToArray();
            }
            ApplicationEngine engine = ApplicationEngine.Run(script, extraGAS: 20000000L * accounts.Length);
            if (engine.State.HasFlag(VMState.FAULT))
                return new BigDecimal(0, 0);
            byte decimals = (byte)engine.ResultStack.Pop().GetBigInteger();
            BigInteger amount = engine.ResultStack.Pop().GetBigInteger();
            return new BigDecimal(amount, decimals);
        }

        public Fixed8 GetAvailable(UInt256 asset_id)
        {
            return FindUnspentCoins().Where(p => p.Output.AssetId.Equals(asset_id)).Sum(p => p.Output.Value);
        }

        public BigDecimal GetAvailable(UIntBase asset_id)
        {
            if (asset_id is UInt160 asset_id_160)
            {
                byte[] script;
                UInt160[] accounts = GetAccounts().Where(p => !p.WatchOnly).Select(p => p.ScriptHash).ToArray();
                using (ScriptBuilder sb = new ScriptBuilder())
                {
                    sb.EmitPush(0);
                    foreach (UInt160 account in accounts)
                    {
                        sb.EmitAppCall(asset_id_160, "balanceOf", account);
                        sb.Emit(OpCode.ADD);
                    }
                    sb.EmitAppCall(asset_id_160, "decimals");
                    script = sb.ToArray();
                }
                ApplicationEngine engine = ApplicationEngine.Run(script, extraGAS: (Fixed8.FromDecimal(0.2m) * accounts.Length).value);
                if (engine.State.HasFlag(VMState.FAULT))
                    return new BigDecimal(0, 0);
                byte decimals = (byte)engine.ResultStack.Pop().GetBigInteger();
                BigInteger amount = engine.ResultStack.Pop().GetBigInteger();
                return new BigDecimal(amount, decimals);
            }
            else
            {
                return new BigDecimal(GetAvailable((UInt256)asset_id).GetData(), 8);
            }
        }

        public Fixed8 GetBalance(UInt256 asset_id)
        {
            return GetCoins(GetAccounts().Select(p => p.ScriptHash)).Where(p => !p.State.HasFlag(CoinState.Spent) && p.Output.AssetId.Equals(asset_id)).Sum(p => p.Output.Value);
        }

        public virtual UInt160 GetChangeAddress()
        {
            WalletAccount[] accounts = GetAccounts().ToArray();
            WalletAccount account = accounts.FirstOrDefault(p => p.IsDefault);
            if (account == null)
                account = accounts.FirstOrDefault(p => p.Contract?.Script.IsSignatureContract() == true);
            if (account == null)
                account = accounts.FirstOrDefault(p => !p.WatchOnly);
            if (account == null)
                account = accounts.FirstOrDefault();
            return account?.ScriptHash;
        }

        public IEnumerable<Coin> GetCoins()
        {
            return GetCoins(GetAccounts().Select(p => p.ScriptHash));
        }

        public static byte[] GetPrivateKeyFromBRC2(string brc2, string passphrase, int N = 16384, int r = 8, int p = 8)
        {
            if (brc2 == null) throw new ArgumentNullException(nameof(brc2));
            if (passphrase == null) throw new ArgumentNullException(nameof(passphrase));
            byte[] data = brc2.Base58CheckDecode();
            if (data.Length != 39 || data[0] != 0x01 || data[1] != 0x42 || data[2] != 0xe0)
                throw new FormatException();
            byte[] addresshash = new byte[4];
            Buffer.BlockCopy(data, 3, addresshash, 0, 4);
            byte[] derivedkey = SCrypt.DeriveKey(Encoding.UTF8.GetBytes(passphrase), addresshash, N, r, p, 64);
            byte[] derivedhalf1 = derivedkey.Take(32).ToArray();
            byte[] derivedhalf2 = derivedkey.Skip(32).ToArray();
            byte[] encryptedkey = new byte[32];
            Buffer.BlockCopy(data, 7, encryptedkey, 0, 32);
            byte[] prikey = XOR(encryptedkey.AES256Decrypt(derivedhalf2), derivedhalf1);
            Cryptography.ECC.ECPoint pubkey = Cryptography.ECC.ECCurve.Secp256.G * prikey;
            UInt160 script_hash = Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash();
            string address = script_hash.ToAddress();
            if (!Encoding.ASCII.GetBytes(address).Sha256().Sha256().Take(4).SequenceEqual(addresshash))
                throw new FormatException();
            return prikey;
        }

        public static byte[] GetPrivateKeyFromWIF(string wif)
        {
            if (wif == null) throw new ArgumentNullException();
            byte[] data = wif.Base58CheckDecode();
            if (data.Length != 34 || data[0] != 0x80 || data[33] != 0x01)
                throw new FormatException();
            byte[] privateKey = new byte[32];
            Buffer.BlockCopy(data, 1, privateKey, 0, privateKey.Length);
            Array.Clear(data, 0, data.Length);
            return privateKey;
        }

        public IEnumerable<Coin> GetUnclaimedCoins()
        {
            IEnumerable<UInt160> accounts = GetAccounts().Where(p => !p.Lock && !p.WatchOnly).Select(p => p.ScriptHash);
            IEnumerable<Coin> coins = GetCoins(accounts);
            coins = coins.Where(p => p.Output.AssetId.Equals(Blockchain.GoverningToken.Hash));
            coins = coins.Where(p => p.State.HasFlag(CoinState.Confirmed) && p.State.HasFlag(CoinState.Spent));
            coins = coins.Where(p => !p.State.HasFlag(CoinState.Claimed) && !p.State.HasFlag(CoinState.Frozen));
            return coins;
        }

        public virtual WalletAccount Import(X509Certificate2 cert)
        {
            byte[] privateKey;
            using (ECDsa ecdsa = cert.GetECDsaPrivateKey())
            {
                privateKey = ecdsa.ExportParameters(true).D;
            }
            WalletAccount account = CreateAccount(privateKey);
            Array.Clear(privateKey, 0, privateKey.Length);
            return account;
        }

        public virtual WalletAccount Import(string wif)
        {
            byte[] privateKey = GetPrivateKeyFromWIF(wif);
            WalletAccount account = CreateAccount(privateKey);
            Array.Clear(privateKey, 0, privateKey.Length);
            return account;
        }

        public virtual WalletAccount Import(string brc2, string passphrase)
        {
            byte[] privateKey = GetPrivateKeyFromBRC2(brc2, passphrase);
            WalletAccount account = CreateAccount(privateKey);
            Array.Clear(privateKey, 0, privateKey.Length);
            return account;
        }

        public T MakeTransaction<T>(T tx, UInt160 from = null, UInt160 change_address = null, Fixed8 fee = default(Fixed8)) where T : Transaction
        {
            if (tx.Outputs == null) tx.Outputs = new TransactionOutput[0];
            if (tx.Attributes == null) tx.Attributes = new TransactionAttribute[0];
            fee += tx.SystemFee;
            var pay_total = tx.Outputs.GroupBy(p => p.AssetId, (k, g) => new
            {
                AssetId = k,
                Value = g.Sum(p => p.Value)
            }).ToDictionary(p => p.AssetId);
            if (fee > Fixed8.Zero)
            {
                if (pay_total.ContainsKey(Blockchain.UtilityToken.Hash))
                {
                    pay_total[Blockchain.UtilityToken.Hash] = new
                    {
                        AssetId = Blockchain.UtilityToken.Hash,
                        Value = pay_total[Blockchain.UtilityToken.Hash].Value + fee
                    };
                }
                else
                {
                    pay_total.Add(Blockchain.UtilityToken.Hash, new
                    {
                        AssetId = Blockchain.UtilityToken.Hash,
                        Value = fee
                    });
                }
            }
            var pay_coins = pay_total.Select(p => new
            {
                AssetId = p.Key,
                Unspents = from == null ? FindUnspentCoins(p.Key, p.Value.Value) : FindUnspentCoins(p.Key, p.Value.Value, from)
            }).ToDictionary(p => p.AssetId);
            if (pay_coins.Any(p => p.Value.Unspents == null)) return null;
            var input_sum = pay_coins.Values.ToDictionary(p => p.AssetId, p => new
            {
                p.AssetId,
                Value = p.Unspents.Sum(q => q.Output.Value)
            });
            if (change_address == null) change_address = GetChangeAddress();
            List<TransactionOutput> outputs_new = new List<TransactionOutput>(tx.Outputs);
            foreach (UInt256 asset_id in input_sum.Keys)
            {
                if (input_sum[asset_id].Value > pay_total[asset_id].Value)
                {
                    outputs_new.Add(new TransactionOutput
                    {
                        AssetId = asset_id,
                        Value = input_sum[asset_id].Value - pay_total[asset_id].Value,
                        ScriptHash = change_address
                    });
                }
            }
            tx.Inputs = pay_coins.Values.SelectMany(p => p.Unspents).Select(p => p.Reference).ToArray();
            tx.Outputs = outputs_new.ToArray();
            return tx;
        }

        public Transaction MakeTransaction(IEnumerable<TransactionAttribute> attributes, TransferOutput[] outputs, UInt160 from = null)
        {
            uint nonce = (uint)rand.Next();
            var totalPay = outputs.GroupBy(p => p.AssetId, (k, g) => (k, g.Select(p => p.Value.Value).Sum())).ToArray();
            UInt160[] accounts;
            if (from is null)
            {
                accounts = GetAccounts().Where(p => !p.Lock && !p.WatchOnly).Select(p => p.ScriptHash).ToArray();
            }
            else
            {
                if (!Contains(from)) return null;
                accounts = new[] { from };
            }
            TransactionAttribute[] attr = attributes?.ToArray() ?? new TransactionAttribute[0];
            using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot())
            {
                uint validUntilBlock = snapshot.Height + Transaction.MaxValidUntilBlockIncrement;
                foreach (UInt160 account in accounts)
                {
                    Transaction tx = MakeTransaction(snapshot, 0, nonce, totalPay, outputs, account, validUntilBlock, attr);
                    if (tx != null) return tx;
                }
            }
            return null;
        }

        private Transaction MakeTransaction(Snapshot snapshot, byte version, uint nonce, (UIntBase, BigInteger)[] totalPay, TransferOutput[] outputs, UInt160 sender, uint validUntilBlock, TransactionAttribute[] attributes)
        {
            BigInteger balance_gas = BigInteger.Zero;
            foreach (var (assetId, amount) in totalPay)
                using (ScriptBuilder sb = new ScriptBuilder())
                {
                    sb.EmitAppCall(UInt160.Parse(assetId.ToString()), "balanceOf", sender);
                    ApplicationEngine engine = ApplicationEngine.Run(sb.ToArray());
                    if (engine.State.HasFlag(VMState.FAULT)) return null;
                    BigInteger balance = engine.ResultStack.Peek().GetBigInteger();
                    if (balance < amount) return null;
                    if (assetId.Equals(NativeContract.GAS.Hash))
                    {
                        balance_gas = balance - amount;
                        if (balance_gas.Sign <= 0) return null;
                    }
                }
            byte[] script;
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                foreach (var output in outputs)
                {
                    sb.EmitAppCall(UInt160.Parse(output.AssetId.ToString()), "transfer", sender, output.ScriptHash, output.Value.Value);
                    sb.Emit(OpCode.THROWIFNOT);
                }
                script = sb.ToArray();
            }
            Transaction tx = new Transaction
            {
                Version = version,
                Nonce = nonce,
                Script = script,
                Sender = sender,
                ValidUntilBlock = validUntilBlock,
                Attributes = attributes
            };
            try
            {
                tx.CalculateFees();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            BigInteger fee = tx.Gas + tx.NetworkFee;
            if (balance_gas == BigInteger.Zero)
                balance_gas = NativeContract.GAS.BalanceOf(snapshot, sender);
            if (balance_gas < fee) return null;
            return tx;
        }

        public bool Sign(ContractParametersContext context)
        {
            WalletAccount account = GetAccount(context.ScriptHash);
            if (account?.HasKey != true) return false;
            KeyPair key = account.GetKey();
            byte[] signature = context.Verifiable.Sign(key);
            return context.AddSignature(account.Contract, key.PublicKey, signature);
        }

        public abstract bool VerifyPassword(string password);

        private static byte[] XOR(byte[] x, byte[] y)
        {
            if (x.Length != y.Length) throw new ArgumentException();
            return x.Zip(y, (a, b) => (byte)(a ^ b)).ToArray();
        }
    }
}
