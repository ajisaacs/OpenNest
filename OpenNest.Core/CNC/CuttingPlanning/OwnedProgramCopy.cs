using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>Lossless owned copies at the cutting-plan boundary, not a general Clone change.</summary>
internal static class OwnedProgramCopy
{
    private const int Limit = 1000000;
    private const int MaxDepth = 64;

    internal static Program Copy(Program original, CancellationToken token = default)
    {
        Validate(original, token);
        // Built-in Clone preserves mode/rotation and binds calls without rotating setters.
        // Its per-parent maps can duplicate diamonds; restore one global owned graph below.
        var cloned = (Program)original.Clone();
        var programs = new Dictionary<Program, Program>(ReferenceEqualityComparer.Instance);
        var codes = new Dictionary<ICode, ICode>(ReferenceEqualityComparer.Instance);
        var bindings = new Dictionary<Dictionary<string, string>, Dictionary<string, string>>(ReferenceEqualityComparer.Instance);
        return Restore(original, cloned);

        Program Restore(Program source, Program copy)
        {
            token.ThrowIfCancellationRequested();
            if (programs.TryGetValue(source, out var existing)) return existing;
            programs.Add(source, copy);
            for (var i = 0; i < source.Codes.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var authored = source.Codes[i];
                if (codes.TryGetValue(authored, out var owned))
                {
                    copy.Codes[i] = owned;
                    continue;
                }
                owned = copy.Codes[i];
                codes.Add(authored, owned);
                if (authored is Motion motion)
                {
                    var target = (Motion)owned;
                    // All other built-in fields are retained by their Clone implementations.
                    target.UseExactStop = motion.UseExactStop;
                    target.Feedrate = motion.Feedrate;
                    if (motion.VariableRefs != null)
                    {
                        if (!bindings.TryGetValue(motion.VariableRefs, out var refs))
                        {
                            refs = new Dictionary<string, string>(motion.VariableRefs, motion.VariableRefs.Comparer);
                            bindings.Add(motion.VariableRefs, refs);
                        }
                        target.VariableRefs = refs;
                    }
                }
                if (authored is SubProgramCall call)
                {
                    var target = (SubProgramCall)owned;
                    target.BindProgram(Restore(call.Program, target.Program));
                }
            }
            foreach (var (id, child) in source.SubPrograms)
            {
                token.ThrowIfCancellationRequested();
                copy.SubPrograms[id] = Restore(child, copy.SubPrograms[id]);
            }
            return copy;
        }
    }

    // Unlike the execution reader, inactive registered graphs can be motionless or
    // contain suppressed motions. They still must be supported, acyclic and bounded.
    internal static void Validate(Program program, CancellationToken token)
    {
        var active = new HashSet<Program>(ReferenceEqualityComparer.Instance);
        var done = new Dictionary<Program, (int Cost, int Depth)>(ReferenceEqualityComparer.Instance);
        var budget = Limit;
        Visit(program);
        (int Cost, int Depth) Visit(Program current)
        {
            token.ThrowIfCancellationRequested();
            if (current == null || active.Contains(current) || active.Count >= MaxDepth)
                throw new ArgumentException("Missing, recursive or excessively nested clone graph.");
            if (done.TryGetValue(current, out var previous))
            {
                if (active.Count + previous.Depth > MaxDepth)
                    throw new ArgumentException("Excessively nested clone graph.");
                return previous;
            }
            if (current.GetType() != typeof(Program) || !Enum.IsDefined(current.Mode))
                throw new NotSupportedException("Unsupported program runtime type or mode.");
            if (current.Codes == null)
                throw new ArgumentException("Missing clone graph instructions.");
            active.Add(current);
            var children = new HashSet<Program>(ReferenceEqualityComparer.Instance);
            var cost = 1;
            var depth = 1;
            foreach (var code in current.Codes)
            {
                Step();
                cost++;
                if (code == null)
                    throw new ArgumentException("Missing clone graph instruction.");
                var type = code.GetType();
                if (type != typeof(RapidMove) && type != typeof(LinearMove) && type != typeof(ArcMove)
                    && type != typeof(SubProgramCall) && type != typeof(Comment) && type != typeof(Feedrate) && type != typeof(Kerf))
                    throw new NotSupportedException("Unsupported instruction runtime type.");
                if (code is SubProgramCall call) Child(call.Program);
            }
            foreach (var child in current.SubPrograms.Values)
            {
                Step();
                Child(child);
            }
            if (cost > Limit)
                throw new ArgumentException("Clone graph exceeds the verification limit.");
            active.Remove(current);
            var result = (cost, depth);
            done.Add(current, result);
            return result;

            void Child(Program child)
            {
                var summary = Visit(child);
                // Program.Clone shares children only within one parent; bound its real
                // expanded work as well as the distinct original graph before calling it.
                if (!children.Add(child)) return;
                if (summary.Cost > Limit - cost)
                    throw new ArgumentException("Clone expansion exceeds the verification limit.");
                cost += summary.Cost;
                depth = System.Math.Max(depth, summary.Depth + 1);
            }
        }

        void Step()
        {
            token.ThrowIfCancellationRequested();
            if (--budget < 0)
                throw new ArgumentException("Clone graph exceeds the verification limit.");
        }
    }
}
