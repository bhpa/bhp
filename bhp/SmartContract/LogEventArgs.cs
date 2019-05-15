using Bhp.Network.P2P.Payloads;
using System;

namespace Bhp.SmartContract
{
    public class LogEventArgs : EventArgs
    {
        public IVerifiable ScriptContainer { get; }
        public UInt160 ScriptHash { get; }
        public string Message { get; }

        public LogEventArgs(IVerifiable container, UInt160 script_hash, string message)
        {
            this.ScriptContainer = container;
            this.ScriptHash = script_hash;
            this.Message = message;
        }
    }
}
