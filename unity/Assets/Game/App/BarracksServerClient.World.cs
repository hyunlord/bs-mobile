using System;
using System.Collections;
using System.Globalization;

namespace Game.App
{
    public sealed partial class BarracksServerClient
    {
        public WorldMapTarget WorldCity { get; private set; }
        public WorldMapChunk WorldChunk { get; private set; }
        public bool WorldCityMissing { get; private set; }
        partial void ClearWorldAuthentication();
        partial void ClearWorldAuthentication() { WorldCity = null; WorldChunk = null; WorldCityMissing = false; }
        public IEnumerator LoadWorldCity(bool assign = false)
        {
            if (Busy || !Connected) yield break;
            WorldCityMissing = false;
            yield return Request(assign ? "POST" : "GET", "/v1/world/city", assign ? "{}" : null, json =>
            {
                var city = WorldMapResponse.ReadCity(json);
                if (pendingScope != null && !pendingScope.Contains("\nprototype-") && pendingScope != Endpoint + "\n" + Guid.Parse(city.AccountId).ToString("D"))
                    throw new FormatException("City belongs to another account.");
                WorldCity = city; Status = "connected";
            }, false);
            if (Status == "city-not-assigned") { WorldCity = null; WorldCityMissing = true; }
        }
        public IEnumerator LoadWorldChunk(int x, int y)
        {
            if (Busy || !Connected || x < 0 || y < 0) yield break;
            yield return Request("GET", "/v1/world/chunks/" + x.ToString(CultureInfo.InvariantCulture) + "/" + y.ToString(CultureInfo.InvariantCulture), null, json =>
            {
                var chunk = WorldMapResponse.ReadChunk(json);
                if (chunk.X != x || chunk.Y != y || WorldChunk != null && (chunk.WorldId != WorldChunk.WorldId || chunk.ContentHash != WorldChunk.ContentHash || chunk.Size != WorldChunk.Size))
                    throw new FormatException("World content changed during navigation.");
                WorldChunk = chunk; Status = "connected";
            }, false);
        }
    }
}
