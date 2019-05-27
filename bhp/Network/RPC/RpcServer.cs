using Akka.Actor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Bhp.IO;
using Bhp.IO.Json;
using Bhp.Ledger;
using Bhp.Network.P2P;
using Bhp.Network.P2P.Payloads;
using Bhp.Persistence;
using Bhp.Plugins;
using Bhp.SmartContract;
using Bhp.VM;
using Bhp.Wallets;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Bhp.BhpExtensions.RPC;
using Bhp.SmartContract.Native;

namespace Bhp.Network.RPC
{
    public sealed class RpcServer : IDisposable
    {
        public Wallet Wallet;

        private IWebHost host;
        private Fixed8 maxGasInvoke;
        public readonly BhpSystem system;
        public RpcExtension rpcExtension;

        public RpcServer(BhpSystem system, Wallet wallet = null, Fixed8 maxGasInvoke = default(Fixed8))
        {
            this.system = system;
            this.Wallet = wallet;
            this.maxGasInvoke = maxGasInvoke;

            rpcExtension = new RpcExtension(system, wallet, this);
        }

        public void SetWallet(Wallet wallet)
        {
            this.Wallet = wallet;
        }

        private static JObject CreateErrorResponse(JObject id, int code, string message, JObject data = null)
        {
            JObject response = CreateResponse(id);
            response["error"] = new JObject();
            response["error"]["code"] = code;
            response["error"]["message"] = message;
            if (data != null)
                response["error"]["data"] = data;
            return response;
        }

        private static JObject CreateResponse(JObject id)
        {
            JObject response = new JObject();
            response["jsonrpc"] = "2.0";
            response["id"] = id;
            return response;
        }

        public void Dispose()
        {
            if (host != null)
            {
                host.Dispose();
                host = null;
            }
        }

        public void OpenWallet(Wallet wallet)
        {
            this.Wallet = wallet;
            rpcExtension.SetWallet(wallet);
        }

        private JObject Process(string method, JArray _params)
        {
            switch (method)
            {
                case "getassetstate":
                    return GetAssetState(_params);
                case "getbestblockhash":
                    return Blockchain.Singleton.CurrentBlockHash.ToString();
                case "getblock":
                    return GetBlock(_params);
                case "getblockcount":
                    return Blockchain.Singleton.Height + 1;
                case "getblockhash":
                    return GetBlockHash(_params);
                case "getblockheader":
                    return GetBlockHeader(_params);
                case "getblocksysfee":
                    return GetBlockSysFee(_params);
                case "getconnectioncount":
                    return LocalNode.Singleton.ConnectedCount;
                case "getcontractstate":
                    return GetContractState(_params);
                case "getpeers":
                    return GetPeers();
                case "getrawmempool":
                    return GetRawMempool(_params);
                case "getrawtransaction":
                    return GetRawTransaction(_params);
                case "getstorage":
                    return GetStorage(_params);
                case "gettransactionheight":
                    return GetTransactionHeight(_params);
                case "gettxout":
                    return GetTxOut(_params);
                case "getvalidators":
                    return GetValidators();
                case "getversion":
                    return GetVersion();
                case "invokefunction":
                    return InvokeFunction(_params);
                case "invokescript":
                    byte[] script = _params[0].AsString().HexToBytes();
                    return GetInvokeResult(script);
                case "sendrawtransaction":
                    return SendRawTransaction(_params);
                case "submitblock":
                    return SubmitBlock(_params);
                case "validateaddress":
                    return ValidateAddress(_params);
                case "listplugins":
                    return ListPlugins();
                default:
                    return rpcExtension.Process(method, _params);
            }
        }

        private JObject GetAssetState(JArray _params)
        {
            UInt256 asset_id = UInt256.Parse(_params[0].AsString());
            AssetState asset = Blockchain.Singleton.Store.GetAssets().TryGet(asset_id);
            return asset?.ToJson() ?? throw new RpcException(-100, "Unknown asset");
        }

