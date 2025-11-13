using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IS2Mod.ControlTypes.Events
{
    public class MouseWheelEventArgs : EventArgs
    {
        //
        // Zusammenfassung:
        //     The rough change in time since last called.
        public int delta;

        //
        // Zusammenfassung:
        //     The precise change in time since last called.
        public float deltaPrecise;

        //
        // Zusammenfassung:
        //     The rough change in value.
        public int value;

        //
        // Zusammenfassung:
        //     The precise change in value.
        public float valuePrecise;

        //
        // Zusammenfassung:
        //     Is the current event being handled?
        public bool IsHandled { get; private set; }

        //
        // Zusammenfassung:
        //     Changes or sets the current handled state.
        //
        // Parameter:
        //   value:
        //     Should the event be handled?
        public void SetHandled(bool value = true)
        {
            IsHandled = value;
        }

        public MouseWheelEventArgs(Vintagestory.API.Client.MouseWheelEventArgs vsArgs)
        {
            delta = vsArgs.delta;
            deltaPrecise = vsArgs.deltaPrecise;
            valuePrecise = vsArgs.valuePrecise;
            IsHandled = vsArgs.IsHandled;
            value = vsArgs.value;

        }
    }
}
