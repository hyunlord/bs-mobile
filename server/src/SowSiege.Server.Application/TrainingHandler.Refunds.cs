using SowSiege.Core;
using SowSiege.Server.Contracts;

namespace SowSiege.Server.Application;

public sealed partial class TrainingHandler
{
    public DomainResult AfterCommand(DomainContext context)
    {
        var events = new List<DomainEvent>();
        var (state, account) = FlushRefunds(Read(context), context.Account.State, events);
        return Result(context, state, account, true, "refunds-settled", events.ToArray());
    }

    private int WalletCap(string material) => Math.Min(estate.Config.WalletCaps[material],
        estate.Meta.Materials.Single(m => m.Id == material).WalletCap);

    private (TrainingState State, MetaState Account) FlushRefunds(TrainingState state, MetaState account, List<DomainEvent> events)
    {
        var pending = new Dictionary<string, int>(state.PendingRefunds, StringComparer.Ordinal);
        var wallet = new Dictionary<string, int>(account.Wallet);
        foreach (var material in pending.Keys.Order(StringComparer.Ordinal).ToArray())
        {
            int amount = Math.Min(pending[material], Math.Max(0, WalletCap(material) - wallet[material]));
            if (amount == 0) { continue; }
            wallet[material] = checked(wallet[material] + amount);
            pending[material] -= amount;
            if (pending[material] == 0) { pending.Remove(material); }
            events.Add(new("training-refund-credited", Encode(new { materialId = material, amount })));
        }
        return (state with { PendingRefunds = pending }, account with { Wallet = wallet });
    }

    private void ValidatePending(TrainingState state, MetaState account)
    {
        var level = Level(account);
        if (state.PendingRefunds is null) { throw new InvalidDataException("Missing pending training refund balances."); }
        foreach (var material in state.PendingRefunds)
        {
            int cap = content.Rules.Units.Where(u => u.RequiredBarracksLevel <= level.Level)
                .Select(u => checked(u.CostPerUnit.GetValueOrDefault(material.Key) * level.BatchLimit)).DefaultIfEmpty(0).Max();
            if (!estate.Config.WalletCaps.ContainsKey(material.Key) || material.Value <= 0 || material.Value > cap)
            {
                throw new InvalidDataException("Pending training refund exceeds one waiting batch's material bound.");
            }
        }
    }

    private TrainingRefundPreview[] RefundPreview(TrainingState state, MetaState account) => state.Waiting is null ? [] :
        state.Waiting.ChargedCost.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c =>
        {
            int room = Math.Max(0, WalletCap(c.Key) - account.Wallet[c.Key] - state.PendingRefunds.GetValueOrDefault(c.Key));
            int immediate = Math.Min(room, c.Value);
            return new TrainingRefundPreview(c.Key, immediate, c.Value - immediate);
        }).ToArray();
}
