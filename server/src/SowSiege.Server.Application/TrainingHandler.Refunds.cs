using SowSiege.Core;
using SowSiege.Server.Contracts;

namespace SowSiege.Server.Application;

public sealed partial class TrainingHandler
{
    private int WalletCap(string material) => Math.Min(estate.Config.WalletCaps[material],
        estate.Meta.Materials.Single(m => m.Id == material).WalletCap);

    // Refunds go to the shared writer (#599): it fills the wallet up to its cap and holds the rest, oldest first.
    private static DomainCredit[] Credits(Dictionary<string, int> materials) => materials.Where(m => m.Value > 0)
        .OrderBy(m => m.Key, StringComparer.Ordinal).Select(m => new DomainCredit(m.Key, m.Value)).ToArray();

    // Development saves before r6 kept refunds in this domain; hand them to the shared held queue once.
    private static (TrainingState State, DomainCredit[] Credits) MigrateLegacyRefunds(TrainingState state, List<DomainEvent> events)
    {
        if (state.PendingRefunds.Count == 0) { return (state, []); }
        events.Add(new("training-refunds-moved-to-held", Encode(new { materials = Materials(state.PendingRefunds) })));
        return (state with { PendingRefunds = new(StringComparer.Ordinal) }, Credits(state.PendingRefunds));
    }

    private void ValidateLegacyRefunds(TrainingState state, MetaState account)
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

    // Held material of a kind only remains while that wallet is full, so cap - wallet - held is the room the next credit gets.
    private TrainingRefundPreview[] RefundPreview(DomainContext context, TrainingState state, MetaState account) => state.Waiting is null ? [] :
        state.Waiting.ChargedCost.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c =>
        {
            int held = context.Held?.GetValueOrDefault(c.Key) ?? 0;
            int room = Math.Max(0, WalletCap(c.Key) - account.Wallet[c.Key] - held);
            int immediate = Math.Min(room, c.Value);
            return new TrainingRefundPreview(c.Key, immediate, c.Value - immediate);
        }).ToArray();
}
