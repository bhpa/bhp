using Bhp.VM;
using System;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;

namespace Bhp.Cryptography
{
    public class CryptoK1MS : ICrypto
    {
        public static readonly ICrypto Default = new CryptoK1MS();         

        public byte[] Hash160(byte[] message)
        {
            return message.Sha256().RIPEMD160();
        }

        public byte[] Hash256(byte[] message)
        {
            return message.Sha256().Sha256();
        }


        private ECCurve CreateECCurve()
        {
            ECCurve curve = new ECCurve();
            return curve;
        }

        public byte[] Sign(byte[] message, byte[] prikey, byte[] pubkey)
        {
            using (var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.CreateFromFriendlyName("secp256k1"),
                D = prikey,
                Q = new ECPoint
                {
                    X = pubkey.Take(32).ToArray(),
                    Y = pubkey.Skip(32).Take(32).ToArray()
                }
            }))
            {
                return ecdsa.SignData(message, HashAlgorithmName.SHA256);
            }
        }

        public bool VerifySignature(byte[] message, byte[] signature, byte[] pubkey)
        {
            if (pubkey.Length == 33 && (pubkey[0] == 0x02 || pubkey[0] == 0x03))
            {
                try
                {
                    pubkey = Cryptography.ECC.ECPoint.DecodePoint(pubkey, Cryptography.ECC.ECCurve.Secp256).EncodePoint(false).Skip(1).ToArray();
                }
                catch
                {
                    return false;
                }
            }
            else if (pubkey.Length == 65 && pubkey[0] == 0x04)
            {
                pubkey = pubkey.Skip(1).ToArray();
            }
            else if (pubkey.Length != 64)
            {
                throw new ArgumentException();
            }
            using (var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.CreateFromFriendlyName("secp256k1"),
                Q = new ECPoint
                {
                    X = pubkey.Take(32).ToArray(),
                    Y = pubkey.Skip(32).Take(32).ToArray()
                }
            }))
            {
                return ecdsa.VerifyData(message, signature, HashAlgorithmName.SHA256); 
            }
        }
    }
}
