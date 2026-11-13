using System.Globalization;
using Hisa.Core.Models;

namespace Hisa.App;

public sealed record EstimatedProbeRow(int Number, string X, string Y, string Z, string Distance);

public sealed partial class ProbeFormationDesignerViewModel
{
    private bool showEstimatedPlacement;

    public bool ShowEstimatedPlacement
    {
        get => showEstimatedPlacement;
        set
        {
            if (showEstimatedPlacement == value) return;
            showEstimatedPlacement = value;
            Notify();
            RefreshPlacement();
        }
    }

    public bool HasFormation => Current is not null;
    public bool HasEstimatedPlacement => ShowEstimatedPlacement && HasFormation;
    public string EstimatedShift => Current is { } formation
        ? $"Estimated shift ({Units}) · {PositionText(formation.Centroid * -1)}"
        : "";

    public IReadOnlyList<EstimatedProbeRow> EstimatedProbes { get; private set; } = [];

    private void RefreshPlacement()
    {
        if (ShowEstimatedPlacement && Current is { } formation)
        {
            var centroid = formation.Centroid;
            EstimatedProbes = formation.Probes.Select((probe, index) =>
            {
                var offset = ProbeFormationMath.EstimatedPlacement(probe.Position, centroid);
                return new EstimatedProbeRow(index + 1, Display(offset.X), Display(offset.Y), Display(offset.Z), Display(offset.Length));
            }).ToArray();
        }
        else
            EstimatedProbes = [];

        Notify(nameof(HasFormation));
        Notify(nameof(HasEstimatedPlacement));
        Notify(nameof(EstimatedShift));
        Notify(nameof(EstimatedProbes));
    }

    private string Display(double meters) => meters == 0 ? "0" : (meters / UnitFactor).ToString("G6", CultureInfo.InvariantCulture);
    private string PositionText(ProbePosition position) => $"{Display(position.X)}, {Display(position.Y)}, {Display(position.Z)}";

    public string PreviewCounterweight(int? index)
    {
        var candidate = CounterweightCandidate(index);
        var position = candidate.Probes[index ?? candidate.Probes.Count - 1].Position;
        return $"XYZ ({Units}) · {PositionText(position)}\nDistance from ship · {Display(position.Length)} {Units}";
    }

    public void ApplyCounterweight(int? index)
    {
        var candidate = CounterweightCandidate(index);
        CommitChange(candidate, [index ?? candidate.Probes.Count - 1]);
    }

    private ProbeFormation CounterweightCandidate(int? index)
    {
        CommitPendingEdits();
        return ProbeFormationMath.BalanceWithCounterweight(
            Current ?? throw new ArgumentException("Select a formation to balance."), index);
    }

    public void CreateSixAxisPerch(double distanceKm)
    {
        CommitPendingEdits();
        var formation = ProbeFormationMath.CreateSixAxisPerch(UniqueName("Six-axis perch"), distanceKm * 1_000);
        Remember();
        var row = new ProbeFormationLibraryRow(formation);
        Library.Add(row);
        selected = row;
        SelectAllCore();
        Refresh();
    }
}
