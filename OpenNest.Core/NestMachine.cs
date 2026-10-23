using System;

namespace OpenNest
{
    /// <summary>
    /// The machine a nest is cut on: the id of its machine configuration plus the name it had,
    /// so a nest still says which machine it was made for on a PC without that configuration.
    /// </summary>
    public sealed class NestMachine
    {
        public NestMachine(Guid id, string name)
        {
            Id = id;
            Name = name ?? string.Empty;
        }

        public Guid Id { get; }

        public string Name { get; }
    }
}
