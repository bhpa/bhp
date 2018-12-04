using System;

namespace Bhp.Wallets.BRC6
{
    internal class WalletLocker : IDisposable
    {
        private BRC6Wallet wallet;

        public WalletLocker(BRC6Wallet wallet)
        {
            this.wallet = wallet;
        }

        public void Dispose()
        {
            wallet.Lock();
        }
    }
}
