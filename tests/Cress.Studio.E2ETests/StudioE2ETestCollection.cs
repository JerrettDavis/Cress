using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Cress.Studio.E2ETests;

[CollectionDefinition("Studio E2E", DisableParallelization = true)]
public sealed class StudioE2ETestCollection
{
}
