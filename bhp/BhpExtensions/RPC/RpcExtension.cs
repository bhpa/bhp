using Bhp.BhpExtensions.Transactions;
using Bhp.BhpExtensions.Wallets;
using Bhp.IO.Json;
using Bhp.Ledger;
using Bhp.Network.P2P.Payloads;
using Bhp.Network.RPC;
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
            switch (method)
            {
                case "unlock":
                    return Unlock(_params);
                case "verifytx":
                    return VerifyTx(_params);
                case "getutxoofaddress":
                    return GetUtxoOfAddress(_params);
                case "gettransaction":
                    return GetTransaction(_params);
                case "getdeposits":
                    return GetDeposits(_params);
                case "get_tx_list":
                    return GetTxList(_params);
                default:
                    throw new RpcException(-32601, "Method not found");
            }
        }

        private JObject Unlock(JArray _params)
        {
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
        }

        private static JObject VerifyTx(JArray _params)
        {
            JObject json = new JObject();
            Transaction tx = Transaction.DeserializeFrom(_params[0].AsString().HexToBytes());
            string res = VerifyTransaction.Verify(Blockchain.Singleton.GetSnapshot(), new List<Transaction> { tx }, tx);

            json["result"] = res;
            if ("success".Equals(res))
            {
                json["tx"] = tx.ToJson();
            }
            return json;
        }

        private JObject GetUtxoOfAddress(JArray _params)
        {
            JObject json = new JObject();
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

        private JObject GetTransaction(JArray _params)
        {
            JObject json = new JObject();
            string from = _params[0].AsString();
            string position = _params.Count >= 2 ? _params[1].AsString() : "1";
            string offset = _params.Count >= 3 ? _params[2].AsString() : "20";
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

        private JObject GetDeposits(JArray _params)
        {
            return null;
            /*
            JObject json = new JObject();
            string from = _params[0].AsString();
            string position = _params.Count >= 2 ? _params[1].AsString() : "1";
            string offset = _params.Count >= 3 ? _params[2].AsString() : "20";
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
            */
        }

        private JObject GetTxList(JArray _params)
        {
            JObject json = new JObject();
            string from = _params[0].AsString();
            string position = _params.Count >= 2 ? _params[1].AsString() : "1";
            string offset = _params.Count >= 3 ? _params[2].AsString() : "20";
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

    }
}