        private JObject GetBlock(JArray _params)
        {
            Block block;
            if (_params[0] is JNumber)
            {
                uint index = uint.Parse(_params[0].AsString());
                block = Blockchain.Singleton.Store.GetBlock(index);
            }
            else
            {
                UInt256 hash = UInt256.Parse(_params[0].AsString());
                block = Blockchain.Singleton.Store.GetBlock(hash);
            }
            if (block == null)
                throw new RpcException(-100, "Unknown block");
            bool verbose = _params.Count >= 2 && _params[1].AsBoolean();
            if (verbose)
            {
                JObject json = block.ToJson();
                json["confirmations"] = Blockchain.Singleton.Height - block.Index + 1;
                UInt256 hash = Blockchain.Singleton.Store.GetNextBlockHash(block.Hash);
                if (hash != null)
                    json["nextblockhash"] = hash.ToString();
                return json;
            }
            return block.ToArray().ToHexString();
        }

        private JObject GetBlockHash(JArray _params)
        {
            uint height = uint.Parse(_params[0].AsString());
            if (height <= Blockchain.Singleton.Height)
                using (ApplicationEngine engine = NativeContract.GAS.TestCall("getSysFeeAmount", height))
                {
                    return engine.ResultStack.Peek().GetBigInteger().ToString();
                }
            throw new RpcException(-100, "Invalid Height");
        }

        private JObject GetBlockHeader(JArray _params)
        {
            Header header;
            if (_params[0] is JNumber)
            {
                uint height = uint.Parse(_params[0].AsString());
                header = Blockchain.Singleton.Store.GetHeader(height);
            }
            else
            {
                UInt256 hash = UInt256.Parse(_params[0].AsString());
                header = Blockchain.Singleton.Store.GetHeader(hash);
            }
            if (header == null)
                throw new RpcException(-100, "Unknown block");

            bool verbose = _params.Count >= 2 && _params[1].AsBoolean();
            if (verbose)
            {
                JObject json = header.ToJson();
                json["confirmations"] = Blockchain.Singleton.Height - header.Index + 1;
                UInt256 hash = Blockchain.Singleton.Store.GetNextBlockHash(header.Hash);
                if (hash != null)
                    json["nextblockhash"] = hash.ToString();
                return json;
            }

            return header.ToArray().ToHexString();
        }

        private JObject GetBlockSysFee(JArray _params)
        {
            uint height = uint.Parse(_params[0].AsString());
            if (height <= Blockchain.Singleton.Height)
                using (ApplicationEngine engine = NativeContract.GAS.TestCall("getSysFeeAmount", height))
                {
                    return engine.ResultStack.Peek().GetBigInteger().ToString();
                }
            throw new RpcException(-100, "Invalid Height");
        }

        private JObject GetContractState(JArray _params)
        {
            UInt160 script_hash = UInt160.Parse(_params[0].AsString());
            ContractState contract = Blockchain.Singleton.Store.GetContracts().TryGet(script_hash);
            return contract?.ToJson() ?? throw new RpcException(-100, "Unknown contract");
        }

        private JObject GetPeers()
        {
            JObject json = new JObject();
            json["unconnected"] = new JArray(LocalNode.Singleton.GetUnconnectedPeers().Select(p =>
            {
                JObject peerJson = new JObject();
                peerJson["address"] = p.Address.ToString();
                peerJson["port"] = p.Port;
                return peerJson;
            }));
            json["bad"] = new JArray(); //badpeers has been removed
            json["connected"] = new JArray(LocalNode.Singleton.GetRemoteNodes().Select(p =>
            {
                JObject peerJson = new JObject();
                peerJson["address"] = p.Remote.Address.ToString();
                peerJson["port"] = p.ListenerTcpPort;
                return peerJson;
            }));
            return json;
        }

        private JObject GetRawMempool(JArray _params)
        {
            bool shouldGetUnverified = _params.Count >= 1 && _params[0].AsBoolean();
            if (!shouldGetUnverified)
                return new JArray(Blockchain.Singleton.MemPool.GetVerifiedTransactions().Select(p => (JObject)p.Hash.ToString()));

            JObject json = new JObject();
            json["height"] = Blockchain.Singleton.Height;
            Blockchain.Singleton.MemPool.GetVerifiedAndUnverifiedTransactions(
                out IEnumerable<Transaction> verifiedTransactions,
                out IEnumerable<Transaction> unverifiedTransactions);
            json["verified"] = new JArray(verifiedTransactions.Select(p => (JObject)p.Hash.ToString()));
            json["unverified"] = new JArray(unverifiedTransactions.Select(p => (JObject)p.Hash.ToString()));
            return json;
        }

