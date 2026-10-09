using System.Globalization;

namespace SowSiege.Sim;

public static class MetaEconomyCli
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "meta-economy")
        {
            return false;
        }

        if (args.Length is < 3 or > 4)
        {
            throw new ArgumentException("meta-economy <repository-root> <output.csv> [days=21]");
        }

        string root = Path.GetFullPath(args[1]);
        int days = args.Length == 4 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 21;
        string output = Path.GetFullPath(args[2]);
        if (File.Exists(output))
        {
            throw new IOException("Refusing to overwrite economy evidence: " + output);
        }

        string dataHash = ContentLoader.Hash(Path.Combine(root, "data"), false);
        var meta = MetaContentLoader.Load(root);
        var content = ContentLoader.Load(Path.Combine(root, "data"), profileName: "first-playable");
        if (dataHash != ContentLoader.Hash(Path.Combine(root, "data"), false))
        {
            throw new IOException("Data changed while loading economy inputs.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var tasks = MetaEconomySimulation.Archetypes.Select((type, index) => Task.Run(() =>
        {
            var partial = new List<EconomyRow>();
            return MetaEconomySimulation.Run(meta, content, days, new[] { type }, emitted: row =>
            {
                partial.Add(row);
                if (row.Event == "run")
                {
                    Console.Error.WriteLine($"{row.Archetype} day={row.Day} run={row.Run} chapter={row.Chapter} cleared={row.Cleared} ticks={row.Ticks}");
                }

                if (row.Run == type.RunsPerDay)
                {
                    File.WriteAllText(output + "." + type.Id + ".partial.csv", MetaEconomySimulation.Csv(partial, meta, dataHash));
                }
            }, seedOffset: index * 100000);
        })).ToArray();
        Task.WaitAll(tasks);
        var rows = tasks.SelectMany(task => task.Result).ToArray();
        File.WriteAllText(output, MetaEconomySimulation.Csv(rows, meta, dataHash));
        Console.WriteLine($"Economy observation: {rows.Length} rows, {days} days; {output}");
        return true;
    }
}
