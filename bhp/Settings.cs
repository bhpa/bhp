using Microsoft.Extensions.Configuration;
using Bhp.Network.P2P.Payloads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Bhp
{
    public class ProtocolSettings
    {
        public uint Magic { get; }
        public byte AddressVersion { get; }
        public string[] StandbyValidators { get; }
        public string[] SeedList { get; }
        public IReadOnlyDictionary<TransactionType, Fixed8> SystemFee { get; }
        public uint SecondsPerBlock { get; }

        static ProtocolSettings _default;

        static bool UpdateDefault(IConfiguration configuration)
        {
            var settings = new ProtocolSettings(configuration.GetSection("ProtocolConfiguration"));
            return null == Interlocked.CompareExchange(ref _default, settings, null);
        }

        public static bool Initialize(IConfiguration configuration)
        {
            return UpdateDefault(configuration);
        }

        public static ProtocolSettings Default
        {
            get
            {
                if (_default == null)
                {
                    var configuration = new ConfigurationBuilder().AddJsonFile("protocol.json", true).Build();
                    UpdateDefault(configuration);
                }

                return _default;
            }
        }

        private ProtocolSettings(IConfigurationSection section)
        {
            this.Magic = section.GetValue("Magic", 0x38263E2u);
            this.AddressVersion = section.GetValue("AddressVersion", (byte)0x17);
            IConfigurationSection section_sv = section.GetSection("StandbyValidators");
            if (section_sv.Exists())
                this.StandbyValidators = section_sv.GetChildren().Select(p => p.Get<string>()).ToArray();
            else
                this.StandbyValidators = new[]
                {
                    "03e2a25adfc636cfbaa693539bf41e72917466ba2f95ed472a21cbf0c1b138ae96",
                    "023c2c27d875fa92be23a7dd4035c199320a5ea2129d966fec5fdb5ea434123be9",
                    "023beac0024fefda918bc40e5ec132ba4c24f4832556ccd63548c5b16f80820034",
                    "02137d620054a82950e204453787929e6fa88623084eec26ef3cf37ac53376bf33"
                };
            IConfigurationSection section_sl = section.GetSection("SeedList");
            if (section_sl.Exists())
                this.SeedList = section_sl.GetChildren().Select(p => p.Get<string>()).ToArray();
            else
                this.SeedList = new[]
                {
                    "seed01.bhpa.io:20555",
                    "seed02.bhpa.io:20555",
                    "seed03.bhpa.io:20555",
                    "seed04.bhpa.io:20555",
                    "seed05.bhpa.io:20555",
                    "seed06.bhpa.io:20555",
                    "seed07.bhpa.io:20555",
                    "seed08.bhpa.io:20555"
                };
            Dictionary<TransactionType, Fixed8> sys_fee = new Dictionary<TransactionType, Fixed8>
            {
                [TransactionType.RegisterTransaction] = Fixed8.FromDecimal(10000)
            };
            foreach (IConfigurationSection child in section.GetSection("SystemFee").GetChildren())
            {
                TransactionType key = (TransactionType)Enum.Parse(typeof(TransactionType), child.Key, true);
                sys_fee[key] = Fixed8.Parse(child.Value);
            }
            this.SystemFee = sys_fee;
            this.SecondsPerBlock = section.GetValue("SecondsPerBlock", 15u);
        }
    }
}
