using Bhp.Wallets;


namespace Bhp.BhpExtensions
{
    public class ExtensionSettings
    {
        public WalletConfigSettings WalletConfig { get; set; }
        public DataRPCServerSettings DataRPCServer { get; set; }

        public static ExtensionSettings Default { get; }

        static ExtensionSettings()
        {
            Default = new ExtensionSettings();
        }

        public ExtensionSettings()
        {
            WalletConfig = new WalletConfigSettings();
            DataRPCServer = new DataRPCServerSettings();
        }

        public class WalletConfigSettings
        {
            public WalletIndexer Indexer { get; set; }
            public string Path { get; set; }
            public string Index { get; set; }
            public bool AutoLock { get; set; }  

            public void Set(WalletIndexer Indexer, string Path, string Index, bool AutoLock)
            {
                this.Indexer = Indexer;
                this.Path = Path;
                this.Index = Index;
                this.AutoLock = AutoLock;
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
    }
}
