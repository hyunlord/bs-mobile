using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class ReleaseBoundary
{
    public static int Verify(string directory)
    {
        try
        {
            if (!File.Exists(Path.Combine(directory, "Game.App.dll")))
            {
                throw new InvalidDataException("Missing release Game.App.dll.");
            }

            var foundCoordinator = false;
            foreach (var path in Directory.GetFiles(directory, "Game.*.dll"))
            {
                using var stream = File.OpenRead(path);
                using var pe = new PEReader(stream);
                var metadata = pe.GetMetadataReader();
                foreach (var handle in metadata.TypeDefinitions)
                {
                    var type = metadata.GetTypeDefinition(handle);
                    var name = metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
                    if (name is "Game.Debug.DebugOverlay" or "Game.Debug.DebugSnapshot" or "Game.Debug.DebugIntent" or "Game.App.DeviceParity")
                    {
                        throw new InvalidDataException("Developer type leaked: " + name);
                    }

                    if (name == "Game.App.RunCoordinator")
                    {
                        foundCoordinator = true;
                    }
                }
            }
            if (!foundCoordinator)
            {
                throw new InvalidDataException("Release coordinator missing.");
            }

            Console.WriteLine("UNITY_RELEASE_BOUNDARY_PASS actual Android release assemblies contain gameplay and no developer controls.");
            return 0;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or BadImageFormatException)
        {
            Console.Error.WriteLine("UNITY_RELEASE_BOUNDARY_FAIL " + e.Message);
            return 1;
        }
    }
}
