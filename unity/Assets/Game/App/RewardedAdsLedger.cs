#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.App
{
    public interface IRewardedAdsStore
    {
        byte[]? Load();
        void Save(byte[] bytes);
    }
    public sealed class RewardedAdsAtomicStore : IRewardedAdsStore
    {
        readonly AtomicSaveStore store;
        public RewardedAdsAtomicStore(string directory) { store = new AtomicSaveStore(directory, "rewarded-ads.bin"); }
        public byte[]? Load() => store.Load(RewardedAdsLedger.Validate).Payload;
        public void Save(byte[] bytes) => store.Save(bytes, RewardedAdsLedger.Validate);
    }
    [Serializable] internal sealed class RewardedAdsReceipt
    {
        public string id = "", run = "", claim = "";
        public int slot, ordinal, day, outcome;
        public bool acknowledged, expired;
    }
    [Serializable] internal sealed class RewardedAdsActivity
    {
        public string run = "";
        public long sequence;
    }
    [Serializable] internal sealed class RewardedAdsLedger
    {
        public int version = 1, firstDay, maximumDay;
        public bool cooling;
        public List<RewardedAdsReceipt> receipts = new List<RewardedAdsReceipt>();
        public List<RewardedAdsActivity> activities = new List<RewardedAdsActivity>();
        public byte[] Encode() => Encoding.UTF8.GetBytes(JsonUtility.ToJson(this));
        public RewardedAdsLedger Copy() => Decode(Encode());
        public static RewardedAdsLedger Decode(byte[] bytes)
        {
            var value = JsonUtility.FromJson<RewardedAdsLedger>(new UTF8Encoding(false, true).GetString(bytes));
            if (value == null || value.version != 1 || value.firstDay <= 0 || value.maximumDay < value.firstDay || value.receipts == null || value.activities == null)
                throw new InvalidDataException("Invalid rewarded-ad ledger header.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var receipt in value.receipts)
            {
                if (receipt == null || !Identity(receipt.id) || !ids.Add(receipt.id) || receipt.run == null || receipt.claim == null ||
                    !Enum.IsDefined(typeof(RewardedAdSlot), receipt.slot) || !Enum.IsDefined(typeof(RewardedAdOutcome), receipt.outcome) ||
                    receipt.day <= value.firstDay || receipt.day > value.maximumDay || receipt.ordinal < 0 ||
                    (receipt.outcome == (int)RewardedAdOutcome.Completed ? receipt.ordinal < 1 : receipt.ordinal != 0 || receipt.acknowledged || receipt.expired))
                    throw new InvalidDataException("Invalid rewarded-ad receipt.");
                var slot = (RewardedAdSlot)receipt.slot;
                if (receipt.expired && (receipt.acknowledged || slot != RewardedAdSlot.Revive && slot != RewardedAdSlot.Reroll))
                    throw new InvalidDataException("Only unacknowledged run rewards may expire.");
                if (slot != RewardedAdSlot.Idle && !Identity(receipt.run) || slot == RewardedAdSlot.Idle && !Identity(receipt.claim))
                    throw new InvalidDataException("Reward has no stable run or claim identity.");
            }
            var ordinals = new Dictionary<(int Slot, string Scope), HashSet<int>>();
            var idleClaims = new HashSet<string>(StringComparer.Ordinal);
            foreach (var receipt in value.receipts)
            {
                if (receipt.outcome != (int)RewardedAdOutcome.Completed) continue;
                bool idle = receipt.slot == (int)RewardedAdSlot.Idle;
                var key = (receipt.slot, idle ? receipt.day.ToString(System.Globalization.CultureInfo.InvariantCulture) : receipt.run);
                if (!ordinals.TryGetValue(key, out var sequence)) ordinals.Add(key, sequence = new HashSet<int>());
                int maximum = idle ? RewardedAdsPolicy.IdleDailyLimitHypothesis : receipt.slot == (int)RewardedAdSlot.Reroll ? 2 : 1;
                if (!sequence.Add(receipt.ordinal) || receipt.ordinal > maximum || idle && !idleClaims.Add(receipt.claim))
                    throw new InvalidDataException("Reward receipt quota or ordinal is invalid.");
            }
            foreach (var sequence in ordinals.Values)
                for (int ordinal = 1; ordinal <= sequence.Count; ordinal++)
                    if (!sequence.Contains(ordinal)) throw new InvalidDataException("Reward ordinals must be consecutive.");
            ids.Clear();
            foreach (var activity in value.activities)
                if (activity == null || !Identity(activity.run) || !ids.Add(activity.run) || activity.sequence < 0)
                    throw new InvalidDataException("Invalid gameplay activity identity.");
            return value;
        }
        internal static bool Identity(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;
        public static AtomicSaveValidation Validate(byte[] bytes)
        {
            try
            {
                var header = JsonUtility.FromJson<RewardedAdsLedger>(Encoding.UTF8.GetString(bytes));
                if (header != null && header.version != 1) return AtomicSaveValidation.UnsupportedVersion;
                Decode(bytes); return AtomicSaveValidation.Valid;
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidDataException || error is DecoderFallbackException)
            { return AtomicSaveValidation.Corrupt; }
        }
    }
}
