using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        ShapeMeshes actorGroundMeshes;
        ShapeBatch actorGrounding, actorWings;

        void InitializeActorGrounding()
        {
            actorGroundMeshes = new ShapeMeshes();
            var shapeShader = Resources.Load<Shader>("WorldShape");
            actorGrounding = new ShapeBatch(actorGroundMeshes[WorldShape.Circle], shapeShader, renderCamera, GameVisualTokens.AllyLayer - 1);
            actorWings = new ShapeBatch(actorGroundMeshes[WorldShape.Circle], shapeShader, renderCamera, GameVisualTokens.EnemyLayer - 1);
        }

        void DrawActorGrounding(Vector2 position, Vector2 size, Vector2 movement, float time, WaveEnemyKind kind, bool authoredWings = false)
        {
            var shadow = GameVisualTokens.Ink;
            shadow.a = GameVisualTokens.ActorShadowOpacity;
            actorGrounding.Add(position, new Vector2(size.x * .26f, size.y * .055f), 0, shadow);
            if (kind == WaveEnemyKind.Ranged)
            {
                if (authoredWings) return;
                var hover = ActorMotion.SampleEnemy(kind, time, movement.sqrMagnitude > .000001f).Offset;
                var wing = GameVisualTokens.Attack;
                wing.a = .15f + .04f * Mathf.Sin(time * 53);
                var wingSize = new Vector2(size.x * .17f, size.y * .045f);
                var body = position + hover + Vector2.up * (size.y * .56f);
                actorWings.Add(body + Vector2.left * size.x * .23f, wingSize, 28, wing);
                actorWings.Add(body + Vector2.right * size.x * .23f, wingSize, -28, wing);
                return;
            }
            if (movement.sqrMagnitude <= .000001f) return;
            var direction = movement.normalized;
            var across = new Vector2(-direction.y, direction.x);
            var cycle = kind == WaveEnemyKind.Charger ? .19f : GameVisualTokens.ActorDustSeconds;
            for (var foot = 0; foot < 2; foot++)
            {
                var age = Mathf.Repeat(time / cycle + foot * .5f, 1);
                var dust = GameVisualTokens.Attack;
                dust.a = GameVisualTokens.ActorDustOpacity * (1 - age) * (1 - age);
                var point = position - direction * (age * size.x * .28f)
                    + across * ((foot == 0 ? -1 : 1) * size.x * .12f);
                var radius = size.x * (.018f + age * .034f);
                actorGrounding.Add(point, new Vector2(radius, radius * .55f), 0, dust);
            }
        }

        void DisposeActorGrounding()
        {
            actorGrounding?.Dispose(); actorWings?.Dispose(); actorGroundMeshes?.Dispose();
        }
    }
}