        private JObject GetRawTransaction(JArray _params)
        {
            UInt256 hash = UInt256.Parse(_params[0].AsString());
            bool verbose = _params.Count >= 2 && _params[1].AsBoolean();
            Transaction tx = Blockchain.Singleton.GetTransaction(hash);
            if (tx == null)
                throw new RpcException(-100, "Unknown transaction");
            if (verbose)
            {
                JObject json = tx.ToJson();
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

        private JObject GetStorage(JArray _params)
        {
            UInt160 script_hash = UInt160.Parse(_params[0].AsString());
            byte[] key = _params[1].AsString().HexToBytes();
            StorageItem item = Blockchain.Singleton.Store.GetStorages().TryGet(new StorageKey
            {
                ScriptHash = script_hash,
                Key = key
            }) ?? new StorageItem();
            return item.Value?.ToHexString();
        }

        private JObject GetTransactionHeight(JArray _params)
        {
            UInt256 hash = UInt256.Parse(_params[0].AsString());
            uint? height = Blockchain.Singleton.Store.GetTransactions().TryGet(hash)?.BlockIndex;
            if (height.HasValue) return height.Value;
            throw new RpcException(-100, "Unknown transaction");
        }

        private JObject GetTxOut(JArray _params)
        {
            UInt256 hash = UInt256.Parse(_params[0].AsString());
            ushort index = ushort.Parse(_params[1].AsString());
            return Blockchain.Singleton.Store.GetUnspent(hash, index)?.ToJson(index);
        }

        private JObject GetValidators()
        {
            using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot())
            {
                var validators = NativeContract.Bhp.GetValidators(snapshot);
                return NativeContract.Bhp.GetRegisteredValidators(snapshot).Select(p =>
                {
                    JObject validator = new JObject();
                    validator["publickey"] = p.PublicKey.ToString();
                    validator["votes"] = p.Votes.ToString();
                    validator["active"] = validators.Contains(p.PublicKey);
                    return validator;
                }).ToArray();
            }
        }

        private JObject GetVersion()
        {
            JObject json = new JObject();
            json["tcpPort"] = LocalNode.Singleton.ListenerTcpPort;
            json["wsPort"] = LocalNode.Singleton.ListenerWsPort;
            json["nonce"] = LocalNode.Nonce;
            json["useragent"] = LocalNode.UserAgent;
            return json;
        }

        private JObject InvokeFunction(JArray _params)
        {
            UInt160 script_hash = UInt160.Parse(_params[0].AsString());
            string operation = _params[1].AsString();
            ContractParameter[] args = _params.Count >= 3 ? ((JArray)_params[2]).Select(p => ContractParameter.FromJson(p)).ToArray() : new ContractParameter[0];
            byte[] script;
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                script = sb.EmitAppCall(script_hash, operation, args).ToArray();
            }
            return GetInvokeResult(script);
        }

        private JObject GetInvokeResult(byte[] script)
        {
            ApplicationEngine engine = ApplicationEngine.Run(script, extraGAS: maxGasInvoke.value);
            JObject json = new JObject();
            json["script"] = script.ToHexString();
            json["state"] = engine.State;
            json["gas_consumed"] = engine.GasConsumed.ToString();
            try
            {
                json["stack"] = new JArray(engine.ResultStack.Select(p => p.ToParameter().ToJson()));
            }
            catch (InvalidOperationException)
            {
                json["stack"] = "error: recursive reference";
            }
            if (Wallet != null)
            {
                InvocationTransaction tx = new InvocationTransaction
                {
                    Version = 1,
                    Script = json["script"].AsString().HexToBytes(),
                    Gas = Fixed8.Parse(json["gas_consumed"].AsString())
                };
                tx.Gas -= Fixed8.FromDecimal(10);
                if (tx.Gas < Fixed8.Zero) tx.Gas = Fixed8.Zero;
                tx.Gas = tx.Gas.Ceiling();
                tx = Wallet.MakeTransaction(tx);
                if (tx != null)
                {
                    ContractParametersContext context = new ContractParametersContext(tx);
                    Wallet.Sign(context);
                    if (context.Completed)
                        tx.Witness = context.GetWitness();
                    else
                        tx = null;
                }

                if (tx?.Size > Transaction.MaxTransactionSize)
                    throw new RpcException(-301, "The size of the free transaction must be less than 102400 bytes");

                json["tx"] = tx?.ToArray().ToHexString();
            }
            return json;
        }

