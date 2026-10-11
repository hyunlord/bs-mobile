#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Tests.PlayMode
{
    internal static class PortraitTextGeometry
    {
        [Serializable] sealed class Dump
        {
            public string surface = "Editor diagnostic; cached generation and submitted mesh, no layout mutation";
            public int screenWidth, screenHeight;
            public Label[] labels;
        }

        [Serializable] sealed class Label
        {
            public string path, text, font, alignment, horizontalOverflow, verticalOverflow;
            public int fontSize, generatedFontSize, generatorVertices, submittedVertices;
            public float pixelsPerUnit, canvasScale;
            public bool fontDynamic, bestFit;
            public Vector2 pivot, anchoredPosition, anchorMin, anchorMax, sizeDelta, roundingOffset;
            public Vector3 localScale, worldScale;
            public Rect localRect, parentLocalRect, generatedRawBounds, generatedScreenBounds, submittedLocalBounds, submittedScreenBounds;
            public Vector3[] worldCorners, parentWorldCorners;
            public Vector3[] firstGeneratedQuad, firstSubmittedQuad;
        }

        public static void Write(Transform cards, string output)
        {
            var labels = new List<Label>();
            foreach (var text in cards.GetComponentsInChildren<Text>())
            {
                var rect = text.rectTransform;
                var parent = rect.parent as RectTransform;
                var vertices = text.cachedTextGenerator.verts;
                var raw = vertices.Select(vertex => vertex.position).ToArray();
                var unitsPerPixel = 1 / text.pixelsPerUnit;
                var first = raw.Length == 0 ? Vector2.zero : (Vector2)raw[0] * unitsPerPixel;
                var rounding = text.PixelAdjustPoint(first) - first;
                var generatedScreen = raw.Select(point => ScreenPoint(rect, point * unitsPerPixel + (Vector3)rounding)).ToArray();
                var mesh = text.canvasRenderer.GetMesh();
                var submitted = mesh != null ? mesh.vertices : Array.Empty<Vector3>();
                labels.Add(new Label
                {
                    path = PathTo(rect, cards), text = text.text, font = text.font.name,
                    alignment = text.alignment.ToString(), fontSize = text.fontSize,
                    generatedFontSize = text.cachedTextGenerator.fontSizeUsedForBestFit,
                    horizontalOverflow = text.horizontalOverflow.ToString(), verticalOverflow = text.verticalOverflow.ToString(),
                    pixelsPerUnit = text.pixelsPerUnit, canvasScale = text.canvas.scaleFactor,
                    fontDynamic = text.font.dynamic, bestFit = text.resizeTextForBestFit,
                    pivot = rect.pivot, anchoredPosition = rect.anchoredPosition, anchorMin = rect.anchorMin, anchorMax = rect.anchorMax,
                    sizeDelta = rect.sizeDelta, localScale = rect.localScale, worldScale = rect.lossyScale,
                    localRect = rect.rect, parentLocalRect = parent != null ? parent.rect : default,
                    worldCorners = Corners(rect), parentWorldCorners = parent != null ? Corners(parent) : Array.Empty<Vector3>(),
                    generatorVertices = raw.Length, submittedVertices = submitted.Length, roundingOffset = rounding,
                    generatedRawBounds = Bounds(raw), generatedScreenBounds = Bounds(generatedScreen),
                    submittedLocalBounds = Bounds(submitted), submittedScreenBounds = Bounds(submitted.Select(point => ScreenPoint(rect, point)).ToArray()),
                    firstGeneratedQuad = raw.Take(4).ToArray(), firstSubmittedQuad = submitted.Take(4).ToArray()
                });
            }
            File.WriteAllText(output, JsonUtility.ToJson(new Dump { screenWidth = Screen.width, screenHeight = Screen.height, labels = labels.ToArray() }, true));
        }

        static Vector3 ScreenPoint(RectTransform rect, Vector3 point)
            => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(point));

        static Vector3[] Corners(RectTransform rect)
        { var result = new Vector3[4]; rect.GetWorldCorners(result); return result; }

        static Rect Bounds(Vector3[] points)
            => points.Length == 0 ? default : Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y), points.Max(point => point.x), points.Max(point => point.y));

        static string PathTo(Transform value, Transform root)
        {
            var path = value.name;
            while (value != root && value.parent != null) { value = value.parent; path = value.name + "/" + path; }
            return path;
        }
    }
}
#endif
