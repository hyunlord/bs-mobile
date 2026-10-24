#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.App
{
    // Development builds only (#376): `--estate-demo <dir>` plays a fixed 30-second tour of the estate map prototype (three zoom
    // levels, a drag, building selection, construction start and completion, stock and task claims, a move) at a fixed 30 fps
    // and saves every frame as a PNG for ffmpeg. The prototype copy's clock follows game time here, so the run is the same on
    // every machine. Nothing is saved to the account.
    public sealed partial class RunCoordinator
    {
        const int DemoFrames = 900, DemoFps = 30;

        void TryStartEstateDemo()
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "--estate-demo");
            if (index < 0) return;
            if (!IsWave || index + 1 >= args.Length || !Ui.PortraitEnabled) { Fail(new ArgumentException("--estate-demo needs wave-1a portrait and an output directory.")); return; }
            StartCoroutine(EstateDemo(Path.GetFullPath(args[index + 1])));
        }

        IEnumerator EstateDemo(string directory)
        {
            Directory.CreateDirectory(directory);
            Time.captureFramerate = DemoFps; forceEstatePrototype = true; estateClock = () => Time.timeAsDouble;
            ShowPortraitEstate();
            var frame = 0;
            IEnumerator Hold(int frames)
            {
                for (var i = 0; i < frames && frame < DemoFrames; i++) { ScreenCapture.CaptureScreenshot(Path.Combine(directory, $"frame-{frame++:00000}.png")); yield return null; }
            }
            EstateMapView View() => Ui.GetComponentsInChildren<EstateMapView>().Single();
            int Id(string kind) => estateMap.State.Estate.Buildings.Single(b => b.Kind == kind).Id;
            IEnumerator Redraw(int frames) { ShowEstateMap(); yield return Hold(frames); }
            IEnumerator Pan(Vector2 pixels, int frames)
            {
                var view = View(); var pointer = new PointerEventData(EventSystem.current) { pointerId = 41, position = new Vector2(Screen.width * .5f, Screen.height * .5f) };
                view.OnBeginDrag(pointer);
                for (var i = 0; i < frames; i++) { pointer.position += pixels / frames; view.OnDrag(pointer); yield return Hold(1); }
                view.OnEndDrag(pointer);
            }

            yield return Hold(45);
            View().Step(-1); yield return Hold(55);                 // whole estate
            View().Step(1); yield return Hold(25);                  // neighbourhood
            View().Step(1); yield return Hold(55);                  // one building
            View().Step(-1); yield return Hold(25);
            yield return Pan(new Vector2(-140, 40), 30); yield return Hold(10);
            yield return Pan(new Vector2(140, -40), 30);

            var granary = Id("meta:granary");
            estateMap.Select(granary); yield return Redraw(45);
            estateMap.StartWork(granary); yield return Redraw(45);   // construction starts: the work line counts down
            estateMap.SkipTime(60); yield return Redraw(50);         // completed: the stage art changes
            var field = Id("m3a:field");
            estateMap.Select(field); estateMap.StartWork(field); yield return Redraw(40);
            estateMap.SkipTime(30); yield return Redraw(40);
            estateMap.SkipTime(600); yield return Redraw(45);        // stock appears
            estateMap.ClaimProduction(); yield return Redraw(45);
            estateMap.ClaimGoal(); yield return Redraw(45);
            var forge = Id("meta:forge"); var at = estateMap.Building(forge);
            estateMap.Select(forge); estateMap.BeginMove(); yield return Redraw(35);
            estateMap.Place(new Vector2Int(at.X - 4, at.Y + 4)); yield return Redraw(45);
            estateMap.Confirm(); yield return Redraw(45);
            estateMap.Select(null); yield return Redraw(DemoFrames);

            Time.captureFramerate = 0;
            File.WriteAllText(Path.Combine(directory, "demo.json"), "{\"frames\": " + frame + ", \"fps\": " + DemoFps + ", \"commit\": \"" + Generated.BuildIdentity.Commit + "\", \"sourceDirty\": " +
                Generated.BuildIdentity.SourceDirty.ToString().ToLowerInvariant() + ", \"screen\": \"" + Screen.width + "x" + Screen.height + "\"}\n");
            Application.Quit();
        }
    }
}
#endif
