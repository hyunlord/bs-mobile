using System;
using System.IO;
using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        ArtVisual heroConceptVisual;
        Texture2D heroConceptTexture;

        public void ConfigureHeroConceptReview(string candidate, string imagePath)
        {
            if (heroConceptTexture != null) throw new InvalidOperationException("A concept is already loaded.");
            if (candidate != "A" && candidate != "B" && candidate != "C") throw new ArgumentException("Unknown hero concept.");
            var bytes = File.ReadAllBytes(imagePath);
            heroConceptTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Review hero " + candidate, filterMode = FilterMode.Bilinear };
            if (!heroConceptTexture.LoadImage(bytes, true)) throw new InvalidDataException("Invalid hero concept image.");
            if (heroConceptTexture.width != 1536 || heroConceptTexture.height != 1024)
                throw new InvalidDataException("Concept sheet dimensions differ from the reviewed source.");
            var frame = candidate == "A" ? new ArtPixelRect { x = 0, y = 15, width = 532, height = 925 }
                : candidate == "B" ? new ArtPixelRect { x = 0, y = 0, width = 495, height = 995 }
                : new ArtPixelRect { x = 0, y = 25, width = 470, height = 940 };
            var source = art.ResolveRole("hero.preview-b.idle");
            // Alpha >= 8/255 bounds, measured from the source PNGs. The existing layered
            // B idle occupies y=44..210 (167px) in its 256px cell / 252px role crop.
            // Match visible paint height, not the padded quad height (which enlarged concepts).
            const float referencePaintedHeight = 167f;
            var paintedHeight = candidate == "A" ? 888f : candidate == "B" ? 968f : 892f;
            var paintedBottom = candidate == "A" ? 900f : candidate == "B" ? 985f : 917f;
            var height = source.WorldSize.y * GameVisualTokens.HeroRigHeightScale
                * (referencePaintedHeight / 252f) * (frame.height / paintedHeight);
            heroConceptVisual = new ArtVisual(new ArtRoleDefinition
            {
                id = "review.hero." + candidate, frames = new[] { frame },
                pivot = new Vector2(.5f, (frame.height - paintedBottom - .5f) / frame.height), worldSize = new Vector2(height * frame.width / frame.height, height),
                frameMs = 1000, tween = "none"
            }, heroConceptTexture);
        }

        void DisposeHeroConceptReview()
        {
            if (heroConceptVisual != null) Destroy(heroConceptVisual.Sprite);
            if (heroConceptTexture != null) Destroy(heroConceptTexture);
        }
    }
}
