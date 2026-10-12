using Game.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tests.EditMode
{
    public sealed class HeroRigGpuTests
    {
        [Test]
        public void LiveHeroCutoutsPreserveAuthoredPixelCoordinates()
        {
            var parts = Resources.Load<Texture2D>("HeroMotion/parts");
            var sword = Resources.Load<Texture2D>("HeroMotion/sword");
            Assert.That(parts, Is.Not.Null);
            Assert.That(sword, Is.Not.Null);
            Assert.That(new Vector2Int(parts.width, parts.height), Is.EqualTo(new Vector2Int(1254, 1254)));
            Assert.That(new Vector2Int(sword.width, sword.height), Is.EqualTo(new Vector2Int(724, 2172)));
            foreach (var part in HeroLiveRigData.Load().sprites)
            {
                var texture = part.source == "cleanSword" ? sword : parts;
                var rectangle = part.rect;
                Assert.That(rectangle[0], Is.GreaterThanOrEqualTo(0), part.bone);
                Assert.That(rectangle[1], Is.GreaterThanOrEqualTo(0), part.bone);
                Assert.That(rectangle[0] + rectangle[2], Is.LessThanOrEqualTo(texture.width), part.bone);
                Assert.That(rectangle[1] + rectangle[3], Is.LessThanOrEqualTo(texture.height), part.bone);
            }
        }

        [Test]
        public void LiveHeroResourceMaterialReferencesThePackageShader()
        {
            var material = Resources.Load<Material>("HeroMotion/live-material");
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material.shader)), Is.EqualTo("13c02b14c4d048fa9653293d54f6e0e1"));
        }

        [Test]
        public void RetainedHeroMaterialEnablesInstancing()
        {
            var material=Resources.Load<Material>("HeroRigMaterial");
            Assert.That(material,Is.Not.Null,"Runtime-created materials need a retained instanced material asset in player builds.");
            Assert.That(material.shader,Is.EqualTo(Resources.Load<Shader>("HeroRig")));
            Assert.That(material.enableInstancing,Is.True);
        }

        [TestCase(0)]
        [TestCase(6)]
        [TestCase(12)]
        public void ActualLayeredHeroProducesOpaqueGpuPixels(int frame)
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null), "Run this gate with the native GPU; -nographics cannot verify pixels.");
            var shader=Resources.Load<Shader>("HeroRig");
            var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/fallow-hero-b-layers.png");
            Assert.That(shader,Is.Not.Null); Assert.That(shader.isSupported,Is.True); Assert.That(atlas,Is.Not.Null);
            var control=ReadOpaquePixels(Texture2D.whiteTexture,Resources.Load<Shader>("WorldSprite"),frame,false);
            Assert.That(control,Is.GreaterThan(1000),"The control instanced white quad must render before judging the hero shader.");
            var current=ReadOpaquePixels(atlas,shader,frame,false);
            var materialUniforms=ReadOpaquePixels(atlas,shader,frame,true);
            TestContext.WriteLine($"Hero GPU frame={frame}, current={current}, material-uniforms={materialUniforms}, device={SystemInfo.graphicsDeviceType}");
            Assert.That(current,Is.GreaterThan(200),"The submitted one-quad hero must draw real opaque pixels, not merely report a supported shader.");
        }

        static int ReadOpaquePixels(Texture2D atlas,Shader shader,int frame,bool moveUniformsToMaterial)
        {
            var owner=new GameObject("Hero GPU regression camera");
            var camera=owner.AddComponent<Camera>();
            var target=new RenderTexture(128,128,0,RenderTextureFormat.ARGB32);
            var readback=new Texture2D(128,128,TextureFormat.RGBA32,false);
            var previous=RenderTexture.active;
            using var commands=new CommandBuffer { name="Hero layered GPU regression" };
            using var batch=new SpriteBatch(atlas,shader,camera,0);
            try
            {
                target.Create(); batch.BeginFrame();
                if(shader.name=="Game/View/HeroRig")
                    batch.SetHeroRigPose(HeroGaitState.FrameRect(frame),HeroGaitState.FrameRect(frame),Vector2.zero,Vector2.zero,HeroGaitState.Warp);
                if(moveUniformsToMaterial)batch.MoveHeroRigUniformsToMaterialForTesting();
                batch.Add(Vector2.zero,Vector2.one,Vector2.zero,new Rect(0,0,1,1),0,Color.white); batch.Flush();
                commands.SetRenderTarget(target); commands.ClearRenderTarget(false,true,Color.clear);
                commands.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0,1,-.125f,1,-1,1),true));
                batch.DrawSingleInstanceForTesting(commands);
                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active=target; readback.ReadPixels(new Rect(0,0,128,128),0,0);readback.Apply();
                var count=0;foreach(var pixel in readback.GetPixels32())if(pixel.a>64)count++;
                return count;
            }
            finally
            {
                RenderTexture.active=previous;target.Release();
                Object.DestroyImmediate(target);Object.DestroyImmediate(readback);Object.DestroyImmediate(owner);
            }
        }
    }
}
