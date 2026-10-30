using SowSiege.Core;
using SowSiege.Server.Contracts;

namespace SowSiege.Server.Application;

public sealed record StoredAccount(Guid Id, long Version, string ContentHash, MetaState State, ClockState Clock);
public sealed record AppliedAccount(StoredAccount Account, CommandReply Reply);
public sealed class CommandConflictException(string code) : Exception(code);

public sealed class EstateApplication(EstateContent content)
{
    public StoredAccount Create(long nowMilliseconds) => new(Guid.NewGuid(), 0, content.Hash,
        AccountEstate.NewPrototype(content.Meta, content.Config, 0, 0), new(nowMilliseconds, 0, 0));

    public AppliedAccount Apply(StoredAccount account, EstateCommand? command, long nowMilliseconds)
    {
        if (account.ContentHash != content.Hash || account.State.Estate?.ConfigVersion != content.Config.Version)
        {
            throw new CommandConflictException("content-version-mismatch");
        }
        if (MetaValidation.ValidateState(content.Meta, account.State).Length != 0)
        {
            throw new InvalidDataException("Stored account violates the pinned catalog.");
        }
        if (command is not null && command.ExpectedVersion != account.Version)
        {
            throw new CommandConflictException("version-conflict");
        }
        var clock = EstateClock.Observe(account.Clock, nowMilliseconds, content.Config.MaximumIntervalSeconds);
        long seconds = clock.State.LogicalSeconds;
        var result = command?.Type switch
        {
            null => AccountEstate.Advance(content.Meta, content.Config, account.State, seconds, seconds),
            "start-construction" => AccountEstate.StartConstruction(content.Meta, content.Config, account.State, seconds, seconds, command.BuildingId!.Value),
            "claim-production" => AccountEstate.ClaimProduction(content.Meta, content.Config, account.State, seconds, seconds),
            _ => throw new ArgumentException("Unsupported command.")
        };
        var updated = account with { Version = checked(account.Version + 1), State = result.State, Clock = clock.State };
        return new(updated, new(result.Done, result.Reason, View(updated, result.CreditedSeconds, clock.Reason)));
    }

    public static bool IsValid(EstateCommand command) => command.CommandId != Guid.Empty && command.ExpectedVersion >= 0 &&
        (command.Type == "start-construction" ? command.BuildingId is > 0 : command.Type == "claim-production" && command.BuildingId is null);

    public static EstateView View(StoredAccount account, long creditedSeconds = 0, string clock = "initialized")
    {
        var estate = account.State.Estate ?? throw new InvalidDataException("Missing estate.");
        return new(account.Version, estate.ConfigVersion, account.State.Wallet,
            estate.Buildings.Select(b => new SowSiege.Server.Contracts.BuildingView(b.Id, b.Kind, b.Level, b.X, b.Y, b.Stock)).ToArray(),
            estate.Active is { } work ? new(work.BuildingId, work.TargetLevel, work.RemainingSeconds) : null, creditedSeconds, clock);
    }
}
