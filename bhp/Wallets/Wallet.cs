using Bhp.BhpExtensions.Transactions;
using Bhp.Cryptography;
using Bhp.IO;
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

        //By BHP
        TransactionContract transactionContract = new TransactionContract();

        public abstract string Name { get; }
        public abstract Version Version { get; }
        public abstract uint WalletHeight { get; }
        public abstract string WalletPath { get; }

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

        private List<(UInt160 Account, BigInteger Value)> FindPayingAccounts(List<(UInt160 Account, BigInteger Value)> orderedAccounts, BigInteger amount)
        {
            var result = new List<(UInt160 Account, BigInteger Value)>();
            BigInteger sum_balance = orderedAccounts.Select(p => p.Value).Sum();
            if (sum_balance == amount)
            {
                result.AddRange(orderedAccounts);
                orderedAccounts.Clear();
            }
            else
            {
                for (int i = 0; i < orderedAccounts.Count; i++)
                {
                    if (orderedAccounts[i].Value < amount)
                        continue;
                    if (orderedAccounts[i].Value == amount)
                    {
                        result.Add(orderedAccounts[i]);
                        orderedAccounts.RemoveAt(i);
                    }
                    else
                    {
                        result.Add((orderedAccounts[i].Account, amount));
                        orderedAccounts[i] = (orderedAccounts[i].Account, orderedAccounts[i].Value - amount);
                    }
                    break;
                }
                if (result.Count == 0)
                {
                    int i = orderedAccounts.Count - 1;
                    while (orderedAccounts[i].Value <= amount)
                    {
                        result.Add(orderedAccounts[i]);
                        amount -= orderedAccounts[i].Value;
                        orderedAccounts.RemoveAt(i);
                        i--;
                    }
                    for (i = 0; i < orderedAccounts.Count; i++)
                    {
                        if (orderedAccounts[i].Value < amount)
                            continue;
                        if (orderedAccounts[i].Value == amount)
                        {
                            result.Add(orderedAccounts[i]);
                            orderedAccounts.RemoveAt(i);
                        }
                        else
                        {
                            result.Add((orderedAccounts[i].Account, amount));
                            orderedAccounts[i] = (orderedAccounts[i].Account, orderedAccounts[i].Value - amount);
                        }
                        break;
                    }
                }
            }
            return result;
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
            byte[] datapassphrase = Encoding.UTF8.GetBytes(passphrase);
            byte[] derivedkey = SCrypt.DeriveKey(datapassphrase, addresshash, N, r, p, 64);
            Array.Clear(datapassphrase, 0, datapassphrase.Length);
            byte[] derivedhalf1 = derivedkey.Take(32).ToArray();
            byte[] derivedhalf2 = derivedkey.Skip(32).ToArray();
            Array.Clear(derivedkey, 0, derivedkey.Length);
            byte[] encryptedkey = new byte[32];
            Buffer.BlockCopy(data, 7, encryptedkey, 0, 32);
            Array.Clear(data, 0, data.Length);
            byte[] prikey = XOR(encryptedkey.AES256Decrypt(derivedhalf2), derivedhalf1);
            Array.Clear(derivedhalf1, 0, derivedhalf1.Length);
            Array.Clear(derivedhalf2, 0, derivedhalf2.Length);
            ECPoint pubkey = Cryptography.ECC.ECCurve.Secp256.G * prikey;
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
            fee += Fixed8.Parse(tx.SystemFee.ToString());
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

        public Transaction MakeTransaction(TransferOutput[] outputs, UInt160 from = null)
        {
            UInt160[] accounts;
            if (from is null)
            {
                accounts = GetAccounts().Where(p => !p.Lock && !p.WatchOnly).Select(p => p.ScriptHash).ToArray();
            }
            else
            {
                if (!Contains(from))
                    throw new ArgumentException($"The address {from.ToString()} was not found in the wallet");
                accounts = new[] { from };
            }
            using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot())
            {
                HashSet<UInt160> cosigners = new HashSet<UInt160>();
                byte[] script;
                List<(UInt160 Account, BigInteger Value)> balances_gas = null;
                using (ScriptBuilder sb = new ScriptBuilder())
                {
                    foreach (var (assetId, group, sum) in outputs.GroupBy(p => p.AssetId, (k, g) => (k, g, g.Select(p => p.Value.Value).Sum())))
                    {
                        var balances = new List<(UInt160 Account, BigInteger Value)>();
                        foreach (UInt160 account in accounts)
                            using (ScriptBuilder sb2 = new ScriptBuilder())
                            {
                                sb2.EmitAppCall((UInt160)assetId, "balanceOf", account);
                                using (ApplicationEngine engine = ApplicationEngine.Run(sb2.ToArray(), snapshot, testMode: true))
                                {
                                    if (engine.State.HasFlag(VMState.FAULT))
                                        throw new InvalidOperationException($"Execution for {assetId.ToString()}.balanceOf('{account.ToString()}' fault");
                                    BigInteger value = engine.ResultStack.Pop().GetBigInteger();
                                    if (value.Sign > 0) balances.Add((account, value));
                                }
                            }
                        BigInteger sum_balance = balances.Select(p => p.Value).Sum();
                        if (sum_balance < sum)
                            throw new InvalidOperationException($"It does not have enough balance, expected: {sum.ToString()} found: {sum_balance.ToString()}");
                        foreach (TransferOutput output in group)
                        {
                            balances = balances.OrderBy(p => p.Value).ToList();
                            var balances_used = FindPayingAccounts(balances, output.Value.Value);
                            cosigners.UnionWith(balances_used.Select(p => p.Account));
                            foreach (var (account, value) in balances_used)
                            {
                                sb.EmitAppCall((UInt160)output.AssetId, "transfer", account, output.ScriptHash, value);
                                sb.Emit(OpCode.THROWIFNOT);
                            }
                        }
                        if (assetId.Equals(NativeContract.GAS.Hash))
                            balances_gas = balances;
                    }
                    script = sb.ToArray();
                }
                if (balances_gas is null)
                    balances_gas = accounts.Select(p => (Account: p, Value: NativeContract.GAS.BalanceOf(snapshot, p))).Where(p => p.Value.Sign > 0).ToList();
                TransactionAttribute[] attributes = cosigners.Select(p => new TransactionAttribute { Usage = TransactionAttributeUsage.Cosigner, Data = p.ToArray() }).ToArray();
                return MakeTransaction(snapshot, attributes, script, balances_gas);
            }
        }

        public Transaction MakeTransaction(TransactionAttribute[] attributes, byte[] script, UInt160 sender = null)
        {
            UInt160[] accounts;
            if (sender is null)
            {
                accounts = GetAccounts().Where(p => !p.Lock && !p.WatchOnly).Select(p => p.ScriptHash).ToArray();
            }
            else
            {
                if (!Contains(sender))
                    throw new ArgumentException($"The address {sender.ToString()} was not found in the wallet");
                accounts = new[] { sender };
            }
            using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot())
            {
                var balances_gas = accounts.Select(p => (Account: p, Value: NativeContract.GAS.BalanceOf(snapshot, p))).Where(p => p.Value.Sign > 0).ToList();
                return MakeTransaction(snapshot, attributes, script, balances_gas);
            }
        }

        private Transaction MakeTransaction(Snapshot snapshot, TransactionAttribute[] attributes, byte[] script, List<(UInt160 Account, BigInteger Value)> balances_gas)
        {
            Random rand = new Random();
            foreach (var (account, value) in balances_gas)
            {
                Transaction tx = new Transaction
                {
                    Version = 0,
                    Nonce = (uint)rand.Next(),
                    Script = script,
                    Sender = account,
                    ValidUntilBlock = snapshot.Height + Transaction.MaxValidUntilBlockIncrement,
                    Attributes = attributes
                };
                using (ApplicationEngine engine = ApplicationEngine.Run(script, snapshot, tx, testMode: true))
                {
                    if (engine.State.HasFlag(VMState.FAULT))
                        throw new InvalidOperationException($"Failed execution for '{script.ToHexString()}'");
                    tx.SystemFee = Math.Max(engine.GasConsumed - ApplicationEngine.GasFree, 0);
                    if (tx.SystemFee > 0)
                    {
                        long d = (long)NativeContract.GAS.Factor;
                        long remainder = tx.SystemFee % d;
                        if (remainder > 0)
                            tx.SystemFee += d - remainder;
                        else if (remainder < 0)
                            tx.SystemFee -= remainder;
                    }
                }
                UInt160[] hashes = tx.GetScriptHashesForVerifying(snapshot);
                int size = Transaction.HeaderSize + attributes.GetVarSize() + script.GetVarSize() + IO.Helper.GetVarSize(hashes.Length);
                foreach (UInt160 hash in hashes)
                {
                    script = GetAccount(hash)?.Contract?.Script ?? snapshot.Contracts.TryGet(hash)?.Script;
                    if (script is null) continue;
                    if (script.IsSignatureContract())
                    {
                        size += 66 + script.GetVarSize();
                        tx.NetworkFee += ApplicationEngine.OpCodePrices[OpCode.PUSHBYTES64] + ApplicationEngine.OpCodePrices[OpCode.PUSHBYTES33] + InteropService.GetPrice(InteropService.Bhp_Crypto_CheckSig, null);
                    }
                    else if (script.IsMultiSigContract(out int m, out int n))
                    {
                        int size_inv = 65 * m;
                        size += IO.Helper.GetVarSize(size_inv) + size_inv + script.GetVarSize();
                        tx.NetworkFee += ApplicationEngine.OpCodePrices[OpCode.PUSHBYTES64] * m;
                        using (ScriptBuilder sb = new ScriptBuilder())
                            tx.NetworkFee += ApplicationEngine.OpCodePrices[(OpCode)sb.EmitPush(m).ToArray()[0]];
                        tx.NetworkFee += ApplicationEngine.OpCodePrices[OpCode.PUSHBYTES33] * n;
                        using (ScriptBuilder sb = new ScriptBuilder())
                            tx.NetworkFee += ApplicationEngine.OpCodePrices[(OpCode)sb.EmitPush(n).ToArray()[0]];
                        tx.NetworkFee += InteropService.GetPrice(InteropService.Bhp_Crypto_CheckSig, null) * n;
                    }
                    else
                    {
                        //We can support more contract types in the future.
                    }
                }
                tx.NetworkFee += size * NativeContract.Policy.GetFeePerByte(snapshot);
                if (value >= tx.SystemFee + tx.NetworkFee) return tx;
            }
            throw new InvalidOperationException("Insufficient GAS");
        }

        public bool Sign(ContractParametersContext context)
        {
            bool fSuccess = false;
            foreach (UInt160 scriptHash in context.ScriptHashes)
            {
                WalletAccount account = GetAccount(scriptHash);
                if (account?.HasKey != true) continue;
                KeyPair key = account.GetKey();
                byte[] signature = context.Verifiable.Sign(key);
                fSuccess |= context.AddSignature(account.Contract, key.PublicKey, signature);
            }
            return fSuccess;
        }

        public abstract bool VerifyPassword(string password);

        private static byte[] XOR(byte[] x, byte[] y)
        {
            if (x.Length != y.Length) throw new ArgumentException();
            return x.Zip(y, (a, b) => (byte)(a ^ b)).ToArray();
        }
    }
}
