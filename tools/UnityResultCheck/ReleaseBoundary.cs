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
                if (Path.GetFileName(path).EndsWith(".Editor.dll", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Editor assembly leaked: " + Path.GetFileName(path));
                }

                using var stream = File.OpenRead(path);
                using var pe = new PEReader(stream);
                var metadata = pe.GetMetadataReader();
                foreach (var handle in metadata.TypeDefinitions)
                {
                    var type = metadata.GetTypeDefinition(handle);
                    var name = metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
                    if (name is "Game.Debug.DebugOverlay" or "Game.Debug.DebugSnapshot" or "Game.Debug.DebugIntent" or "Game.App.DeviceParity" or "Game.App.DevelopmentProfilerTrace" ||
                        IsLongFrameDiagnostic(metadata, handle) ||
                        metadata.GetString(type.Namespace).StartsWith("Game.Editor", StringComparison.Ordinal) ||
                        metadata.GetString(type.Namespace).StartsWith("Game.P1Capture", StringComparison.Ordinal))
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

            VerifyCoreVariant(Path.Combine(directory, "SowSiege.Core.dll"));

            Console.WriteLine("UNITY_RELEASE_BOUNDARY_PASS inspected release assemblies contain gameplay and no developer controls: " + Path.GetFullPath(directory));
            return 0;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or BadImageFormatException)
        {
            Console.Error.WriteLine("UNITY_RELEASE_BOUNDARY_FAIL " + e.Message);
            return 1;
        }
    }

    private static bool IsLongFrameDiagnostic(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        while (!handle.IsNil)
        {
            var type = metadata.GetTypeDefinition(handle);
            var name = metadata.GetString(type.Name);
            if (metadata.GetString(type.Namespace) == "Game.App" && name == "DevelopmentLongFrameRecorder") return true;
            var parent = type.GetDeclaringType();
            if (!parent.IsNil && name is "LongFrameStages" or "LongFrameProvenance")
            {
                var owner = metadata.GetTypeDefinition(parent);
                if (metadata.GetString(owner.Namespace) == "Game.App" && metadata.GetString(owner.Name) == "RunCoordinator") return true;
            }
            handle = parent;
        }
        return false;
    }

    private static void VerifyCoreVariant(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            if (metadata.GetString(type.Namespace) != "SowSiege.Core" || metadata.GetString(type.Name) != "RuntimeDiagnosticsCapabilities") continue;
            foreach (var fieldHandle in type.GetFields())
            {
                var field = metadata.GetFieldDefinition(fieldHandle);
                if (metadata.GetString(field.Name) != "FineStagesEnabled") continue;
                var constantHandle = field.GetDefaultValue();
                if (constantHandle.IsNil) throw new InvalidDataException("Core diagnostics capability is not constant.");
                var constant = metadata.GetConstant(constantHandle);
                var bytes = metadata.GetBlobBytes(constant.Value);
                if (constant.TypeCode != ConstantTypeCode.Boolean || bytes.Length != 1 || bytes[0] != 0)
                    throw new InvalidDataException("Development Core fine diagnostics leaked into release.");
                return;
            }
        }
        throw new InvalidDataException("Release Core diagnostics capability missing.");
    }
}
