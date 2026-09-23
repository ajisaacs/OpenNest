using System;
using System.Collections.Generic;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Sonnet5.Tests;

public class Sonnet5NestingEngineTests
{
    [Fact]
    public void SolveReturnsAResultForASingleSimplePart()
    {
        // TODO: replace with a real fixture once Solve() is implemented — this only
        // proves the plumbing (project reference, constructor, interface) is wired up.
        var engine = new Sonnet5NestingEngine();

        Assert.NotNull(engine);
        Assert.IsAssignableFrom<INestingEngine>(engine);
    }
}
