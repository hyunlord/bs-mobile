using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.PlayMode
{
    public sealed class CombatFxCaptureTests
    {
        [UnityTest]
        public IEnumerator ApprovedRolesRenderThroughWorldPresentationWithoutMutatingSession()
        {
            if (CanonicalContent.ProfileName != "first-playable") Assert.Ignore("First-playable role presentation fixture.");
            var catalog = CanonicalContent.CreateCatalog();
            var options = new InteractiveOptions(new(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash);
            var session = new InteractiveSession(catalog, options);
            var stateHash = session.ComputeStateHash();
            var original = session.View.CaptureFrame();
            var origin = original.Lord.Position;
            var attackPoint = new WorldPoint(origin.X + 200, origin.Y + 450);
            var enemies = Enumerable.Range(0, 12).Select(i => new EnemyView(i + 1, "core:raider", new WorldPoint(origin.X - 350 + i % 4 * 300, origin.Y + 250 + i / 4 * 250), 100, 100)).ToArray();
            var frame = original with { Enemies = enemies, Events = Array.Empty<PresentationEvent>(), Buildings = Array.Empty<BuildingView>(), Farms = Array.Empty<FarmView>(), People = Array.Empty<PersonView>(), Loot = Array.Empty<GroundLootView>(), Remains = Array.Empty<RemainView>() };
            var snapshot = session.View.CaptureFirstPlayable() with { Attacks = Array.Empty<ActiveAttackView>(), BuildingProgress = Array.Empty<BuildingProgressView>(), People = Array.Empty<PersonActivityView>(), MapEvents = Array.Empty<MapEventView>() };
            var sourceIds = new[] { "iron_blade", "harvest_scythe", "ember_wand", "ward_orbit", "canal_bow", "spore_fan" };
            var forms = new[] { "sector90", "sector180", "projectile", "orbit", "piercing", "field" };
            var output = Environment.GetEnvironmentVariable("COMBAT_FX_FIXTURE_OUTPUT");
            if (output != null) { Assert.That(Directory.Exists(output), Is.False, "Fresh fixture output required."); Directory.CreateDirectory(output); }
            var priorCaptureRate = Time.captureFramerate; Time.captureFramerate = 60;
            var target = new RenderTexture(390, 844, 24);
            var image = new Texture2D(390, 844, TextureFormat.RGB24, false);
            var cameraOwner = new GameObject("Combat FX fixture camera");
            var camera = cameraOwner.AddComponent<Camera>(); camera.targetTexture = target;
            try
            {
                for (var index = 0; index < sourceIds.Length; index++)
                {
                    var owner = new GameObject("Combat FX fixture " + sourceIds[index]);
                    var world = owner.AddComponent<WorldRenderer>();
                    var settings = CanonicalContent.Presentation.Camera;
                    world.Initialize(camera, new WorldCameraSettings(settings.WorldUnitsPerUnityUnit, settings.MinHalfHeight, settings.MaxHalfHeight, settings.EstatePadding, settings.FollowMilliseconds, settings.ZoomMilliseconds), options.Run.EstateId, frame.MapWidth, frame.MapHeight);
                    try
                    {
                        yield return PlayModeCaptureGuard.WaitForShaders("combat-fx-fixture:" + sourceIds[index]);
                        for (var n = 0; n < 3; n++) { world.Present(frame, frame, snapshot, 1, 0, new Rect(0, 0, 390, 844)); yield return null; }
                        Assert.That(frame.Buildings.Count + frame.Farms.Count + frame.People.Count + snapshot.BuildingProgress.Count, Is.Zero, "No inherited fresh-session growth props in isolated fixture.");
                        var enemyVisual = ArtCatalog.Load().Resolve("enemy", "core:raider", "move");
                        Debug.Log("Combat FX fixture enemy role=" + enemyVisual.RoleId + " texture=" + enemyVisual.Texture.name + " uv=" + enemyVisual.UvRects[0]);
                        foreach (var enemy in enemies)
                        {
                            var ground = new Vector3((float)enemy.Position.X / settings.WorldUnitsPerUnityUnit, (float)enemy.Position.Y / settings.WorldUnitsPerUnityUnit, 0);
                            var lower = camera.WorldToViewportPoint(ground - new Vector3(enemyVisual.WorldSize.x * enemyVisual.Pivot.x, enemyVisual.WorldSize.y * enemyVisual.Pivot.y, 0));
                            var upper = camera.WorldToViewportPoint(ground + new Vector3(enemyVisual.WorldSize.x * (1 - enemyVisual.Pivot.x), enemyVisual.WorldSize.y * (1 - enemyVisual.Pivot.y), 0));
                            Assert.That(lower.x, Is.InRange(0f, 1f)); Assert.That(lower.y, Is.InRange(0f, 1f));
                            Assert.That(upper.x, Is.InRange(0f, 1f)); Assert.That(upper.y, Is.InRange(0f, 1f));
                        }
                        Read(); var control = image.GetPixels32();
                        var source = "core:" + sourceIds[index];
                        var role = ArtCatalog.Load().Resolve("attack", source, forms[index]);
                        Assert.That(role.RoleId, Is.EqualTo("attack.core." + sourceIds[index] + "." + forms[index]));
                        Debug.Log("Combat FX fixture role=" + role.RoleId + " texture=" + role.Texture.name + " uv=" + role.UvRects[0] + " visibleEnemies=" + enemies.Length + " inheritedBuildings=" + frame.Buildings.Count);
                        var shown = frame;
                        var fx = snapshot;
                        if (index == 0 || index == 1)
                            shown = frame with { Events = new[] { new PresentationEvent(100 + index, frame.Tick, PresentationKind.Attack, source, attackPoint, new WorldPoint(1, 0), forms[index], catalog.WeaponCombat.Weapons[source].Levels[0].Range, 0, Array.Empty<WorldPoint>(), Array.Empty<int>()) } };
                        else
                            fx = snapshot with { Attacks = new[] { new ActiveAttackView(100 + index, source, forms[index], attackPoint, new WorldPoint(attackPoint.X - 1, attackPoint.Y), catalog.FirstPlayable.Weapons[source].Radius, 0, catalog.FirstPlayable.Weapons[source].LifetimeTicks) } };
                        for (var sample = 0; sample < (output == null ? 1 : 120); sample++)
                        {
                            var presented = shown;
                            if (shown.Events.Count > 0)
                                presented = shown with { Events = new[] { shown.Events[0] with { Id = 100 + index * 10 + sample / 30 } } };
                            if (fx.Attacks.Count > 0) fx = fx with { Attacks = new[] { fx.Attacks[0] with { AgeTicks = sample / 2 } } };
                            world.Present(presented, presented, fx, 1, 1f / 60, new Rect(0, 0, 390, 844)); yield return null;
                            Read(); var pixels = image.GetPixels32();
                            var changed = pixels.Where((p, i) => Math.Abs(p.r - control[i].r) + Math.Abs(p.g - control[i].g) + Math.Abs(p.b - control[i].b) > 6).Count();
                            if (sample == 0) Assert.That(changed, Is.GreaterThan(4), "Actual renderer effect must change visible pixels: " + role.RoleId);
                            Assert.That(world.UnsupportedShapeCount, Is.Zero);
                            Assert.That(session.ComputeStateHash(), Is.EqualTo(stateHash), "View fixture must not mutate authoritative session.");
                            if (output != null)
                            {
                                var png = image.EncodeToPNG();
                                File.WriteAllBytes(Path.Combine(output, "frame-" + (index * 120 + sample).ToString("D6") + ".png"), png);
                                if (sample == 0) File.WriteAllBytes(Path.Combine(output, sourceIds[index] + ".png"), png);
                                File.AppendAllText(Path.Combine(output, "coverage.csv"), role.RoleId + "," + sample + "," + changed + "," + world.DrawCalls + "," + stateHash + "\n");
                            }
                        }
                    }
                    finally { Object.Destroy(owner); }
                    yield return null;
                }
                if (output != null) File.WriteAllText(Path.Combine(output, "CLASSIFICATION.txt"), "View-only synthetic attack/enemy snapshot fixture through WorldRenderer and runtime shader; no Core attack execution, collision, performance, or normal-play claim. 390x844. Fixed 60fps: six roles x 120 frames, transient events every 30 frames. coverage.csv: role,sample,changedPixelsVsNoEffect,drawCalls,unchangedSessionHash.\n");
            }
            finally { Time.captureFramerate = priorCaptureRate; camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(image); Object.Destroy(cameraOwner); }
            yield return null;
            void Read()
            {
                var prior = RenderTexture.active;
                try { RenderTexture.active = target; PlayModeCaptureGuard.ReadPixels(image, new Rect(0, 0, 390, 844), 0, 0, "combat-fx-fixture"); image.Apply(); }
                finally { RenderTexture.active = prior; }
            }
        }
    }
}
