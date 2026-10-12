using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// Deliberate violations for the architecture tests. "Alphabet" exists to catch a prefix bug: it must not count as part of "Alpha".
namespace ExtraSpace.Api.Tests.ArchFixtures.Features.Beta.Contracts
{
    public interface IBetaService
    {
        int Count();
    }
}

namespace ExtraSpace.Api.Tests.ArchFixtures.Features.Beta
{
    internal sealed class BetaEntity
    {
        public int Id { get; set; }
    }

    internal sealed class BetaService : Contracts.IBetaService
    {
        public int Count() => 0;
    }

    internal sealed class BetaController(ArchFixtures.Platform.FixtureDbContext db) : ControllerBase
    {
        internal int Rows() => db.ChangeTracker.Entries().Count();
    }

    internal sealed class BetaAsyncController : ControllerBase
    {
        internal static Task<int> Rows() => Task.Run(async () =>
        {
            await Task.Yield();
            using var db = new ArchFixtures.Platform.FixtureDbContext();
            return db.ChangeTracker.Entries().Count();
        });
    }

    internal static class BetaHost
    {
        internal sealed class NestedController(ArchFixtures.Platform.FixtureDbContext db) : ControllerBase
        {
            internal int Rows() => db.ChangeTracker.Entries().Count();
        }
    }
}

namespace ExtraSpace.Api.Tests.ArchFixtures.Features.Alpha
{
    internal sealed class AlphaInternal
    {
        public int Value { get; set; }
    }

    internal sealed class AlphaGood(Beta.Contracts.IBetaService beta)
    {
        public int Use() => beta.Count();
    }

    internal sealed class AlphaBad
    {
        public object Peek() => new Beta.BetaEntity();
    }

    internal sealed class AlphaBadAsync
    {
        public async Task<object> Peek() => await Task.Run<object>(async () =>
        {
            await Task.Yield();
            return new Beta.BetaEntity();
        });
    }

    internal sealed class AlphaBadSet(ArchFixtures.Platform.FixtureDbContext db)
    {
        public object Peek() => db.Set<Beta.BetaEntity>();
    }

    file sealed class AlphaBadFile
    {
        public object Peek() => new Beta.BetaEntity();
    }
}

namespace ExtraSpace.Api.Tests.ArchFixtures.Features.Alphabet
{
    internal sealed class AlphabetBad
    {
        public object Peek() => new Alpha.AlphaInternal();
    }
}

namespace ExtraSpace.Api.Tests.ArchFixtures.Platform
{
    internal sealed class FixtureDbContext : DbContext;

    internal sealed class PlatformBad
    {
        public object Peek() => new Features.Beta.BetaEntity();
    }

    internal sealed class PlatformBadAsync
    {
        public Task<object> Peek() => Task.Run<object>(async () =>
        {
            await Task.Yield();
            return new Features.Beta.BetaEntity();
        });
    }

    file sealed class PlatformBadFile
    {
        public object Peek() => new Features.Beta.BetaEntity();
    }
}

namespace ExtraSpace.Api.Tests.ArchFixtures.Integrations
{
    internal sealed class IntegrationsBad
    {
        public object Peek() => new Features.Beta.BetaService();
    }
}
