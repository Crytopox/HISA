using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Hisa.Core.Models;

namespace Hisa.Rendering.Controls;

public sealed class ProbeFormationThumbnail : Control
{
    public static readonly StyledProperty<ProbeFormation?> FormationProperty=AvaloniaProperty.Register<ProbeFormationThumbnail,ProbeFormation?>(nameof(Formation));
    public ProbeFormation? Formation{get=>GetValue(FormationProperty);set=>SetValue(FormationProperty,value);}
    static ProbeFormationThumbnail()=>AffectsRender<ProbeFormationThumbnail>(FormationProperty);
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brush.Parse("#0B131E"),new Rect(Bounds.Size));
        if(Formation is not {Probes.Count:>0} f)return;var camera=new ProbeCamera();camera.Fit(f);
        foreach(var probe in f.Probes)if(camera.Project(probe.Position,Bounds.Size) is { } point)context.DrawEllipse(Brushes.DeepSkyBlue,null,point,3,3);
    }
}
