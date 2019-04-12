using Akka.Actor;
using Bhp.Consensus;
using Bhp.Cryptography.ECC;
using Bhp.Ledger;
using Bhp.Network.P2P;
using Bhp.Network.RPC;
using Bhp.Persistence;
using Bhp.Plugins;
using Bhp.Wallets;
using System;
using System.Net;

namespace Bhp
{
    public class BhpSystem : IDisposable
    {
        private Peer.Start start_message = null;
        private bool suspend = false;

        public ActorSystem ActorSystem { get; } = ActorSystem.Create(nameof(BhpSystem),
            $"akka {{ log-dead-letters = off }}" +
            $"blockchain-mailbox {{ mailbox-type: \"{typeof(BlockchainMailbox).AssemblyQualifiedName}\" }}" +
            $"task-manager-mailbox {{ mailbox-type: \"{typeof(TaskManagerMailbox).AssemblyQualifiedName}\" }}" +
            $"remote-node-mailbox {{ mailbox-type: \"{typeof(RemoteNodeMailbox).AssemblyQualifiedName}\" }}" +
            $"protocol-handler-mailbox {{ mailbox-type: \"{typeof(ProtocolHandlerMailbox).AssemblyQualifiedName}\" }}" +
            $"consensus-service-mailbox {{ mailbox-type: \"{typeof(ConsensusServiceMailbox).AssemblyQualifiedName}\" }}");
        public IActorRef Blockchain { get; }
        public IActorRef LocalNode { get; }
        internal IActorRef TaskManager { get; }
        public IActorRef Consensus { get; private set; }
        public RpcServer RpcServer { get; private set; }

        public BhpSystem(Store store)
        {
            Plugin.LoadPlugins(this);
            this.Blockchain = ActorSystem.ActorOf(Ledger.Blockchain.Props(this, store));
            this.LocalNode = ActorSystem.ActorOf(Network.P2P.LocalNode.Props(this));
            this.TaskManager = ActorSystem.ActorOf(Network.P2P.TaskManager.Props(this));
            Plugin.NotifyPluginsLoadedAfterSystemConstructed();
        }

        public void Dispose()
        {
            RpcServer?.Dispose();
            ActorSystem.Stop(LocalNode);
            ActorSystem.Dispose();
        }

        internal void ResumeNodeStartup()
        {
            suspend = false;
            if (start_message != null)
            {
                LocalNode.Tell(start_message);
                start_message = null;
            }
        }

        /*
        public void StartConsensus(Wallet wallet)
        {
            Consensus = ActorSystem.ActorOf(ConsensusService.Props(this.LocalNode, this.TaskManager, wallet));
            Consensus.Tell(new ConsensusService.Start());
        }
        */

        /// <summary>
        /// By BHP
        /// </summary>
        /// <param name="wallet"></param>
        public void StartConsensus(Wallet wallet)
        {
            bool found = false;
            foreach (WalletAccount account in wallet.GetAccounts())
            {
                string publicKey = account.GetKey().PublicKey.EncodePoint(true).ToHexString();
                foreach (ECPoint point in Ledger.Blockchain.StandbyValidators)
                {
                    string validator = point.EncodePoint(true).ToHexString();
                    if (validator.Equals(publicKey))
                    {
                        found = true;
                        break;
                    }
                }
                if (found) { break; }
            }
            //只有共识节点才能开启共识
            if (found)
            {
                Consensus = ActorSystem.ActorOf(ConsensusService.Props(LocalNode, TaskManager, wallet));
                Consensus.Tell(new ConsensusService.Start());
            }
        }

        public void StartNode(int port = 0, int wsPort = 0, int minDesiredConnections = Peer.DefaultMinDesiredConnections,
            int maxConnections = Peer.DefaultMaxConnections)
        {
            start_message = new Peer.Start
            {
                Port = port,
                WsPort = wsPort,
                MinDesiredConnections = minDesiredConnections,
                MaxConnections = maxConnections
            };
            if (!suspend)
            {
                LocalNode.Tell(start_message);
                start_message = null;
            }
        }

        public void StartRpc(IPAddress bindAddress, int port, Wallet wallet = null, string sslCert = null, string password = null,
            string[] trustedAuthorities = null, Fixed8 maxGasInvoke = default(Fixed8))
        {
            RpcServer = new RpcServer(this, wallet, maxGasInvoke);
            RpcServer.Start(bindAddress, port, sslCert, password, trustedAuthorities);
        }

        internal void SuspendNodeStartup()
        {
            suspend = true;
        }
    }
}
