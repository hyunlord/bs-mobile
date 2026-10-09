using SowSiege.Core;

namespace SowSiege.Sim;

public static class MetaReplayFile
{
    public static ReplayVerification Verify(string replayPath, string dataRoot, ContentCatalog source, string dataHash, ReplayDocument replay)
    {
        string contextPath = replayPath + ".meta";
        if (!File.Exists(contextPath))
        {
            return ReplayRunner.Verify(source, dataHash, replay);
        }

        if (new FileInfo(contextPath).Length > 1049000)
        {
            throw new InvalidDataException("Meta replay context exceeds size limit.");
        }

        return MetaReplayContext.Verify(File.ReadAllBytes(contextPath), source, MetaContentLoader.Load(dataRoot), dataHash, replay);
    }
}
