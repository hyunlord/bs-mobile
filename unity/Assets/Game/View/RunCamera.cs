using System;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public readonly struct WorldCameraSettings
    {
        public readonly int WorldUnitsPerUnityUnit;
        public readonly int MinHalfHeight;
        public readonly int MaxHalfHeight;
        public readonly int EstatePadding;
        public readonly int FollowMilliseconds;
        public readonly int ZoomMilliseconds;
        public WorldCameraSettings(int worldUnitsPerUnityUnit, int minHalfHeight, int maxHalfHeight, int estatePadding, int followMilliseconds, int zoomMilliseconds)
        {
            if (worldUnitsPerUnityUnit <= 0 || minHalfHeight <= 0 || maxHalfHeight < minHalfHeight || estatePadding < 0 || followMilliseconds <= 0 || zoomMilliseconds <= 0)
                throw new ArgumentException("Invalid canonical camera settings.");
            WorldUnitsPerUnityUnit = worldUnitsPerUnityUnit; MinHalfHeight = minHalfHeight; MaxHalfHeight = maxHalfHeight;
            EstatePadding = estatePadding; FollowMilliseconds = followMilliseconds; ZoomMilliseconds = zoomMilliseconds;
        }
    }

    public sealed class RunCamera
    {
        private readonly Camera camera;
        private readonly WorldCameraSettings settings;
        private bool positioned;
        public RunCamera(Camera camera, WorldCameraSettings settings)
        {
            this.camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera)); this.settings = settings;
            camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = GamePalette.Ground;
            camera.transform.rotation = Quaternion.identity;
        }

        public void Present(Vector2 lord, int estateExtent, float deltaTime)
        {
            var units = settings.WorldUnitsPerUnityUnit;
            var target = new Vector3(lord.x, lord.y, -10);
            var desired = Mathf.Clamp((estateExtent + settings.EstatePadding) / Mathf.Min(1, Mathf.Max(0.01f, camera.aspect)), settings.MinHalfHeight, settings.MaxHalfHeight) / units;
            if (!positioned) { camera.transform.position = target; camera.orthographicSize = desired; positioned = true; return; }
            var follow = 1 - Mathf.Exp(-Mathf.Max(0, deltaTime) * 1000 / settings.FollowMilliseconds);
            var zoom = 1 - Mathf.Exp(-Mathf.Max(0, deltaTime) * 1000 / settings.ZoomMilliseconds);
            camera.transform.position = Vector3.Lerp(camera.transform.position, target, follow);
            camera.orthographicSize = Mathf.Lerp(camera.orthographicSize, desired, zoom);
        }

        public Vector2 Point(WorldPoint point) => new Vector2((float)point.X / settings.WorldUnitsPerUnityUnit, (float)point.Y / settings.WorldUnitsPerUnityUnit);
    }
}
