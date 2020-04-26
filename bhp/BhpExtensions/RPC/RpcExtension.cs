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

        /// <summary>
        /// 密码长度限制128
        /// </summary>
        public const int MaxPWLength = 128;
        /// <summary>
        /// invoke 随机数
        /// </summary>
        private static readonly Random rand = new Random();

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

        public static bool VerifyPW(string password)
        {
            if (password.Length > MaxPWLength)
            {
                return false;
            }
            return true;
        }

        public JObject Process(string method, JArray _params)
        {
            JObject json = new JObject();

            switch (method)
            {
                case "claimgas": return ClaimGas(json);
                case "exportaddresswif": return ExportAddressWif(_params, json);
                case "get_tx_list": return GetTxList(_params, json);
                case "getcontractopcode": return GetContractOpCode(_params, json);
                case "getdeposits": return GetDeposits(_params, json);
                case "getrawtransactionorder": return GetRawTransactionOrder(_params, ref json);
                case "gettransaction": return GetTransaction(_params, json);
                case "gettransactiondata": return GetTransactionData(_params);
                case "getutxoofaddress": return GetUtxoOfAddress(_params, json);
                case "getutxos": return GetUtxos(_params, json);
                case "listsinceblock": return ListSinceBlock(_params, json);
                case "sendissuetransaction": return SendIssueTransaction(_params);
                case "sendinvokescript": return SendInvokeScript(_params);
                case "sendtoaddressorder": return SendToAddressOrder(_params);
                case "sendtocold": return SendToCold(_params);
                case "showgas": return ShowGas(json);
                case "unlock": return Unlock(_params);
                case "verifytx": return VerifyTx(_params, json);
                default:
                    throw new RpcException(-32601, "Method not found");
            }
        }

        /// <summary>
        /// 提取钱包中的GAS
        /// </summary>
        /// <returns>提取GAS的交易ID集合</returns>
        private JObject ClaimGas(JObject json)
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

        /// <summary>
        /// 获取指定地址的信息
        /// </summary>
        /// <param name="_params[0]">地址</param>
        /// <returns>地址的相关信息(WIF、私钥、公钥、地址)</returns>
        private JObject ExportAddressWif(JArray _params, JObject json)
        {
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
        }

        /// <summary>
        /// 获取指定地址的交易列表
        /// </summary>
        /// <param name="_params[0]">地址</param>
        /// <param name="_params[1]">起始索引位置，默认1</param>
        /// <param name="_params[2]">偏移量，默认20</param>
        /// <returns>交易列表</returns>
        private JObject GetTxList(JArray _params, JObject json)
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

        /// <summary>
        /// 获取合约脚本的指令解析
        /// </summary>
        /// <param name="_params">合约脚本</param>
        /// <returns>指令集</returns>
        private static JObject GetContractOpCode(JArray _params, JObject json)
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

        /// <summary>
        /// 获取指定地址的交易列表
        /// </summary>
        /// <param name="_params[0]">地址</param>
        /// <param name="_params[1]">起始索引位置，默认1</param>
        /// <param name="_params[2]">偏移量，默认20</param>
        /// <returns>交易列表</returns>
        private JObject GetDeposits(JArray _params, JObject json)
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

        /// <summary>
        /// 查询订单交易信息
        /// </summary>
        /// <param name="_params[0]">交易ID</param>
        /// <param name="_params[1]">可选参数，verbose 默认值为 0，(0：返回区块头的序列化信息；1：返回Json格式的区块头信息)</param>
        /// <returns>订单交易信息</returns>
        private JObject GetRawTransactionOrder(JArray _params, ref JObject json)
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

        /// <summary>
        /// 获取指定地址的交易列表
        /// </summary>
        /// <param name="_params[0]">地址</param>
        /// <param name="_params[1]">起始索引位置，默认1</param>
        /// <param name="_params[2]">偏移量，默认20</param>
        /// <returns>交易列表</returns>
        private JObject GetTransaction(JArray _params, JObject json)
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

        /// <summary>
        /// 获取交易的的十六进制字符串(不上链)
        /// </summary>
        /// <param name="_params[0]">资产 ID</param>
        /// <param name="_params[1]">收款地址</param>
        /// <param name="_params[2]">转账金额</param>
        /// <param name="_params[3]">手续费，可选参数，默认为 0</param>
        /// <param name="_params[4]">找零地址，可选参数，默认为钱包中第一个标准地址</param>
        /// <param name="_params[5]">bhp手续费地址，可选参数。（转账资产为BHP时，此参数无效）</param>
        /// <returns>交易的的十六进制字符串</returns>
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

        /// <summary>
        /// 获取指定地址的未花费UTXO
        /// </summary>
        /// <param name="_params[0]">地址</param>
        /// <returns>指定地址的未花费UTXO</returns>
        private JObject GetUtxoOfAddress(JArray _params, JObject json)
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

        /// <summary>
        /// 查询指定地址指定资产的未花费UTXO
        /// </summary>
        /// <param name="_params[0]">地址</param>
        /// <param name="_params[1]">可选参数，资产ID，默认BHP</param>
        /// <returns>指定地址指定资产的未花费UTXO</returns>
        private JObject GetUtxos(JArray _params, JObject json)
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
                Coin[] coins_array = coins.ToArray();

                json["utxos"] = new JArray(coins_array.Select(p =>
                {
                    return p.Reference.ToJson();
                }));

                return json;
            }
        }

        /// <summary>
        /// 根据参数返回与钱包相关的所有交易
        /// </summary>
        /// <param name="_params[0]">开始的区块高度（包含该区块）</param>
        /// <param name="_params[1]">目标确认数，默认6</param>
        /// <returns>满足条件的交易</returns>
        private JObject ListSinceBlock(JArray _params, JObject json)
        {
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
        }

        /// <summary>
        /// 分发资产
        /// </summary>
        /// <param name="_params[0]">资产 ID</param>
        /// <param name="_params[1]">数组{"address": \<收款地址>,"value": \<转账金额>}</param>
        /// <returns>交易</returns>
        private JObject SendIssueTransaction(JArray _params)
        {
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
        }

        /// <summary>
        /// 发送invoke交易
        /// </summary>
        /// <param name="script">合约执行脚本</param>
        /// <param name="gas_consumed">手续费</param>
        /// <param name="check_witness_address">见证者地址并作为输入地址，可选参数</param>
        /// <returns></returns>
        private JObject SendInvokeScript(JArray _params)
        {
            if (wallet == null || walletTimeLock.IsLocked())
                throw new RpcException(-400, "Access denied");
            else
            {
                byte[] script = _params[0].AsString().HexToBytes();
                Fixed8 gas_consumed = Fixed8.Parse(_params[1].AsString());
                if (gas_consumed < Fixed8.Zero)
                    throw new RpcException(-32602, "Invalid params");
                UInt160 check_witness_address = _params.Count >= 3 ? _params[2].AsString().ToScriptHash() : null;

                InvocationTransaction tx = null;

                gas_consumed -= Fixed8.FromDecimal(10);
                if (gas_consumed < Fixed8.Zero) gas_consumed = Fixed8.Zero;
                gas_consumed = gas_consumed.Ceiling();

                tx = new InvocationTransaction
                {
                    Version = 1,
                    Script = script,
                    Gas = gas_consumed
                };

                byte[] timeStamp = System.Text.ASCIIEncoding.ASCII.GetBytes(DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
                byte[] nonce = new byte[8];
                rand.NextBytes(nonce);
                tx.Attributes = new TransactionAttribute[] {
                    new TransactionAttribute() {
                        Usage = TransactionAttributeUsage.Remark,
                        Data = timeStamp.Concat(nonce).ToArray()
                    }
                };

                tx = wallet.MakeTransaction(tx, from: check_witness_address);
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

        /// <summary>
        /// 发送一个包含订单信息的交易
        /// </summary>
        /// <param name="_params[0]">订单信息</param>
        /// <param name="_params[1]">资产 ID</param>
        /// <param name="_params[2]">收款地址</param>
        /// <param name="_params[3]">转账金额</param>
        /// <param name="_params[4]">手续费，可选参数，默认为 0</param>
        /// <param name="_params[5]">找零地址，可选参数，默认为钱包中第一个标准地址</param>
        /// <param name="_params[6]">bhp手续费地址，可选参数。（转账资产包含BHP时，此参数无效）</param>
        /// <returns>交易</returns>
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

        /// <summary>
        /// 将钱包内的资产转账到指定地址
        /// </summary>
        /// <param name="_params[0]">收款地址</param>
        /// <param name="_params[1]">资产ID</param>
        /// <param name="_params[2]">bhp手续费地址，可选参数。（转账资产包含BHP时，此参数无效）</param>
        /// <returns>交易</returns>
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

        /// <summary>
        /// 返回本钱包的BhpGas
        /// </summary>
        /// <returns>本钱包的BhpGas</returns>
        private JObject ShowGas(JObject json)
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

        /// <summary>
        /// 将钱包解锁指定时长，单位秒
        /// </summary>
        /// <param name="_params[0]">钱包密码</param>
        /// <param name="_params[1]">解锁钱包的秒数，600=10分钟</param>
        /// <returns>解锁结果</returns>
        private JObject Unlock(JArray _params)
        {
            if (ExtensionSettings.Default.WalletConfig.Path.Trim().Length < 1) throw new RpcException(-500, "Wallet file is exists.");

            if (_params.Count < 2) throw new RpcException(-501, "parameter is error.");

            string password = _params[0].AsString();
            if (!VerifyPW(password))
            {
                throw new RpcException(-501, $"password max length {MaxPWLength}");
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
        }

        /// <summary>
        /// 验证交易是否有效
        /// </summary>
        /// <param name="_params[0]">交易的十六进字符串</param>
        /// <returns>验证结果</returns>
        private static JObject VerifyTx(JArray _params, JObject json)
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
    }
}
