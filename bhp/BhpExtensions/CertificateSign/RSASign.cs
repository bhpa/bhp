using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Bhp.BhpExtensions.CertificateSign
{
    public class RSASign
    {
        /// <summary>    
        /// pem private key to rsa private key
        /// </summary>    
        /// <param name="privateKey">pem private key</param>    
        /// <returns></returns>    
        private static string RSAPrivateKey(string privateKey)
        {
            RsaPrivateCrtKeyParameters privateKeyParam = (RsaPrivateCrtKeyParameters)PrivateKeyFactory.CreateKey(Convert.FromBase64String(privateKey));
            return string.Format("<RSAKeyValue><Modulus>{0}</Modulus><Exponent>{1}</Exponent><P>{2}</P><Q>{3}</Q><DP>{4}</DP><DQ>{5}</DQ><InverseQ>{6}</InverseQ><D>{7}</D></RSAKeyValue>",
            Convert.ToBase64String(privateKeyParam.Modulus.ToByteArrayUnsigned()),
            Convert.ToBase64String(privateKeyParam.PublicExponent.ToByteArrayUnsigned()),
            Convert.ToBase64String(privateKeyParam.P.ToByteArrayUnsigned()),
            Convert.ToBase64String(privateKeyParam.Q.ToByteArrayUnsigned()),
            Convert.ToBase64String(privateKeyParam.DP.ToByteArrayUnsigned()),
            Convert.ToBase64String(privateKeyParam.DQ.ToByteArrayUnsigned()),
            Convert.ToBase64String(privateKeyParam.QInv.ToByteArrayUnsigned()),
            Convert.ToBase64String(privateKeyParam.Exponent.ToByteArrayUnsigned()));
        }

        /// <summary>
        /// get private key from pem
        /// </summary>
        /// <param name="path">pem key path</param>
        /// <returns></returns>
        private static string GetPriKey(string path)
        {
            string str = File.ReadAllText(path);
            str = str.Replace("-----BEGIN PRIVATE KEY-----", string.Empty).Replace("-----END PRIVATE KEY-----", string.Empty).Replace("\n", string.Empty);
            string privateKey = RSAPrivateKey(str);
            return privateKey;
        }

        /// <summary>
        /// get public key
        /// </summary>
        /// <returns></returns>
        private static string GetPubKey(string path)
        {
            X509Certificate2 cert = new X509Certificate2(path);
            RSACryptoServiceProvider csp = (RSACryptoServiceProvider)cert.PublicKey.Key;
            string publicKey = csp.ToXmlString(false);
            return publicKey;
        }

        /// <summary>
        /// rsa sign
        /// </summary>
        /// <param name="originalData">original data</param>
        /// <param name="privateKey">private key</param>
        /// <returns>sign data</returns>
        private static string HashAndSignString(string originalData, string privateKey)
        {
            UnicodeEncoding ByteConverter = new UnicodeEncoding();
            byte[] dataToEncrypt = ByteConverter.GetBytes(originalData);
            using (RSACryptoServiceProvider RSAalg = new RSACryptoServiceProvider())
            {
                RSAalg.FromXmlString(privateKey);
                //Use SHA1 for digest algorithm and generate signature
                byte[] encryptedData = RSAalg.SignData(dataToEncrypt, new SHA1CryptoServiceProvider());
                return Convert.ToBase64String(encryptedData);
            }
        }

        /// <summary>
        /// sign zip
        /// </summary>
        /// <param name="originalFile">ogrginal file</param>
        /// <param name="targetPath">target zip path</param>
        /// <param name="privatePath">private file path</param>
        public static bool SignZip(string originalFile, string targetPath, string privatePath)
        {
            bool result = true;
            try
            {
                string originalData = File.ReadAllText(originalFile);
                string privateKey = GetPriKey(privatePath);

                //Generate signature by digest algorithm
                string signedData = HashAndSignString(originalData, privateKey);
                File.WriteAllText(originalFile + ".txt", signedData);
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
                ZipFile.CreateFromDirectory(originalFile.Substring(0, originalFile.LastIndexOf("\\")), targetPath);
            }
            catch
            {
                result = false;
            }
            return result;
        }


        /// <summary>
        /// verify sign
        /// </summary>
        /// <param name="originalData">original data</param>
        /// <param name="signedData">signed data</param>
        /// <param name="publicKey">public key</param>
        /// <returns></returns>
        private static bool VerifySigned(string originalData, string signedData, string publicKey)
        {
            using (RSACryptoServiceProvider RSAalg = new RSACryptoServiceProvider())
            {
                RSAalg.FromXmlString(publicKey);
                UnicodeEncoding ByteConverter = new UnicodeEncoding();
                byte[] dataToVerifyBytes = ByteConverter.GetBytes(originalData);
                byte[] signedDataBytes = Convert.FromBase64String(signedData);
                return RSAalg.VerifyData(dataToVerifyBytes, new SHA1CryptoServiceProvider(), signedDataBytes);
            }
        }

        public static bool DeleteDirectory(string folder)
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(folder);
                FileSystemInfo[] fileinfo = dir.GetFileSystemInfos();  //返回目录中所有文件和子目录
                foreach (FileSystemInfo i in fileinfo)
                {
                    if (i is DirectoryInfo)            //判断是否文件夹
                    {
                        DirectoryInfo subdir = new DirectoryInfo(i.FullName);
                        subdir.Delete(true);          //删除子目录和文件
                    }
                    else
                    {
                        //如果 使用了 streamreader 在删除前 必须先关闭流 ，否则无法删除 sr.close();
                        File.Delete(i.FullName);      //删除指定文件
                    }
                }
            }
            catch
            {
                return false;
            }
            return true;
        }

        public static bool GetAndVerifyZip(string zipPath = "")
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string updateZip = Path.Combine(basePath, "update.zip");
            try
            {
                if (!VerifyZip(updateZip)) return false;
                if (File.Exists(updateZip))
                {
                    File.Delete(updateZip);
                }
                DirectoryInfo updateDir = new DirectoryInfo(Path.Combine(basePath, "update"));
                FileInfo[] files = updateDir.GetFiles();
                for (int i = 0; i < 2; i++)
                {
                    if (files[i].Extension == ".zip")
                    {
                        if (string.IsNullOrEmpty(zipPath))
                        {
                            File.Copy(files[i].FullName, Path.Combine(basePath, "update.zip"));
                        }
                        else
                        {
                            File.Copy(files[i].FullName, zipPath);
                        }
                        break;
                    }
                }
                DeleteDirectory(Path.Combine(basePath, "update"));
                DeleteDirectory(Path.Combine(basePath, "signzip"));
            }
            catch
            {
                return false;
            }
            return true;
        }

        private static bool VerifyZip(string path)
        {
            if (!File.Exists(path)) return false;

            string updatePath = Path.Combine(Path.GetDirectoryName(path), "update");
            if (Directory.Exists(updatePath))
            {
                if (!DeleteDirectory(updatePath)) return false;
            }

            ZipFile.ExtractToDirectory(path, updatePath);

            DirectoryInfo updateDir = new DirectoryInfo(updatePath);
            FileInfo[] files = updateDir.GetFiles();
            if (files.Length != 2) return false;
            string originalData = string.Empty, signedData = string.Empty;
            for (int i = 0; i < 2; i++)
            {
                if (files[i].Extension == ".zip")
                {
                    originalData = File.ReadAllText(files[i].FullName);
                }
                else if (files[i].Extension == ".txt")
                {
                    signedData = File.ReadAllText(files[i].FullName);
                }
            }
            if (string.IsNullOrEmpty(originalData) || string.IsNullOrEmpty(signedData)) return false;

            string publicKey = GetPubKey(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ExtensionSettings.Default.Certificate.Name));
            return VerifySigned(originalData, signedData, publicKey);
        }
    }
}
