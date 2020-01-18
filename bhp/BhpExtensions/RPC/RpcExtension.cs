using Akka.Actor;
using Bhp.BhpExtensions.Fees;
using Bhp.BhpExtensions.Transactions;
using Bhp.BhpExtensions.Wallets;
using Bhp.IO;
using Bhp.IO.Json;
using Bhp.Ledger;
using Bhp.Network.P2P;
using Bhp.Network.P2P.Payloads;
using Bhp.Network.RPC;
using Bhp.Persistence;
using Bhp.SmartContract;
using Bhp.VM;
using Bhp.Wallets;
using Bhp.Wallets.BRC6;
using Bhp.Wallets.SQLite;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Bhp.BhpExtensions.RPC
{
    /// <summary>
    /// RPC Extension method by BHP
    /// </summary>
    public class RpcExtension
    {
        private Wallet wallet;
        public WalletTimeLock walletTimeLock;
        private bool Unlocking;
        private BhpSystem system;
        private RpcServer rpcServer;

        public RpcExtension()
        {
            walletTimeLock = new WalletTimeLock();
            Unlocking = false;
        }

        public RpcExtension(BhpSystem system, Wallet wallet, RpcServer rpcServer)
        {
            this.system = system;
            this.wallet = wallet;
            walletTimeLock = new WalletTimeLock();
            Unlocking = false;
            this.rpcServer = rpcServer;
        }

        public void SetWallet(Wallet wallet)
        {
            this.wallet = wallet;
        }

        public void SetSystem(BhpSystem system)
        {
            this.system = system;
        }

        private Wallet OpenWallet(WalletIndexer indexer, string path, string password)
        {
            if (Path.GetExtension(path) == ".db3")
            {
                return UserWallet.Open(indexer, path, password);
            }
            else
            {
                BRC6Wallet brc6wallet = new BRC6Wallet(indexer, path);
                brc6wallet.Unlock(password);
                return brc6wallet;
            }
        }

        public JObject Process(string method, JArray _params)
        {
            JObject json = new JObject();

            switch (method)
            {
                case "unlock":
                    //if (wallet == null) return "wallet is null.";
                    if (ExtensionSettings.Default.WalletConfig.Path.Trim().Length < 1) throw new RpcException(-500, "Wallet file is exists.");

                    if (_params.Count < 2) throw new RpcException(-501, "parameter is error.");

                    string password = _params[0].AsString();
                    if (!RpcExtension.VerifyPW(password))
                    {
                        throw new RpcException(-501, $"password max length {RpcExtension.MaxPWLength}");
                    }

                    int duration = (int)_params[1].AsNumber();

                    if (Unlocking) { throw new RpcException(-502, "wallet is unlocking...."); }

                    Unlocking = true;
                    try
                    {
                        if (wallet == null)
                        {
                            wallet = OpenWallet(ExtensionSettings.Default.WalletConfig.Indexer, ExtensionSettings.Default.WalletConfig.Path, password);
                            walletTimeLock.SetDuration(wallet == null ? 0 : duration);
                            rpcServer.SetWallet(wallet);
                            return $"success";
                        }
                        else
                        {
                            bool ok = walletTimeLock.UnLock(wallet, password, duration);
                            return ok ? "success" : "failure";
                        }
                    }
                    finally
                    {
                        Unlocking = false;
                    }

                case "getutxos":
                    {
                        if (wallet == null || walletTimeLock.IsLocked())
                            throw new RpcException(-400, "Access denied");
                        else
                        {
                            //address,assetid
                            UInt160 scriptHash = _params[0].AsString().ToScriptHash();
                            IEnumerable<Coin> coins = wallet.FindUnspentCoins();
                            UInt256 assetId;
                            if (_params.Count >= 2)
                            {
                                switch (_params[1].AsString())
                                {
                                    case "bhp":
                                        assetId = Blockchain.GoverningToken.Hash;
                                        break;
                                    case "gas":
                                        assetId = Blockchain.UtilityToken.Hash;
                                        break;
                                    default:
                                        assetId = UInt256.Parse(_params[1].AsString());
                                        break;
                                }
                            }
                            else
                            {
                                assetId = Blockchain.GoverningToken.Hash;
                            }
                            coins = coins.Where(p => p.Output.AssetId.Equals(assetId) && p.Output.ScriptHash.Equals(scriptHash));

                            //json["utxos"] = new JObject();
                            Coin[] coins_array = coins.ToArray();
                            //const int MAX_SHOW = 100;

                            json["utxos"] = new JArray(coins_array.Select(p =>
                            {
                                return p.Reference.ToJson();
                            }));

                            return json;
                        }
                    }

                case "verifytx":
                    {
                        Transaction tx = Transaction.DeserializeFrom(_params[0].AsString().HexToBytes());
                        string res = VerifyTransaction.Verify(Blockchain.Singleton.GetSnapshot(), new List<Transaction> { tx }, tx);

                        json["result"] = res;
                        if ("success".Equals(res))
                        {
                            json["tx"] = tx.ToJson();
                        }
                        return json;
                    }

                case "claimgas":
                    {
                        if (wallet == null || walletTimeLock.IsLocked())
                            throw new RpcException(-400, "Access denied");
                        else
                        {
                            RpcCoins coins = new RpcCoins(wallet, system);
                            ClaimTransaction[] txs = coins.ClaimAll();
                            if (txs == null)
                            {
                                json["txs"] = new JArray();
                            }
                            else
                            {
                                json["txs"] = new JArray(txs.Select(p =>
                                {
                                    return p.ToJson();
                                }));
                            }
                            return json;
                        }
                    }
                case "showgas":
                    {
                        if (wallet == null || walletTimeLock.IsLocked())
                            throw new RpcException(-400, "Access denied");
                        else
                        {
                            RpcCoins coins = new RpcCoins(wallet, system);
                            json["unavailable"] = coins.UnavailableBonus().ToString();
                            json["available"] = coins.AvailableBonus().ToString();
                            return json;
                        }
                    }
                case "getutxoofaddress":
                    {
                        string from = _params[0].AsString();
                        string jsonRes = RequestRpc("getUtxo", $"address={from}");

                        Newtonsoft.Json.Linq.JArray jsons = (Newtonsoft.Json.Linq.JArray)JsonConvert.DeserializeObject(jsonRes);
                        json["utxo"] = new JArray(jsons.Select(p =>
                        {
                            JObject peerJson = new JObject();
                            peerJson["asset"] = p["asset"].ToString();
                            peerJson["txid"] = p["txid"].ToString();
                            peerJson["n"] = (int)p["n"];
                            peerJson["value"] = (double)p["value"];
                            peerJson["address"] = p["address"].ToString();
                            peerJson["blockHeight"] = (int)p["blockHeight"];
                            return peerJson;
                        }));
                        return json;
                    }

                case "gettransaction":
                    {
                        string from = _params[0].AsString();
                        string position = _params[1].AsString() != "" ? _params[1].AsString() : "1";
                        string offset = _params[2].AsString() != "" ? _params[2].AsString() : "20";
                        string jsonRes = RequestRpc("findTxVout", $"address={from}&position={position}&offset={offset}");

                        Newtonsoft.Json.Linq.JArray jsons = Newtonsoft.Json.Linq.JArray.Parse(jsonRes);

                        json["transaction"] = new JArray(jsons.Select(p =>
                        {
                            JObject peerJson = new JObject();
                            peerJson["blockHeight"] = p["blockHeight"].ToString();
                            peerJson["txid"] = p["txid"].ToString();
                            peerJson["type"] = p["type"].ToString();
                            Newtonsoft.Json.Linq.JToken[] jt = p["inAddress"].ToArray();
                            JArray j_inaddress = new JArray();
                            foreach (Newtonsoft.Json.Linq.JToken i in jt)
                            {
                                string s = i.ToString();
                                j_inaddress.Add(s);
                            }
                            peerJson["inputaddress"] = j_inaddress;
                            peerJson["asset"] = p["asset"].ToString();
                            peerJson["n"] = (int)p["n"];
                            peerJson["value"] = (double)p["value"];
                            peerJson["outputaddress"] = p["address"].ToString();
                            peerJson["time"] = p["time"].ToString();
                            peerJson["utctime"] = (int)p["utcTime"];
                            peerJson["confirmations"] = p["confirmations"].ToString();
                            return peerJson;
                        }));
                        return json;
                    }
                case "getdeposits":
                    {
                        string from = _params[0].AsString();
                        string position = _params[1].AsString() != "" ? _params[1].AsString() : "1";
                        string offset = _params[2].AsString() != "" ? _params[2].AsString() : "20";
                        string jsonRes = RequestRpc("getDeposit", $"address={from}&position={position}&offset={offset}");

                        Newtonsoft.Json.Linq.JArray jsons = Newtonsoft.Json.Linq.JArray.Parse(jsonRes);

                        json["transaction"] = new JArray(jsons.Select(p =>
                        {
                            JObject peerJson = new JObject();
                            peerJson["blockHeight"] = p["blockHeight"].ToString();
                            peerJson["txid"] = p["txid"].ToString();
                            peerJson["type"] = p["type"].ToString();
                            Newtonsoft.Json.Linq.JToken[] jt = p["inAddress"].ToArray();
                            JArray j_inaddress = new JArray();
                            foreach (Newtonsoft.Json.Linq.JToken i in jt)
                            {
                                string s = i.ToString();
                                j_inaddress.Add(s);
                            }
                            peerJson["inputaddress"] = j_inaddress;
                            peerJson["asset"] = p["asset"].ToString();
                            peerJson["n"] = (int)p["n"];
                            peerJson["value"] = (double)p["value"];
                            peerJson["outputaddress"] = p["address"].ToString();
                            peerJson["time"] = p["time"].ToString();
                            peerJson["utctime"] = (int)p["utcTime"];
                            peerJson["confirmations"] = p["confirmations"].ToString();
                            return peerJson;
                        }));
                        return json;
                    }
                case "get_tx_list":
                    {
                        string from = _params[0].AsString();
                        string position = _params[1].AsString() != "" ? _params[1].AsString() : "1";
                        string offset = _params[2].AsString() != "" ? _params[2].AsString() : "20";
                        string jsonRes = RequestRpc("findTxAddressRecord", $"address={from}&position={position}&offset={offset}");
                        Newtonsoft.Json.Linq.JObject jsons = Newtonsoft.Json.Linq.JObject.Parse(jsonRes);
                        json["transaction"] = new JArray(jsons["txAddressRecord"].Select(p =>
                        {
                            JObject peerJson = new JObject();
                            peerJson["txid"] = p["txid"].ToString();
                            peerJson["blockHeight"] = p["blockHeight"].ToString();
                            peerJson["time"] = p["time"].ToString();
                            peerJson["type"] = p["type"].ToString();
                            Newtonsoft.Json.Linq.JToken[] jt = p["inAddressList"].ToArray();
                            JArray j_inaddress = new JArray();
                            foreach (Newtonsoft.Json.Linq.JToken i in jt)
                            {
                                string s = i.ToString();
                                j_inaddress.Add(s);
                            }
                            peerJson["inputaddress"] = j_inaddress;
                            peerJson["outputaddress"] = new JArray(p["outAddressList"].OrderBy(g => g["n"]).Select(k =>
                           {
                               JObject a = new JObject();
                               a["n"] = k["n"].ToString();
                               a["asset"] = k["asset"].ToString();
                               a["value"] = (double)k["value"];
                               a["address"] = k["outAddress"].ToString();
                               a["svalue"] = k["svalue"].ToString();
                               return a;
                           }));
                            return peerJson;
                        }));
                        return json;
                    }
                case "sendissuetransaction":
                    if (wallet == null || walletTimeLock.IsLocked())
                        throw new RpcException(-400, "Access denied");
                    else
                    {
                        UInt256 asset_id = UInt256.Parse(_params[0].AsString());
                        JArray to = (JArray)_params[1];
                        if (to.Count == 0)
                            throw new RpcException(-32602, "Invalid params");
                        TransactionOutput[] outputs = new TransactionOutput[to.Count];
                        for (int i = 0; i < to.Count; i++)
                        {
                            AssetDescriptor descriptor = new AssetDescriptor(asset_id);
                            outputs[i] = new TransactionOutput
                            {
                                AssetId = asset_id,
                                Value = Fixed8.Parse(to[i]["value"].AsString()),
                                ScriptHash = to[i]["address"].AsString().ToScriptHash()
                            };
                        }
                        IssueTransaction tx = wallet.MakeTransaction(new IssueTransaction
                        {
                            Version = 1,
                            Outputs = outputs
                        }, fee: Fixed8.One);
                        if (tx == null)
                            throw new RpcException(-300, "Insufficient funds");
                        ContractParametersContext context = new ContractParametersContext(tx);
                        wallet.Sign(context);
                        if (context.Completed)
                        {
                            tx.Witnesses = context.GetWitnesses();

                            if (tx.Size > Transaction.MaxTransactionSize)
                                throw new RpcException(-301, "The size of the free transaction must be less than 102400 bytes");

                            wallet.ApplyTransaction(tx);
                            system.LocalNode.Tell(new LocalNode.Relay { Inventory = tx });
                            return tx.ToJson();
                        }
                        else
                        {
                            return context.ToJson();
                        }
                    }
                case "gettransactiondata":
                    return GetTransactionData(_params);
                case "listsinceblock":
                    if (wallet == null || walletTimeLock.IsLocked())
                        throw new RpcException(-400, "Access denied");
                    else
                    {
                        try
                        {
                            uint walletHeight = wallet.WalletHeight;
                            var Transactions = wallet.GetTransactions();
                            int startBlockHeight = _params[0].AsString() != "" ? int.Parse(_params[0].AsString()) : 0;
                            int targetConfirmations = _params[1].AsString() != "" ? int.Parse(_params[1].AsString()) : 6;
                            using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot())
                            {
                                var trans = Transactions.Select(p => snapshot.Transactions.TryGet(p)).Where(p => p.Transaction != null
                               && p.BlockIndex >= startBlockHeight).Select(p => new
                               {
                                   p.Transaction,
                                   p.BlockIndex,
                                   Time = snapshot.GetHeader(p.BlockIndex).Timestamp,
                                   BlockHash = snapshot.GetHeader(p.BlockIndex).Hash
                               }).OrderBy(p => p.Time);

                                uint CurrentHeight = Blockchain.Singleton.Height;
                                json["txs"] = new JArray(
                                    trans.Select(p =>
                                    {
                                        JObject peerjson = new JObject();
                                        peerjson["txid"] = p.Transaction.Hash.ToString();
                                        peerjson["blockheight"] = p.BlockIndex;
                                        peerjson["blockhash"] = p.BlockHash.ToString();
                                        peerjson["utctime"] = p.Time;
                                        peerjson["confirmations"] = ((CurrentHeight - p.BlockIndex) > 0) ? (CurrentHeight - p.BlockIndex) : 0;

                                        List<string> addresses = new List<string>();
                                        foreach (var s in p.Transaction.References)
                                        {
                                            addresses.Add(s.Value.ScriptHash.ToAddress());
                                        }
                                        List<string> addres = addresses.Distinct().ToList();
                                        peerjson["inaddress"] = new JArray(addres.Select(g =>
                                        {
                                            JObject obj = g.ToString();
                                            return obj;
                                        }));
                                        peerjson["output"] = new JArray(p.Transaction.Outputs.Select((g, i) =>
                                        {
                                            JObject jobject = new JObject();
                                            jobject["asset"] = g.AssetId.ToString();
                                            jobject["outaddress"] = g.ScriptHash.ToAddress();
                                            jobject["value"] = g.Value.ToString();
                                            jobject["n"] = (ushort)i;
                                            return jobject;
                                        }));
                                        return peerjson;
                                    }));
                                json["lastblockheight"] = (walletHeight - targetConfirmations > 0) ? (walletHeight - targetConfirmations) : 0;
                                return json;
                            }
                        }
                        catch (Exception ex)
                        {
                            int startBlockHeight = _params[0].AsString() != "" ? int.Parse(_params[0].AsString()) : 0;
                            json["txs"] = new JArray();
                            json["lastblockheight"] = startBlockHeight;
                            return json;
                        }
                    }
                case "sendtocold":
                    return SendToCold(_params);
                case "sendtoaddressorder":
                    return SendToAddressOrder(_params);
                case "getrawtransactionorder":
                    {
                        UInt256 hash = UInt256.Parse(_params[0].AsString());
                        bool verbose = _params.Count >= 2 && _params[1].AsBooleanOrDefault(false);
                        Transaction tx = Blockchain.Singleton.GetTransaction(hash);
                        if (tx == null)
                            throw new RpcException(-100, "Unknown transaction");
                        if (verbose)
                        {
                            json = tx.ToJson();
                            if (tx.Attributes.Length > 0)
                            {
                                JArray attrs = (JArray)json["attributes"];
                                for (int i = 0; i < tx.Attributes.Length; i++)
                                {
                                    if (tx.Attributes[i].Usage == TransactionAttributeUsage.Description)
                                    {
                                        attrs[i]["data"] = ReadOderData(tx.Attributes[i].Data);
                                    }
                                }
                                json["attributes"] = attrs;
                            }
                            uint? height = Blockchain.Singleton.Store.GetTransactions().TryGet(hash)?.BlockIndex;
                            if (height != null)
                            {
                                Header header = Blockchain.Singleton.Store.GetHeader((uint)height);
                                json["blockhash"] = header.Hash.ToString();
                                json["confirmations"] = Blockchain.Singleton.Height - header.Index + 1;
                                json["blocktime"] = header.Timestamp;
                            }
                            return json;
                        }
                        return tx.ToArray().ToHexString();
                    }
                case "exportaddresswif":
                    if (wallet == null || walletTimeLock.IsLocked())
                        throw new RpcException(-400, "Access denied");
                    else
                    {
                        UInt160 scriptHash = _params[0].AsString().ToScriptHash();
                        WalletAccount account = wallet.GetAccount(scriptHash);
                        if (account == null)
                        {
                            throw new RpcException(-2146232969, $"The given key '{scriptHash}' was not present in the dictionary.");
                        }
                        json["wif"] = account.GetKey().Export();
                        json["prikey"] = account.GetKey().PrivateKey.ToHexString();
                        json["pubkey"] = account.GetKey().PublicKey.EncodePoint(true).ToHexString();
                        json["address"] = account.Address;
                        return json;
                    }
                case "getcontractopcode":
                    {
                        byte[] script = _params[0].AsString().HexToBytes();
                        var ops = Avm2Asm.Trans(script); ;
                        json["opcode"] = new JArray(ops.Select(p =>
                        {
                            JObject opJson = new JObject();
                            opJson = p.ToString();
                            return opJson;
                        }));
                        return json;
                    }
                default:
                    throw new RpcException(-32601, "Method not found");
            }
        }

        private JObject SendToCold(JArray _params)
        {
            if (wallet == null || walletTimeLock.IsLocked())
                throw new RpcException(-400, "Access denied");
            else
            {
                UInt160 scriptHash = _params[0].AsString().ToScriptHash();
                UInt256 assetId = _params.Count >= 2 ? UInt256.Parse(_params[1].AsString()) : Blockchain.GoverningToken.Hash;
                UInt160 fee_address = _params.Count >= 3 ? _params[2].AsString().ToScriptHash() : null;
                IEnumerable<Coin> allCoins = wallet.FindUnspentCoins();
                Coin[] coins = TransactionContract.FindUnspentCoins(allCoins, assetId);
                Transaction tx = MakeToColdTransaction(coins, scriptHash, assetId, fee_address);
                if (tx == null)
                    throw new RpcException(-300, "Insufficient funds");
                ContractParametersContext context = new ContractParametersContext(tx);
                wallet.Sign(context);
                if (context.Completed)
                {
                    tx.Witnesses = context.GetWitnesses();
                    if (tx.Size > Transaction.MaxTransactionSize)
                        throw new RpcException(-301, "The size of the free transaction must be less than 102400 bytes");
                    wallet.ApplyTransaction(tx);
                    system.LocalNode.Tell(new LocalNode.Relay { Inventory = tx });
                    return tx.ToJson();
                }
                else
                {
                    return context.ToJson();
                }
            }
        }

        private JObject SendToAddressOrder(JArray _params)
        {
            if (wallet == null || walletTimeLock.IsLocked())
                throw new RpcException(-400, "Access denied");
            else
            {
                string remarks = _params[0].AsString();
                List<TransactionAttribute> attributes = new List<TransactionAttribute>();
                using (ScriptBuilder sb = new ScriptBuilder())
                {
                    sb.EmitPush(remarks);
                    attributes.Add(new TransactionAttribute
                    {
                        Usage = TransactionAttributeUsage.Description,
                        Data = sb.ToArray()
                    });
                }
                UIntBase assetId = UIntBase.Parse(_params[1].AsString());
                AssetDescriptor descriptor = new AssetDescriptor(assetId);
                UInt160 scriptHash = _params[2].AsString().ToScriptHash();
                BigDecimal value = BigDecimal.Parse(_params[3].AsString(), descriptor.Decimals);
                if (value.Sign <= 0)
                    throw new RpcException(-32602, "Invalid params");
                Fixed8 fee = _params.Count >= 5 ? Fixed8.Parse(_params[4].AsString()) : Fixed8.Zero;
                if (fee < Fixed8.Zero)
                    throw new RpcException(-32602, "Invalid params");
                UInt160 change_address = _params.Count >= 6 ? _params[5].AsString().ToScriptHash() : null;
                UInt160 fee_address = _params.Count >= 7 ? _params[6].AsString().ToScriptHash() : null;
                if (assetId.Equals(Blockchain.GoverningToken.Hash)) fee_address = null;
                Transaction tx = wallet.MakeTransaction(attributes, new[]
                {
                    new TransferOutput
                    {
                        AssetId = assetId,
                        Value = value,
                        ScriptHash = scriptHash
                    }
                }, fee_address: fee_address, change_address: change_address, fee: fee);
                if (tx == null)
                    throw new RpcException(-300, "Insufficient funds");
                ContractParametersContext context = new ContractParametersContext(tx);
                wallet.Sign(context);
                if (context.Completed)
                {
                    tx.Witnesses = context.GetWitnesses();

                    if (tx.Size > Transaction.MaxTransactionSize)
                        throw new RpcException(-301, "The size of the free transaction must be less than 102400 bytes");

                    wallet.ApplyTransaction(tx);
                    system.LocalNode.Tell(new LocalNode.Relay { Inventory = tx });
                    return tx.ToJson();
                }
                else
                {
                    return context.ToJson();
                }
            }
        }

        private JObject GetTransactionData(JArray _params)
        {
            if (wallet == null || walletTimeLock.IsLocked())
                throw new RpcException(-400, "Access denied");
            else
            {
                UIntBase assetId = UIntBase.Parse(_params[0].AsString());
                AssetDescriptor descriptor = new AssetDescriptor(assetId);
                UInt160 scriptHash = _params[1].AsString().ToScriptHash();
                BigDecimal value = BigDecimal.Parse(_params[2].AsString(), descriptor.Decimals);
                if (value.Sign <= 0)
                    throw new RpcException(-32602, "Invalid params");
                Fixed8 fee = _params.Count >= 4 ? Fixed8.Parse(_params[3].AsString()) : Fixed8.Zero;
                if (fee < Fixed8.Zero)
                    throw new RpcException(-32602, "Invalid params");
                UInt160 change_address = _params.Count >= 5 ? _params[4].AsString().ToScriptHash() : null;
                UInt160 fee_address = _params.Count >= 6 ? _params[5].AsString().ToScriptHash() : null;
                if (assetId.Equals(Blockchain.GoverningToken.Hash)) fee_address = null;
                Transaction tx = wallet.MakeTransaction(null, new[]
                {
                    new TransferOutput
                    {
                        AssetId = assetId,
                        Value = value,
                        ScriptHash = scriptHash
                    }
                }, fee_address: fee_address, change_address: change_address, fee: fee);
                if (tx == null)
                    throw new RpcException(-300, "Insufficient funds");
                ContractParametersContext context = new ContractParametersContext(tx);
                wallet.Sign(context);
                if (context.Completed)
                {
                    tx.Witnesses = context.GetWitnesses();

                    if (tx.Size > Transaction.MaxTransactionSize)
                        throw new RpcException(-301, "The data is too long.");

                    return Bhp.IO.Helper.ToArray(tx).ToHexString();
                }
                else
                {
                    return context.ToJson();
                }
            }
        }

        private string ReadOderData(byte[] attrData)
        {
            byte[] data;
            BinaryReader OpReader = new BinaryReader(new MemoryStream(attrData, false));
            OpCode opcode = (OpCode)OpReader.ReadByte();
            if (opcode >= OpCode.PUSHBYTES1 && opcode <= OpCode.PUSHBYTES75)
                data = OpReader.ReadBytes((byte)opcode);
            else
                switch (opcode)
                {
                    // Push value
                    case OpCode.PUSH0:
                        data = new byte[0];
                        break;
                    case OpCode.PUSHDATA1:
                        data = OpReader.ReadBytes(OpReader.ReadByte());
                        break;
                    case OpCode.PUSHDATA2:
                        data = OpReader.ReadBytes(OpReader.ReadUInt16());
                        break;
                    case OpCode.PUSHDATA4:
                        data = OpReader.ReadBytes(OpReader.ReadInt32());
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
                        data = ((int)opcode - (int)OpCode.PUSH1 + 1).ToString().HexToBytes();
                        break;
                    default:
                        data = new byte[0];
                        break;
                }
            return System.Text.Encoding.Default.GetString(data);
        }

        private string RequestRpc(string method, string kvs)
        {
            string jsonRes = "";
            using (HttpClient client = new HttpClient())
            {
                string uri = $"{ExtensionSettings.Default.DataRPCServer.Host}/{method}?{kvs}";
                client.BaseAddress = new Uri(uri);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var response = client.GetAsync(uri).Result;
                Task<Stream> task = response.Content.ReadAsStreamAsync();
                Stream backStream = task.Result;
                StreamReader reader = new StreamReader(backStream);
                jsonRes = reader.ReadToEnd();
                reader.Close();
                backStream.Close();
            }
            return jsonRes;
        }

        private Transaction MakeToColdTransaction(Coin[] coins, UInt160 outAddress, UInt256 assetId, UInt160 fee_address = null)
        {
            int MaxInputCount = 50;
            Transaction tx = new ContractTransaction();
            tx.Attributes = new TransactionAttribute[0];
            tx.Witnesses = new Witness[0];

            List<CoinReference> inputs = new List<CoinReference>();
            List<TransactionOutput> outputs = new List<TransactionOutput>();

            Fixed8 sum = Fixed8.Zero;
            if (coins.Length < 50)
            {
                MaxInputCount = coins.Length;
            }
            for (int j = 0; j < MaxInputCount; j++)
            {
                sum += coins[j].Output.Value;
                inputs.Add(new CoinReference
                {
                    PrevHash = coins[j].Reference.PrevHash,
                    PrevIndex = coins[j].Reference.PrevIndex
                });
            }
            tx.Inputs = inputs.ToArray();
            outputs.Add(new TransactionOutput
            {
                AssetId = assetId,
                ScriptHash = outAddress,
                Value = sum

            });
            if (tx.SystemFee > Fixed8.Zero)
            {
                outputs.Add(new TransactionOutput
                {
                    AssetId = Blockchain.UtilityToken.Hash,
                    Value = tx.SystemFee
                });
            }
            tx.Outputs = outputs.ToArray();
            Fixed8 transfee = BhpTxFee.EstimateTxFee(tx, assetId);
            if (tx.Outputs[0].Value <= transfee)
            {
                return null;
            }
            tx.Outputs[0].Value -= transfee;
            return TransactionContract.EstimateFee(wallet, tx, null, fee_address);
        }

        /// <summary>
        /// 密码长度限制128
        /// </summary>
        public const int MaxPWLength = 128;
        public static bool VerifyPW(string password)
        {
            if (password.Length > MaxPWLength)
            {
                return false;
            }
            return true;
        }
    }
}
