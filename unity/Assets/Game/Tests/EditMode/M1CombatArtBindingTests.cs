using Game.View;
using NUnit.Framework;
using UnityEngine;

public sealed class M1CombatArtBindingTests
{
    [TestCase(0f,0.5f,0.5f),TestCase(0.4f,0.5f,0.5f),TestCase(1.8f,0.5f,0.5f),TestCase(1.8f,0.1f,0.9f)]
    public void SpearFootStaysOnImpactAndPaintedAxisHasRequestedHeight(float height,float pivotX,float pivotY)
    {
        var ground=new Vector2(3.25f,-2.7f);
        var pivot=new Vector2(pivotX,pivotY);
        M1CombatSpearGeometry.Pose(ground,height,pivot,out var position,out var size,out var degrees);
        var rotation=Quaternion.Euler(0,0,degrees);
        Vector2 foot=position+(Vector2)(rotation*Vector2.Scale(M1CombatSpearGeometry.Foot-pivot,size));
        Vector2 tip=position+(Vector2)(rotation*Vector2.Scale(M1CombatSpearGeometry.Tip-pivot,size));
        Assert.That(Vector2.Distance(foot,ground),Is.LessThan(0.00001f));
        Assert.That(Vector2.Distance(tip,ground+Vector2.up*height),Is.LessThan(0.00001f));
    }

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
