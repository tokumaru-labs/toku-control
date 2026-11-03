using System.Windows;
using System.Windows.Media;

namespace TokuControl;

internal sealed class NightMandala : FrameworkElement
{
    int days = -1;
    public void SetDays(int value) { if (days != value) { days = value; InvalidateVisual(); } }

    protected override void OnRender(DrawingContext dc)
    {
        double scale = Math.Min(ActualWidth, ActualHeight) / 256;
        if (scale <= 0) return;
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, (ActualWidth - 256*scale)/2, (ActualHeight - 256*scale)/2));
        dc.DrawRectangle(Retro.Brush("#090918"), null, new Rect(0,0,256,256));
        var gold = Retro.Brush("#DCB980"); var violet = Retro.Brush("#AB98E1"); var jade = Retro.Brush("#8AD8C0");
        var faint = new Pen(Retro.Brush("#39334C"), 1);
        Point Polar(double radius, double angle) => new(128+radius*Math.Sin(angle),128-radius*Math.Cos(angle));
        void Shape(int sides, double radius, double offset, Brush? fill, Pen edge)
        {
            var g = new StreamGeometry();
            using (var c = g.Open()) { c.BeginFigure(Polar(radius,offset),fill != null,true);
                for(int i=1;i<sides;i++) c.LineTo(Polar(radius,offset+Math.Tau*i/sides),true,false); }
            dc.DrawGeometry(fill,edge,g);
        }
        // Eight permanent gates surround the earned marks.
        var petalEdge = new Pen(Retro.Brush("#584256"), 0.9);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.Tau / 8;
            var petal = new StreamGeometry();
            using (var c = petal.Open())
            {
                c.BeginFigure(Polar(19,a),true,true);
                c.QuadraticBezierTo(Polar(82,a-.42),Polar(104,a+.08),true,false);
                c.QuadraticBezierTo(Polar(84,a+.36),Polar(19,a),true,false);
            }
            dc.DrawGeometry(Retro.Brush("#171222"),petalEdge,petal);
            dc.DrawLine(new Pen(gold,1),Polar(111,a),Polar(122,a));
            dc.DrawEllipse(Retro.Brush("#090918"),new Pen(gold,1),Polar(115,a),2.5,2.5);
        }
        Shape(8,124,Math.PI/8,null,new Pen(gold,1.4)); Shape(4,122,0,null,faint);
        Shape(4,122,Math.PI/4,null,faint); Shape(8,18,Math.PI/8,null,new Pen(violet,1.4));
        Shape(4,11,0,jade,new Pen(gold,1));
        int index=0;
        for(int ring=1;ring<=11;ring++)
        {
            double radius=19+ring*8.3;
            dc.DrawEllipse(null,faint,new Point(128,128),radius,radius);
            int slots=Math.Min(6*ring,365-index);
            for(int j=0;j<slots;j++,index++)
            {
                if (index >= days) continue;
                double angle=Math.Tau*j/slots + (ring%2)*Math.PI/slots;
                var g=new StreamGeometry();
                using(var c=g.Open()){c.BeginFigure(Polar(radius-3.5,angle),true,true);
                    c.LineTo(Polar(radius,angle+.33*Math.Tau/slots),true,false);
                    c.LineTo(Polar(radius+3.5,angle),true,false);
                    c.LineTo(Polar(radius,angle-.33*Math.Tau/slots),true,false);}
                dc.DrawGeometry(ring%3==1?gold:ring%3==2?jade:violet,null,g);
            }
        }
        dc.Pop();
    }
}
