using Game.App;
using Xunit;

namespace SowSiege.Tests;

public sealed class AtomicSaveStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "atomic-save-" + Guid.NewGuid().ToString("N"));
    private string Primary => Path.Combine(directory, "progress.json");
    private AtomicSaveStore Store => new(directory, "progress.json", 16);
    private static AtomicSaveValidation Valid(byte[] payload) => payload.Length >= 2
        ? payload[0] == 1 ? AtomicSaveValidation.Valid : AtomicSaveValidation.UnsupportedVersion
        : AtomicSaveValidation.Corrupt;

    [Fact]
    public void MissingSaveDoesNotCreateFiles()
    {
        var result = Store.Load(Valid);
        Assert.Equal(AtomicSaveLoadStatus.Missing, result.Status);
        Assert.Null(result.Payload);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void RoundtripAndReplacementRetainPreviousValidatedGeneration()
    {
        Store.Save([1, 2], Valid);
        Store.Save([1, 3], Valid);
        var result = Store.Load(Valid);
        Assert.Equal(AtomicSaveLoadStatus.Primary, result.Status);
        Assert.Equal(new byte[] { 1, 3 }, result.Payload);
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Primary + ".bak"));
        Assert.False(File.Exists(Primary + ".tmp"));
    }

    [Fact]
    public void CorruptPrimaryRecoversBackupWithoutOverwritingEvidence()
    {
        Store.Save([1, 2], Valid);
        Store.Save([1, 3], Valid);
        File.WriteAllBytes(Primary, [9]);
        var result = Store.Load(Valid);
        Assert.Equal(AtomicSaveLoadStatus.RecoveredBackup, result.Status);
        Assert.Equal(new byte[] { 1, 2 }, result.Payload);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Primary));
        Store.Save([1, 4], Valid);
        Assert.Equal(new byte[] { 1, 4 }, Store.Load(Valid).Payload);
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Primary + ".bak"));
    }

    [Fact]
    public void FailedTemporaryWriteLeavesCommittedSaveIntact()
    {
        Store.Save([1, 2], Valid);
        Directory.CreateDirectory(Primary + ".tmp");
        var error = Record.Exception(() => Store.Save([1, 3], Valid));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(new byte[] { 1, 2 }, Store.Load(Valid).Payload);
    }

    [Fact]
    public void InvalidOrOversizedPayloadCannotReplaceGoodSave()
    {
        Store.Save([1, 2], Valid);
        Assert.Throws<InvalidDataException>(() => Store.Save([2, 3], Valid));
        Assert.Throws<InvalidDataException>(() => Store.Save(new byte[17], _ => AtomicSaveValidation.Valid));
        Assert.Throws<InvalidDataException>(() => Store.Save([], _ => AtomicSaveValidation.Valid));
        Assert.Equal(new byte[] { 1, 2 }, Store.Load(Valid).Payload);
    }

    [Fact]
    public void OrphanTemporaryFileIsIgnoredEvenWithoutCommittedSave()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Primary + ".tmp", [1, 9]);
        Assert.Equal(AtomicSaveLoadStatus.Missing, Store.Load(Valid).Status);
        Store.Save([1, 2], Valid);
        Assert.Equal(new byte[] { 1, 2 }, Store.Load(Valid).Payload);
    }

    [Fact]
    public void UnknownFutureVersionCannotBecomeBlankSave()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Primary, [2, 9]);
        Assert.Throws<InvalidDataException>(() => Store.Load(Valid));
        Assert.Throws<InvalidDataException>(() => Store.Save([1, 0], Valid));
        Assert.Equal(new byte[] { 2, 9 }, File.ReadAllBytes(Primary));
    }

    [Fact]
    public void FutureVersionDoesNotFallBackToOlderBackup()
    {
        Store.Save([1, 2], Valid);
        Store.Save([1, 3], Valid);
        File.WriteAllBytes(Primary, [2, 9]);
        Assert.Throws<InvalidDataException>(() => Store.Load(Valid));
        Assert.Throws<InvalidDataException>(() => Store.Save([1, 4], Valid));
        Assert.Equal(new byte[] { 2, 9 }, File.ReadAllBytes(Primary));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Primary + ".bak"));
    }

    [Fact]
    public void OversizedPrimaryCanRecoverBackupWithoutLoadingUnboundedBytes()
    {
        Store.Save([1, 2], Valid);
        Store.Save([1, 3], Valid);
        File.WriteAllBytes(Primary, new byte[17]);
        Assert.Equal(AtomicSaveLoadStatus.RecoveredBackup, Store.Load(Valid).Status);
    }

    [Theory]
    [InlineData("../progress.json")]
    [InlineData("..")]
    [InlineData("/progress.json")]
    [InlineData("save\\progress.json")]
    [InlineData("save:progress.json")]
    public void UnsafeNamesAreRejected(string name) => Assert.Throws<ArgumentException>(() => new AtomicSaveStore(directory, name));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
