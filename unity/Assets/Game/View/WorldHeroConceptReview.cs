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
            var height = source.WorldSize.y * GameVisualTokens.HeroRigHeightScale;
            heroConceptVisual = new ArtVisual(new ArtRoleDefinition
            {
                id = "review.hero." + candidate, frames = new[] { frame },
                pivot = new Vector2(.5f, .04f), worldSize = new Vector2(height * frame.width / frame.height, height),
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
