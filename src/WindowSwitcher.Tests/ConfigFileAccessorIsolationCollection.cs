using Xunit;

namespace WindowSwitcher.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConfigFileAccessorIsolationCollection
{
    public const string Name = "ConfigFileAccessor isolation";
}
