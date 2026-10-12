using Game.View;
using NUnit.Framework;
using UnityEngine;

public sealed class M1CombatArtBindingTests
{
    [TestCase("core:iron_blade"),TestCase("core:ward_orbit"),TestCase("core:storm_fork"),TestCase("core:granary_sling"),
     TestCase("core:spore_fan"),TestCase("core:ditch_pike"),TestCase("core:canal_bow"),TestCase("core:muster_horn")]
    public void EveryCombatCaseHasAnExplicitLoadableHudIcon(string id)
    {
        var registry=Resources.Load<ArtRegistry>("Wave1aArt");
        Assert.That(registry,Is.Not.Null,"Run ArtPreparation.Prepare before checking actual bindings.");
        var catalog=ArtCatalog.FromJson(registry.manifest.text,registry.atlases);
        var art=catalog.Resolve("icon",id,"default");
        var sprite=art.Sprite;
        try {Assert.That(sprite,Is.Not.Null);Assert.That(sprite.rect.width,Is.GreaterThan(0));}
        finally {if(sprite!=null)Object.DestroyImmediate(sprite);}
    }
}
