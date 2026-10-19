using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using ModelContextProtocol.Server;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.IO;

namespace OpenNest.Mcp.Tools
{
    /// <summary>
    /// Cut planning in two steps, like the desktop panel: plan_cutting reports a proposal and
    /// changes nothing; apply_cutting_plan installs that exact proposal, all plates or none.
    /// </summary>
    [McpServerToolType]
    public class CuttingTools
    {
        private readonly NestSession _session;

        public CuttingTools(NestSession session)
        {
            _session = session;
        }

        [McpServerTool(Name = "plan_cutting")]
        [Description(
            "Plan cutting for one plate or every occupied plate: whole-part order, contour order, entries and "
                + "lead-ins regenerated from confirmed cutting settings. Locked parts keep their programs. Nothing "
                + "changes until apply_cutting_plan. Settings come from cuttingParametersPath, else from the plates' "
                + "own saved settings, which must agree; defaults are never assumed. Part numbers in the report are "
                + "1-based positions in the plate's part list."
        )]
        public string PlanCutting(
            [Description("Index of the plate to plan, or -1 for every occupied plate")] int plateIndex = -1,
            [Description("Keep the current whole-part order (contour order and entries are still planned)")]
                bool keepPartOrder = false,
            [Description("Absolute path to a cutting settings JSON file (the desktop's cutting settings format); omit to use the plates' saved settings")]
                string cuttingParametersPath = null,
            CancellationToken cancellationToken = default
        )
        {
            // A new attempt replaces the previous proposal, whatever its outcome.
            _session.CuttingProposal = null;

            var all = _session.AllPlates();
            IEnumerable<Plate> candidates;
            if (plateIndex == -1)
                candidates = all;
            else
            {
                var plate = _session.GetPlate(plateIndex);
                if (plate == null)
                    return $"Error: plate {plateIndex} not found";
                candidates = new[] { plate };
            }

            var scope = candidates.Where(p => p.Parts.Count > 0).ToArray();
            if (scope.Length == 0)
                return plateIndex == -1
                    ? "Error: no occupied plates to plan cutting for"
                    : $"Error: plate {plateIndex} has no parts to plan cutting for";

            CuttingParameters parameters;
            if (cuttingParametersPath != null)
            {
                try
                {
                    parameters = CuttingParametersSerializer.DeserializeExplicit(File.ReadAllText(cuttingParametersPath));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                    or NotSupportedException or ArgumentException)
                {
                    return $"Error: cannot read cutting settings '{cuttingParametersPath}': {ex.Message}";
                }
            }
            else
            {
                parameters = SavedCuttingParameters(scope);
                if (parameters == null)
                    return "Error: no confirmed cutting settings. Pass cuttingParametersPath, or plan plates that all "
                        + "carry the same saved settings. Nothing changed.";
            }

            var batch = CuttingPlanBatch.Capture(scope, parameters, keepPartOrder,
                scope.Select(p => all.FindIndex(candidate => ReferenceEquals(candidate, p))).ToArray(),
                cancellationToken);
            var proposal = batch.Plan(token: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var sb = new StringBuilder();
            sb.AppendLine($"Cutting settings: {cuttingParametersPath ?? "saved with the plates"}");
            foreach (var line in proposal.Describe(UnitsHelper.GetShortString(_session.Nest?.Units ?? default),
                "Pass cuttingParametersPath settings that select"))
                sb.AppendLine(line);
            sb.AppendLine();
            if (proposal.CanApply)
                sb.AppendLine("Nothing has changed yet. Call apply_cutting_plan to apply this plan.");
            else if (proposal.RequiresWarningAcceptance)
                sb.AppendLine("Nothing has changed yet. Review the warnings; to apply this unverified plan, call "
                    + "apply_cutting_plan with acceptWarnings=true.");
            else
                sb.AppendLine("Nothing can be applied. Resolve the findings and call plan_cutting again.");

            if (proposal.CanApplyWithWarnings)
                _session.CuttingProposal = proposal;
            return sb.ToString();
        }

        [McpServerTool(Name = "apply_cutting_plan")]
        [Description(
            "Apply the last plan_cutting proposal to every plate in it, or to none. Refused if any planned plate "
                + "changed since planning; plan again then. Accepting warnings applies only this proposal and is not "
                + "consent to post CNC output."
        )]
        public string ApplyCuttingPlan(
            [Description("Explicitly accept this unverified plan's warnings and apply it anyway")] bool acceptWarnings = false,
            CancellationToken cancellationToken = default
        )
        {
            var proposal = _session.CuttingProposal;
            if (proposal == null)
                return "Error: no cutting plan to apply. Call plan_cutting first.";

            var all = _session.AllPlates();
            if (proposal.Plates.Any(planned => !all.Any(p => ReferenceEquals(p, planned.Plate))))
            {
                _session.CuttingProposal = null;
                return "Error: a planned plate is no longer in the session. Call plan_cutting again. Nothing changed.";
            }

            if (!proposal.CanApply && !acceptWarnings)
                return "Error: this plan is unverified. Review its warnings, then call apply_cutting_plan with "
                    + "acceptWarnings=true to apply it. Nothing changed.";

            var commit = proposal.Apply(acceptWarnings, cancellationToken);
            if (commit.Status == CuttingCommitStatus.Cancelled)
                return "Apply cancelled. Nothing changed.";

            // Applied or refused, a proposal is used at most once.
            _session.CuttingProposal = null;
            if (commit.Status != CuttingCommitStatus.Applied)
                return commit.Status == CuttingCommitStatus.Stale
                    ? "Error: a plate changed since planning. Call plan_cutting again. Nothing changed."
                    : $"Error: cutting plan not applied ({commit.Status}): {commit.Message} Nothing changed.";

            var plates = string.Join(", ", proposal.Plates.Select(p => p.PlateNumber));
            return $"Applied cutting plan to plate(s) {plates}: "
                + $"{proposal.Plates.Sum(p => p.RegeneratedCount)} part program(s) regenerated, "
                + $"{proposal.Plates.Count(p => p.OrderChanged)} plate(s) with a new part order. "
                + "Save with save_nest to keep it.";
        }

        /// <summary>The plates' saved settings when every plate carries the same ones; otherwise null.</summary>
        private static CuttingParameters SavedCuttingParameters(IReadOnlyList<Plate> plates)
        {
            var first = plates[0].CuttingParameters;
            if (first == null)
                return null;
            var json = CuttingParametersSerializer.Serialize(first);
            return plates.All(p => p.CuttingParameters != null
                && CuttingParametersSerializer.Serialize(p.CuttingParameters) == json) ? first : null;
        }
    }
}
