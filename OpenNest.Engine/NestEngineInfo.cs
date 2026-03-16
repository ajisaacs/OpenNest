using System;

namespace OpenNest
{
    public class NestEngineInfo
    {
        public NestEngineInfo(string name, string description, Func<Plate, NestEngineBase> factory)
        {
            Name = name;
            Description = description;
            Factory = factory;
        }

        public string Name { get; }
        public string Description { get; }
        public Func<Plate, NestEngineBase> Factory { get; }
    }
}
