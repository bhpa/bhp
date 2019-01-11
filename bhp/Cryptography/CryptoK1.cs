using Bhp.VM;
using System;
using System.Linq;
using System.Numerics;

namespace Bhp.Cryptography
{
    public class CryptoK1 : ICrypto
    {
        public static readonly ICrypto Default = new CryptoK1();         

        public byte[] Hash160(byte[] message)
        {
            return message.Sha256().RIPEMD160();
        }

        public byte[] Hash256(byte[] message)
        {
            return message.Sha256().Sha256();
        }

        public byte[] Sign(byte[] message, byte[] privateKey, byte[] publicKey)
        {
            using (ECC.ECDsa ecdsa = new ECC.ECDsa(privateKey, ECC.ECCurve.Secp256))
            {
                byte[] hash = Hash256(message);
                BigInteger[] signature = ecdsa.GenerateSignature(hash);

                byte[] signatureBytes = new byte[64];

                byte[] rb = signature[0].ToByteArrayUnsigned(true);
                byte[] sb = signature[1].ToByteArrayUnsigned(true);

                Buffer.BlockCopy(rb, 0, signatureBytes, 0 + (32 - rb.Length), rb.Length);
                Buffer.BlockCopy(sb, 0, signatureBytes, 32 + (32 - sb.Length), sb.Length);
                return signatureBytes;
            }
        }

        public bool VerifySignature(byte[] message, byte[] signature, byte[] pubkey)
        {
            using (ECC.ECDsa ecdsa = new ECC.ECDsa(ECC.ECPoint.DecodePoint(pubkey, ECC.ECCurve.Secp256)))
            {
                byte[] rb = new byte[32];
                byte[] sb = new byte[32];
                Buffer.BlockCopy(signature, 0, rb, 0, 32);
                Buffer.BlockCopy(signature, 32, sb, 0, 32);

                byte[] hash = Hash256(message);
                BigInteger r = rb.ToBigIntegerUnsigned(true);
                BigInteger s = sb.ToBigIntegerUnsigned(true);

                return ecdsa.VerifySignature(hash, r, s);
            }
        }

    }
}
