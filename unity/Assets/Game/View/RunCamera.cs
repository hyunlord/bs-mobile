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
        public bool KeepViewportInsideMap { get; set; }
        public float EdgeActorMargin { get; set; }
        private Vector2 visualOffset;
        private Vector2 mapSize;
        public RunCamera(Camera camera, WorldCameraSettings settings)
        {
            this.camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera)); this.settings = settings;
            camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = GamePalette.Ground;
            camera.transform.rotation = Quaternion.identity;
        }

        public void SetMapBounds(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Map dimensions must be positive.");
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Map dimensions must be positive.");
            mapSize = new Vector2((float)width / settings.WorldUnitsPerUnityUnit, (float)height / settings.WorldUnitsPerUnityUnit);
        }

        public void Present(Vector2 lord, int estateExtent, float deltaTime, Rect? safeAreaPixels = null)
        {
            SetVisualOffset(Vector2.zero);
            var units = settings.WorldUnitsPerUnityUnit; var aspect = Mathf.Max(0.01f, camera.aspect);
            var desired = Mathf.Clamp((estateExtent + settings.EstatePadding) / Mathf.Min(1, aspect), settings.MinHalfHeight, settings.MaxHalfHeight) / units;
            var maximum = Mathf.Max((float)settings.MinHalfHeight / units, (float)settings.MaxHalfHeight / units * camera.pixelHeight / GameVisualTokens.CameraReferencePixelHeight);
            if (mapSize.x > 0)
                maximum = Mathf.Min(maximum, Mathf.Min((mapSize.x * 0.5f + (KeepViewportInsideMap ? 0 : GameVisualTokens.CameraOutsideMargin)) / aspect, mapSize.y * 0.5f + (KeepViewportInsideMap ? 0 : GameVisualTokens.CameraOutsideMargin)));
            desired = Mathf.Min(desired, maximum);
            var follow = 1 - Mathf.Exp(-Mathf.Max(0, deltaTime) * 1000 / settings.FollowMilliseconds);
            var zoom = 1 - Mathf.Exp(-Mathf.Max(0, deltaTime) * 1000 / settings.ZoomMilliseconds);
            camera.orthographicSize = positioned ? Mathf.Min(Mathf.Lerp(camera.orthographicSize, desired, zoom), maximum) : desired;
            var viewport = camera.pixelRect;
            var safe = safeAreaPixels ?? viewport;
            safe = Rect.MinMaxRect(Mathf.Max(viewport.xMin, safe.xMin), Mathf.Max(viewport.yMin, safe.yMin),
                Mathf.Min(viewport.xMax, safe.xMax), Mathf.Min(viewport.yMax, safe.yMax));
            if (safe.width <= 0 || safe.height <= 0) safe = viewport;
            var pixelsToWorld = camera.orthographicSize * 2 / Mathf.Max(1, viewport.height);
            var excluded = new Vector4(safe.xMin - viewport.xMin, safe.yMin - viewport.yMin,
                viewport.xMax - safe.xMax, viewport.yMax - safe.yMax) * pixelsToWorld;
            var target = ClampToMap(lord - (safe.center - viewport.center) * pixelsToWorld, excluded);
            var position = positioned ? Vector2.Lerp(camera.transform.position, target, follow) : target;
            position = ClampToMap(position, excluded);
            camera.transform.position = new Vector3(position.x, position.y, -10); positioned = true;
        }

        Vector2 ClampToMap(Vector2 position, Vector4 excluded)
        {
            if (mapSize.x <= 0) return position;
            var halfHeight = camera.orthographicSize; var halfWidth = halfHeight * camera.aspect;
            var margin = KeepViewportInsideMap ? 0 : GameVisualTokens.CameraOutsideMargin;
            if(KeepViewportInsideMap)
                return new Vector2(mapSize.x <= halfWidth * 2 ? mapSize.x * .5f : Mathf.Clamp(position.x, halfWidth-EdgeActorMargin, mapSize.x-halfWidth+EdgeActorMargin),
                    mapSize.y <= halfHeight * 2 ? mapSize.y * .5f : Mathf.Clamp(position.y, halfHeight-EdgeActorMargin, mapSize.y-halfHeight+EdgeActorMargin));
            return new Vector2(Mathf.Clamp(position.x, halfWidth - margin - excluded.x, mapSize.x - halfWidth + margin + excluded.z),
                Mathf.Clamp(position.y, halfHeight - margin - excluded.y, mapSize.y - halfHeight + margin + excluded.w));
        }

        public void SetVisualOffset(Vector2 offset)
        {
            camera.transform.position += new Vector3(offset.x - visualOffset.x, offset.y - visualOffset.y, 0);
            visualOffset = offset;
        }

        public Vector2 Point(WorldPoint point) => new Vector2((float)point.X / settings.WorldUnitsPerUnityUnit, (float)point.Y / settings.WorldUnitsPerUnityUnit);
    }
}
