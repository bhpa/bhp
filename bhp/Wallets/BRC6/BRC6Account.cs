using Bhp.IO.Json;
using System;

namespace Bhp.Wallets.BRC6
{
    internal class BRC6Account : WalletAccount
    {
        private readonly BRC6Wallet wallet;
        private readonly string brc2key;
        private KeyPair key;
        public JObject Extra;

        public bool Decrypted => brc2key == null || key != null;
        public override bool HasKey => brc2key != null;

        public BRC6Account(BRC6Wallet wallet, UInt160 scriptHash, string brc2key = null)
            : base(scriptHash)
        {
            this.wallet = wallet;
            this.brc2key = brc2key;
        }

        public BRC6Account(BRC6Wallet wallet, UInt160 scriptHash, KeyPair key, string password)
            : this(wallet, scriptHash, key.Export(password, wallet.Scrypt.N, wallet.Scrypt.R, wallet.Scrypt.P))
        {
            this.key = key;
        }

        public static BRC6Account FromJson(JObject json, BRC6Wallet wallet)
        {
            return new BRC6Account(wallet, json["address"].AsString().ToScriptHash(), json["key"]?.AsString())
            {
                Label = json["label"]?.AsString(),
                IsDefault = json["isDefault"].AsBoolean(),
                Lock = json["lock"].AsBoolean(),
                Contract = BRC6Contract.FromJson(json["contract"]),
                Extra = json["extra"]
            };
        }

        public override KeyPair GetKey()
        {
            if (brc2key == null) return null;
            if (key == null)
            {
                key = wallet.DecryptKey(brc2key);
            }
            return key;
        }

        public KeyPair GetKey(string password)
        {
            if (brc2key == null) return null;
            if (key == null)
            {
                key = new KeyPair(Wallet.GetPrivateKeyFromBRC2(brc2key, password, wallet.Scrypt.N, wallet.Scrypt.R, wallet.Scrypt.P));
            }
            return key;
        }

        public JObject ToJson()
        {
            JObject account = new JObject();
            account["address"] = ScriptHash.ToAddress();
            account["label"] = Label;
            account["isDefault"] = IsDefault;
            account["lock"] = Lock;
            account["key"] = brc2key;
            account["contract"] = ((BRC6Contract)Contract)?.ToJson();
            account["extra"] = Extra;
            return account;
        }

        public bool VerifyPassword(string password)
        {
            try
            {
                Wallet.GetPrivateKeyFromBRC2(brc2key, password, wallet.Scrypt.N, wallet.Scrypt.R, wallet.Scrypt.P);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
