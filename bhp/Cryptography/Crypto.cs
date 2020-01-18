using Bhp.VM;

namespace Bhp.Cryptography
{
    public class Crypto : ICrypto
    {
        public static readonly ICrypto Default = CryptoK1MS.Default;

        public byte[] Hash160(byte[] message)
        {
            return Default.Hash160(message);
        }

        public byte[] Hash256(byte[] message)
        {
            return Default.Hash256(message);
        }

        public bool VerifySignature(byte[] message, byte[] signature, byte[] pubkey)
        {
            return Default.VerifySignature(message, signature, pubkey);
        }

        public byte[] Sign(byte[] message, byte[] privateKey, byte[] publicKey)
        {
            return Default.Sign(message, privateKey, publicKey);
        }
    }
}
