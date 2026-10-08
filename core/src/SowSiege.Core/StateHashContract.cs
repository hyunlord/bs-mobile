using System;

namespace SowSiege.Core
{
    public interface IStateHasher
    {
        string Compute(object snapshot);
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class OmitWhenNullAttribute : Attribute
    {
    }
}
