using Bhp.Wallets;


namespace Bhp.BhpExtensions
{
    public class ExtensionSettings
    {
        public WalletConfigSettings WalletConfig { get; set; }
        public DataRPCServerSettings DataRPCServer { get; set; }
        public CertificateSettings Certificate { get; set; }

        public static ExtensionSettings Default { get; }

        static ExtensionSettings()
        {
            Default = new ExtensionSettings();
        }

        public ExtensionSettings()
        {
            WalletConfig = new WalletConfigSettings();
            DataRPCServer = new DataRPCServerSettings();
            Certificate = new CertificateSettings();
        }

        public class WalletConfigSettings
        {
            public WalletIndexer Indexer { get; set; }
            public string Path { get; set; }
            public string Index { get; set; }
            public bool AutoLock { get; set; }
            public bool IsBhpFee { get; set; }

            public void Set(WalletIndexer Indexer, string Path, string Index, bool AutoLock, bool IsBhpFee)
            {
                this.Indexer = Indexer;
                this.Path = Path;
                this.Index = Index;
                this.AutoLock = AutoLock;
                this.IsBhpFee = IsBhpFee;
            }
        }

        public class DataRPCServerSettings
        {
            public string Host { get; set; }
            public int Port { get; set; }

            public void Set(string Host, int Port)
            {
                this.Host = Host;
                this.Port = Port;
            }
        }

        public class CertificateSettings
        {
            public string Name { get; set; }

            public void Set(string Name)
            {
                this.Name = Name;
            }
        }
    }
}
