using Bhp.Wallets;
using System;
using System.Threading;

namespace Bhp.Network.RPC
{
    public class WalletTimeLock
    {
        private int Duration = 10; // seconds 
        private DateTime UnLockTime;        
        private bool IsAutoLock;
        private ReaderWriterLockSlim rwlock;

        public WalletTimeLock(bool isAutoLock)
        {
            UnLockTime = DateTime.Now;
            Duration = 10;
            IsAutoLock = isAutoLock;
            rwlock = new ReaderWriterLockSlim();
        }

        public void SetAutoLock(bool isAutoLock)
        {
            IsAutoLock = isAutoLock;
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
                    UnLockTime = DateTime.Now;
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
            if (IsAutoLock == false)
            {
                return false;
            }

            //wallet is locked by default.
            bool locked = true;
            try
            {
                rwlock.EnterReadLock();
                TimeSpan span = new TimeSpan(DateTime.Now.Ticks) - new TimeSpan(UnLockTime.Ticks);
                locked = ((int)span.TotalSeconds >= Duration);
            }
            finally
            {
                rwlock.ExitReadLock();
            }
            return locked;
        }
    }
}
