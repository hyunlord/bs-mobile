using System;
using System.IO;
using Game.App;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class WorldMarchResponseTests
    {
        static WorldMarchOrder Gather() => new WorldMarchOrder { commandId=Guid.NewGuid().ToString("D"),expectedVersion=17,operation="gather",targetId="target:grain",generation=9,heroId="slg:owned",troops=new[]{new WorldMarchTroops{unitId="slg:levy",count=3}} };
        [Test] public void MarchEnvelopeIsNotAnAccountCommandAndCancelOmitsUnusedNullableGuidFields()
        {
            var gather=WorldMarchResponse.OrderJson(Gather());
            Assert.That(gather,Does.Not.Contain("marchId")); Assert.That(WorldMarchResponse.ValidateOrder(gather),Is.EqualTo("/v1/world/marches/commands"));
            Assert.Throws<FormatException>(()=>PendingAccountCommand.Domain(gather));
            var cancel=WorldMarchResponse.OrderJson(new WorldMarchOrder{commandId=Guid.NewGuid().ToString("D"),expectedVersion=19,operation="cancel",marchId=Guid.NewGuid().ToString("D")});
            Assert.That(cancel,Does.Not.Contain("targetId"));Assert.That(cancel,Does.Not.Contain("troops"));
            Assert.Throws<FormatException>(()=>WorldMarchResponse.ValidateOrder(cancel.Replace("\"cancel\"","\"attack\"")));
        }
        [Test] public void JournalRestartKeepsExactWorldBodyAndCannotReadAnAccountEndpointScope()
        {
            var root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../artifacts/world-client/journal-tests",Guid.NewGuid().ToString("N")));
            const string scope="http://127.0.0.1:5084\naccount-a";
            var body=WorldMarchResponse.OrderJson(Gather());
            new PendingAccountCommand(root,scope+"\n"+WorldMarchResponse.Endpoint,WorldMarchResponse.ValidateOrder).Write(body);
            Assert.That(new PendingAccountCommand(root,scope+"\n"+WorldMarchResponse.Endpoint,WorldMarchResponse.ValidateOrder).Read(),Is.EqualTo(body));
            Assert.That(new PendingAccountCommand(root,scope).Read(),Is.Null);
            Assert.That(new PendingAccountCommand(root,"http://127.0.0.1:5084\naccount-b\n"+WorldMarchResponse.Endpoint,WorldMarchResponse.ValidateOrder).Read(),Is.Null);
            Assert.Throws<FormatException>(()=>new PendingAccountCommand(root,scope+"\n"+WorldMarchResponse.Endpoint).Read());
        }
        [Test] public void MarchReadsOnlyProvidedStateAndDueTimeWithoutDerivingStageProgress()
        {
            var id=Guid.NewGuid().ToString("D");
            var json="[{\"marchId\":\""+id+"\",\"targetId\":\"target:grain\",\"generation\":9,\"operation\":\"gather\",\"heroId\":\"slg:owned\",\"troops\":[{\"unitId\":\"slg:levy\",\"count\":3}],\"path\":[{\"x\":2,\"y\":4}],\"startedAtMs\":1000,\"dueAtMs\":95000,\"state\":\"returning\",\"reason\":\"recalled\"}]";
            var march=WorldMarchResponse.ReadList(json)[0];
            Assert.That(march.StartedAtMs,Is.EqualTo(1000));Assert.That(march.DueAtMs,Is.EqualTo(95000));
            Assert.That(PortraitMetaPanels.MarchStateName(march.State),Is.EqualTo("귀환 중"));
            Assert.Throws<FormatException>(()=>WorldMarchResponse.ReadList(json.Replace("\"dueAtMs\":95000,","")));
        }
        [Test] public void MissingShieldTimeDoesNotBecomeAFreeProtectionClaim()
        {
            Assert.Throws<FormatException>(()=>WorldMarchResponse.ReadShield("{\"utcHighWatermark\":1,\"shieldUntilMs\":2,\"nextFreeAtMs\":3}"));
            var shield=WorldMarchResponse.ReadShield("{\"utcHighWatermark\":1,\"shieldUntilMs\":2,\"nextFreeAtMs\":3,\"hostileUntilMs\":0}");
            Assert.That(shield.ShieldUntilMs,Is.EqualTo(2));
        }
    }
}
