using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IS2Mod.Enums
{
    /// <summary>
    /// Reads two different ways depending on which property it is assigned to, and the two are
    /// at right angles to each other - which is the whole idea.
    ///
    /// As <see cref="IS2Mod.ControlTypes.UIControl.InsideOrientation"/> it is the direction a
    /// container stacks its children in: <see cref="Top"/> downwards, <see cref="Left"/>
    /// sideways, <see cref="None"/> all on the same spot.
    ///
    /// As <see cref="IS2Mod.ControlTypes.UIControl.Orientation"/> it is where the control sits
    /// *across* that direction. In a column that means Left, Right, Center or Fill; in a row it
    /// means Top, Bottom, Center or Fill. A value that names the stacking direction itself says
    /// nothing - the order of the children decides that - and is treated as Fill.
    /// </summary>
    public enum Orientation
    {
        Top,
        Bottom,
        Left,
        Right,

        /// <summary>
        /// Stretch across the container. The default for a control's own orientation, and what
        /// every control did before there was a choice.
        /// </summary>
        Fill,
        None,

        /// <summary>Centred across the container, at the control's natural size.</summary>
        Center
    }
}
