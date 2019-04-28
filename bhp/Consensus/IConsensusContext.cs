using Bhp.Cryptography.ECC;
using Bhp.IO;
using Bhp.Network.P2P.Payloads;
using Bhp.Persistence;
using System;
using System.Collections.Generic;

namespace Bhp.Consensus
{
    public interface IConsensusContext : IDisposable, ISerializable
    {
        //public const uint Version = 0;
        UInt256 PrevHash { get; }
        uint BlockIndex { get; }
        byte ViewNumber { get; }
        ECPoint[] Validators { get; }
        int MyIndex { get; }
        uint PrimaryIndex { get; }
        uint Timestamp { get; set; }
        ulong Nonce { get; set; }
        UInt160 NextConsensus { get; set; }
        UInt256[] TransactionHashes { get; set; }
        Dictionary<UInt256, Transaction> Transactions { get; set; }
        ConsensusPayload[] PreparationPayloads { get; set; }
        ConsensusPayload[] CommitPayloads { get; set; }
        ConsensusPayload[] ChangeViewPayloads { get; set; }
        int[] LastSeenMessage { get; set; }
        Block Block { get; set; }
        Snapshot Snapshot { get; }

        Block CreateBlock();

        bool Load();

        ConsensusPayload MakeChangeView();

        ConsensusPayload MakeCommit();

        Block MakeHeader();

        ConsensusPayload MakePrepareRequest();

        ConsensusPayload MakeRecoveryRequest();

        ConsensusPayload MakeRecoveryMessage();

        ConsensusPayload MakePrepareResponse();

        void Reset(byte viewNumber);

        void Save();        
    }
}