        public JObject SignAndShowResult(Transaction tx, bool isHexString = false)
        {
            if (tx == null)
                throw new RpcException(-300, "Insufficient funds");
            ContractParametersContext context = new ContractParametersContext(tx);
            Wallet.Sign(context);
            if (context.Completed)
            {
                tx.Witness = context.GetWitness();

                if (tx.Size > Transaction.MaxTransactionSize)
                    throw new RpcException(-301, "The size of the free transaction must be less than 102400 bytes");

                //Wallet.ApplyTransaction(tx);
                system.LocalNode.Tell(new LocalNode.Relay { Inventory = tx });

                if (isHexString)
                    return Bhp.IO.Helper.ToArray(tx).ToHexString();
                return tx.ToJson();
            }
            else
            {
                return context.ToJson();
            }
        }

        private JObject SendRawTransaction(JArray _params)
        {
            Transaction tx = Transaction.DeserializeFrom(_params[0].AsString().HexToBytes());
            RelayResultReason reason = system.Blockchain.Ask<RelayResultReason>(tx).Result;
            return GetRelayResult(reason);
        }

        private JObject SubmitBlock(JArray _params)
        {
            Block block = _params[0].AsString().HexToBytes().AsSerializable<Block>();
            RelayResultReason reason = system.Blockchain.Ask<RelayResultReason>(block).Result;
            return GetRelayResult(reason);
        }

        private JObject ValidateAddress(JArray _params)
        {
            JObject json = new JObject();
            UInt160 scriptHash;
            try
            {
                scriptHash = _params[0].AsString().ToScriptHash();
            }
            catch
            {
                scriptHash = null;
            }
            json["address"] = _params[0];
            json["isvalid"] = scriptHash != null;
            return json;
        }

        private JObject ListPlugins()
        {
            return new JArray(Plugin.Plugins
                .OrderBy(u => u.Name)
                .Select(u => new JObject
                {
                    ["name"] = u.Name,
                    ["version"] = u.Version.ToString(),
                    ["interfaces"] = new JArray(u.GetType().GetInterfaces()
                        .Select(p => p.Name)
                        .Where(p => p.EndsWith("Plugin"))
                        .Select(p => (JObject)p))
                }));
        }

        private static JObject GetRelayResult(RelayResultReason reason)
        {
            switch (reason)
            {
                case RelayResultReason.Succeed:
                    return true;
                case RelayResultReason.AlreadyExists:
                    throw new RpcException(-501, "Block or transaction already exists and cannot be sent repeatedly.");
                case RelayResultReason.OutOfMemory:
                    throw new RpcException(-502, "The memory pool is full and no more transactions can be sent.");
                case RelayResultReason.UnableToVerify:
                    throw new RpcException(-503, "The block cannot be validated.");
                case RelayResultReason.Invalid:
                    throw new RpcException(-504, "Block or transaction validation failed.");
                case RelayResultReason.PolicyFail:
                    throw new RpcException(-505, "One of the Policy filters failed.");
                default:
                    throw new RpcException(-500, "Unknown error.");
            }
        }

