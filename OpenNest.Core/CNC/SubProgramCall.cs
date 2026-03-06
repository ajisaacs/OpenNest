using OpenNest.Math;

namespace OpenNest.CNC
{
    public class SubProgramCall : ICode
    {
        private double rotation;
        private Program program;

        public SubProgramCall()
        {
        }

        public SubProgramCall(Program program, double rotation)
        {
            this.program = program;
            this.Rotation = rotation;
        }

        /// <summary>
        /// The program ID.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the program.
        /// </summary>
        public Program Program
        {
            get { return program; }
            set
            {
                program = value;
                UpdateProgramRotation();
            }
        }

        /// <summary>
        /// Gets or sets the rotation of the program in degrees.
        /// </summary>
        public double Rotation
        {
            get { return rotation; }
            set
            {
                rotation = value;
                UpdateProgramRotation();
            }
        }

        /// <summary>
        /// Rotates the program by the difference of the current
        /// rotation set in the sub program call and the program.
        /// </summary>
        private void UpdateProgramRotation()
        {
            if (program != null)
            {
                var diffAngle = Angle.ToRadians(rotation) - program.Rotation;

                if (!diffAngle.IsEqualTo(0.0))
                    program.Rotate(diffAngle);
            }
        }

        /// <summary>
        /// Gets the code type.
        /// </summary>
        /// <returns></returns>
        public CodeType Type
        {
            get { return CodeType.SubProgramCall; }
        }

        /// <summary>
        /// Gets a shallow copy.
        /// </summary>
        /// <returns></returns>
        public ICode Clone()
        {
            return new SubProgramCall(program, Rotation);
        }

        public override string ToString()
        {
            return string.Format("G65 P{0} R{1}", Id, Rotation);
        }
    }
}
