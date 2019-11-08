using Bhp.IO.Json;
using System.Linq;
using System.Numerics;

namespace Bhp.Network.RPC.Models
{
    public class RpcBrc5Balances
    {
        public string Address { get; set; }

        public RpcBrc6Balance[] Balances { get; set; }

        public JObject ToJson()
        {
            JObject json = new JObject();
            json["address"] = Address;
            json["balance"] = Balances.Select(p => p.ToJson()).ToArray();
            return json;
        }

        public static RpcBrc5Balances FromJson(JObject json)
        {
            RpcBrc5Balances brc6Balance = new RpcBrc5Balances();
            brc6Balance.Address = json["address"].AsString();
            //List<Balance> listBalance = new List<Balance>();
            brc6Balance.Balances = ((JArray)json["balance"]).Select(p => RpcBrc6Balance.FromJson(p)).ToArray();
            return brc6Balance;
        }
    }

    public class RpcBrc6Balance
    {
        public UInt160 AssetHash { get; set; }

        public BigInteger Amount { get; set; }

        public uint LastUpdatedBlock { get; set; }

        public JObject ToJson()
        {
            JObject json = new JObject();
            json["asset_hash"] = AssetHash.ToString();
            json["amount"] = Amount.ToString();
            json["last_updated_block"] = LastUpdatedBlock.ToString();
            return json;
        }

        public static RpcBrc6Balance FromJson(JObject json)
        {
            RpcBrc6Balance balance = new RpcBrc6Balance();
            balance.AssetHash = UInt160.Parse(json["asset_hash"].AsString());
            balance.Amount = BigInteger.Parse(json["amount"].AsString());
            balance.LastUpdatedBlock = uint.Parse(json["last_updated_block"].AsString());
            return balance;
        }
    }
}