        private async Task ProcessAsync(HttpContext context)
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
            context.Response.Headers["Access-Control-Max-Age"] = "31536000";
            if (context.Request.Method != "GET" && context.Request.Method != "POST") return;
            JObject request = null;
            if (context.Request.Method == "GET")
            {
                string jsonrpc = context.Request.Query["jsonrpc"];
                string id = context.Request.Query["id"];
                string method = context.Request.Query["method"];
                string _params = context.Request.Query["params"];
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(method) && !string.IsNullOrEmpty(_params))
                {
                    try
                    {
                        _params = Encoding.UTF8.GetString(Convert.FromBase64String(_params));
                    }
                    catch (FormatException) { }
                    request = new JObject();
                    if (!string.IsNullOrEmpty(jsonrpc))
                        request["jsonrpc"] = jsonrpc;
                    request["id"] = id;
                    request["method"] = method;
                    request["params"] = JObject.Parse(_params);
                }
            }
            else if (context.Request.Method == "POST")
            {
                using (StreamReader reader = new StreamReader(context.Request.Body))
                {
                    try
                    {
                        request = JObject.Parse(reader);
                    }
                    catch (FormatException) { }
                }
            }
            JObject response;
            if (request == null)
            {
                response = CreateErrorResponse(null, -32700, "Parse error");
            }
            else if (request is JArray array)
            {
                if (array.Count == 0)
                {
                    response = CreateErrorResponse(request["id"], -32600, "Invalid Request");
                }
                else
                {
                    response = array.Select(p => ProcessRequest(context, p)).Where(p => p != null).ToArray();
                }
            }
            else
            {
                response = ProcessRequest(context, request);
            }
            if (response == null || (response as JArray)?.Count == 0) return;
            context.Response.ContentType = "application/json-rpc";
            await context.Response.WriteAsync(response.ToString(), Encoding.UTF8);
        }

        private JObject ProcessRequest(HttpContext context, JObject request)
        {
            if (!request.ContainsProperty("id")) return null;
            if (!request.ContainsProperty("method") || !request.ContainsProperty("params") || !(request["params"] is JArray))
            {
                return CreateErrorResponse(request["id"], -32600, "Invalid Request");
            }
            JObject result = null;
            try
            {
                string method = request["method"].AsString();
                JArray _params = (JArray)request["params"];
                foreach (IRpcPlugin plugin in Plugin.RpcPlugins)
                    plugin.PreProcess(context, method, _params);
                foreach (IRpcPlugin plugin in Plugin.RpcPlugins)
                {
                    result = plugin.OnProcess(context, method, _params);
                    if (result != null) break;
                }
                if (result == null)
                    result = Process(method, _params);
            }
            catch (FormatException)
            {
                return CreateErrorResponse(request["id"], -32602, "Invalid params");
            }
            catch (IndexOutOfRangeException)
            {
                return CreateErrorResponse(request["id"], -32602, "Invalid params");
            }
            catch (Exception ex)
            {
#if DEBUG
                return CreateErrorResponse(request["id"], ex.HResult, ex.Message, ex.StackTrace);
#else
                return CreateErrorResponse(request["id"], ex.HResult, ex.Message);
#endif
            }
            JObject response = CreateResponse(request["id"]);
            response["result"] = result;
            return response;
        }

        public void Start(IPAddress bindAddress, int port, string sslCert = null, string password = null, string[] trustedAuthorities = null)
        {
            host = new WebHostBuilder().UseKestrel(options => options.Listen(bindAddress, port, listenOptions =>
            {
                if (string.IsNullOrEmpty(sslCert)) return;
                listenOptions.UseHttps(sslCert, password, httpsConnectionAdapterOptions =>
                {
                    if (trustedAuthorities is null || trustedAuthorities.Length == 0)
                        return;
                    httpsConnectionAdapterOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    httpsConnectionAdapterOptions.ClientCertificateValidation = (cert, chain, err) =>
                    {
                        if (err != SslPolicyErrors.None)
                            return false;
                        X509Certificate2 authority = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
                        return trustedAuthorities.Contains(authority.Thumbprint);
                    };
                });
            }))
            .Configure(app =>
            {
                app.UseResponseCompression();
                app.Run(ProcessAsync);
            })
            .ConfigureServices(services =>
            {
                services.AddResponseCompression(options =>
                {
                    // options.EnableForHttps = false;
                    options.Providers.Add<GzipCompressionProvider>();
                    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "application/json-rpc" });
                });

                services.Configure<GzipCompressionProviderOptions>(options =>
                {
                    options.Level = CompressionLevel.Fastest;
                });
            })
            .Build();

            host.Start();
        }
    }
}
