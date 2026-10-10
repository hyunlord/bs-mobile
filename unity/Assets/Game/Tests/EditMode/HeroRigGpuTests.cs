using System.Reflection;
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
                var material=Field<Material>(batch,"material");
                var properties=Field<MaterialPropertyBlock>(batch,"properties");
                if(moveUniformsToMaterial)
                {
                    foreach(var name in new[]{"_RigFrame","_RigUpperFrame","_RigFarDelta","_RigNearDelta","_RigWarp","_RigCanvas"})
                    {
                        var id=Shader.PropertyToID(name);
                        if(properties.HasVector(id))material.SetVector(id,properties.GetVector(id));
                    }
                    properties.Clear();
                }
                batch.Add(Vector2.zero,Vector2.one,Vector2.zero,new Rect(0,0,1,1),0,Color.white); batch.Flush();
                commands.SetRenderTarget(target); commands.ClearRenderTarget(false,true,Color.clear);
                commands.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0,1,-.125f,1,-1,1),true));
                commands.DrawMeshInstanced(Field<Mesh>(batch,"mesh"),0,material,0,Field<Matrix4x4[]>(batch,"matrices"),1,properties);
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
        static T Field<T>(SpriteBatch batch,string name)=> (T)typeof(SpriteBatch).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(batch);
    }
}
