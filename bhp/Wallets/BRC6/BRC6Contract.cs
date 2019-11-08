using Bhp.IO.Json;
using Bhp.SmartContract;
using System;
using System.Linq;

namespace Bhp.Wallets.BRC6
{
    internal class BRC6Contract : Contract
    {
        public string[] ParameterNames;
        public bool Deployed;

        public static BRC6Contract FromJson(JObject json)
        {
            if (json == null) return null;
            return new BRC6Contract
            {
                Script = Convert.FromBase64String(json["script"].AsString()),
                ParameterList = ((JArray)json["parameters"]).Select(p => p["type"].TryGetEnum<ContractParameterType>()).ToArray(),
                ParameterNames = ((JArray)json["parameters"]).Select(p => p["name"].AsString()).ToArray(),
                Deployed = json["deployed"].AsBoolean()
            };
        }

        public JObject ToJson()
        {
            JObject contract = new JObject();
            contract["script"] = Convert.ToBase64String(Script);
            contract["parameters"] = new JArray(ParameterList.Zip(ParameterNames, (type, name) =>
            {
                JObject parameter = new JObject();
                parameter["name"] = name;
                parameter["type"] = type;
                return parameter;
            }));
            contract["deployed"] = Deployed;
            return contract;
        }
    }
}
