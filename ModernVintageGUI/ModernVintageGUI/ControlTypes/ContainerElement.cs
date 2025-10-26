using Cairo;
using IS2Mod.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes
{
    public class ContainerElement : UIControl
    {
        public ContainerElement(string _Name = "",PointD? _Size = null, Orientation _Orientation = Orientation.Top, double _Margin = 0, double _Padding = 0, int _Index = 0, ObservableCollection<UIControl> _Children = null) : base(_Name, _Size, _Orientation, _Margin, _Padding, _Index,_Children)
        { 

        }

        public override GuiElement Compose()
        {
            return new GuiElementContainer(Composer.Api, Bounds);
        }
    }
}
