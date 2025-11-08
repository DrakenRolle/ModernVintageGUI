using Cairo;
using IS2Mod.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;

namespace IS2Mod.ControlTypes
{
    public class RectangleControl : UIControl
    {
        public RectangleControl(string _Name = "", PointD? _Size = null, Orientation _Orientation = Orientation.Top, double _Margin = 0, double _Padding = 0, int _Index = 0, ObservableCollection<UIControl> _Children = null) : base(_Name, _Size, _Orientation, _Margin, _Padding, _Index, _Children)
        {

        }
        public override PointD CalculateSize()
        {
            return base.CalculateSize();
        }

        public override GuiElement Compose()
        {
            var con = new ContainerElement.ContainerElementOveride(Composer.Api, this, Bounds);

            using (ImageSurface surface = new ImageSurface(Format.Argb32, 400, 300))
            using (Context context = new Context(surface))
            {
                context.SetSourceRGB(1, 1, 1);
                context.Paint();

                context.SetSourceRGB(0, 0, 0);
                context.LineWidth = 3;

                context.Rectangle(50, 50, 200, 100);

                context.Stroke();

                surface.WriteToPng("rechteck_umriss.png");
            }
            //ctx.SetSourceRGBA(255, 0, 0, 0.8);

            //Bounds.renderX

            //ctx.NewPath();
            //ctx.LineTo(Position.X, Position.Y);
            //ctx.LineTo(Position.X+Size.X,Position.Y);
            //ctx.LineTo(Position.X + Size.X, Position.Y+Size.Y);
            //ctx.LineTo(Position.X, Position.Y + Size.Y);
            //ctx.ClosePath();
            //ctx.SetSourceRGBA(255, 0, 0, 0.8);
            //ctx.Fill();

            return new ContainerElement.ContainerElementOveride(Composer.Api, this, Bounds);
        }
    }
}
