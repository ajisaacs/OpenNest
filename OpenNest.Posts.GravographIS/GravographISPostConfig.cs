using System.ComponentModel;
using OpenNest.CNC;

namespace OpenNest.Posts.GravographIS
{
    /// <summary>
    /// Cut parameters for one kind of pass (engrave or cut). Edited in the post
    /// configuration PropertyGrid and persisted to JSON.
    /// </summary>
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public sealed class LayerCutConfig
    {
        [DisplayName("Feed (mm/sec)")]
        [Description("XY and Z feed for this pass. Patches the VS and VZ wire commands.")]
        public int FeedMmPerSec { get; set; } = 10;

        [DisplayName("Depth (inches)")]
        [Description("Programmed Z plunge (DZ). Note: the spring-floated spindle means this does not set actual cut depth — tool protrusion does.")]
        public double Depth { get; set; } = 0.25;

        [DisplayName("Pause Before")]
        [Description("Stop the spindle and prompt the operator before this pass begins, so the tool can be swapped/adjusted.")]
        public bool PauseBefore { get; set; }

        [DisplayName("Pause Message")]
        [Description("Message shown on the controller during the pause.")]
        public string PauseMessage { get; set; } = "";

        public override string ToString() => $"{FeedMmPerSec} mm/s, {Depth:0.###}\"" + (PauseBefore ? ", pause" : "");
    }

    /// <summary>
    /// Configuration for the Gravograph IS post processor: one <see cref="LayerCutConfig"/>
    /// per cut kind. The engrave block applies to <see cref="LayerType.Scribe"/> paths,
    /// the cut block to <see cref="LayerType.Cut"/>/<see cref="LayerType.Leadin"/>/<see cref="LayerType.Leadout"/>.
    /// The cut block carries the tool-change pause by default.
    /// </summary>
    public sealed class GravographISPostConfig
    {
        [Category("Engrave (Scribe)")]
        [DisplayName("Engrave")]
        [Description("Parameters for engrave/scribe geometry (text).")]
        public LayerCutConfig Engrave { get; set; } = new LayerCutConfig
        {
            FeedMmPerSec = 10,
            Depth = 0.25,
            PauseBefore = false,
            PauseMessage = "",
        };

        [Category("Cut")]
        [DisplayName("Cut")]
        [Description("Parameters for cut geometry (outlines). Pauses for a tool change by default.")]
        public LayerCutConfig Cut { get; set; } = new LayerCutConfig
        {
            FeedMmPerSec = 3,
            Depth = 0.25,
            PauseBefore = true,
            PauseMessage = "Change tool",
        };

        /// <summary>
        /// Returns the cut config a polyline of the given layer should use, or null
        /// if the layer is non-cutting (<see cref="LayerType.Display"/>) and should be skipped.
        /// </summary>
        public LayerCutConfig ConfigFor(LayerType layer)
        {
            switch (layer)
            {
                case LayerType.Scribe:
                    return Engrave;
                case LayerType.Cut:
                case LayerType.Leadin:
                case LayerType.Leadout:
                    return Cut;
                default:
                    return null;
            }
        }
    }
}
