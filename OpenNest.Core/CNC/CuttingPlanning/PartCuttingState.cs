using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>Every Part field a cutting commit can change, held by reference for exact rollback.</summary>
internal sealed record PartCuttingState(Program Program, bool OwnsProgram, double PreLeadInRotation,
    bool HasManualLeadIns, bool LeadInsLocked, CuttingParameters CuttingParameters, Vector Location,
    Box BoundingBox);
