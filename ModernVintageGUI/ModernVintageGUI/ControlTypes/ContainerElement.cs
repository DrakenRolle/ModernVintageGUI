using Cairo;
using IS2Mod.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes
{
    public class ContainerElement : UIControl
    {
        public ContainerElement(string _Name = "", PointD? _Size = null, Orientation _Orientation = Orientation.Top, double _Margin = 0, double _Padding = 0, int _Index = 0, ObservableCollection<UIControl> _Children = null) : base(_Name, _Size, _Orientation, _Margin, _Padding, _Index, _Children)
        {
            this.onMouseDown += ContainerElement_onMouseDown;
        }

        private void ContainerElement_onMouseDown(object? sender, EventArgs e)
        {
            
        }
        /// <summary>
        /// This becomes relevant when implementing the EventChain for Childcontrols, so we can redirect the event to the actual Children there the stuff happens.
        /// </summary>
        /// <returns></returns>
        //public UIControl bestFittingChildrenToPos()
        //{

        //}

        public override PointD CalculateSize()
        {            
            return base.CalculateSize();
        }

        public override GuiElement Compose()
        {
            return new ContainerElementOveride(Composer.Api,this, Bounds);
        }
        public class ContainerElementOveride : GuiElementContainer
        {
            private UIControl parent;
            public ContainerElementOveride(ICoreClientAPI capi,UIControl _parent ,ElementBounds bounds) : base(capi, bounds)
            {
                parent = _parent;
            }

            public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
            {
                base.OnKeyDown(api, args);
                parent.InvokeOnKeyDown(new EventArgs());
            }
            public override void OnKeyUp(ICoreClientAPI api, KeyEvent args)
            {
                base.OnKeyUp(api, args);
                parent.InvokeOnKeyUp(new EventArgs());
            }
            public override void OnKeyPress(ICoreClientAPI api, KeyEvent args)
            {
                base.OnKeyPress(api, args);
                parent.InvokeOnKeyPress(new EventArgs());
            }
            public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
            {
                base.OnMouseDown(api, args);
                parent.InvokeOnMouseDown(new EventArgs());
            }
            public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
            {
                base.OnMouseUp(api, args);
                parent.InvokeOnMouseUp(new EventArgs());
            }
            public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
            {
                base.OnMouseWheel(api, args);
                parent.InvokeOnMouseWheel(new EventArgs());
            }

        }
    }
}
