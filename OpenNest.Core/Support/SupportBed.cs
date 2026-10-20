using System;
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Support
{
    /// <summary>
    /// What a cut part rests on: the machine's support bed, fixed to the machine and measured
    /// from the plate origin into the sheet, so its position on any sheet follows from the
    /// sheet's quadrant alone. A layout only reports where it touches material; the
    /// <see cref="SupportAnalyzer"/> decides from those contacts whether a part stays level.
    /// The laser grid (<see cref="SupportGrid"/>) is the built-in layout; straight or curved
    /// slats imported from a CAD layout can follow as further subclasses.
    /// </summary>
    public abstract class SupportBed : IEquatable<SupportBed>
    {
        protected SupportBed(double registrationTolerance, double stabilityMargin)
        {
            if (!double.IsFinite(registrationTolerance) || registrationTolerance < 0)
                throw new ArgumentOutOfRangeException(nameof(registrationTolerance),
                    "Registration tolerance must be finite and zero or more.");
            if (!double.IsFinite(stabilityMargin) || stabilityMargin < 0)
                throw new ArgumentOutOfRangeException(nameof(stabilityMargin),
                    "Stability margin must be finite and zero or more.");
            RegistrationTolerance = registrationTolerance;
            StabilityMargin = stabilityMargin;
        }

        /// <summary>
        /// How far a sheet may sit from its nominal place on the bed, along each axis. The
        /// analyzer checks the bed shifted by up to this much and keeps the worst result.
        /// </summary>
        public double RegistrationTolerance { get; }

        /// <summary>
        /// How far inside its contacts a part's center of gravity must lie for the part to count
        /// as supported; it allows for slag, gas pressure and a contact that is worn low.
        /// </summary>
        public double StabilityMargin { get; }

        /// <summary>
        /// The contacts under <paramref name="material"/> (world coordinates) on a sheet in
        /// <paramref name="quadrant"/>, with the bed moved by <paramref name="shift"/>. With
        /// <paramref name="extremesOnly"/>, a layout may omit contacts that cannot be corners of
        /// their convex hull; the analyzer needs no others.
        /// </summary>
        public abstract IReadOnlyList<Vector> Contacts(SupportMaterial material, int quadrant, Vector shift,
            bool extremesOnly = false);

        /// <summary>This layout with every length multiplied by <paramref name="factor"/> (a unit change).</summary>
        public abstract SupportBed Scaled(double factor);

        public abstract bool Equals(SupportBed other);

        public override bool Equals(object obj) => obj is SupportBed other && Equals(other);

        public abstract override int GetHashCode();

        /// <summary>-1 where a sheet in <paramref name="quadrant"/> lies on the negative side of the origin along X.</summary>
        protected static double SignX(int quadrant) => quadrant is 2 or 3 ? -1 : 1;

        /// <summary>-1 where a sheet in <paramref name="quadrant"/> lies on the negative side of the origin along Y.</summary>
        protected static double SignY(int quadrant) => quadrant is 3 or 4 ? -1 : 1;
    }
}
