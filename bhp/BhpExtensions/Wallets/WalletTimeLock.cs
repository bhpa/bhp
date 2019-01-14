using System;
using System.Threading;
using Bhp.Wallets;

namespace Bhp.BhpExtensions.Wallets
{
    public class WalletTimeLock
    {
        private int Duration = 10; // seconds 
        private DateTime UnLockTime;    
        private ReaderWriterLockSlim rwlock;

        public WalletTimeLock()
        {
            UnLockTime = DateTime.UtcNow;
            Duration = 10;           
            rwlock = new ReaderWriterLockSlim();
        }
        
        public void SetDuration(int Duration)
        {
            try
            {
                rwlock.EnterWriteLock();
                this.Duration = Duration >= 1 ? Duration : 1;
            }
            finally
            {
                rwlock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Unlock wallet
        /// </summary>
        /// <param name="Duration">Unlock duration</param>
        public bool UnLock(Wallet wallet, string password, int duration)
        {
            bool unlock = false;
            try
            {
                rwlock.EnterWriteLock();
                if (wallet.VerifyPassword(password))
                {
                    Duration = duration > 1 ? duration : 1;
                    UnLockTime = DateTime.UtcNow;
                    unlock = true;
                }
                else
                {
                    Duration = 0;
                }
            }
            finally
            {
                rwlock.ExitWriteLock();
            }
            return unlock;
        }

        public bool IsLocked()
        {
            if (ExtensionSettings.Default.WalletConfig.AutoLock == false)
            {
                return false;
            }

            //wallet is locked by default.
            bool locked = true;
            try
            {
                rwlock.EnterReadLock();
                locked = (DateTime.UtcNow - UnLockTime).TotalSeconds >= Duration;

                //Console.WriteLine($"differ: {(DateTime.UtcNow - UnLockTime).TotalSeconds},Duration:{Duration}");
            }
            finally
            {
                rwlock.ExitReadLock();
            }
            return locked;
        }
    }
}
