using Akka.Actor;
using Bhp.BhpExtensions.Transactions;
using Bhp.BhpExtensions.Wallets;
using Bhp.IO.Json;
using Bhp.Ledger;
using Bhp.Network.P2P;
using Bhp.Network.P2P.Payloads;
using Bhp.Network.RPC;
using Bhp.SmartContract;
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

        public RpcExtension(BhpSystem system,Wallet wallet, RpcServer rpcServer)
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
                        string res = VerifyTransaction.Verify(Blockchain.Singleton.GetSnapshot(), new List<Transaction> { tx },tx);

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
                        string jsonRes = RequestRpc("getUtxo",$"address={from}");

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
                        string position = _params[1].AsString() != "" ? _params[1].AsString() :"1";
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
                            Newtonsoft.Json.Linq.JToken [] jt = p["inAddressList"].ToArray();
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
                            wallet.ApplyTransaction(tx);
                            system.LocalNode.Tell(new LocalNode.Relay { Inventory = tx });
                            return tx.ToJson();
                        }
                        else
                        {
                            return context.ToJson();
                        }
                    }
                default:
                    throw new RpcException(-32601, "Method not found");
            } 
        }

        private string RequestRpc(string method,string kvs)
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
         
    }
}
