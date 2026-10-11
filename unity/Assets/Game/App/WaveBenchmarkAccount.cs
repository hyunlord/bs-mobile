using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Game.App
{
    // Startup runs before the benchmark harness: isolate its save migration and preference defaults too.
    public sealed class WaveBenchmarkAccount
    {
        static readonly Lazy<WaveBenchmarkAccount> current = new Lazy<WaveBenchmarkAccount>(
            () => Parse(Environment.GetCommandLineArgs()));
        public static WaveBenchmarkAccount Current => current.Value;
        public string OutputDirectory { get; }
        public string ProfileDirectory { get; }
        public string PreferencesPrefix { get; }
        WaveBenchmarkAccount(string output)
        {
            OutputDirectory = output;
            ProfileDirectory = output + ".account";
            using var hash = SHA256.Create();
            var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(output));
            PreferencesPrefix = "sowsiege.wave-benchmark." + BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant() + ".";
        }
        public static WaveBenchmarkAccount Parse(string[] args)
        {
            if(args == null) throw new ArgumentNullException(nameof(args));
            bool requested = false;
            foreach(var argument in args)
                if(argument != null && (argument.StartsWith("--wave-benchmark", StringComparison.Ordinal) || argument == "--crowd-target60-experiment")) requested = true;
            if(!requested) return null;
            int replay = Array.IndexOf(args, "--wave-benchmark");
            int output = Array.IndexOf(args, "--wave-benchmark-output");
            if(replay < 0 || replay + 1 >= args.Length || string.IsNullOrWhiteSpace(args[replay + 1]) || args[replay + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Benchmark startup requires an explicit replay path.");
            if(output < 0 || output + 1 >= args.Length || string.IsNullOrWhiteSpace(args[output + 1]) || args[output + 1].StartsWith("--", StringComparison.Ordinal) ||
                Array.LastIndexOf(args, "--wave-benchmark-output") != output || Array.LastIndexOf(args, "--wave-benchmark") != replay)
                throw new ArgumentException("Benchmark startup requires one explicit --wave-benchmark-output directory.");
            var destination = Path.GetFullPath(args[output + 1]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if(string.IsNullOrEmpty(destination) || string.IsNullOrEmpty(Path.GetDirectoryName(destination)))
                throw new ArgumentException("Benchmark output cannot be a filesystem root.");
            return new WaveBenchmarkAccount(destination);
        }
    }
}
