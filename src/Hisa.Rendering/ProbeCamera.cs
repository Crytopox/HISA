using Avalonia;
using Hisa.Core.Models;

namespace Hisa.Rendering;

public sealed class ProbeCamera
{
    public double Yaw { get; set; } = .65;
    public double Pitch { get; set; } = .4;
    public double Distance { get; set; } = 2_000_000;
    public ProbePosition Target { get; set; }
    public ProbePosition Forward => new(-Math.Sin(Yaw) * Math.Cos(Pitch), -Math.Sin(Pitch), -Math.Cos(Yaw) * Math.Cos(Pitch));
    public ProbePosition Right => new(Math.Cos(Yaw), 0, -Math.Sin(Yaw));
    public ProbePosition Up => Right.Cross(Forward);
    public ProbePosition Eye => Target - Forward * Distance;
    private static double Focal(Size size) => Math.Max(1, Math.Min(size.Width, size.Height)) * 1.2;
    public Point? Project(ProbePosition position, Size size)
    {
        var delta = position - Eye; var depth = delta.Dot(Forward);
        if (!double.IsFinite(depth) || depth < Math.Max(1e-9, Distance * 1e-6)) return null;
        var f = Focal(size) / depth;
        var point = new Point(size.Width / 2 + delta.Dot(Right) * f, size.Height / 2 - delta.Dot(Up) * f);
        return double.IsFinite(point.X) && double.IsFinite(point.Y) ? point : null;
    }
    public void Fit(ProbeFormation formation)
    {
        Target = formation.Centroid;
        Distance = Math.Clamp(formation.Probes.Select(p => (p.Position - Target).Length).DefaultIfEmpty(1).Max() * 3.6, 1, 1e25);
    }
    public void View(string view)
    {
        (Yaw, Pitch) = view switch { "Top" => (0, Math.PI / 2 - .0001), "Side" => (Math.PI / 2, 0), "Front" => (0, 0), _ => (.65, .4) };
    }
    public void Pan(double dx, double dy, Size size) => Target += (Right * -dx + Up * dy) * (Distance / Focal(size));
    public void Zoom(double delta) => Distance = Math.Clamp(Distance * Math.Exp(-Math.Clamp(delta, -20, 20) * .12), 1, 1e25);
    public bool TryPlanePoint(Point screen, Size size, ProbePosition origin, ProbePosition normal, out ProbePosition point)
    {
        var ray = Forward + Right * ((screen.X - size.Width / 2) / Focal(size)) + Up * (-(screen.Y - size.Height / 2) / Focal(size));
        var denominator = ray.Dot(normal); point = default;
        if (Math.Abs(denominator) < 1e-8) return false;
        var distance = (origin - Eye).Dot(normal) / denominator;
        if (!double.IsFinite(distance) || distance <= 0) return false;
        point = Eye + ray * distance;
        return double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
    }
    public bool TryAxisParameter(Point screen, Size size, ProbePosition origin, ProbeAxis axis, out double parameter)
    {
        var ray = Forward + Right * ((screen.X - size.Width / 2) / Focal(size)) + Up * (-(screen.Y - size.Height / 2) / Focal(size));
        ray *= 1 / ray.Length;
        var direction = AxisVector(axis); var c = direction.Dot(ray); var denominator = 1 - c * c;
        parameter = 0;
        if (denominator < 1e-5) return false;
        var offset = Eye - origin;
        parameter = (offset.Dot(direction) - c * offset.Dot(ray)) / denominator;
        return double.IsFinite(parameter);
    }
    public static ProbePosition AxisVector(ProbeAxis axis) => axis switch { ProbeAxis.X => new(1, 0, 0), ProbeAxis.Y => new(0, 1, 0), _ => new(0, 0, 1) };
}
