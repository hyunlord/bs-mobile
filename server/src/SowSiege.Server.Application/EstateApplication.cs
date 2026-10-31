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

    /// <param name="effects">ADR0079 estate effect timeline (fromUtc, bonuses) in time order; null keeps the historical path.</param>
    public AppliedAccount Apply(StoredAccount account, EstateCommand? command, long nowMilliseconds, (long FromUtc, AccountEstateBonuses Bonuses)[]? effects = null)
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
        var state = account.State;
        long credited = 0;
        AccountEstateBonuses? bonuses = null;
        if (effects is { Length: > 0 })
        {
            // The clock is observed once, so the offline cap applies once; effect changes map onto that credited span.
            var previous = account.Clock;
            AccountEstateBonuses At(long utc) => effects.LastOrDefault(e => e.FromUtc <= utc).Bonuses ?? AccountEstateBonuses.None;
            bonuses = At(previous.HighWatermarkMilliseconds);
            long reached = previous.LogicalSeconds;
            foreach (var change in effects.Where(e => e.FromUtc > previous.HighWatermarkMilliseconds && e.FromUtc < nowMilliseconds))
            {
                long boundary = checked(previous.LogicalSeconds + Math.Min(clock.CreditedSeconds,
                    (change.FromUtc - previous.HighWatermarkMilliseconds + previous.RemainderMilliseconds) / 1000));
                if (boundary > reached)
                {
                    var segment = AccountEstate.Advance(content.Meta, content.Config, state, boundary, boundary, bonuses);
                    state = segment.State;
                    credited += segment.CreditedSeconds;
                    reached = boundary;
                }
                bonuses = change.Bonuses;
            }
        }
        var result = command?.Type switch
        {
            null => AccountEstate.Advance(content.Meta, content.Config, state, seconds, seconds, bonuses),
            "start-construction" => AccountEstate.StartConstruction(content.Meta, content.Config, state, seconds, seconds, command.BuildingId!.Value, bonuses),
            "claim-production" => AccountEstate.ClaimProduction(content.Meta, content.Config, state, seconds, seconds, bonuses),
            _ => throw new ArgumentException("Unsupported command.")
        };
        var updated = account with { Version = checked(account.Version + 1), State = result.State, Clock = clock.State };
        return new(updated, new(result.Done, result.Reason, View(updated, checked(credited + result.CreditedSeconds), clock.Reason)));
    }

    /// <summary>Effective wallet cap: the smaller of the r8 config cap and the material catalog cap.</summary>
    public int WalletCap(string material) => Math.Min(content.Config.WalletCaps.TryGetValue(material, out int cap) ? cap : 0,
        content.Meta.Materials.Single(m => m.Id == material).WalletCap);

    public void Validate(MetaState state)
    {
        if (MetaValidation.ValidateState(content.Meta, state).Length != 0 ||
            content.Config.WalletCaps.Any(cap => state.Wallet.GetValueOrDefault(cap.Key) > cap.Value))
        {
            throw new InvalidDataException("Domain result violates the pinned catalog.");
        }
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
