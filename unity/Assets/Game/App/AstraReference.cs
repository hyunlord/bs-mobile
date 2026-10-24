using System.Linq;

namespace Game.App
{
    // Astra's fixed B composition from Release m3-estate-autodecor-20261024 (assembly.js): the reference input that the ported
    // placement must reproduce exactly (#375). Screen pixels of a 390x844 board; the church/market circles and the screen bounds
    // are that board's reservations, not game rules.
    public static class AstraReference
    {
        public static readonly (string Key, string Kind, int Stage, int X, int Y, int W)[] BuildingsB =
        {
            ("home-01", "house", 1, 42, 76, 70), ("home-02", "house", 2, 112, 147, 70), ("home-03", "house", 2, 149, 113, 70), ("home-04", "house", 2, 283, 154, 70),
            ("home-05", "house", 1, 337, 197, 70), ("home-06", "house", 2, 290, 235, 70), ("home-07", "house", 3, 80, 330, 78), ("home-08", "house", 2, 177, 371, 74),
            ("well-01", "well", 2, 74, 219, 58), ("granary-01", "granary", 2, 369, 352, 105),
        };

        public static AstraScene SceneB(bool reversed = false)
        {
            var scene = new AstraScene { LandOriginX = 15, LandOriginY = 20, LandColumns = 8, LandRows = 15 };
            var buildings = BuildingsB.Select(b => new AstraBuilding(b.Key, b.Kind, b.Stage, b.X, b.Y, b.W));
            scene.Buildings.AddRange(reversed ? buildings.Reverse() : buildings);
            double[][] R(params int[] xy) => Enumerable.Range(0, xy.Length / 2).Select(i => new double[] { xy[2 * i], xy[2 * i + 1] }).ToArray();
            scene.Routes.Add(R(48, 70, 112, 144, 78, 207, 107, 284, 177, 364, 256, 426, 348, 466));
            scene.Routes.Add(R(338, 198, 274, 232, 199, 270, 107, 284, 43, 325));
            scene.Routes.Add(R(177, 364, 112, 433, 31, 477, 82, 559, 210, 602, 342, 668));
            scene.Routes.Add(R(78, 207, 145, 178, 199, 191, 221, 255, 199, 270));
            scene.Obstacles.Add((199, 155, .7, 45)); scene.Obstacles.Add((217, 277, 1, 28));
            scene.RoadsideAllowed = (x, y) => !(x < 8 || x > 324);
            scene.LandAllowed = (x, y) => !(x > 319 || x < 10 || y < 10 || y > 818 || y > 425 && y < 566 && x < 150);
            scene.LandState = (x, y) => y < 410 ? "restored" : y < 630 ? "sage" : "fallow";
            return scene;
        }

        // Fixed art pieces of the B board that are not placed by the rules: (id, x, y, display width), bottom-centre anchored.
        public static readonly (string Id, int X, int Y, int W)[] FixedB =
        {
            ("church", 199, 194, 111), ("market", 217, 292, 78), ("bridge", 338, 411, 103), ("bridge", 330, 665, 103),
            ("field-s2-body", 45, 493, 148), ("field-s3-body", 35, 560, 148),
            ("worker", 45, 215, 15), ("worker", 119, 178, 15), ("worker", 277, 322, 15), ("worker", 265, 440, 15), ("worker", 165, 499, 15), ("worker", 86, 540, 15), ("worker", 105, 94, 15),
        };
    }
}
