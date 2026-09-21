using System;

namespace OpenNest.Engine.Jobs;

/// <summary>Display metadata plus a fresh-instance factory for one registered whole-job engine.</summary>
public class NestingEngineInfo
{
    public NestingEngineInfo(string name, string description, Func<INestingEngine> factory)
    {
        Name = name;
        Description = description;
        Factory = factory;
    }

    public string Name { get; }
    public string Description { get; }
    public Func<INestingEngine> Factory { get; }
}
