using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace IS2Mod.ControlTypes.Events
{
    public class MouseEventArgs : EventArgs
    {
        public int X { get; }

        public int Y { get; }

        public int DeltaX { get; }

        public int DeltaY { get; }


        public EnumMouseButton Button { get; }

        public int Modifiers { get; }

        public bool Handled { get; set; }
        public MouseEventArgs(int x, int y, int deltaX, int deltaY, EnumMouseButton button, int modifiers)
        {
            X = x;
            Y = y;
            DeltaX = deltaX;
            DeltaY = deltaY;
            Button = button;
            Modifiers = modifiers;
        }

        public MouseEventArgs(int x, int y, int deltaX, int deltaY, EnumMouseButton button)
            : this(x, y, deltaX, deltaY, button, 0)
        {
        }

        public MouseEventArgs(int x, int y, int deltaX, int deltaY)
            : this(x, y, deltaX, deltaY, EnumMouseButton.None, 0)
        {
        }

        public MouseEventArgs(int x, int y, EnumMouseButton button, int modifiers)
            : this(x, y, 0, 0, button, modifiers)
        {
        }

        public MouseEventArgs(int x, int y, EnumMouseButton button)
            : this(x, y, 0, 0, button, 0)
        {
        }

        public MouseEventArgs(int x, int y)
            : this(x, y, 0, 0, EnumMouseButton.None, 0)
        {
        }
    }

}
