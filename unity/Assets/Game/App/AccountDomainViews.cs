using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Xml.Linq;

namespace Game.App
{
    public sealed class RecruitOdds
    {
        public string heroId;
        public string rarity;
        public int weight;
        public long scale;
        public double probability;
    }
    public sealed class RecruitResult
    {
        public long drawIndex;
        public string heroId;
        public string rarity;
        public string outcome;
        public int shards;
        public bool guaranteed;
        public int pityBefore;
        public int pityAfter;
    }
    public sealed class RecruitAccountView
    {
        public string poolFamilyId;
        public string poolVersion;
        public string policyHash;
        public int costGems;
        public int guaranteeAfterMisses;
        public string guaranteedRarity;
        public Dictionary<string, int> duplicateShards;
        public RecruitOdds[] baseOdds;
        public RecruitOdds[] next;
        public int pity;
        public int drawsUntilGuarantee;
        public int gems;
        public Dictionary<string, int> heroes;
        public RecruitResult last;
    }
    public sealed class ResearchAccountNode
    {
        public string id;
        public string category;
        public int level;
        public int maxLevel;
        public string status;
        public string[] lockReasons;
        public Dictionary<string, int> cost;
        public long seconds;
        public string domain;
        public int value;
    }
    public sealed class ResearchAccountJob
    {
        public string nodeId;
        public int level;
        public long startedAtMs;
        public long completesAtMs;
        public long remainingSeconds;
    }
    public sealed class ResearchAccountEffect
    {
        public string domain;
        public int raw;
        public int cap;
        public int applied;
    }
    public sealed class ResearchAccountView
    {
        public long serverUtcMs;
        public ResearchAccountNode[] nodes;
        public ResearchAccountJob[] active;
        public ResearchAccountEffect[] effects;
        public string[] proofs;
    }
    public sealed class AccountDomainViews
    {
        public RecruitAccountView recruit;
        public ResearchAccountView research;

        public static AccountDomainViews Read(string json)
        {
            var domains = AccountJson.Field(AccountJson.Parse(json), "domains", "object");
            var r = domains.Element("recruit"); var research = domains.Element("research");
            var value = new AccountDomainViews
            {
                recruit = r == null ? null : new RecruitAccountView
                {
                    poolFamilyId=S(r,"poolFamilyId"), poolVersion=S(r,"poolVersion"), policyHash=S(r,"policyHash"),
                    costGems=I(r,"costGems"), guaranteeAfterMisses=I(r,"guaranteeAfterMisses"), guaranteedRarity=S(r,"guaranteedRarity"),
                    duplicateShards=AccountJson.Map(r,"duplicateShards"), baseOdds=AccountJson.Array(r,"base",ReadOdds), next=AccountJson.Array(r,"next",ReadOdds),
                    pity=I(r,"pity"), drawsUntilGuarantee=I(r,"drawsUntilGuarantee"), gems=I(r,"gems"), heroes=AccountJson.Map(r,"heroes"),
                    last=ReadResult(AccountJson.Field(r,"last"))
                },
                research = research == null ? null : new ResearchAccountView
                {
                    serverUtcMs=L(research,"serverUtcMs"),
                    nodes=AccountJson.Array(research,"nodes",n=>new ResearchAccountNode
                    { id=S(n,"id"),category=S(n,"category"),level=I(n,"level"),maxLevel=I(n,"maxLevel"),status=S(n,"status"),
                      lockReasons=AccountJson.Array(n,"lockReasons",AccountJson.StringValue),cost=AccountJson.Map(n,"cost"),seconds=L(n,"seconds"),domain=S(n,"domain"),value=I(n,"value") }),
                    active=AccountJson.Array(research,"active",n=>new ResearchAccountJob
                    {nodeId=S(n,"nodeId"),level=I(n,"level"),startedAtMs=L(n,"startedAtMs"),completesAtMs=L(n,"completesAtMs"),remainingSeconds=L(n,"remainingSeconds")}),
                    effects=AccountJson.Array(research,"effects",n=>new ResearchAccountEffect
                    {domain=S(n,"domain"),raw=I(n,"raw"),cap=I(n,"cap"),applied=I(n,"applied")}),
                    proofs=AccountJson.Array(research,"proofs",AccountJson.StringValue)
                }
            };
            if (value.recruit != null)
            {
                var recruit = value.recruit;
                if (string.IsNullOrEmpty(recruit.poolVersion) || string.IsNullOrEmpty(recruit.policyHash) || recruit.costGems <= 0 || recruit.gems < 0 || recruit.pity < 0 || recruit.drawsUntilGuarantee < 1
                    || recruit.heroes == null || recruit.duplicateShards == null || recruit.baseOdds == null || recruit.next == null || recruit.baseOdds.Length == 0 || recruit.next.Length != recruit.baseOdds.Length)
                    throw new FormatException("Invalid recruit view.");
                ValidateOdds(recruit.baseOdds); ValidateOdds(recruit.next);
            }
            if (value.research != null && (value.research.nodes == null || value.research.active == null || value.research.effects == null || value.research.proofs == null))
                throw new FormatException("Invalid research view.");
            if (value.research != null)
                foreach (var node in value.research.nodes)
                    if (node == null || string.IsNullOrEmpty(node.id) || node.cost == null || node.lockReasons == null || node.seconds <= 0)
                        throw new FormatException("Invalid research node.");
            return value;
        }
        static string S(XElement node,string key)=>AccountJson.StringValue(AccountJson.Field(node,key,"string"));
        static long L(XElement node,string key)=>AccountJson.LongValue(AccountJson.Field(node,key,"number"));
        static int I(XElement node,string key)=>checked((int)L(node,key));
        static RecruitOdds ReadOdds(XElement n)=>new RecruitOdds
        {heroId=S(n,"heroId"),rarity=S(n,"rarity"),weight=I(n,"weight"),scale=L(n,"scale"),probability=AccountJson.DoubleValue(AccountJson.Field(n,"probability","number"))};
        static RecruitResult ReadResult(XElement n)
        {
            if ((string)n.Attribute("type")=="null")return null;
            AccountJson.RequireType(n,"object");
            return new RecruitResult {drawIndex=L(n,"drawIndex"),heroId=S(n,"heroId"),rarity=S(n,"rarity"),outcome=S(n,"outcome"),shards=I(n,"shards"),
                guaranteed=bool.Parse(AccountJson.Field(n,"guaranteed","boolean").Value),pityBefore=I(n,"pityBefore"),pityAfter=I(n,"pityAfter")};
        }
        static void ValidateOdds(RecruitOdds[] rows)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
                if (row == null || string.IsNullOrEmpty(row.heroId) || !ids.Add(row.heroId) || string.IsNullOrEmpty(row.rarity)
                    || row.weight < 0 || row.scale <= 0 || double.IsNaN(row.probability) || double.IsInfinity(row.probability) || row.probability < 0 || row.probability > 1)
                    throw new FormatException("Invalid recruit odds.");
        }
    }
}
