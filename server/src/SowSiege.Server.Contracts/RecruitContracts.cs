using System.Text.Json.Serialization;

namespace SowSiege.Server.Contracts;

public sealed record RecruitCommand([property: JsonRequired] Guid CommandId, [property: JsonRequired] long ExpectedVersion, [property: JsonRequired] string PoolVersion);
public sealed record RecruitOddsEntry(string HeroId, string Rarity, int Weight, long Scale, double Probability);
public sealed record RecruitOdds(string PoolFamilyId, string PoolVersion, string PolicyHash, int CostGems, int GuaranteeAfterMisses,
    string GuaranteedRarity, IReadOnlyDictionary<string, int> DuplicateShards, RecruitOddsEntry[] Base, RecruitOddsEntry[] Next,
    int Pity, int DrawsUntilGuarantee, long Gems, long AccountVersion);
public sealed record HeroHolding(string HeroId, int Shards);
public sealed record RecruitResult(long DrawIndex, string HeroId, string Rarity, string Outcome, int Shards, bool Guaranteed, int PityBefore, int PityAfter);
public sealed record RecruitReply(bool Done, string Reason, RecruitResult? Result, long Gems, int Pity, long AccountVersion, HeroHolding[] Heroes);
