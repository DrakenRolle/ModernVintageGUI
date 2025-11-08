using Cairo;
using IS2Mod.Enums;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes
{
    /// <summary>
    /// Not fully implemented yet. Will be disgared if Custom Drawing is implemented, to unbind our System as far as possible from the GuiTextButtonElement.
    /// </summary>
    partial class ButtomElement : UIControl
    {
        public ButtomElement(string _Text, CairoFont _font, CairoFont _hoverfont, string _Name = "", PointD? _Size = null, Orientation _Orientation = Orientation.Top, double _Margin = 0, double _Padding = 0, int _Index = 0, ObservableCollection<UIControl> _Children = null) : base(_Name, _Size, _Orientation, _Margin, _Padding, _Index, _Children)
        {
            Text = _Text;
            Font = _font;
            HoverFont = _hoverfont;
            this.onMouseDown += ButtomElement_onMouseDown;
        }

        private void ButtomElement_onMouseDown(object? sender, EventArgs e)
        {

        }

        private string m_Text;

        public string Text
        {
            get { return m_Text; }
            set { m_Text = value; }
        }

        private CairoFont m_Font;

        public CairoFont Font
        {
            get { return m_Font; }
            set { m_Font = value; }
        }

        private CairoFont m_HoverFont;

        public CairoFont HoverFont
        {
            get { return m_HoverFont; }
            set { m_HoverFont = value; }
        }



        public override PointD CalculateSize()
        {
            PointD retVal = new PointD(0, 0);
            if (IsAutoSize)
            {
                var bounds = ElementBounds.Empty;
                CairoFont usedFont = Font;
                usedFont.AutoBoxSize(Text, bounds);
                retVal = new PointD(bounds.fixedWidth, bounds.fixedHeight);
                Size = retVal;
            }
            else
            {
                base.CalculateSize();
            }
            return retVal;

        }

        public override GuiElement Compose()
        {
            var element = new GuiElementTextButton(Composer.Api, Text, Font, HoverFont, () => { return true; }, Bounds);
            return element;
        }

    }
}
