using Cairo;
using IS2Mod.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes
{
    public class StaticTextElement : UIControl
    {
        public StaticTextElement(string _text, CairoFont _font,string _Name = "", EnumTextOrientation _orientation = EnumTextOrientation.Left, PointD? _Size = null, Orientation _Orientation = Orientation.None, double _Margin = 0, double _Padding = 0, int _Index = 0) : base(_Name, _Size, _Orientation, _Margin, _Padding, _Index)
        {
            Text = _text;
            TextOrientation = _orientation;
            Font = _font;
            onMouseDown += StaticTextElement_onMouseDown;

        }

        /// <summary>
        /// Not fully implemented yet. Eventchain from Container needed for this. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StaticTextElement_onMouseDown(object? sender, EventArgs e)
        {
        }

        private string m_Text;

        public string Text
        {
            get { return m_Text; }
            set { m_Text = value; OnPropertyChanged(); }
        }

        private EnumTextOrientation m_TextOrientation;

        public EnumTextOrientation TextOrientation
        {
            get { return m_TextOrientation; }
            set { m_TextOrientation = value;OnPropertyChanged(); }
        }

        private CairoFont m_Font;

        public CairoFont Font
        {
            get { return m_Font; }
            set { m_Font = value; }
        }

        public override PointD CalculateSize()
        {
            PointD retVal = new PointD(0,0);
            if (IsAutoSize)
            {
                var bounds = ElementBounds.Empty;
                 
                Font.AutoBoxSize(Text, bounds);
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
            return new GuiElementStaticText(Composer.Api, Text, TextOrientation, Bounds, Font);
        }

    }
}
