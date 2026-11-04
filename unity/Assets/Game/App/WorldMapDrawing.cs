using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    public sealed class WorldMapDrawing : MonoBehaviour, IBeginDragHandler, IEndDragHandler
    {
        struct Piece { public RectTransform Rect; public Vector2 Cell, Pivot; public Vector2 Size; public float Lift; }
        readonly List<Piece> pieces = new List<Piece>();
        readonly List<WorldMapTerrainGraphic> terrains = new List<WorldMapTerrainGraphic>();
        public EstateMapModel Model;
        public WorldMapChunk Chunk;
        public Action<int, int> Move;
        public Func<bool> CanMove;
        Vector2 lastOffset, dragStart;
        float lastScale;
        bool laidOut;

        public void Add(RectTransform rect, Vector2 cell, Vector2 size, Vector2 pivot, float lift = 0)
        {
            pieces.Add(new Piece { Rect = rect, Cell = cell, Size = size, Pivot = pivot, Lift = lift });
        }
        public void Add(WorldMapTerrainGraphic graphic) => terrains.Add(graphic);
        void LateUpdate()
        {
            if (Model != null && (!laidOut || Model.Offset != lastOffset || Model.Scale != lastScale)) Layout();
        }
        public void Layout()
        {
            lastOffset = Model.Offset; lastScale = Model.Scale; laidOut = true;
            foreach (var terrain in terrains) terrain.SetVerticesDirty();
            foreach (var piece in pieces)
            {
                var visible = WorldMapCameraState.Visible(Model, piece.Cell, Mathf.Max(piece.Size.x, piece.Size.y) * Model.Scale);
                if (piece.Rect.gameObject.activeSelf != visible) piece.Rect.gameObject.SetActive(visible);
                if (!visible) continue;
                var p = Model.GridToViewport(piece.Cell); var size = piece.Size * Model.Scale;
                PortraitWidgets.Position(piece.Rect, p.x - size.x * piece.Pivot.x, p.y - size.y * (1 - piece.Pivot.y) - piece.Lift * Model.Scale, size.x, size.y);
            }
        }
        public void OnBeginDrag(PointerEventData data) { dragStart = data.position; }
        public void OnEndDrag(PointerEventData data)
        {
            if (Model == null || Chunk == null || CanMove?.Invoke() == false) return;
            var direction = WorldMapCameraState.EdgeMove(Model, Chunk, data.position - dragStart);
            if (direction != Vector2Int.zero) Move?.Invoke(direction.x, direction.y);
        }
    }
}
