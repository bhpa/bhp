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
using Bhp.Wallets.BRC6;
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

namespace Bhp.Network.RPC
{
    public sealed class RpcServer : IDisposable
    {
        public Wallet Wallet;

        private IWebHost host;
        private Fixed8 maxGasInvoke;
        private readonly BhpSystem system;
        private RpcExtension rpcExtension;

        public RpcServer(BhpSystem system, Wallet wallet = null, Fixed8 maxGasInvoke = default(Fixed8))
        {
            this.system = system;
            this.Wallet = wallet;
            this.maxGasInvoke = maxGasInvoke;

            rpcExtension = new RpcExtension(system, wallet, this);
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

        private JObject GetInvokeResult(byte[] script, IVerifiable checkWitnessHashes = null)
        {
            ApplicationEngine engine = ApplicationEngine.Run(script, checkWitnessHashes, extraGAS: maxGasInvoke);
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
            return json;
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

        public void SetWallet(Wallet wallet)
        {
            this.Wallet = wallet;
        }

        public void OpenWallet(Wallet wallet)
        {
            this.Wallet = wallet;
            rpcExtension.SetWallet(wallet);
        }

        public void CloseWallet()
        {
            this.Wallet = null;
            rpcExtension.SetWallet(null);
        }

        private JObject Process(string method, JArray _params)
        {
            switch (method)
            {
                case "dumpprivkey": return DumpPrivKey(_params);
                case "getaccountstate": return GetAccountsState(_params);
                case "getassetstate": return GetAssetState(_params);
                case "getbalance": return GetBalance(_params);
                case "getbestblockhash":
                    return Blockchain.Singleton.CurrentBlockHash.ToString();
                case "getblock": return GetBlock(_params);
                case "getblockcount":
                    return Blockchain.Singleton.Height + 1;
                case "getblockhash": return GetBlockHash(_params);
                case "getblockheader": return GetBlockHeader(_params);
                case "getblocksysfee": return GetBlockSysFee(_params);
                case "getconnectioncount":
                    return LocalNode.Singleton.ConnectedCount;
                case "getcontractstate": return GetContractState(_params);
                case "getnewaddress": return GetNewAddress();
                case "getpeers": return GetPeers();
                case "getrawmempool": return GetRawMempool(_params);
                case "getrawtransaction": return GetRawTransaction(_params);
                case "getstorage": return GetStorage(_params);
                case "gettransactionheight": return GetTransactionHeight(_params);
                case "gettxout": return GetTxOut(_params);
                case "getvalidators": return GetValidators();
                case "getversion": return GetVersion();
                case "getwalletheight": return GetWalletHeight();
                case "invoke": return Invoke(_params);
                case "invokefunction": return InvokeFunction(_params);
                case "invokescript": return InvokeScript(_params);
                case "listaddress": return ListAddress();
                case "sendfrom": return SendFrom(_params);
                case "sendmany": return SendMany(_params);
                case "sendrawtransaction": return SendRawTransaction(_params);
                case "sendtoaddress": return SendToAddress(_params);
                case "submitblock": return SubmitBlock(_params);
                case "validateaddress": return ValidateAddress(_params);
                default:
                    return rpcExtension.Process(method, _params);
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
                {
                    result = plugin.OnProcess(context, method, _params);
                    if (result != null) break;
                }
                if (result == null)
                    result = Process(method, _params);
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

        /// <summary>
        /// 导出私钥
        /// </summary>
        /// <param name="_params[0]">地址</param>
        /// <returns>该地址的WIF</returns>
        private JObject DumpPrivKey(JArray _params)
        {
            if (Wallet == null || rpcExtension.walletTimeLock.IsLocked())
                throw new RpcException(-400, "Access denied");
            else
            {
                UInt160 scriptHash = _params[0].AsString().ToScriptHash();
                WalletAccount account = Wallet.GetAccount(scriptHash);
                return account.GetKey().Export();
            }
        }

        /// <summary>
        /// 查询账户全局资产（如 BHP、GAS 等）资产信息
        /// </summary>
        /// <param name="_params[0]">地址</param>
        /// <returns>指定账户的全局资产信息</returns>
        private static JObject GetAccountsState(JArray _params)
        {
            UInt160 script_hash = _params[0].AsString().ToScriptHash();
            AccountState account = Blockchain.Singleton.Store.GetAccounts().TryGet(script_hash) ?? new AccountState(script_hash);
            return account.ToJson();
        }

        /// <summary>
        /// 查询资产信息
        /// </summary>
        /// <param name="_params[0]">资产ID</param>
        /// <returns>指定资产信息</returns>
        private static JObject GetAssetState(JArray _params)
        {
            UInt256 asset_id = UInt256.Parse(_params[0].AsString());
            AssetState asset = Blockchain.Singleton.Store.GetAssets().TryGet(asset_id);
            return asset?.ToJson() ?? throw new RpcException(-100, "Unknown asset");
        }

        /// <summary>
        /// 查询钱包中指定资产的余额信息
        /// </summary>
        /// <param name="_params[0]">全局资产ID 或 符合BRC20标准的合约资产HASH</param>
        /// <returns>钱包中指定资产的余额信息</returns>
        private JObject GetBalance(JArray _params)
        {
            if (Wallet == null || rpcExtension.walletTimeLock.IsLocked())
                throw new RpcException(-400, "Access denied.");
            else
            {
                JObject json = new JObject();
                switch (UIntBase.Parse(_params[0].AsString()))
                {
                    case UInt160 asset_id_160: //BRC-5 balance
                        json["balance"] = Wallet.GetAvailable(asset_id_160).ToString();
                        break;
                    case UInt256 asset_id_256: //Global Assets balance
                        IEnumerable<Coin> coins = Wallet.GetCoins().Where(p => !p.State.HasFlag(CoinState.Spent) && p.Output.AssetId.Equals(asset_id_256));
                        json["balance"] = coins.Sum(p => p.Output.Value).ToString();
                        json["confirmed"] = coins.Where(p => p.State.HasFlag(CoinState.Confirmed)).Sum(p => p.Output.Value).ToString();
                        break;
                }
                return json;
            }
        }

        /// <summary>
        /// 根据指定的散列值或区块索引，返回对应的区块信息。
        /// </summary>
        /// <param name="_params[0]">区块散列值 或 区块索引</param>
        /// <param name="_params[1]">可选参数，verbose 默认值为 0，(0：返回区块的序列化信息；1：返回Json格式的区块信息)</param>
        /// <returns>区块信息</returns>
        private static JObject GetBlock(JArray _params)
        {
            Block block;
            if (_params[0] is JNumber)
            {
                uint index = (uint)_params[0].AsNumber();
                block = Blockchain.Singleton.Store.GetBlock(index);
            }
            else
            {
                UInt256 hash = UInt256.Parse(_params[0].AsString());
                block = Blockchain.Singleton.Store.GetBlock(hash);
            }
            if (block == null)
                throw new RpcException(-100, "Unknown block");
            bool verbose = _params.Count >= 2 && _params[1].AsBooleanOrDefault(false);
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

        /// <summary>
        /// 根据指定的索引，返回对应区块的散列值。
        /// </summary>
        /// <param name="_params[0]">区块索引</param>
        /// <returns>区块散列</returns>
        private static JObject GetBlockHash(JArray _params)
        {
            uint height = (uint)_params[0].AsNumber();
            if (height <= Blockchain.Singleton.Height)
            {
                return Blockchain.Singleton.GetBlockHash(height).ToString();
            }
            throw new RpcException(-100, "Invalid Height");
        }

        /// <summary>
        /// 根据指定的散列值，返回对应的区块头信息。
        /// </summary>
        /// <param name="_params[0]">区块散列值</param>
        /// <param name="_params[1]">可选参数，verbose 默认值为 0，(0：返回区块头的序列化信息；1：返回Json格式的区块头信息)</param>
        /// <returns>区块头信息</returns>
        private static JObject GetBlockHeader(JArray _params)
        {
            Header header;
            if (_params[0] is JNumber)
            {
                uint height = (uint)_params[0].AsNumber();
                header = Blockchain.Singleton.Store.GetHeader(height);
            }
            else
            {
                UInt256 hash = UInt256.Parse(_params[0].AsString());
                header = Blockchain.Singleton.Store.GetHeader(hash);
            }
            if (header == null)
                throw new RpcException(-100, "Unknown block");

            bool verbose = _params.Count >= 2 && _params[1].AsBooleanOrDefault(false);
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

        /// <summary>
        /// 根据指定的索引，返回截止到该区块前的系统手续费
        /// </summary>
        /// <param name="_params[0]">区块索引</param>
        /// <returns>截止到该区块前的系统手续费</returns>
        private static JObject GetBlockSysFee(JArray _params)
        {
            uint height = (uint)_params[0].AsNumber();
            if (height <= Blockchain.Singleton.Height)
            {
                return Blockchain.Singleton.Store.GetSysFeeAmount(height).ToString();
            }
            throw new RpcException(-100, "Invalid Height");
        }

        /// <summary>
        /// 查询合约信息
        /// </summary>
        /// <param name="_params[0]">合约脚本散列</param>
        /// <returns>合约信息</returns>
        private static JObject GetContractState(JArray _params)
        {
            UInt160 script_hash = UInt160.Parse(_params[0].AsString());
            ContractState contract = Blockchain.Singleton.Store.GetContracts().TryGet(script_hash);
            return contract?.ToJson() ?? throw new RpcException(-100, "Unknown contract");
        }

        /// <summary>
        /// 创建一个新的地址
        /// </summary>
        /// <returns>新地址</returns>
        private JObject GetNewAddress()
        {
            if (Wallet == null || rpcExtension.walletTimeLock.IsLocked())
                throw new RpcException(-400, "Access denied");
            else
            {
                WalletAccount account = Wallet.CreateAccount();
                if (Wallet is BRC6Wallet brc6)
                    brc6.Save();
                return account.Address;
            }
        }

        /// <summary>
        /// 获得该节点当前已连接/未连接的节点列表
        /// </summary>
        /// <returns>该节点当前已连接/未连接的节点列表</returns>
        private static JObject GetPeers()
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
                peerJson["port"] = p.ListenerPort;
                return peerJson;
            }));
            return json;
        }

        /// <summary>
        /// 获取内存中未确认的交易列表
        /// </summary>
        /// <param name="_params"></param>
        /// <returns>内存中未确认的交易列表</returns>
        private static JObject GetRawMempool(JArray _params)
        {
            bool shouldGetUnverified = _params.Count >= 1 && _params[0].AsBooleanOrDefault(false);
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

        /// <summary>
        /// 查询指定交易信息
        /// </summary>
        /// <param name="_params[0]">交易ID</param>
        /// <param name="_params[1]">可选参数，verbose 默认值为 0，(0：返回区块头的序列化信息；1：返回Json格式的区块头信息)</param>
        /// <returns>交易信息</returns>
        private static JObject GetRawTransaction(JArray _params)
        {
            UInt256 hash = UInt256.Parse(_params[0].AsString());
            bool verbose = _params.Count >= 2 && _params[1].AsBooleanOrDefault(false);
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

        /// <summary>
        /// 查询合约存储区的值
        /// </summary>
        /// <param name="_params[0]">合约脚本散列</param>
        /// <param name="_params[1]">存储区的键（需要转化为hex string）</param>
        /// <returns>存储的值</returns>
        private static JObject GetStorage(JArray _params)
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

        /// <summary>
        /// 获取交易高度
        /// </summary>
        /// <param name="_params">交易ID</param>
        /// <returns>交易高度</returns>
        private static JObject GetTransactionHeight(JArray _params)
        {
            UInt256 hash = UInt256.Parse(_params[0].AsString());
            uint? height = Blockchain.Singleton.Store.GetTransactions().TryGet(hash)?.BlockIndex;
            if (height.HasValue) return height.Value;
            throw new RpcException(-100, "Unknown transaction");
        }

        /// <summary>
        /// 查询unspent交易输出（零钱）信息。如果交易输出已经花费，返回结果为空
        /// </summary>
        /// <param name="_params[0]">交易ID</param>
        /// <param name="_params[1]">要获取的交易输出在该交易中的索引（从 0 开始）</param>
        /// <returns>unspent交易输出信息</returns>
        private static JObject GetTxOut(JArray _params)
        {
            UInt256 hash = UInt256.Parse(_params[0].AsString());
            ushort index = (ushort)_params[1].AsNumber();
            return Blockchain.Singleton.Store.GetUnspent(hash, index)?.ToJson(index);
        }

        /// <summary>
        /// 获取当前BHP共识节点的信息及投票情况
        /// </summary>
        /// <returns>当前BHP共识节点的信息及投票情况</returns>
        private static JObject GetValidators()
        {
            using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot())
            {
                var validators = snapshot.GetValidators();
                return snapshot.GetEnrollments().Select(p =>
                {
                    JObject validator = new JObject();
                    validator["publickey"] = p.PublicKey.ToString();
                    validator["votes"] = p.Votes.ToString();
                    validator["active"] = validators.Contains(p.PublicKey);
                    return validator;
                }).ToArray();
            }
        }

        /// <summary>
        /// 查询节点的版本信息
        /// </summary>
        /// <returns>节点的版本信息</returns>
        private static JObject GetVersion()
        {
            JObject json = new JObject();
            json["port"] = LocalNode.Singleton.ListenerPort;
            json["nonce"] = LocalNode.Nonce;
            json["useragent"] = LocalNode.UserAgent;
            return json;
        }

        /// <summary>
        /// 获取当前钱包索引高度
        /// </summary>
        /// <returns>当前钱包索引高度</returns>
        private JObject GetWalletHeight()
        {
            if (Wallet == null || rpcExtension.walletTimeLock.IsLocked())
                throw new RpcException(-400, "Access denied.");
            else
                return (Wallet.WalletHeight > 0) ? Wallet.WalletHeight - 1 : 0;
        }

        /// <summary>
        /// 使用给定的参数以散列值调用智能合约，并返回结果(不上链)
        /// </summary>
        /// <param name="_params[0]">智能合约脚本散列</param>
        /// <param name="_params[1]">智能合约的参数</param>
        /// <param name="_params[2]">可选，见证者的地址脚本</param>
        /// <returns>脚本执行结果，当打开钱包时，将返回交易脚本及交易ID</returns>
        private JObject Invoke(JArray _params)
        {
            UInt160 script_hash = UInt160.Parse(_params[0].AsString());
            ContractParameter[] parameters = ((JArray)_params[1]).Select(p => ContractParameter.FromJson(p)).ToArray();
            CheckWitnessHashes checkWitnessHashes = null;
            if (_params.Count > 2)
            {
                UInt160[] scriptHashesForVerifying = _params.Skip(2).Select(u => UInt160.Parse(u.AsString())).ToArray();
                checkWitnessHashes = new CheckWitnessHashes(scriptHashesForVerifying);
            }
            byte[] script;
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                script = sb.EmitAppCall(script_hash, parameters).ToArray();
            }
            return GetInvokeResult(script, checkWitnessHashes);
        }

        /// <summary>
        /// 使用给定的操作和参数，以散列值调用智能合约之后返回结果(不上链)
        /// </summary>
        /// <param name="_params[0]">智能合约脚本散列</param>
        /// <param name="_params[1]">操作名称（字符串）</param>
        /// <param name="_params[2]">智能合约的参数</param>
        /// <param name="_params[3]">可选，见证者的地址脚本</param>
        /// <returns>脚本执行结果，当打开钱包时，将返回交易脚本及交易ID</returns>
        private JObject InvokeFunction(JArray _params)
        {
            UInt160 script_hash = UInt160.Parse(_params[0].AsString());
            string operation = _params[1].AsString();
            ContractParameter[] args = _params.Count >= 3 ? ((JArray)_params[2]).Select(p => ContractParameter.FromJson(p)).ToArray() : new ContractParameter[0];
            CheckWitnessHashes checkWitnessHashes = null;
            if (_params.Count > 3)
            {
                UInt160[] scriptHashesForVerifying = _params.Skip(3).Select(u => UInt160.Parse(u.AsString())).ToArray();
                checkWitnessHashes = new CheckWitnessHashes(scriptHashesForVerifying);
            }
            byte[] script;
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                script = sb.EmitAppCall(script_hash, operation, args).ToArray();
            }
            return GetInvokeResult(script, checkWitnessHashes);
        }

        /// <summary>
        /// 通过虚拟机传递脚本之后返回结果(不上链)
        /// </summary>
        /// <param name="_params[0]">一个由虚拟机运行的脚本，与 InvocationTransaction 中携带的脚本相同</param>
        /// <param name="_params[1]">可选，见证者的地址脚本</param>
        /// <returns>脚本执行结果，当打开钱包时，将返回交易脚本及交易ID</returns>
        private JObject InvokeScript(JArray _params)
        {
            byte[] script = _params[0].AsString().HexToBytes();
            CheckWitnessHashes checkWitnessHashes = null;
            if (_params.Count > 1)
            {
                UInt160[] scriptHashesForVerifying = _params.Skip(1).Select(u => UInt160.Parse(u.AsString())).ToArray();
                checkWitnessHashes = new CheckWitnessHashes(scriptHashesForVerifying);
            }
            return GetInvokeResult(script, checkWitnessHashes);
        }

        /// <summary>
        /// 列出当前钱包内的所有地址
        /// </summary>
        /// <returns>当前钱包内的所有地址</returns>
        private JObject ListAddress()
        {
            if (Wallet == null || rpcExtension.walletTimeLock.IsLocked())
                throw new RpcException(-400, "Access denied.");
            else
                return Wallet.GetAccounts().Select(p =>
                {
                    JObject account = new JObject();
                    account["address"] = p.Address;
                    account["haskey"] = p.HasKey;
                    account["label"] = p.Label;
                    account["watchonly"] = p.WatchOnly;
                    return account;
                }).ToArray();
        }

        /// <summary>
        /// 从指定地址，向指定地址转账
        /// </summary>
        /// <param name="_params[0]">资产 ID</param>
        /// <param name="_params[1]">转账地址</param>
        /// <param name="_params[2]">收款地址</param>
        /// <param name="_params[3]">转账金额</param>
        /// <param name="_params[4]">gas手续费，可选参数，默认为 0</param>
        /// <param name="_params[5]">找零地址，可选参数，默认为钱包中第一个标准地址</param>
        /// <param name="_params[6]">备注，可选参数</param>
        /// <param name="_params[7]">bhp手续费地址，可选参数</param>
        /// <returns>交易</returns>
        private JObject SendFrom(JArray _params)
        {
            if (Wallet == null || rpcExtension.walletTimeLock.IsLocked())
                throw new RpcException(-400, "Access denied");
            else
            {
                UIntBase assetId = UIntBase.Parse(_params[0].AsString());
                AssetDescriptor descriptor = new AssetDescriptor(assetId);
                UInt160 from = _params[1].AsString().ToScriptHash();
                UInt160 to = _params[2].AsString().ToScriptHash();
                BigDecimal value = BigDecimal.Parse(_params[3].AsString(), descriptor.Decimals);
                if (value.Sign <= 0)
                    throw new RpcException(-32602, "Invalid params");
                Fixed8 fee = _params.Count >= 5 ? Fixed8.Parse(_params[4].AsString()) : Fixed8.Zero;
                if (fee < Fixed8.Zero)
                    throw new RpcException(-32602, "Invalid params");
                UInt160 change_address = _params.Count >= 6 ? _params[5].AsString().ToScriptHash() : null;
                string remark = _params.Count >= 7 ? _params[6].AsString() : string.Empty;
                UInt160 fee_address = _params.Count >= 8 ? _params[7].AsString().ToScriptHash() : null;
                if (fee_address != null && fee_address.Equals(from) && assetId.Equals(Blockchain.GoverningToken.Hash)) fee_address = null;
                List<TransactionAttribute> attributes = null;
                if (!string.IsNullOrEmpty(remark))
                {
                    attributes = new List<TransactionAttribute>();
                    using (ScriptBuilder sb = new ScriptBuilder())
                    {
                        sb.EmitPush(remark);
                        attributes.Add(new TransactionAttribute
                        {
                            Usage = TransactionAttributeUsage.Description,
                            Data = sb.ToArray()
                        });
                    }
                }
                Transaction tx = Wallet.MakeTransaction(attributes, new[]
                {
                    new TransferOutput
                    {
                        AssetId = assetId,
                        Value = value,
                        ScriptHash = to
                    }
                }, fee_address: fee_address, from: from, change_address: change_address, fee: fee);
                if (tx == null)
                    throw new RpcException(-300, "Insufficient funds");
                ContractParametersContext context = new ContractParametersContext(tx);
                Wallet.Sign(context);
                if (context.Completed)
                {
                    tx.Witnesses = context.GetWitnesses();

                    if (tx.Size > Transaction.MaxTransactionSize)
                        throw new RpcException(-301, "The size of the free transaction must be less than 102400 bytes");

                    Wallet.ApplyTransaction(tx);
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
        /// 批量转账命令，并且可以指定找零地址
        /// </summary>
        /// <param name="_params[0]">数组{"asset": \<资产ID>,"value": \<转账金额>,"address": \<收款地址>}</param>
        /// <param name="_params[1]">gas手续费，可选参数，默认为 0</param>
        /// <param name="_params[2]">找零地址，可选参数，默认为钱包中第一个标准地址</param>
        /// <param name="_params[3]">bhp手续费地址，可选参数。（转账资产包含BHP时，此参数无效）</param>        
        /// <returns>交易</returns>
        private JObject SendMany(JArray _params)
        {
            if (Wallet == null || rpcExtension.walletTimeLock.IsLocked())
                throw new RpcException(-400, "Access denied");
            else
            {
                JArray to = (JArray)_params[0];
                if (to.Count == 0)
                    throw new RpcException(-32602, "Invalid params");
                TransferOutput[] outputs = new TransferOutput[to.Count];
                bool hasBhp = false;
                for (int i = 0; i < to.Count; i++)
                {
                    UIntBase asset_id = UIntBase.Parse(to[i]["asset"].AsString());
                    if (!hasBhp && asset_id.Equals(Blockchain.GoverningToken.Hash)) hasBhp = true;
                    AssetDescriptor descriptor = new AssetDescriptor(asset_id);
                    outputs[i] = new TransferOutput
                    {
                        AssetId = asset_id,
                        Value = BigDecimal.Parse(to[i]["value"].AsString(), descriptor.Decimals),
                        ScriptHash = to[i]["address"].AsString().ToScriptHash()
                    };
                    if (outputs[i].Value.Sign <= 0)
                        throw new RpcException(-32602, "Invalid params");
                }
                Fixed8 fee = _params.Count >= 2 ? Fixed8.Parse(_params[1].AsString()) : Fixed8.Zero;
                if (fee < Fixed8.Zero)
                    throw new RpcException(-32602, "Invalid params");
                UInt160 change_address = _params.Count >= 3 ? _params[2].AsString().ToScriptHash() : null;
                UInt160 fee_address = _params.Count >= 4 ? _params[3].AsString().ToScriptHash() : null;
                if (hasBhp) fee_address = null;
                Transaction tx = Wallet.MakeTransaction(null, outputs, fee_address: fee_address, change_address: change_address, fee: fee);
                if (tx == null)
                    throw new RpcException(-300, "Insufficient funds");
                ContractParametersContext context = new ContractParametersContext(tx);
                Wallet.Sign(context);
                if (context.Completed)
                {
                    tx.Witnesses = context.GetWitnesses();

                    if (tx.Size > Transaction.MaxTransactionSize)
                        throw new RpcException(-301, "The size of the free transaction must be less than 102400 bytes");

                    Wallet.ApplyTransaction(tx);
                    system.LocalNode.Tell(new LocalNode.Relay { Inventory = tx });
                    //Console.WriteLine(tx.ToArray().ToHexString());
                    return tx.ToJson();
                }
                else
                {
                    return context.ToJson();
                }
            }
        }

        /// <summary>
        /// 广播交易
        /// </summary>
        /// <param name="_params[0]">在程序中构造的已签名的交易序列化后的 16 进制字符串</param>
        /// <returns>广播结果</returns>
        private JObject SendRawTransaction(JArray _params)
        {
            Transaction tx = Transaction.DeserializeFrom(_params[0].AsString().HexToBytes());
            RelayResultReason reason = system.Blockchain.Ask<RelayResultReason>(tx).Result;
            return GetRelayResult(reason);
        }

        /// <summary>
        /// 向指定地址转账
        /// </summary>
        /// <param name="_params[0]">资产 ID</param>
        /// <param name="_params[1]">收款地址</param>
        /// <param name="_params[2]">转账金额</param>
        /// <param name="_params[3]">手续费，可选参数，默认为 0</param>
        /// <param name="_params[4]">找零地址，可选参数，默认为钱包中第一个标准地址</param>
        /// <param name="_params[5]">bhp手续费地址，可选参数。（转账资产为BHP时，此参数无效）</param>
        /// <returns>交易</returns>
        private JObject SendToAddress(JArray _params)
        {
            if (Wallet == null || rpcExtension.walletTimeLock.IsLocked())
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
                Transaction tx = Wallet.MakeTransaction(null, new[]
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
                Wallet.Sign(context);
                if (context.Completed)
                {
                    tx.Witnesses = context.GetWitnesses();

                    if (tx.Size > Transaction.MaxTransactionSize)
                        throw new RpcException(-301, "The size of the free transaction must be less than 102400 bytes");

                    Wallet.ApplyTransaction(tx);
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
        /// 在BHP网络广播原始区块
        /// </summary>
        /// <param name="_params[0]">序列化区块的十六进制字符串</param>
        /// <returns>广播结果</returns>
        private JObject SubmitBlock(JArray _params)
        {
            Block block = _params[0].AsString().HexToBytes().AsSerializable<Block>();
            RelayResultReason reason = system.Blockchain.Ask<RelayResultReason>(block).Result;
            return GetRelayResult(reason);
        }

        /// <summary>
        /// 验证地址是否是正确的BHP地址
        /// </summary>
        /// <param name="_params[0]">地址</param>
        /// <returns>验证结果</returns>
        private static JObject ValidateAddress(JArray _params)
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

        public class CheckWitnessHashes : IVerifiable
        {
            private readonly UInt160[] _scriptHashesForVerifying;
            public Witness[] Witnesses { get; set; }
            public int Size { get; }

            public CheckWitnessHashes(UInt160[] scriptHashesForVerifying)
            {
                _scriptHashesForVerifying = scriptHashesForVerifying;
            }

            public void Serialize(BinaryWriter writer)
            {
                throw new NotImplementedException();
            }

            public void Deserialize(BinaryReader reader)
            {
                throw new NotImplementedException();
            }

            public void DeserializeUnsigned(BinaryReader reader)
            {
                throw new NotImplementedException();
            }

            public UInt160[] GetScriptHashesForVerifying(Snapshot snapshot)
            {
                return _scriptHashesForVerifying;
            }

            public void SerializeUnsigned(BinaryWriter writer)
            {
                throw new NotImplementedException();
            }

            public byte[] GetMessage()
            {
                throw new NotImplementedException();
            }
        }
    }
}