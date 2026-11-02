using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SowSiege.Content;
using SowSiege.Core;

namespace SowSiege.Server.Application;

public sealed record EstateContent(MetaCatalog Meta, AccountEstateConfig Config, string Hash)
{
    public static EstateContent Load(string root)
    {
        string data = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
        byte[] metaBytes = File.ReadAllBytes(Path.Combine(data, "meta", "progression.json"));
        byte[] estateBytes = File.ReadAllBytes(AccountEstateConfigReader.PathFor(data, AccountEstateConfigReader.MapPrototypeVersion));
        return Parse(metaBytes, estateBytes);
    }

    public static EstateContent Parse(byte[] metaBytes, byte[] estateBytes)
    {
        var meta = MetaCatalogReader.Parse(metaBytes);
        MetaCatalogValidation.ValidateIntrinsic(meta);
        var config = AccountEstateConfigReader.Parse(estateBytes, meta, AccountEstateConfigReader.MapPrototypeVersion);
        var initial = AccountEstate.NewPrototype(meta, config, 0, 0);
        var errors = MetaValidation.ValidateState(meta, initial);
        if (errors.Length != 0)
        {
            throw new InvalidDataException(string.Join("; ", errors));
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (name, bytes) in new[] { ("progression.json", metaBytes), (config.Version + ".json", estateBytes) })
        {
            hash.AppendData(Encoding.UTF8.GetBytes(name + ":" + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\n"));
            hash.AppendData(bytes);
        }
        return new(meta, config, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }
}
