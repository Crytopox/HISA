using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hisa.Core;
using Hisa.Core.Models;
using Hisa.Rendering;
using System.Globalization;
using System.Text.Json;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net.Http;
using NetTopologySuite.Triangulate;
using NetTopologySuite.Precision;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;
using NtsEnvelope = NetTopologySuite.Geometries.Envelope;
using NtsGeometryCollection = NetTopologySuite.Geometries.GeometryCollection;
using NtsGeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using NtsPolygon = NetTopologySuite.Geometries.Polygon;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using NtsMultiPolygon = NetTopologySuite.Geometries.MultiPolygon;
using NtsUnaryUnionOp = NetTopologySuite.Operation.Union.UnaryUnionOp;

namespace Hisa.Rendering.Controls;

public sealed class MapControl : Control
{
    private sealed class VoronoiCacheModel
    {
        public required string Key { get; init; }
        public required Dictionary<long, List<PointDto>> Polygons { get; init; }
    }

    private sealed class PointDto
    {
        public double X { get; init; }
        public double Y { get; init; }
    }

    private static readonly string VoronoiCacheDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hisa", "voronoi-cache-v2");
    private static readonly VoronoiCacheStore VoronoiMemoryCache = new();
    private static readonly JsonSerializerOptions VoronoiJsonOptions = new() { WriteIndented = false };

    private static readonly Point NodeLabelOffset = new(14, 8);
    private static readonly Typeface NodeLabelTypeface = new("Inter", FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface RegionCardTypeface = new("Inter", FontStyle.Normal, FontWeight.SemiBold);
    private const double NodeLabelFontSize = 11;
    private const double EditorNodeLabelFontSize = 9;
    private const double NodeRegionConstellationFontSize = 9.5;
    private const double EditorRegionConstellationFontSize = 9.0;
    private const double UniverseMinNodeScale = 0.55;
    private const int MaxUniverseTileEdge = 8192;
    private const long MaxUniverseAtlasPixels = 512L * 1024 * 1024 / 4;
    private const double UniverseAtlasPad = 48.0;
    private const double IconSize = 18.0;
    private const double SovIconSize = 22.0;
    private const double IndicatorIconLeftPadding = 2.0;
    private const double IndicatorIconSlotGap = 3.0;
    private const string A0BlueSmallName = "Sun A0 (Blue Small)";
    private const int A0BlueSmallTypeId = 3801;

    private static readonly IBrush BackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#0D131D"));

    private static readonly Pen LinksPen = new(new ImmutableSolidColorBrush(Color.Parse("#304762")), 1);
    private static readonly Pen SameConstellationPen = new(new ImmutableSolidColorBrush(Color.Parse("#4D6FA2")), 1.1);
    private static readonly Pen SameRegionPen = new(new ImmutableSolidColorBrush(Color.Parse("#3E8A7E")), 1.1);
    private static readonly Pen CrossRegionPen = new(new ImmutableSolidColorBrush(Color.Parse("#8E5C8A")), 1.1);

    private static readonly Pen HighlightedDefaultPen = new(new ImmutableSolidColorBrush(Color.Parse("#8CB3DD")), 2.2);
    private static readonly Pen HighlightedSameConstellationPen = new(new ImmutableSolidColorBrush(Color.Parse("#7FA7E3")), 2.2);
    private static readonly Pen HighlightedSameRegionPen = new(new ImmutableSolidColorBrush(Color.Parse("#63C2B2")), 2.2);
    private static readonly Pen HighlightedCrossRegionPen = new(new ImmutableSolidColorBrush(Color.Parse("#C28ABB")), 2.2);
    private static readonly Pen HighlightedSearchConstellationPen = new(new ImmutableSolidColorBrush(Color.Parse("#E8B75E")), 2.4);

    private static readonly Pen RegionEmphasisDefaultPen = new(new ImmutableSolidColorBrush(Color.Parse("#6F8FB6")), 1.8);
    private static readonly Pen RegionEmphasisSameConstellationPen = new(new ImmutableSolidColorBrush(Color.Parse("#6F97D2")), 1.8);
    private static readonly Pen RegionEmphasisSameRegionPen = new(new ImmutableSolidColorBrush(Color.Parse("#57AEA1")), 1.8);
    private static readonly Pen RegionEmphasisCrossRegionPen = new(new ImmutableSolidColorBrush(Color.Parse("#A2749E")), 1.8);

    private static readonly IBrush SelectedBrush = new ImmutableSolidColorBrush(Color.Parse("#E8B75E"));
    private static readonly IBrush HoveredBrush = new ImmutableSolidColorBrush(Color.Parse("#7CC8FF"));
    private static readonly IBrush RegionSelectedBrush = new ImmutableSolidColorBrush(Color.Parse("#6BC1B5"));
    private static readonly IBrush EditorMissingConnectionBrush = new ImmutableSolidColorBrush(Color.Parse("#FF6B6B"));
    private static readonly Pen MissingConnectionRingPen = new(
        new ImmutableSolidColorBrush(Color.Parse("#C567D6")),
        1.8,
        dashStyle: new DashStyle([1.2, 2.4], 0));
    private static readonly Pen JumpRangeInRangeRingPen = new(
        new ImmutableSolidColorBrush(Color.Parse("#6FD7F7")),
        1.9,
        dashStyle: new DashStyle([2.2, 2.6], 0));
    private static readonly Pen JumpRangeOriginRingPen = new(
        new ImmutableSolidColorBrush(Color.Parse("#ffffff")),
        2.5,
        dashStyle: new DashStyle([3.2, 2.0], 0));
    private static readonly Pen LyCoverageCoveredRingPen = new(
        new ImmutableSolidColorBrush(Color.Parse("#59D38C")),
        2.0,
        dashStyle: new DashStyle([2.0, 2.2], 0));
    private static readonly Pen LyCoverageUncoveredRingPen = new(
        new ImmutableSolidColorBrush(Color.Parse("#FF6A6A")),
        2.0,
        dashStyle: new DashStyle([1.6, 1.6], 0));
    private static readonly Pen JumpRoutePathPen = new(new ImmutableSolidColorBrush(Color.Parse("#63D3FF")), 2.2);
    private static readonly Pen JumpRouteStopRingPen = new(
        new ImmutableSolidColorBrush(Color.Parse("#63D3FF")),
        1.8,
        dashStyle: new DashStyle([2.0, 2.0], 0));
    private static readonly Pen JumpRouteSkippedRingPen = new(new ImmutableSolidColorBrush(Color.Parse("#FF6A6A")), 2.1);
    private static readonly Pen IntelRingPen = new(
        new ImmutableSolidColorBrush(Color.Parse("#FFB347")),
        2.2,
        dashStyle: new DashStyle([2.2, 1.8], 0));
    private static readonly Pen AnsiblexLinkPen = new(
        new ImmutableSolidColorBrush(Color.Parse("#e0bd3c")),
        2.2,
        dashStyle: new DashStyle([4.2, 2.8], 0));
    private static readonly Pen AnsiblexHighlightedLinkPen = new(
        new ImmutableSolidColorBrush(Color.Parse("#fcdc73")),
        3.1,
        dashStyle: new DashStyle([4.2, 2.8], 0));
    private static readonly Pen[] AnsiblexZonePens = CreateAnsiblexZonePens(2.2, 0);
    private static readonly Pen[] AnsiblexZoneHighlightPens = CreateAnsiblexZonePens(3.1, 0.35);
    private static readonly Pen AnsiblexCapitalRingPen = new(
        new ImmutableSolidColorBrush(Color.Parse("#F4F7FF")),
        1.8,
        dashStyle: new DashStyle([2.0, 1.6], 0));
    private static readonly Color AnimatedSameConstellationColor = Color.Parse("#A9CBFF");
    private static readonly Color AnimatedSameRegionColor = Color.Parse("#8DE5D7");
    private static readonly Color AnimatedCrossRegionColor = Color.Parse("#D9A9D4");
    private static readonly Color AnimatedDefaultLinkColor = Color.Parse("#AFC7E8");
    private static readonly Color AnimatedAnsiblexColor = Color.Parse("#ffd34e");
    private static readonly IBrush JumpRouteNumberFillBrush = new ImmutableSolidColorBrush(Color.Parse("#63D3FF"));
    private static readonly IBrush JumpRouteNumberTextBrush = new ImmutableSolidColorBrush(Color.Parse("#0D131D"));
    private static readonly IBrush EditorCrossRegionConnectorBrush = new ImmutableSolidColorBrush(Color.Parse("#8E74D8"));
    private static readonly IBrush NodeHoleBrush = new ImmutableSolidColorBrush(Color.Parse("#0D131D"));
    private static readonly IBrush NodeLabelBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#B5000000"));
    private static readonly IBrush TooltipBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#1A2536"));
    private static readonly IBrush HoverOverlayBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#99000000"));
    private static readonly Pen NodeOutlinePen = new(new ImmutableSolidColorBrush(Color.Parse("#88000000")), 1.1);
    private static readonly IBrush VoronoiBorderBrush = new ImmutableSolidColorBrush(Color.Parse("#AA0D131D"));
    private static readonly Pen TooltipBorderPen = new(new ImmutableSolidColorBrush(Color.Parse("#3B5678")), 1);
    private static readonly Pen HoverOverlayBorderPen = new(new ImmutableSolidColorBrush(Color.Parse("#4A617F")), 1);
    private static readonly IBrush EmptyTextBrush = new ImmutableSolidColorBrush(Color.Parse("#9FB4D2"));
    private static readonly Lazy<Bitmap?> A0StarIcon = new(LoadA0StarIcon);
    private static readonly Lazy<Bitmap?> JoveObservatoryIcon = new(LoadJoveObservatoryIcon);
    private static readonly Lazy<Bitmap?> IceFieldIcon = new(LoadIceFieldIcon);
    private static readonly Lazy<Bitmap?> StormElectricCenterIcon = new(() => LoadIcon("storm_electric_center.png"));
    private static readonly Lazy<Bitmap?> StormElectricStrongIcon = new(() => LoadIcon("storm_electric_strong.png"));
    private static readonly Lazy<Bitmap?> StormElectricWeakIcon = new(() => LoadIcon("storm_electric_weak.png"));
    private static readonly Lazy<Bitmap?> StormGammaCenterIcon = new(() => LoadIcon("storm_gamma_center.png"));
    private static readonly Lazy<Bitmap?> StormGammaStrongIcon = new(() => LoadIcon("storm_gamma_strong.png"));
    private static readonly Lazy<Bitmap?> StormGammaWeakIcon = new(() => LoadIcon("storm_gamma_weak.png"));
    private static readonly Lazy<Bitmap?> StormExoticCenterIcon = new(() => LoadIcon("storm_exotic_center.png"));
    private static readonly Lazy<Bitmap?> StormExoticStrongIcon = new(() => LoadIcon("storm_exotic_strong.png"));
    private static readonly Lazy<Bitmap?> StormExoticWeakIcon = new(() => LoadIcon("storm_exotic_weak.png"));
    private static readonly Lazy<Bitmap?> StormPlasmaCenterIcon = new(() => LoadIcon("storm_plasma_center.png"));
    private static readonly Lazy<Bitmap?> StormPlasmaStrongIcon = new(() => LoadIcon("storm_plasma_strong.png"));
    private static readonly Lazy<Bitmap?> StormPlasmaWeakIcon = new(() => LoadIcon("storm_plasma_weak.png"));
    private static readonly Lazy<Bitmap?> StormUnknownIcon = new(() => LoadIcon("storm_unknown.png"));
    private static readonly Lazy<Bitmap?> WormholeIcon = new(() => LoadIcon("wormhole.png"));
    private static readonly Lazy<Bitmap?> IncursionIcon = new(() => LoadIcon("incursion.png"));
    private static readonly Lazy<Bitmap?> SystemJumpsIcon = new(() => LoadIcon("jumps.png"));
    private static readonly Lazy<Bitmap?> ShipKillsIcon = new(() => LoadIcon("kills.png"));
    private static readonly Lazy<Bitmap?> PodKillsIcon = new(() => LoadIcon("pod.png"));
    private static readonly Lazy<Bitmap?> NpcKillsIcon = new(() => LoadIcon("npc_kills.png"));
    private static readonly Lazy<Bitmap?> KillmailIcon = new(() => LoadIcon("killmail.png"));
    private static readonly Lazy<Bitmap?> QuestionMarkIcon = new(() => LoadIcon("question-mark.png"));
    private static readonly Lazy<Bitmap?> JumpRangeInRangeIcon = new(() => LoadIcon("jumpRange_onRange.png"));
    private static readonly Lazy<Bitmap?> JumpRangeOutRangeIcon = new(() => LoadIcon("jumpRange_outRange.png"));
    private static readonly Dictionary<string, Lazy<Bitmap?>> SovUpgradeIcons = new(StringComparer.OrdinalIgnoreCase);

    public static readonly StyledProperty<MapGraph?> GraphProperty =
        AvaloniaProperty.Register<MapControl, MapGraph?>(nameof(Graph));

    public static readonly StyledProperty<long?> SelectedNodeIdProperty =
        AvaloniaProperty.Register<MapControl, long?>(nameof(SelectedNodeId));
    public static readonly StyledProperty<MapViewMode> ViewModeProperty =
        AvaloniaProperty.Register<MapControl, MapViewMode>(nameof(ViewMode), MapViewMode.Universe);
    public static readonly StyledProperty<bool> StretchToWindowProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(StretchToWindow), true);
    public static readonly StyledProperty<MapNodeColorMode> NodeColorModeProperty =
        AvaloniaProperty.Register<MapControl, MapNodeColorMode>(nameof(NodeColorMode), MapNodeColorMode.None);
    public static readonly StyledProperty<MapNodeColorMode> NodeBackgroundColorModeProperty =
        AvaloniaProperty.Register<MapControl, MapNodeColorMode>(nameof(NodeBackgroundColorMode), MapNodeColorMode.None);
    public static readonly StyledProperty<bool> ShowIndicatorRegionProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorRegion), false);
    public static readonly StyledProperty<bool> ShowIndicatorConstellationProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorConstellation), false);
    public static readonly StyledProperty<bool> ShowIndicatorSecurityStatusProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorSecurityStatus), false);
    public static readonly StyledProperty<bool> ShowIndicatorStarClassProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorStarClass), false);
    public static readonly StyledProperty<bool> ShowIndicatorA0StarIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorA0StarIcon), true);
    public static readonly StyledProperty<bool> ShowIndicatorJoveObservatoryIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorJoveObservatoryIcon), true);
    public static readonly StyledProperty<bool> ShowIndicatorIceBeltsIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorIceBeltsIcon), true);
    public static readonly StyledProperty<bool> ShowIndicatorStormIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorStormIcon), true);
    public static readonly StyledProperty<bool> ShowIndicatorWormholeIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorWormholeIcon), true);
    public static readonly StyledProperty<bool> ShowIndicatorSovUpgradeIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorSovUpgradeIcon), true);
    public static readonly StyledProperty<bool> ShowIndicatorIncursionIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorIncursionIcon), true);
    public static readonly StyledProperty<bool> ShowIndicatorSystemJumpsProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorSystemJumps), true);
    public static readonly StyledProperty<bool> ShowIndicatorShipKillsProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorShipKills), true);
    public static readonly StyledProperty<bool> ShowIndicatorPodKillsProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorPodKills), true);
    public static readonly StyledProperty<bool> ShowIndicatorNpcKillsProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorNpcKills), true);
    public static readonly StyledProperty<bool> ShowIndicatorCharacterPresenceProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorCharacterPresence), true);
    public static readonly StyledProperty<bool> ShowIndicatorJumpRangeLyProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorJumpRangeLy), true);
    public static readonly StyledProperty<bool> EnableLinkAnimationsProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(EnableLinkAnimations), true);
    public static readonly StyledProperty<bool> EnableIntelRingAnimationsProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(EnableIntelRingAnimations), true);
    public static readonly StyledProperty<bool> SuspendAnimationsProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(SuspendAnimations));
    public static readonly StyledProperty<bool> ShowAnsiblexNetworkProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowAnsiblexNetwork), true);
    public static readonly StyledProperty<IEnumerable<MapLink>?> AnsiblexLinksProperty =
        AvaloniaProperty.Register<MapControl, IEnumerable<MapLink>?>(nameof(AnsiblexLinks));
    public static readonly StyledProperty<IReadOnlyDictionary<long, AnsiblexSystemZone>?> AnsiblexZonesByNodeIdProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyDictionary<long, AnsiblexSystemZone>?>(nameof(AnsiblexZonesByNodeId));
    public static readonly StyledProperty<long?> AnsiblexCapitalSystemIdProperty =
        AvaloniaProperty.Register<MapControl, long?>(nameof(AnsiblexCapitalSystemId));
    public static readonly StyledProperty<string?> AnsiblexCapitalNameProperty =
        AvaloniaProperty.Register<MapControl, string?>(nameof(AnsiblexCapitalName));
    public static readonly StyledProperty<string?> AnsiblexShipClassNameProperty =
        AvaloniaProperty.Register<MapControl, string?>(nameof(AnsiblexShipClassName));
    public static readonly StyledProperty<double> AnsiblexShipBaseCostTjProperty =
        AvaloniaProperty.Register<MapControl, double>(nameof(AnsiblexShipBaseCostTj), 13);
    public static readonly StyledProperty<bool> ShowIndicatorAnsiblexZoneProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorAnsiblexZone), true);
    public static readonly StyledProperty<IEnumerable<string>?> IndicatorSovUpgradeFilterKeysProperty =
        AvaloniaProperty.Register<MapControl, IEnumerable<string>?>(nameof(IndicatorSovUpgradeFilterKeys));
    public static readonly StyledProperty<bool> InfoBoxShowRegionProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowRegion), true);
    public static readonly StyledProperty<bool> InfoBoxShowConstellationProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowConstellation), true);
    public static readonly StyledProperty<bool> InfoBoxShowSecurityStatusProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowSecurityStatus), true);
    public static readonly StyledProperty<bool> InfoBoxShowStarClassProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowStarClass), false);
    public static readonly StyledProperty<bool> InfoBoxShowA0StarIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowA0StarIcon), true);
    public static readonly StyledProperty<bool> InfoBoxShowJoveObservatoryIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowJoveObservatoryIcon), true);
    public static readonly StyledProperty<bool> InfoBoxShowIceBeltsIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowIceBeltsIcon), true);
    public static readonly StyledProperty<bool> InfoBoxShowStormIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowStormIcon), true);
    public static readonly StyledProperty<bool> InfoBoxShowWormholeIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowWormholeIcon), true);
    public static readonly StyledProperty<bool> InfoBoxShowSovUpgradeIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowSovUpgradeIcon), true);
    public static readonly StyledProperty<bool> InfoBoxShowIncursionIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowIncursionIcon), true);
    public static readonly StyledProperty<bool> InfoBoxShowSystemJumpsProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowSystemJumps), true);
    public static readonly StyledProperty<bool> InfoBoxShowShipKillsProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowShipKills), true);
    public static readonly StyledProperty<bool> InfoBoxShowPodKillsProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowPodKills), true);
    public static readonly StyledProperty<bool> InfoBoxShowNpcKillsProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowNpcKills), true);
    public static readonly StyledProperty<bool> InfoBoxShowJumpRangeLyProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowJumpRangeLy), true);
    public static readonly StyledProperty<bool> InfoBoxShowAnsiblexZoneProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowAnsiblexZone), true);
    public static readonly StyledProperty<IEnumerable<string>?> OverlaySovUpgradeFilterKeysProperty =
        AvaloniaProperty.Register<MapControl, IEnumerable<string>?>(nameof(OverlaySovUpgradeFilterKeys));
    public static readonly StyledProperty<bool> AlwaysShowHubWormholesProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(AlwaysShowHubWormholes), false);
    public static readonly StyledProperty<bool> AlwaysShowIncursionsProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(AlwaysShowIncursions), false);
    public static readonly StyledProperty<bool> ShowUserSystemMarksProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowUserSystemMarks), true);
    public static readonly StyledProperty<IReadOnlyDictionary<long, UserSystemMarkDisplay>?> UserSystemMarksByNodeIdProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyDictionary<long, UserSystemMarkDisplay>?>(nameof(UserSystemMarksByNodeId));
    public static readonly StyledProperty<HubWormholeMarkerMode> HubWormholeMarkerModeProperty =
        AvaloniaProperty.Register<MapControl, HubWormholeMarkerMode>(nameof(HubWormholeMarkerMode), HubWormholeMarkerMode.Badge);
    public static readonly StyledProperty<bool> ShowEditorGridProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowEditorGrid), false);
    public static readonly StyledProperty<bool> ShowEditorRegionLabelProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowEditorRegionLabel), true);
    public static readonly StyledProperty<double> EditorGridStepProperty =
        AvaloniaProperty.Register<MapControl, double>(nameof(EditorGridStep), 0.01);
    public static readonly StyledProperty<double> MinZoomProperty =
        AvaloniaProperty.Register<MapControl, double>(nameof(MinZoom), 0.4);
    public static readonly StyledProperty<double> MaxZoomOverrideProperty =
        AvaloniaProperty.Register<MapControl, double>(nameof(MaxZoomOverride), 0.0);
    public static readonly StyledProperty<bool> AllowFitBeyondMinZoomProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(AllowFitBeyondMinZoom), false);
    public static readonly StyledProperty<bool> UseBuiltInSelectionProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(UseBuiltInSelection), true);
    public static readonly StyledProperty<IEnumerable<long>?> AdditionalSelectedNodeIdsProperty =
        AvaloniaProperty.Register<MapControl, IEnumerable<long>?>(nameof(AdditionalSelectedNodeIds));
    public static readonly StyledProperty<IEnumerable<long>?> MissingConnectionNodeIdsProperty =
        AvaloniaProperty.Register<MapControl, IEnumerable<long>?>(nameof(MissingConnectionNodeIds));
    public static readonly StyledProperty<bool> ShowMissingConnectionMarkersProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowMissingConnectionMarkers), true);
    public static readonly StyledProperty<IEnumerable<long>?> CrossRegionConnectorNodeIdsProperty =
        AvaloniaProperty.Register<MapControl, IEnumerable<long>?>(nameof(CrossRegionConnectorNodeIds));
    public static readonly StyledProperty<IEnumerable<long>?> JumpRangeOriginNodeIdsProperty =
        AvaloniaProperty.Register<MapControl, IEnumerable<long>?>(nameof(JumpRangeOriginNodeIds));
    public static readonly StyledProperty<IEnumerable<long>?> JumpRangeInRangeNodeIdsProperty =
        AvaloniaProperty.Register<MapControl, IEnumerable<long>?>(nameof(JumpRangeInRangeNodeIds));
    public static readonly StyledProperty<IReadOnlyList<JumpRangeOriginDisplay>?> JumpRangeOriginsDisplayProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyList<JumpRangeOriginDisplay>?>(nameof(JumpRangeOriginsDisplay));
    public static readonly StyledProperty<IReadOnlyDictionary<long, IReadOnlyList<long>>?> JumpRangeMembershipByNodeIdProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyDictionary<long, IReadOnlyList<long>>?>(nameof(JumpRangeMembershipByNodeId));
    public static readonly StyledProperty<IReadOnlyDictionary<long, IReadOnlyList<JumpRangeDistanceDisplay>>?> JumpRangeDistancesByNodeIdProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyDictionary<long, IReadOnlyList<JumpRangeDistanceDisplay>>?>(nameof(JumpRangeDistancesByNodeId));
    public static readonly StyledProperty<IEnumerable<long>?> LyCoverageCoveredNodeIdsProperty =
        AvaloniaProperty.Register<MapControl, IEnumerable<long>?>(nameof(LyCoverageCoveredNodeIds));
    public static readonly StyledProperty<IEnumerable<long>?> LyCoverageUncoveredNodeIdsProperty =
        AvaloniaProperty.Register<MapControl, IEnumerable<long>?>(nameof(LyCoverageUncoveredNodeIds));
    public static readonly StyledProperty<IEnumerable<long>?> JumpRouteNodeIdsProperty =
        AvaloniaProperty.Register<MapControl, IEnumerable<long>?>(nameof(JumpRouteNodeIds));
    public static readonly StyledProperty<IEnumerable<long>?> JumpRouteSkippedNodeIdsProperty =
        AvaloniaProperty.Register<MapControl, IEnumerable<long>?>(nameof(JumpRouteSkippedNodeIds));
    public static readonly StyledProperty<IReadOnlyDictionary<long, int>?> CharacterPresenceCountsByNodeIdProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyDictionary<long, int>?>(nameof(CharacterPresenceCountsByNodeId));
    public static readonly StyledProperty<IReadOnlyDictionary<long, IReadOnlyList<string>>?> CharacterPresenceNamesByNodeIdProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyDictionary<long, IReadOnlyList<string>>?>(nameof(CharacterPresenceNamesByNodeId));
    public static readonly StyledProperty<IReadOnlyDictionary<long, IReadOnlyList<int>>?> CharacterPresenceCharacterIdsByNodeIdProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyDictionary<long, IReadOnlyList<int>>?>(nameof(CharacterPresenceCharacterIdsByNodeId));
    public static readonly StyledProperty<IReadOnlyDictionary<long, DateTime>?> CharacterPresenceLastUpdatedUtcByNodeIdProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyDictionary<long, DateTime>?>(nameof(CharacterPresenceLastUpdatedUtcByNodeId));
    public static readonly StyledProperty<IReadOnlyDictionary<long, IReadOnlyList<string>>?> IntelIconKeysByNodeIdProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyDictionary<long, IReadOnlyList<string>>?>(nameof(IntelIconKeysByNodeId));
    public static readonly StyledProperty<IReadOnlyDictionary<long, IReadOnlyList<IntelMapHoverReport>>?> IntelRecentReportsByNodeIdProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyDictionary<long, IReadOnlyList<IntelMapHoverReport>>?>(nameof(IntelRecentReportsByNodeId));
    public static readonly StyledProperty<IReadOnlyDictionary<long, IReadOnlyList<IntelMapHoverKillmail>>?> ZkillRecentReportsByNodeIdProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyDictionary<long, IReadOnlyList<IntelMapHoverKillmail>>?>(nameof(ZkillRecentReportsByNodeId));
    public static readonly StyledProperty<IReadOnlyDictionary<long, int>?> IntelHostileScoresByNodeIdProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyDictionary<long, int>?>(nameof(IntelHostileScoresByNodeId));
    public static readonly StyledProperty<HostileColorSettings> HostileColorSettingsProperty =
        AvaloniaProperty.Register<MapControl, HostileColorSettings>(nameof(HostileColorSettings), new HostileColorSettings());
    public static readonly StyledProperty<bool> ShowInfoBoxCharacterPresenceProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowInfoBoxCharacterPresence), true);
    public static readonly StyledProperty<int> CharacterPresenceHoverMaxNamesProperty =
        AvaloniaProperty.Register<MapControl, int>(nameof(CharacterPresenceHoverMaxNames), 6);

    private Point? _lastPanPoint;
    private Point? _leftPressPoint;
    private bool _pendingClearSelectionOnLeftRelease;
    private bool _leftDragPanned;
    private Point _panOffset = new(0, 0);
    private double _zoom = 1.0;
    private Point _lastPointerPosition;
    private long? _hoveredNodeId;
    private int? _hoveredRegionId;
    private int? _selectedRegionId;
    private long? _searchHighlightedNodeId;
    private int? _searchHighlightedConstellationId;
    private int? _searchHighlightedRegionId;
    private MapGraph? _lastGraphForCaches;
    private readonly Dictionary<string, FormattedText> _nodeLabelCache = [];
    private readonly Dictionary<int, FormattedText> _regionLabelCache = [];
    private readonly Dictionary<string, FormattedText> _nodeLabelHaloCache = [];
    private readonly Dictionary<string, FormattedText> _nodeSecondaryLabelCache = [];
    private readonly Dictionary<string, FormattedText> _nodeSecondaryLabelHaloCache = [];
    private readonly Dictionary<int, FormattedText> _regionLabelHaloCache = [];
    private readonly Dictionary<long, IReadOnlyList<(double X, double Y)>> _voronoiWorldPolygonsByNodeId = [];
    private MapGraph? _lastGraphForVoronoi;
    private Task<Dictionary<long, IReadOnlyList<(double X, double Y)>>>? _voronoiBuildTask;
    private CancellationTokenSource? _voronoiBuildCts;
    private string? _voronoiBuildKey;
    private string? _lastVoronoiCacheKey;
    private readonly Dictionary<long, MapNode> _nodeById = [];
    private readonly Dictionary<long, int> _nodeIndexById = [];
    private readonly Dictionary<long, StreamGeometry> _voronoiWorldGeometriesByNodeId = [];
    private readonly Dictionary<uint, IBrush> _brushCache = [];
    private MapGraph? _activityRangeGraph;
    private int _activityRangeNodeCount;
    private readonly Dictionary<MapNodeColorMode, (int Min, int Max)> _activityRangesByMode = [];
    private HashSet<long>? _ansiblexBadgeNodeIds;
    private IEnumerable<MapLink>? _ansiblexBadgeSource;
    private long? _ansiblexBadgeCapitalId;
    private Dictionary<long, int> _indicatorExplorationOverlapByNodeId = [];
    private HashSet<long> _indicatorExplorationSourceNodeIds = [];
    private Dictionary<long, int> _overlayExplorationOverlapByNodeId = [];
    private HashSet<long> _overlayExplorationSourceNodeIds = [];
    private Dictionary<long, int> _jumpRangeOverlapByNodeId = [];
    private HashSet<long> _jumpRangeOriginNodeIds = [];
    private Dictionary<long, Color> _jumpRangeOriginColorByNodeId = [];
    private static readonly HttpClient CharacterPortraitHttpClient = new();
    private static readonly ConcurrentDictionary<int, Bitmap?> CharacterPortraitCache = new();
    private static readonly ConcurrentDictionary<int, byte> CharacterPortraitLoading = new();
    private static readonly ConcurrentDictionary<int, DateTime> CharacterPortraitRetryAfterUtc = new();
    private static readonly ConcurrentDictionary<int, Bitmap?> ShipTypeIconCache = new();
    private static readonly ConcurrentDictionary<int, byte> ShipTypeIconLoading = new();
    private static readonly ConcurrentDictionary<int, DateTime> ShipTypeIconRetryAfterUtc = new();
    private static readonly ConcurrentDictionary<string, Bitmap?> OrganizationLogoCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> OrganizationLogoLoading = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime> OrganizationLogoRetryAfterUtc = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CharacterPortraitRetryDelay = TimeSpan.FromMinutes(2);
    private readonly List<(Rect Bounds, string Url)> _intelOverlayLinks = [];
    private readonly List<(Rect Bounds, MapSovUpgradeHit Hit)> _sovUpgradeIconHitTargets = [];
    private readonly List<(Rect Bounds, IReadOnlyList<UserSystemMarkDisplay> Marks, Point Anchor)> _systemMarkClusterHits = [];
    private int _hoveredSystemMarkClusterIndex = -1;
    private Point[] _screenPositions = [];
    private Point _screenCameraPan;
    private double _screenCameraZoom = double.NaN;
    private double _graphMinX;
    private double _graphMaxX;
    private double _graphMinY;
    private double _graphMaxY;
    private double _typicalLinkSpacing;
    private const double BasePadding = 0.0;
    private const double FitPadding = 40.0;
    private const double EditorFitPadding = 60.0;
    private const double EditorFitPaddingWide = 40.0;
    private readonly DispatcherTimer _linkAnimationTimer;
    private readonly DispatcherTimer _universeIdleBakeTimer;
    private bool _isAttachedToVisualTree;
    private double _linkAnimationPhase;
    private bool _staticLayerDirty = true;
    private RenderTargetBitmap? _staticLayer;
    private bool _universeBakeValid;
    private bool _universeBakeBaseOnly;
    private double _bakedZoom = 1.0;
    private Size _bakedViewport;
    private double _bakeMargin;
    private readonly List<UniverseAtlasTile> _universeTiles = [];
    private DateTime _universeBakeNotBefore = DateTime.MinValue;
    private DateTime _universeWheelIdleUntil = DateTime.MinValue;
    private MapGraph? _nodeGeometryGraph;
    private MapNodeColorMode _nodeGeometryColorMode;
    private MapNodeColorMode _nodeGeometryBackgroundMode;
    private int _nodeGeometryVoronoiCount = -1;
    private double _nodeGeometryZoom = double.NaN;
    private double _nodeGeometryPlotWidth;
    private double _nodeGeometryPlotHeight;
    private readonly List<(Color Color, StreamGeometry Geometry)> _nodeGeometries = [];
    private StreamGeometry? _voronoiHoleGeometry;
    private MapGraph? _voronoiFillGraph;
    private MapNodeColorMode _voronoiFillMode;
    private int _voronoiFillPolygonCount = -1;
    private readonly List<(IBrush Fill, StreamGeometry Geometry)> _voronoiFillGeometries = [];
    private MapGraph? _linkBatchGraph;
    private StreamGeometry? _linkSameConstellationGeometry;
    private StreamGeometry? _linkSameRegionGeometry;
    private StreamGeometry? _linkCrossRegionGeometry;
    private MapGraph? _regionCentroidGraph;
    private List<UniverseRegionCentroid> _regionCentroids = [];
    private readonly List<(StreamGeometry Geometry, Pen Pen)> _highlightLinkBatches = [];
    private readonly Dictionary<long, Pen> _worldPenCache = [];
    private readonly List<int> _highlightNodeIndexes = [];
    private long? _highlightBuiltSelected;
    private long? _highlightBuiltHovered;
    private long? _highlightBuiltSearchNode;
    private int? _highlightBuiltSearchConstellation;
    private int? _highlightBuiltSearchRegion;
    private int? _highlightBuiltActiveRegion;
    private IEnumerable<long>? _highlightBuiltAdditional;
    private readonly MapOverlapCache _indicatorOverlapCache = new();
    private readonly MapOverlapCache _overlayOverlapCache = new();
    private readonly MapPenCache _penCache = new();
    private readonly ScreenPointGrid _hitTestGrid = new();
    private readonly List<int> _visibleVoronoiNodeIndexes = [];
    private readonly HashSet<long> _additionalSelectedSet = [];
    private readonly HashSet<long> _missingConnectionSet = [];
    private readonly HashSet<long> _lyCoveredSet = [];
    private readonly HashSet<long> _lyUncoveredSet = [];
    private readonly HashSet<long> _crossRegionConnectorSet = [];
    private readonly HashSet<long> _jumpRouteSkippedSet = [];
    private readonly List<long> _jumpRouteOrderedIds = [];
    private readonly Dictionary<long, int> _jumpRouteOrderByNodeId = [];
    private readonly Dictionary<long, Color> _jumpRouteColorByNodeId = [];
    private readonly List<(int FromIndex, int ToIndex, Color Color)> _jumpRouteLegs = [];
    private IEnumerable<long>? _cachedJumpRouteNodeIds;
    private IEnumerable<long>? _cachedJumpRouteSkippedNodeIds;
    private IEnumerable<long>? _cachedJumpRangeInRangeNodeIds;
    private IEnumerable<long>? _cachedJumpRangeOriginNodeIds;
    private IReadOnlyList<JumpRangeOriginDisplay>? _cachedJumpRangeOriginsDisplay;

    private const double DenseSpacingLow = 0.02;
    private const double DenseSpacingHigh = 0.07;
    private const double RegionLabelSpacingPxSparse = 48.0;
    private const double RegionLabelSpacingPxDense = 52.0;
    private const double RegionMaxZoomSparse = 6.0;
    private const double RegionMaxZoomDense = 14.0;
    public event EventHandler<int>? UniverseRegionNodeDoubleClicked;

    public MapGraph? Graph
    {
        get => GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public long? SelectedNodeId
    {
        get => GetValue(SelectedNodeIdProperty);
        set => SetValue(SelectedNodeIdProperty, value);
    }

    public MapViewMode ViewMode
    {
        get => GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }

    public bool StretchToWindow
    {
        get => GetValue(StretchToWindowProperty);
        set => SetValue(StretchToWindowProperty, value);
    }

    public MapNodeColorMode NodeColorMode
    {
        get => GetValue(NodeColorModeProperty);
        set => SetValue(NodeColorModeProperty, value);
    }

    public MapNodeColorMode NodeBackgroundColorMode
    {
        get => GetValue(NodeBackgroundColorModeProperty);
        set => SetValue(NodeBackgroundColorModeProperty, value);
    }

    public bool ShowIndicatorRegion
    {
        get => GetValue(ShowIndicatorRegionProperty);
        set => SetValue(ShowIndicatorRegionProperty, value);
    }

    public bool ShowIndicatorConstellation
    {
        get => GetValue(ShowIndicatorConstellationProperty);
        set => SetValue(ShowIndicatorConstellationProperty, value);
    }

    public bool ShowIndicatorSecurityStatus
    {
        get => GetValue(ShowIndicatorSecurityStatusProperty);
        set => SetValue(ShowIndicatorSecurityStatusProperty, value);
    }

    public bool ShowIndicatorStarClass
    {
        get => GetValue(ShowIndicatorStarClassProperty);
        set => SetValue(ShowIndicatorStarClassProperty, value);
    }

    public bool ShowIndicatorA0StarIcon
    {
        get => GetValue(ShowIndicatorA0StarIconProperty);
        set => SetValue(ShowIndicatorA0StarIconProperty, value);
    }

    public bool ShowIndicatorJoveObservatoryIcon
    {
        get => GetValue(ShowIndicatorJoveObservatoryIconProperty);
        set => SetValue(ShowIndicatorJoveObservatoryIconProperty, value);
    }

    public bool ShowIndicatorIceBeltsIcon
    {
        get => GetValue(ShowIndicatorIceBeltsIconProperty);
        set => SetValue(ShowIndicatorIceBeltsIconProperty, value);
    }

    public bool ShowIndicatorStormIcon
    {
        get => GetValue(ShowIndicatorStormIconProperty);
        set => SetValue(ShowIndicatorStormIconProperty, value);
    }

    public bool ShowIndicatorWormholeIcon
    {
        get => GetValue(ShowIndicatorWormholeIconProperty);
        set => SetValue(ShowIndicatorWormholeIconProperty, value);
    }

    public bool ShowIndicatorSovUpgradeIcon
    {
        get => GetValue(ShowIndicatorSovUpgradeIconProperty);
        set => SetValue(ShowIndicatorSovUpgradeIconProperty, value);
    }

    public bool ShowIndicatorIncursionIcon
    {
        get => GetValue(ShowIndicatorIncursionIconProperty);
        set => SetValue(ShowIndicatorIncursionIconProperty, value);
    }
    public bool ShowIndicatorSystemJumps
    {
        get => GetValue(ShowIndicatorSystemJumpsProperty);
        set => SetValue(ShowIndicatorSystemJumpsProperty, value);
    }
    public bool ShowIndicatorShipKills
    {
        get => GetValue(ShowIndicatorShipKillsProperty);
        set => SetValue(ShowIndicatorShipKillsProperty, value);
    }
    public bool ShowIndicatorPodKills
    {
        get => GetValue(ShowIndicatorPodKillsProperty);
        set => SetValue(ShowIndicatorPodKillsProperty, value);
    }
    public bool ShowIndicatorNpcKills
    {
        get => GetValue(ShowIndicatorNpcKillsProperty);
        set => SetValue(ShowIndicatorNpcKillsProperty, value);
    }

    public bool ShowIndicatorCharacterPresence
    {
        get => GetValue(ShowIndicatorCharacterPresenceProperty);
        set => SetValue(ShowIndicatorCharacterPresenceProperty, value);
    }

    public bool ShowIndicatorJumpRangeLy
    {
        get => GetValue(ShowIndicatorJumpRangeLyProperty);
        set => SetValue(ShowIndicatorJumpRangeLyProperty, value);
    }

    public bool EnableLinkAnimations
    {
        get => GetValue(EnableLinkAnimationsProperty);
        set => SetValue(EnableLinkAnimationsProperty, value);
    }

    public bool EnableIntelRingAnimations
    {
        get => GetValue(EnableIntelRingAnimationsProperty);
        set => SetValue(EnableIntelRingAnimationsProperty, value);
    }

    public bool SuspendAnimations
    {
        get => GetValue(SuspendAnimationsProperty);
        set => SetValue(SuspendAnimationsProperty, value);
    }

    public bool ShowAnsiblexNetwork
    {
        get => GetValue(ShowAnsiblexNetworkProperty);
        set => SetValue(ShowAnsiblexNetworkProperty, value);
    }

    public IEnumerable<MapLink>? AnsiblexLinks
    {
        get => GetValue(AnsiblexLinksProperty);
        set => SetValue(AnsiblexLinksProperty, value);
    }

    public IReadOnlyDictionary<long, AnsiblexSystemZone>? AnsiblexZonesByNodeId
    {
        get => GetValue(AnsiblexZonesByNodeIdProperty);
        set => SetValue(AnsiblexZonesByNodeIdProperty, value);
    }

    public long? AnsiblexCapitalSystemId
    {
        get => GetValue(AnsiblexCapitalSystemIdProperty);
        set => SetValue(AnsiblexCapitalSystemIdProperty, value);
    }

    public string? AnsiblexCapitalName
    {
        get => GetValue(AnsiblexCapitalNameProperty);
        set => SetValue(AnsiblexCapitalNameProperty, value);
    }

    public string? AnsiblexShipClassName
    {
        get => GetValue(AnsiblexShipClassNameProperty);
        set => SetValue(AnsiblexShipClassNameProperty, value);
    }

    public double AnsiblexShipBaseCostTj
    {
        get => GetValue(AnsiblexShipBaseCostTjProperty);
        set => SetValue(AnsiblexShipBaseCostTjProperty, value);
    }

    public bool ShowIndicatorAnsiblexZone
    {
        get => GetValue(ShowIndicatorAnsiblexZoneProperty);
        set => SetValue(ShowIndicatorAnsiblexZoneProperty, value);
    }

    public IEnumerable<string>? IndicatorSovUpgradeFilterKeys
    {
        get => GetValue(IndicatorSovUpgradeFilterKeysProperty);
        set => SetValue(IndicatorSovUpgradeFilterKeysProperty, value);
    }

    public bool InfoBoxShowRegion
    {
        get => GetValue(InfoBoxShowRegionProperty);
        set => SetValue(InfoBoxShowRegionProperty, value);
    }

    public bool InfoBoxShowConstellation
    {
        get => GetValue(InfoBoxShowConstellationProperty);
        set => SetValue(InfoBoxShowConstellationProperty, value);
    }

    public bool InfoBoxShowSecurityStatus
    {
        get => GetValue(InfoBoxShowSecurityStatusProperty);
        set => SetValue(InfoBoxShowSecurityStatusProperty, value);
    }

    public bool InfoBoxShowStarClass
    {
        get => GetValue(InfoBoxShowStarClassProperty);
        set => SetValue(InfoBoxShowStarClassProperty, value);
    }

    public bool InfoBoxShowA0StarIcon
    {
        get => GetValue(InfoBoxShowA0StarIconProperty);
        set => SetValue(InfoBoxShowA0StarIconProperty, value);
    }

    public bool InfoBoxShowJoveObservatoryIcon
    {
        get => GetValue(InfoBoxShowJoveObservatoryIconProperty);
        set => SetValue(InfoBoxShowJoveObservatoryIconProperty, value);
    }

    public bool InfoBoxShowIceBeltsIcon
    {
        get => GetValue(InfoBoxShowIceBeltsIconProperty);
        set => SetValue(InfoBoxShowIceBeltsIconProperty, value);
    }

    public bool InfoBoxShowStormIcon
    {
        get => GetValue(InfoBoxShowStormIconProperty);
        set => SetValue(InfoBoxShowStormIconProperty, value);
    }

    public bool InfoBoxShowWormholeIcon
    {
        get => GetValue(InfoBoxShowWormholeIconProperty);
        set => SetValue(InfoBoxShowWormholeIconProperty, value);
    }

    public bool InfoBoxShowSovUpgradeIcon
    {
        get => GetValue(InfoBoxShowSovUpgradeIconProperty);
        set => SetValue(InfoBoxShowSovUpgradeIconProperty, value);
    }

    public bool InfoBoxShowIncursionIcon
    {
        get => GetValue(InfoBoxShowIncursionIconProperty);
        set => SetValue(InfoBoxShowIncursionIconProperty, value);
    }
    public bool InfoBoxShowSystemJumps
    {
        get => GetValue(InfoBoxShowSystemJumpsProperty);
        set => SetValue(InfoBoxShowSystemJumpsProperty, value);
    }
    public bool InfoBoxShowShipKills
    {
        get => GetValue(InfoBoxShowShipKillsProperty);
        set => SetValue(InfoBoxShowShipKillsProperty, value);
    }
    public bool InfoBoxShowPodKills
    {
        get => GetValue(InfoBoxShowPodKillsProperty);
        set => SetValue(InfoBoxShowPodKillsProperty, value);
    }
    public bool InfoBoxShowNpcKills
    {
        get => GetValue(InfoBoxShowNpcKillsProperty);
        set => SetValue(InfoBoxShowNpcKillsProperty, value);
    }

    public bool InfoBoxShowJumpRangeLy
    {
        get => GetValue(InfoBoxShowJumpRangeLyProperty);
        set => SetValue(InfoBoxShowJumpRangeLyProperty, value);
    }

    public bool InfoBoxShowAnsiblexZone
    {
        get => GetValue(InfoBoxShowAnsiblexZoneProperty);
        set => SetValue(InfoBoxShowAnsiblexZoneProperty, value);
    }

    public IEnumerable<string>? OverlaySovUpgradeFilterKeys
    {
        get => GetValue(OverlaySovUpgradeFilterKeysProperty);
        set => SetValue(OverlaySovUpgradeFilterKeysProperty, value);
    }

    public bool AlwaysShowHubWormholes
    {
        get => GetValue(AlwaysShowHubWormholesProperty);
        set => SetValue(AlwaysShowHubWormholesProperty, value);
    }

    public bool AlwaysShowIncursions
    {
        get => GetValue(AlwaysShowIncursionsProperty);
        set => SetValue(AlwaysShowIncursionsProperty, value);
    }

    public bool ShowUserSystemMarks
    {
        get => GetValue(ShowUserSystemMarksProperty);
        set => SetValue(ShowUserSystemMarksProperty, value);
    }

    public IReadOnlyDictionary<long, UserSystemMarkDisplay>? UserSystemMarksByNodeId
    {
        get => GetValue(UserSystemMarksByNodeIdProperty);
        set => SetValue(UserSystemMarksByNodeIdProperty, value);
    }

    public HubWormholeMarkerMode HubWormholeMarkerMode
    {
        get => GetValue(HubWormholeMarkerModeProperty);
        set => SetValue(HubWormholeMarkerModeProperty, value);
    }

    public bool ShowEditorGrid
    {
        get => GetValue(ShowEditorGridProperty);
        set => SetValue(ShowEditorGridProperty, value);
    }

    public bool ShowEditorRegionLabel
    {
        get => GetValue(ShowEditorRegionLabelProperty);
        set => SetValue(ShowEditorRegionLabelProperty, value);
    }

    public double EditorGridStep
    {
        get => GetValue(EditorGridStepProperty);
        set => SetValue(EditorGridStepProperty, value);
    }

    public bool UseBuiltInSelection
    {
        get => GetValue(UseBuiltInSelectionProperty);
        set => SetValue(UseBuiltInSelectionProperty, value);
    }

    public double MinZoom
    {
        get => GetValue(MinZoomProperty);
        set => SetValue(MinZoomProperty, value);
    }

    public double MaxZoomOverride
    {
        get => GetValue(MaxZoomOverrideProperty);
        set => SetValue(MaxZoomOverrideProperty, value);
    }

    public bool AllowFitBeyondMinZoom
    {
        get => GetValue(AllowFitBeyondMinZoomProperty);
        set => SetValue(AllowFitBeyondMinZoomProperty, value);
    }

    public IEnumerable<long>? AdditionalSelectedNodeIds
    {
        get => GetValue(AdditionalSelectedNodeIdsProperty);
        set => SetValue(AdditionalSelectedNodeIdsProperty, value);
    }

    public IEnumerable<long>? MissingConnectionNodeIds
    {
        get => GetValue(MissingConnectionNodeIdsProperty);
        set => SetValue(MissingConnectionNodeIdsProperty, value);
    }

    public bool ShowMissingConnectionMarkers
    {
        get => GetValue(ShowMissingConnectionMarkersProperty);
        set => SetValue(ShowMissingConnectionMarkersProperty, value);
    }

    public IEnumerable<long>? CrossRegionConnectorNodeIds
    {
        get => GetValue(CrossRegionConnectorNodeIdsProperty);
        set => SetValue(CrossRegionConnectorNodeIdsProperty, value);
    }

    public IEnumerable<long>? JumpRangeOriginNodeIds
    {
        get => GetValue(JumpRangeOriginNodeIdsProperty);
        set => SetValue(JumpRangeOriginNodeIdsProperty, value);
    }

    public IEnumerable<long>? JumpRangeInRangeNodeIds
    {
        get => GetValue(JumpRangeInRangeNodeIdsProperty);
        set => SetValue(JumpRangeInRangeNodeIdsProperty, value);
    }

    public IReadOnlyList<JumpRangeOriginDisplay>? JumpRangeOriginsDisplay
    {
        get => GetValue(JumpRangeOriginsDisplayProperty);
        set => SetValue(JumpRangeOriginsDisplayProperty, value);
    }

    public IReadOnlyDictionary<long, IReadOnlyList<long>>? JumpRangeMembershipByNodeId
    {
        get => GetValue(JumpRangeMembershipByNodeIdProperty);
        set => SetValue(JumpRangeMembershipByNodeIdProperty, value);
    }

    public IReadOnlyDictionary<long, IReadOnlyList<JumpRangeDistanceDisplay>>? JumpRangeDistancesByNodeId
    {
        get => GetValue(JumpRangeDistancesByNodeIdProperty);
        set => SetValue(JumpRangeDistancesByNodeIdProperty, value);
    }

    public IEnumerable<long>? LyCoverageCoveredNodeIds
    {
        get => GetValue(LyCoverageCoveredNodeIdsProperty);
        set => SetValue(LyCoverageCoveredNodeIdsProperty, value);
    }

    public IEnumerable<long>? LyCoverageUncoveredNodeIds
    {
        get => GetValue(LyCoverageUncoveredNodeIdsProperty);
        set => SetValue(LyCoverageUncoveredNodeIdsProperty, value);
    }

    public IEnumerable<long>? JumpRouteNodeIds
    {
        get => GetValue(JumpRouteNodeIdsProperty);
        set => SetValue(JumpRouteNodeIdsProperty, value);
    }

    public IEnumerable<long>? JumpRouteSkippedNodeIds
    {
        get => GetValue(JumpRouteSkippedNodeIdsProperty);
        set => SetValue(JumpRouteSkippedNodeIdsProperty, value);
    }

    public IReadOnlyDictionary<long, int>? CharacterPresenceCountsByNodeId
    {
        get => GetValue(CharacterPresenceCountsByNodeIdProperty);
        set => SetValue(CharacterPresenceCountsByNodeIdProperty, value);
    }

    public IReadOnlyDictionary<long, IReadOnlyList<string>>? CharacterPresenceNamesByNodeId
    {
        get => GetValue(CharacterPresenceNamesByNodeIdProperty);
        set => SetValue(CharacterPresenceNamesByNodeIdProperty, value);
    }

    public IReadOnlyDictionary<long, IReadOnlyList<int>>? CharacterPresenceCharacterIdsByNodeId
    {
        get => GetValue(CharacterPresenceCharacterIdsByNodeIdProperty);
        set => SetValue(CharacterPresenceCharacterIdsByNodeIdProperty, value);
    }

    public IReadOnlyDictionary<long, DateTime>? CharacterPresenceLastUpdatedUtcByNodeId
    {
        get => GetValue(CharacterPresenceLastUpdatedUtcByNodeIdProperty);
        set => SetValue(CharacterPresenceLastUpdatedUtcByNodeIdProperty, value);
    }

    public IReadOnlyDictionary<long, IReadOnlyList<string>>? IntelIconKeysByNodeId
    {
        get => GetValue(IntelIconKeysByNodeIdProperty);
        set => SetValue(IntelIconKeysByNodeIdProperty, value);
    }

    public IReadOnlyDictionary<long, IReadOnlyList<IntelMapHoverReport>>? IntelRecentReportsByNodeId
    {
        get => GetValue(IntelRecentReportsByNodeIdProperty);
        set => SetValue(IntelRecentReportsByNodeIdProperty, value);
    }

    public IReadOnlyDictionary<long, IReadOnlyList<IntelMapHoverKillmail>>? ZkillRecentReportsByNodeId
    {
        get => GetValue(ZkillRecentReportsByNodeIdProperty);
        set => SetValue(ZkillRecentReportsByNodeIdProperty, value);
    }

    public IReadOnlyDictionary<long, int>? IntelHostileScoresByNodeId
    {
        get => GetValue(IntelHostileScoresByNodeIdProperty);
        set => SetValue(IntelHostileScoresByNodeIdProperty, value);
    }

    public HostileColorSettings HostileColorSettings
    {
        get => GetValue(HostileColorSettingsProperty);
        set => SetValue(HostileColorSettingsProperty, value);
    }

    public bool ShowInfoBoxCharacterPresence
    {
        get => GetValue(ShowInfoBoxCharacterPresenceProperty);
        set => SetValue(ShowInfoBoxCharacterPresenceProperty, value);
    }

    public int CharacterPresenceHoverMaxNames
    {
        get => GetValue(CharacterPresenceHoverMaxNamesProperty);
        set => SetValue(CharacterPresenceHoverMaxNamesProperty, value);
    }

    public MapControl()
    {
        AffectsRender<MapControl>(GraphProperty, SelectedNodeIdProperty, ViewModeProperty, StretchToWindowProperty);
        AffectsRender<MapControl>(
            NodeColorModeProperty,
            NodeBackgroundColorModeProperty,
            ShowIndicatorRegionProperty,
            ShowIndicatorConstellationProperty,
            ShowIndicatorSecurityStatusProperty,
            ShowIndicatorStarClassProperty,
            ShowIndicatorA0StarIconProperty,
            ShowIndicatorJoveObservatoryIconProperty,
            ShowIndicatorIceBeltsIconProperty,
            ShowIndicatorStormIconProperty,
            ShowIndicatorWormholeIconProperty,
            ShowIndicatorSovUpgradeIconProperty,
            ShowIndicatorIncursionIconProperty,
            ShowIndicatorSystemJumpsProperty,
            ShowIndicatorShipKillsProperty,
            ShowIndicatorPodKillsProperty,
            ShowIndicatorNpcKillsProperty,
            ShowIndicatorCharacterPresenceProperty,
            ShowIndicatorJumpRangeLyProperty,
            EnableLinkAnimationsProperty,
            ShowAnsiblexNetworkProperty,
            AnsiblexLinksProperty,
            AnsiblexZonesByNodeIdProperty,
            AnsiblexCapitalSystemIdProperty,
            AnsiblexCapitalNameProperty,
            AnsiblexShipClassNameProperty,
            AnsiblexShipBaseCostTjProperty,
            ShowIndicatorAnsiblexZoneProperty,
            IndicatorSovUpgradeFilterKeysProperty,
            InfoBoxShowRegionProperty,
            InfoBoxShowConstellationProperty,
            InfoBoxShowSecurityStatusProperty,
            InfoBoxShowStarClassProperty,
            InfoBoxShowA0StarIconProperty,
            InfoBoxShowJoveObservatoryIconProperty,
            InfoBoxShowIceBeltsIconProperty,
            InfoBoxShowStormIconProperty,
            InfoBoxShowWormholeIconProperty,
            InfoBoxShowSovUpgradeIconProperty,
            InfoBoxShowIncursionIconProperty,
            InfoBoxShowSystemJumpsProperty,
            InfoBoxShowShipKillsProperty,
            InfoBoxShowPodKillsProperty,
            InfoBoxShowNpcKillsProperty,
            InfoBoxShowJumpRangeLyProperty,
            InfoBoxShowAnsiblexZoneProperty,
            OverlaySovUpgradeFilterKeysProperty,
            AlwaysShowHubWormholesProperty,
            AlwaysShowIncursionsProperty,
            ShowUserSystemMarksProperty,
            UserSystemMarksByNodeIdProperty,
            HubWormholeMarkerModeProperty,
            ShowEditorGridProperty,
            ShowEditorRegionLabelProperty,
            EditorGridStepProperty,
            MinZoomProperty,
            MaxZoomOverrideProperty,
            AllowFitBeyondMinZoomProperty,
            UseBuiltInSelectionProperty,
            AdditionalSelectedNodeIdsProperty,
            MissingConnectionNodeIdsProperty,
            ShowMissingConnectionMarkersProperty,
            CrossRegionConnectorNodeIdsProperty,
            JumpRangeOriginNodeIdsProperty,
            JumpRangeInRangeNodeIdsProperty,
            JumpRangeOriginsDisplayProperty,
            JumpRangeMembershipByNodeIdProperty,
            JumpRangeDistancesByNodeIdProperty,
            LyCoverageCoveredNodeIdsProperty,
            LyCoverageUncoveredNodeIdsProperty,
            JumpRouteNodeIdsProperty,
            JumpRouteSkippedNodeIdsProperty,
            CharacterPresenceCountsByNodeIdProperty,
            CharacterPresenceNamesByNodeIdProperty,
            CharacterPresenceCharacterIdsByNodeIdProperty,
            CharacterPresenceLastUpdatedUtcByNodeIdProperty,
            IntelIconKeysByNodeIdProperty,
            IntelRecentReportsByNodeIdProperty,
            ZkillRecentReportsByNodeIdProperty,
            IntelHostileScoresByNodeIdProperty,
            HostileColorSettingsProperty,
            ShowInfoBoxCharacterPresenceProperty,
            CharacterPresenceHoverMaxNamesProperty,
            EnableIntelRingAnimationsProperty);
        ClipToBounds = true;
        _linkAnimationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Render, (_, _) =>
        {
            if (SuspendAnimations || (!ShouldAnimateAnyLink() && !HasAnimatedIntelRings()))
            {
                _linkAnimationTimer?.Stop();
                return;
            }

            _linkAnimationPhase += 0.012;
            InvalidateVisual();
        });
        _universeIdleBakeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _universeIdleBakeTimer.Tick += (_, _) => OnUniverseIdleBakeTick();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttachedToVisualTree = true;
        UpdateAnimationTimerState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = false;
        _linkAnimationTimer.Stop();
        _universeIdleBakeTimer.Stop();
        DisposeStaticLayer();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        InvalidateMap();
        UpdateAnimationTimerState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SuspendAnimationsProperty &&
            change.Property != SelectedNodeIdProperty)
        {
            _staticLayerDirty = true;
            if (change.Property == IntelHostileScoresByNodeIdProperty &&
                (NodeColorMode == MapNodeColorMode.Hostiles || NodeBackgroundColorMode == MapNodeColorMode.Hostiles))
            {
                InvalidateUniverseGeometryCaches();
            }

            if (UseUniverseFastPath() && _universeTiles.Count > 0)
            {
                ScheduleUniverseIdleBake(resetDeadline: true);
            }
            else
            {
                _universeBakeValid = false;
            }
        }

        if (change.Property == ViewModeProperty && ViewMode != MapViewMode.Universe)
        {
            _universeBakeValid = false;
            DisposeStaticLayer();
        }

        if (change.Property == GraphProperty ||
            change.Property == SelectedNodeIdProperty ||
            change.Property == ViewModeProperty ||
            change.Property == EnableLinkAnimationsProperty ||
            change.Property == EnableIntelRingAnimationsProperty ||
            change.Property == ShowAnsiblexNetworkProperty ||
            change.Property == AnsiblexLinksProperty ||
            change.Property == IntelHostileScoresByNodeIdProperty ||
            change.Property == SuspendAnimationsProperty)
        {
            UpdateAnimationTimerState();
        }
    }

    private void UpdateAnimationTimerState()
    {
        if (!_isAttachedToVisualTree ||
            SuspendAnimations ||
            (!ShouldAnimateAnyLink() && !HasAnimatedIntelRings()))
        {
            _linkAnimationTimer?.Stop();
            return;
        }

        _linkAnimationTimer?.Start();
    }

    private void InvalidateUniverseGeometryCaches()
    {
        _nodeGeometryZoom = double.NaN;
        _voronoiFillPolygonCount = -1;
    }

    private void InvalidateMap()
    {
        _staticLayerDirty = true;
        if (UseUniverseFastPath() && _universeTiles.Count > 0)
        {
            ScheduleUniverseIdleBake(resetDeadline: true);
            InvalidateVisual();
            return;
        }

        _universeBakeValid = false;
        InvalidateVisual();
    }

    private void InvalidateCamera(bool zoomChanged = false)
    {
        if (UseUniverseFastPath())
        {
            if (zoomChanged || !_universeBakeValid || _staticLayerDirty || _universeTiles.Count == 0)
            {
                ScheduleUniverseIdleBake(resetDeadline: true);
            }

            InvalidateVisual();
            return;
        }

        InvalidateMap();
    }

    private void InvalidateInteractive()
    {
        if (UseUniverseFastPath())
        {
            InvalidateVisual();
            return;
        }

        InvalidateMap();
    }

    private bool UseUniverseFastPath()
    {
        return ViewMode == MapViewMode.Universe &&
               !ShowEditorGrid &&
               Graph is not null &&
               Graph.Nodes.Count > 0 &&
               Bounds.Width > 2 &&
               Bounds.Height > 2;
    }

    private void DisposeStaticLayer()
    {
        _staticLayer?.Dispose();
        _staticLayer = null;
        _staticLayerDirty = true;
        _universeBakeValid = false;
        DisposeUniverseAtlas();
    }

    private void DisposeUniverseAtlas()
    {
        foreach (var tile in _universeTiles)
        {
            tile.Dispose();
        }

        _universeTiles.Clear();
    }

    public void FitToView()
    {
        if (Graph is null || Graph.Nodes.Count == 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            _zoom = 1.0;
            _panOffset = new Point(0, 0);
            UpdateAnimationTimerState();
            InvalidateMap();
            return;
        }

        // Node positions can change without node count changing (editor drag/move),
        // so always rebuild bounds before fitting.
        RebuildGraphCaches();

        var plot = GetPlotMetrics();
        var fitPadding = ShowEditorGrid
            ? GetEditorFitPadding(Math.Max(1e-9, _graphMaxX - _graphMinX), Math.Max(1e-9, _graphMaxY - _graphMinY))
            : FitPadding;
        var worldCenterX = (_graphMinX + _graphMaxX) * 0.5;
        var worldCenterY = (_graphMinY + _graphMaxY) * 0.5;
        var maxZoom = GetMaxZoom();
        var minZoom = AllowFitBeyondMinZoom ? 0.0001 : GetMinZoom();
        var low = minZoom;
        var high = maxZoom;
        for (var i = 0; i < 28; i++)
        {
            var mid = (low + high) * 0.5;
            if (DoesFitAtZoom(plot, worldCenterX, worldCenterY, mid, fitPadding))
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        _zoom = AllowFitBeyondMinZoom
            ? Math.Min(low, maxZoom)
            : Math.Clamp(low, GetMinZoom(), maxZoom);
        _panOffset = ComputePanForWorldCenter(plot, worldCenterX, worldCenterY, _zoom);

        UpdateAnimationTimerState();
        InvalidateMap();
    }

    private static double GetEditorFitPadding(double graphWidth, double graphHeight)
    {
        // Wide maps need tighter side padding to avoid excessive empty space.
        var aspect = graphWidth / Math.Max(1e-9, graphHeight);
        return aspect >= 1.35 ? EditorFitPaddingWide : EditorFitPadding;
    }

    private bool DoesFitAtZoom(PlotMetrics plot, double worldCenterX, double worldCenterY, double zoom, double padding)
    {
        var oldZoom = _zoom;
        var oldPan = _panOffset;
        _zoom = zoom;
        _panOffset = ComputePanForWorldCenter(plot, worldCenterX, worldCenterY, zoom);
        UpdateScreenPositions(plot, Bounds.Width * 0.5, Bounds.Height * 0.5);

        var fits = _screenPositions.Length == 0;
        if (_screenPositions.Length > 0)
        {
            var minX = _screenPositions.Min(p => p.X);
            var maxX = _screenPositions.Max(p => p.X);
            var minY = _screenPositions.Min(p => p.Y);
            var maxY = _screenPositions.Max(p => p.Y);
            fits = minX >= padding &&
                   maxX <= Bounds.Width - padding &&
                   minY >= padding &&
                   maxY <= Bounds.Height - padding;
        }

        _zoom = oldZoom;
        _panOffset = oldPan;
        return fits;
    }

    private Point ComputePanForWorldCenter(PlotMetrics plot, double worldCenterX, double worldCenterY, double zoom)
    {
        var baseCenterX = plot.OriginX + (worldCenterX * plot.Width);
        var baseCenterY = plot.OriginY + (worldCenterY * plot.Height);
        var viewCenterX = Bounds.Width * 0.5;
        var viewCenterY = Bounds.Height * 0.5;

        return new Point(
            viewCenterX - (((baseCenterX - viewCenterX) * zoom) + viewCenterX),
            viewCenterY - (((baseCenterY - viewCenterY) * zoom) + viewCenterY));
    }


    public MapViewportState GetViewportState() => new()
    {
        Zoom = _zoom,
        PanOffsetX = _panOffset.X,
        PanOffsetY = _panOffset.Y
    };

    public void SetViewportState(MapViewportState state)
    {
        _zoom = Math.Clamp(state.Zoom, GetMinZoom(), GetMaxZoom());
        _panOffset = new Point(state.PanOffsetX, state.PanOffsetY);
        UpdateAnimationTimerState();
        InvalidateMap();
    }


    private void RebuildGraphCaches()
    {
        _nodeById.Clear();
        _nodeIndexById.Clear();
        _screenPositions = [];

        if (Graph is null || Graph.Nodes.Count == 0)
        {
            _graphMinX = 0;
            _graphMaxX = 0;
            _graphMinY = 0;
            _graphMaxY = 0;
            _typicalLinkSpacing = 0.01;
            UpdateAnimationTimerState();
            return;
        }

        _graphMinX = double.PositiveInfinity;
        _graphMaxX = double.NegativeInfinity;
        _graphMinY = double.PositiveInfinity;
        _graphMaxY = double.NegativeInfinity;

        for (var i = 0; i < Graph.Nodes.Count; i++)
        {
            var node = Graph.Nodes[i];
            _nodeById[node.Id] = node;
            _nodeIndexById[node.Id] = i;

            _graphMinX = Math.Min(_graphMinX, node.X);
            _graphMaxX = Math.Max(_graphMaxX, node.X);
            _graphMinY = Math.Min(_graphMinY, node.Y);
            _graphMaxY = Math.Max(_graphMaxY, node.Y);
        }

        _typicalLinkSpacing = EstimateTypicalLinkSpacing(Graph.Nodes, Graph.Links);
        UpdateAnimationTimerState();
    }

    private void EnsureScreenPositionBuffer()
    {
        if (Graph is null)
        {
            _screenPositions = [];
            return;
        }

        if (_screenPositions.Length != Graph.Nodes.Count)
        {
            _screenPositions = new Point[Graph.Nodes.Count];
        }
    }

    private void UpdateScreenPositions(PlotMetrics plot, double viewCenterX, double viewCenterY)
    {
        if (Graph is null)
        {
            return;
        }

        EnsureScreenPositionBuffer();

        for (var i = 0; i < Graph.Nodes.Count; i++)
        {
            var node = Graph.Nodes[i];
            _screenPositions[i] = ToScreenPointFast(node.X, node.Y, plot, viewCenterX, viewCenterY);
        }

        _screenCameraPan = _panOffset;
        _screenCameraZoom = _zoom;
    }

    private bool ScreenCameraMatches()
    {
        return Graph is not null &&
               _screenPositions.Length == Graph.Nodes.Count &&
               Math.Abs(_screenCameraZoom - _zoom) < 1e-6 &&
               Math.Abs(_screenCameraPan.X - _panOffset.X) < 0.5 &&
               Math.Abs(_screenCameraPan.Y - _panOffset.Y) < 0.5;
    }

    private long? FindClosestNodeAt(Point point, double threshold)
    {
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            return null;
        }

        if (ViewMode == MapViewMode.UniverseRegions)
        {
            if (_screenPositions.Length != Graph.Nodes.Count)
            {
                var plot = GetPlotMetrics();
                UpdateScreenPositions(plot, Bounds.Width / 2.0, Bounds.Height / 2.0);
            }

            for (var i = 0; i < Graph.Nodes.Count; i++)
            {
                var rect = GetUniverseRegionNodeRect(Graph.Nodes[i], _screenPositions[i], 1.0);
                if (rect.Contains(point))
                {
                    return Graph.Nodes[i].Id;
                }
            }
        }

        if (_screenPositions.Length != Graph.Nodes.Count)
        {
            var plot = GetPlotMetrics();
            UpdateScreenPositions(plot, Bounds.Width / 2.0, Bounds.Height / 2.0);
            _hitTestGrid.Rebuild(_screenPositions, Graph.Nodes.Count, Bounds.Size);
        }

        var closestIndex = _hitTestGrid.FindClosestIndex(point, threshold, _screenPositions);
        return closestIndex is int index ? Graph.Nodes[index].Id : null;
    }

    public long? HitTestNode(Point point, double threshold = 10.0)
    {
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            return null;
        }

        var plot = GetPlotMetrics();
        UpdateScreenPositions(plot, Bounds.Width / 2.0, Bounds.Height / 2.0);
        return FindClosestNodeAt(point, threshold);
    }

    public MapSovUpgradeHit? HitTestSovUpgrade(Point point)
    {
        for (var i = _sovUpgradeIconHitTargets.Count - 1; i >= 0; i--)
        {
            if (_sovUpgradeIconHitTargets[i].Bounds.Contains(point))
            {
                return _sovUpgradeIconHitTargets[i].Hit;
            }
        }

        return null;
    }

    public IReadOnlyList<long> GetNodeIdsInScreenRect(Rect screenRect)
    {
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            return [];
        }

        var plot = GetPlotMetrics();
        UpdateScreenPositions(plot, Bounds.Width / 2.0, Bounds.Height / 2.0);
        var normalizedRect = new Rect(
            Math.Min(screenRect.X, screenRect.X + screenRect.Width),
            Math.Min(screenRect.Y, screenRect.Y + screenRect.Height),
            Math.Abs(screenRect.Width),
            Math.Abs(screenRect.Height));

        var result = new List<long>();
        for (var i = 0; i < Graph.Nodes.Count; i++)
        {
            if (normalizedRect.Contains(_screenPositions[i]))
            {
                result.Add(Graph.Nodes[i].Id);
            }
        }

        return result;
    }

    public bool TryScreenToWorld(Point screenPoint, out Point worldPoint)
    {
        worldPoint = default;
        if (Graph is null || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return false;
        }

        var plot = GetPlotMetrics();
        var viewCenterX = Bounds.Width / 2.0;
        var viewCenterY = Bounds.Height / 2.0;
        var baseX = ((screenPoint.X - viewCenterX - _panOffset.X) / _zoom) + viewCenterX;
        var baseY = ((screenPoint.Y - viewCenterY - _panOffset.Y) / _zoom) + viewCenterY;
        var worldX = (baseX - plot.OriginX) / plot.Width;
        var worldY = (baseY - plot.OriginY) / plot.Height;
        worldPoint = new Point(worldX, worldY);
        return true;
    }

    public Point WorldToScreen(Point worldPoint)
    {
        var plot = GetPlotMetrics();
        return ToScreenPointFast(worldPoint.X, worldPoint.Y, plot, Bounds.Width / 2.0, Bounds.Height / 2.0);
    }

    public void PanBy(double dxPixels, double dyPixels)
    {
        _panOffset = new Point(_panOffset.X + dxPixels, _panOffset.Y + dyPixels);
        UpdateAnimationTimerState();
        if (UseUniverseFastPath())
        {
            var plot = GetPlotMetrics();
            UpdateScreenPositions(plot, Bounds.Width / 2.0, Bounds.Height / 2.0);
            _hitTestGrid.Rebuild(_screenPositions, Graph!.Nodes.Count, Bounds.Size);
        }

        InvalidateCamera();
    }

    public void ZoomBy(double factor)
    {
        if (factor <= 0)
        {
            return;
        }

        var oldZoom = _zoom;
        var newZoom = Math.Clamp(_zoom * factor, GetMinZoom(), GetMaxZoom());
        if (Math.Abs(newZoom - oldZoom) < 1e-9)
        {
            return;
        }

        var center = new Point(Bounds.Width * 0.5, Bounds.Height * 0.5);
        var plot = GetPlotMetrics();
        var viewCenterX = Bounds.Width / 2.0;
        var viewCenterY = Bounds.Height / 2.0;
        var baseX = ((center.X - viewCenterX - _panOffset.X) / oldZoom) + viewCenterX;
        var baseY = ((center.Y - viewCenterY - _panOffset.Y) / oldZoom) + viewCenterY;
        var worldX = (baseX - plot.OriginX) / plot.Width;
        var worldY = (baseY - plot.OriginY) / plot.Height;
        var newBaseX = plot.OriginX + (worldX * plot.Width);
        var newBaseY = plot.OriginY + (worldY * plot.Height);

        _zoom = newZoom;
        _panOffset = new Point(
            center.X - (((newBaseX - viewCenterX) * _zoom) + viewCenterX),
            center.Y - (((newBaseY - viewCenterY) * _zoom) + viewCenterY));
        UpdateAnimationTimerState();
        InvalidateCamera(zoomChanged: true);
    }

    private IBrush GetCachedBrush(Color color, double alpha01 = 1.0)
    {
        var a = (byte)Math.Clamp((int)(alpha01 * 255), 0, 255);
        var keyColor = Color.FromArgb(a, color.R, color.G, color.B);
        var key = ((uint)keyColor.A << 24) |
                  ((uint)keyColor.R << 16) |
                  ((uint)keyColor.G << 8) |
                  keyColor.B;

        if (_brushCache.TryGetValue(key, out var brush))
        {
            return brush;
        }

        brush = new ImmutableSolidColorBrush(keyColor);
        _brushCache[key] = brush;
        return brush;
    }

    public override void Render(DrawingContext context)
    {
        RefreshGraphLocalCaches();

        var bounds = Bounds;
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            context.FillRectangle(BackgroundBrush, bounds);
            DrawCenteredText(context, "No map data loaded", bounds);
            return;
        }

        PrepareVisibleState(bounds);
        if (UseUniverseFastPath())
        {
            RenderUniverseFast(context, bounds);
            return;
        }

        if (!TryBlitStaticLayer(context, bounds))
        {
            DrawStaticScene(context, bounds);
        }

        DrawAnimatedOverlays(context, bounds);
    }

    private void RefreshGraphLocalCaches()
    {
        if (ReferenceEquals(_lastGraphForCaches, Graph))
        {
            return;
        }

        _lastGraphForCaches = Graph;
        _nodeLabelCache.Clear();
        _nodeLabelHaloCache.Clear();
        _nodeSecondaryLabelCache.Clear();
        _nodeSecondaryLabelHaloCache.Clear();
        _regionLabelCache.Clear();
        _regionLabelHaloCache.Clear();
        _voronoiWorldPolygonsByNodeId.Clear();
        _voronoiWorldGeometriesByNodeId.Clear();
        _lastGraphForVoronoi = null;
        _lastVoronoiCacheKey = null;
        _indicatorOverlapCache.Clear();
        _overlayOverlapCache.Clear();
        _cachedJumpRouteNodeIds = null;
        _cachedJumpRangeInRangeNodeIds = null;
        CancelVoronoiBuild();
        RebuildGraphCaches();
        _linkBatchGraph = null;
        _regionCentroidGraph = null;
        _nodeGeometryGraph = null;
        _nodeGeometryZoom = double.NaN;
        _voronoiFillGraph = null;
        _voronoiFillPolygonCount = -1;
        _highlightBuiltSelected = null;
        _staticLayerDirty = true;
        _universeBakeValid = false;
    }

    private void PrepareVisibleState(Rect bounds)
    {
        if (Graph is null)
        {
            return;
        }

        (_indicatorExplorationOverlapByNodeId, _indicatorExplorationSourceNodeIds) =
            _indicatorOverlapCache.GetExplorationOverlap(Graph, IndicatorSovUpgradeFilterKeys);
        (_overlayExplorationOverlapByNodeId, _overlayExplorationSourceNodeIds) =
            _overlayOverlapCache.GetExplorationOverlap(Graph, OverlaySovUpgradeFilterKeys);
        EnsureJumpRangeCaches();
        EnsureJumpRouteCaches();
        ReplaceSet(_additionalSelectedSet, AdditionalSelectedNodeIds);
        ReplaceSet(_missingConnectionSet, MissingConnectionNodeIds);
        ReplaceSet(_lyCoveredSet, LyCoverageCoveredNodeIds);
        ReplaceSet(_lyUncoveredSet, LyCoverageUncoveredNodeIds);
        ReplaceSet(_crossRegionConnectorSet, CrossRegionConnectorNodeIds);

        if (ShouldDeferHitGeometry() ||
            (!_staticLayerDirty && ScreenCameraMatches() && !NeedsUniverseRebake()))
        {
            return;
        }

        var plot = GetPlotMetrics();
        UpdateScreenPositions(plot, Bounds.Width / 2.0, Bounds.Height / 2.0);
        if (_lastPanPoint is null || !UseUniverseFastPath())
        {
            _hitTestGrid.Rebuild(_screenPositions, Graph.Nodes.Count, bounds.Size);
        }
    }

    private static void ReplaceSet(HashSet<long> set, IEnumerable<long>? source)
    {
        set.Clear();
        if (source is null)
        {
            return;
        }

        foreach (var id in source)
        {
            set.Add(id);
        }
    }

    private void EnsureJumpRangeCaches()
    {
        if (!ReferenceEquals(_cachedJumpRangeInRangeNodeIds, JumpRangeInRangeNodeIds) ||
            !ReferenceEquals(_cachedJumpRangeOriginNodeIds, JumpRangeOriginNodeIds))
        {
            (_jumpRangeOverlapByNodeId, _jumpRangeOriginNodeIds) =
                MapOverlapCalculator.BuildNodeOverlapCounts(JumpRangeInRangeNodeIds, JumpRangeOriginNodeIds);
            _cachedJumpRangeInRangeNodeIds = JumpRangeInRangeNodeIds;
            _cachedJumpRangeOriginNodeIds = JumpRangeOriginNodeIds;
        }

        if (!ReferenceEquals(_cachedJumpRangeOriginsDisplay, JumpRangeOriginsDisplay))
        {
            _jumpRangeOriginColorByNodeId = BuildJumpRangeOriginColorMap(JumpRangeOriginsDisplay);
            _cachedJumpRangeOriginsDisplay = JumpRangeOriginsDisplay;
        }
    }

    private void EnsureJumpRouteCaches()
    {
        if (ReferenceEquals(_cachedJumpRouteNodeIds, JumpRouteNodeIds) &&
            ReferenceEquals(_cachedJumpRouteSkippedNodeIds, JumpRouteSkippedNodeIds))
        {
            return;
        }

        _cachedJumpRouteNodeIds = JumpRouteNodeIds;
        _cachedJumpRouteSkippedNodeIds = JumpRouteSkippedNodeIds;
        _jumpRouteOrderedIds.Clear();
        _jumpRouteOrderByNodeId.Clear();
        _jumpRouteColorByNodeId.Clear();
        _jumpRouteLegs.Clear();
        _jumpRouteSkippedSet.Clear();
        if (JumpRouteNodeIds is not null)
        {
            _jumpRouteOrderedIds.AddRange(JumpRouteNodeIds);
        }

        if (JumpRouteSkippedNodeIds is not null)
        {
            foreach (var id in JumpRouteSkippedNodeIds)
            {
                _jumpRouteSkippedSet.Add(id);
            }
        }

        for (var i = 0; i < _jumpRouteOrderedIds.Count; i++)
        {
            if (!_jumpRouteOrderByNodeId.ContainsKey(_jumpRouteOrderedIds[i]))
            {
                _jumpRouteOrderByNodeId[_jumpRouteOrderedIds[i]] = i + 1;
                _jumpRouteColorByNodeId[_jumpRouteOrderedIds[i]] = GetJumpRouteStepColor(i, Math.Max(1, _jumpRouteOrderedIds.Count - 1));
            }
        }

        if (_jumpRouteOrderedIds.Count >= 2)
        {
            for (var i = 0; i < _jumpRouteOrderedIds.Count - 1; i++)
            {
                if (!_nodeIndexById.TryGetValue(_jumpRouteOrderedIds[i], out var fromIdx) ||
                    !_nodeIndexById.TryGetValue(_jumpRouteOrderedIds[i + 1], out var toIdx))
                {
                    continue;
                }

                _jumpRouteLegs.Add((fromIdx, toIdx, GetJumpRouteStepColor(i, _jumpRouteOrderedIds.Count - 1)));
            }
        }
    }

    private bool ShouldDeferHitGeometry()
    {
        return UseUniverseFastPath() && _lastPanPoint is not null;
    }

    private bool IsUniverseInteractionActive()
    {
        return _lastPanPoint is not null || DateTime.UtcNow < _universeWheelIdleUntil;
    }

    private bool CanBakeUniverseNow()
    {
        if (IsUniverseInteractionActive())
        {
            return false;
        }

        if (DateTime.UtcNow >= _universeBakeNotBefore)
        {
            return true;
        }

        return Math.Abs(Bounds.Width - _bakedViewport.Width) > 0.5 ||
               Math.Abs(Bounds.Height - _bakedViewport.Height) > 0.5;
    }

    private void ScheduleUniverseIdleBake(bool resetDeadline)
    {
        var now = DateTime.UtcNow;
        if (resetDeadline || _universeBakeNotBefore <= now)
        {
            _universeBakeNotBefore = now.AddMilliseconds(150);
        }

        var delay = _universeBakeNotBefore - now;
        if (delay < TimeSpan.FromMilliseconds(1))
        {
            delay = TimeSpan.FromMilliseconds(1);
        }

        _universeIdleBakeTimer.Interval = delay;
        _universeIdleBakeTimer.Start();
    }

    private void OnUniverseIdleBakeTick()
    {
        if (!UseUniverseFastPath())
        {
            return;
        }

        if (_lastPanPoint is not null)
        {
            ScheduleUniverseIdleBake(resetDeadline: false);
            return;
        }

        if (DateTime.UtcNow < _universeWheelIdleUntil || DateTime.UtcNow < _universeBakeNotBefore)
        {
            ScheduleUniverseIdleBake(resetDeadline: false);
            return;
        }

        if (NeedsUniverseRebake())
        {
            InvalidateVisual();
        }
    }

    private bool NeedsUniverseRebake()
    {
        if (!UseUniverseFastPath())
        {
            return false;
        }

        if (!_universeBakeValid || _universeTiles.Count == 0 || _staticLayerDirty)
        {
            return true;
        }

        if (Math.Abs(Bounds.Width - _bakedViewport.Width) > 0.5 ||
            Math.Abs(Bounds.Height - _bakedViewport.Height) > 0.5)
        {
            return true;
        }

        return Math.Abs(_zoom - _bakedZoom) > 1e-6;
    }

    private void RenderUniverseFast(DrawingContext context, Rect bounds)
    {
        if (NeedsUniverseRebake())
        {
            if (_universeTiles.Count == 0 || CanBakeUniverseNow())
            {
                try
                {
                    BakeUniverseLayer(bounds);
                }
                catch
                {
                    _universeBakeBaseOnly = false;
                    if (_universeTiles.Count == 0)
                    {
                        DisposeStaticLayer();
                    }
                }
            }
            else
            {
                ScheduleUniverseIdleBake(resetDeadline: false);
            }
        }

        if (_lastPanPoint is not null && _leftDragPanned && _universeTiles.Count > 0)
        {
            using (context.PushClip(new Rect(bounds.Size)))
            {
                context.FillRectangle(BackgroundBrush, new Rect(bounds.Size));
                BlitUniverseLayer(context);
                DrawUniverseDragDecorations(context, bounds);
                DrawUniverseInteractiveOverlay(context, bounds);
            }

            return;
        }

        DrawStaticScene(context, bounds);
        DrawAnimatedOverlays(context, bounds);
    }

    private void BakeUniverseLayer(Rect bounds)
    {
        _bakeMargin = 0;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var plot = GetPlotMetrics();
        var viewCenterX = bounds.Width / 2.0;
        var viewCenterY = bounds.Height / 2.0;
        var minWorldX = _graphMinX;
        var maxWorldX = _graphMaxX;
        var minWorldY = _graphMinY;
        var maxWorldY = _graphMaxY;
        if (NodeBackgroundColorMode != MapNodeColorMode.None)
        {
            foreach (var polygon in _voronoiWorldPolygonsByNodeId.Values)
            {
                foreach (var point in polygon)
                {
                    minWorldX = Math.Min(minWorldX, point.X);
                    maxWorldX = Math.Max(maxWorldX, point.X);
                    minWorldY = Math.Min(minWorldY, point.Y);
                    maxWorldY = Math.Max(maxWorldY, point.Y);
                }
            }
        }

        var minX = PanFreeScreenX(minWorldX, plot, viewCenterX, _zoom) - UniverseAtlasPad;
        var maxX = PanFreeScreenX(maxWorldX, plot, viewCenterX, _zoom) + UniverseAtlasPad;
        var minY = PanFreeScreenY(minWorldY, plot, viewCenterY, _zoom) - UniverseAtlasPad;
        var maxY = PanFreeScreenY(maxWorldY, plot, viewCenterY, _zoom) + UniverseAtlasPad;
        var dipWidth = Math.Max(1.0, maxX - minX);
        var dipHeight = Math.Max(1.0, maxY - minY);
        var nativePixels = dipWidth * scaling * dipHeight * scaling;
        var resolution = nativePixels > MaxUniverseAtlasPixels
            ? Math.Sqrt(MaxUniverseAtlasPixels / nativePixels)
            : 1.0;
        var bakeScaling = Math.Max(0.05, scaling * resolution);
        var maxTileDip = Math.Max(1.0, (MaxUniverseTileEdge - 2) / bakeScaling);
        const double overlapDip = 200.0;
        var tileDip = Math.Max(256.0, maxTileDip - overlapDip);
        EnsureUniverseVoronoiFills();

        var fresh = new List<UniverseAtlasTile>();
        try
        {
            for (var top = minY; top < maxY - 0.01; top += tileDip)
            {
                for (var left = minX; left < maxX - 0.01; left += tileDip)
                {
                    var tileWidth = Math.Min(tileDip + overlapDip, maxX - left);
                    var tileHeight = Math.Min(tileDip + overlapDip, maxY - top);
                    var pixelSize = new PixelSize(
                        Math.Max(1, (int)Math.Ceiling(tileWidth * bakeScaling)),
                        Math.Max(1, (int)Math.Ceiling(tileHeight * bakeScaling)));
                    var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * bakeScaling, 96 * bakeScaling));
                    using (var ctx = bitmap.CreateDrawingContext(true))
                    {
                        DrawCachedUniverseScene(
                            ctx,
                            new Rect(0, 0, tileWidth, tileHeight),
                            WorldToAtlasMatrix(plot, viewCenterX, viewCenterY, _zoom, left, top),
                            left,
                            top,
                            viewCenterX,
                            viewCenterY);
                    }

                    fresh.Add(new UniverseAtlasTile(bitmap, left, top));
                }
            }
        }
        catch
        {
            foreach (var tile in fresh)
            {
                tile.Dispose();
            }

            throw;
        }

        _staticLayer?.Dispose();
        _staticLayer = null;
        DisposeUniverseAtlas();
        _universeTiles.AddRange(fresh);
        _bakedZoom = _zoom;
        _bakedViewport = bounds.Size;
        _universeBakeValid = true;
        _staticLayerDirty = false;
    }

    private static double PanFreeScreenX(double worldX, PlotMetrics plot, double viewCenterX, double zoom)
    {
        var x = plot.OriginX + (worldX * plot.Width);
        return ((x - viewCenterX) * zoom) + viewCenterX;
    }

    private static double PanFreeScreenY(double worldY, PlotMetrics plot, double viewCenterY, double zoom)
    {
        var y = plot.OriginY + (worldY * plot.Height);
        return ((y - viewCenterY) * zoom) + viewCenterY;
    }

    private static Matrix WorldToAtlasMatrix(
        PlotMetrics plot,
        double viewCenterX,
        double viewCenterY,
        double zoom,
        double originX,
        double originY)
    {
        var scaleX = plot.Width * zoom;
        var scaleY = plot.Height * zoom;
        var offsetX = ((plot.OriginX - viewCenterX) * zoom) + viewCenterX - originX;
        var offsetY = ((plot.OriginY - viewCenterY) * zoom) + viewCenterY - originY;
        return new Matrix(scaleX, 0, 0, scaleY, offsetX, offsetY);
    }

    private void BlitUniverseLayer(DrawingContext context)
    {
        if (_universeTiles.Count == 0)
        {
            return;
        }

        var zoomRatio = Math.Abs(_bakedZoom) < 1e-9 ? 1.0 : _zoom / _bakedZoom;
        var viewCenterX = Bounds.Width / 2.0;
        var viewCenterY = Bounds.Height / 2.0;
        foreach (var tile in _universeTiles)
        {
            var offsetX = ((tile.OriginX - viewCenterX) * zoomRatio) + viewCenterX + _panOffset.X;
            var offsetY = ((tile.OriginY - viewCenterY) * zoomRatio) + viewCenterY + _panOffset.Y;
            var bitmapSize = tile.Bitmap.Size;
            var destWidth = bitmapSize.Width * zoomRatio;
            var destHeight = bitmapSize.Height * zoomRatio;
            if (offsetX + destWidth < 0 ||
                offsetY + destHeight < 0 ||
                offsetX > Bounds.Width ||
                offsetY > Bounds.Height)
            {
                continue;
            }

            using (context.PushTransform(new Matrix(zoomRatio, 0, 0, zoomRatio, offsetX, offsetY)))
            {
                DrawRenderTarget(context, tile.Bitmap, new Rect(bitmapSize));
            }
        }
    }

    private bool TryBlitStaticLayer(DrawingContext context, Rect bounds)
    {
        if (!EnsureStaticLayer(bounds) || _staticLayer is null)
        {
            return false;
        }

        DrawRenderTarget(context, _staticLayer, new Rect(bounds.Size));
        return true;
    }

    private static void DrawRenderTarget(DrawingContext context, RenderTargetBitmap bitmap, Rect destination)
    {
        // DrawImage's source rect is in device pixels. Bitmap.Size is DIPs, so on a scaled
        // display that samples only the top-left of the texture and shifts the painted map
        // away from hit-testing and the live overlays.
        context.DrawImage(
            bitmap,
            new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
            destination);
    }

    private bool EnsureStaticLayer(Rect bounds)
    {
        if (bounds.Width < 2 || bounds.Height < 2)
        {
            return false;
        }

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var pixelSize = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(bounds.Width * scaling)),
            Math.Max(1, (int)Math.Ceiling(bounds.Height * scaling)));
        if (!_staticLayerDirty &&
            _staticLayer is not null &&
            _staticLayer.PixelSize == pixelSize)
        {
            return true;
        }

        try
        {
            if (_staticLayer is null || _staticLayer.PixelSize != pixelSize)
            {
                _staticLayer?.Dispose();
                _staticLayer = new RenderTargetBitmap(pixelSize, new Vector(96 * scaling, 96 * scaling));
            }

            using (var ctx = _staticLayer.CreateDrawingContext(true))
            {
                DrawStaticScene(ctx, bounds);
            }

            _staticLayerDirty = false;
            return true;
        }
        catch
        {
            DisposeStaticLayer();
            return false;
        }
    }

    private void DrawStaticScene(DrawingContext context, Rect bounds)
    {
        var drawInteractive = true;
        if (drawInteractive)
        {
            _intelOverlayLinks.Clear();
            _sovUpgradeIconHitTargets.Clear();
        }

        context.FillRectangle(BackgroundBrush, bounds);

        var plot = GetPlotMetrics();
        var viewCenterX = Bounds.Width / 2.0;
        var viewCenterY = Bounds.Height / 2.0;
        if (ShowEditorGrid)
        {
            DrawEditorGrid(context, plot, viewCenterX, viewCenterY);
        }

        var renderVoronoiBackground = NodeBackgroundColorMode != MapNodeColorMode.None && _zoom >= GetVoronoiZoomThreshold();
        if (renderVoronoiBackground && _voronoiWorldGeometriesByNodeId.Count == 0)
        {
            EnsureVoronoiWorldPolygons();
        }

        if (renderVoronoiBackground && _voronoiWorldGeometriesByNodeId.Count > 0)
        {
            if (_universeBakeBaseOnly)
            {
                DrawUniverseVoronoiBatches(context, plot);
            }
            else
            {
                const double margin = 220;
                var worldToScreen = GetWorldToScreenMatrix(plot);
                var worldScale = Math.Max(1e-9, ((plot.Width + plot.Height) * 0.5) * _zoom);
                var voronoiBorderPen = _penCache.Get(Color.Parse("#AA0D131D"), 0.8 / worldScale);
                _visibleVoronoiNodeIndexes.Clear();

                using (context.PushTransform(worldToScreen))
                {
                    for (var i = 0; i < Graph!.Nodes.Count; i++)
                    {
                        var centerPoint = _screenPositions[i];
                        if (!IsPointVisible(centerPoint, bounds, margin))
                        {
                            continue;
                        }

                        _visibleVoronoiNodeIndexes.Add(i);
                        DrawVoronoiCellWorld(context, Graph.Nodes[i], voronoiBorderPen);
                    }
                }

                foreach (var index in _visibleVoronoiNodeIndexes)
                {
                    var holeRadius = 10.8 * GetUniverseNodeZoomScale();
                    context.DrawEllipse(NodeHoleBrush, null, _screenPositions[index], holeRadius, holeRadius);
                }
            }
        }

        if (_universeBakeBaseOnly)
        {
            DrawUniverseLinkBatches(context, plot);
        }

        foreach (var link in Graph!.Links)
        {
            if (_universeBakeBaseOnly)
            {
                break;
            }

            if (!_nodeIndexById.TryGetValue(link.FromId, out var fromIndex) ||
                !_nodeIndexById.TryGetValue(link.ToId, out var toIndex))
            {
                continue;
            }

            var from = _screenPositions[fromIndex];
            var to = _screenPositions[toIndex];

            if (!IsSegmentPotentiallyVisible(from, to, bounds, 48))
            {
                continue;
            }

            var isSelectedLink = drawInteractive &&
                                 SelectedNodeId is not null &&
                                 (link.FromId == SelectedNodeId.Value || link.ToId == SelectedNodeId.Value);
            var isHoveredLink = drawInteractive &&
                                _hoveredNodeId is not null &&
                                (link.FromId == _hoveredNodeId.Value || link.ToId == _hoveredNodeId.Value);
            var isSearchConstellationLink = drawInteractive &&
                                            _searchHighlightedConstellationId is not null &&
                                            _nodeById.TryGetValue(link.FromId, out var fromNodeForSearchConstellation) &&
                                            _nodeById.TryGetValue(link.ToId, out var toNodeForSearchConstellation) &&
                                            fromNodeForSearchConstellation.ConstellationId == _searchHighlightedConstellationId.Value &&
                                            toNodeForSearchConstellation.ConstellationId == _searchHighlightedConstellationId.Value;
            var isSearchRegionLink = drawInteractive &&
                                     _searchHighlightedRegionId is not null &&
                                     _nodeById.TryGetValue(link.FromId, out var fromNodeForSearchRegion) &&
                                     _nodeById.TryGetValue(link.ToId, out var toNodeForSearchRegion) &&
                                     (fromNodeForSearchRegion.RegionId == _searchHighlightedRegionId.Value ||
                                      toNodeForSearchRegion.RegionId == _searchHighlightedRegionId.Value);
            var activeRegionId = _selectedRegionId ?? _hoveredRegionId;
            var isRegionLink = drawInteractive &&
                               activeRegionId is not null &&
                               _nodeById.TryGetValue(link.FromId, out var fromNodeForRegion) &&
                               _nodeById.TryGetValue(link.ToId, out var toNodeForRegion) &&
                               (fromNodeForRegion.RegionId == activeRegionId.Value || toNodeForRegion.RegionId == activeRegionId.Value);

            var basePen = GetLinkPen(LinksPen, SameConstellationPen, SameRegionPen, CrossRegionPen, link, _nodeById);
            var pen = isSearchConstellationLink
                ? HighlightedSearchConstellationPen
                : isSelectedLink || isHoveredLink || isSearchRegionLink
                    ? GetHighlightedPen(basePen, LinksPen, SameConstellationPen, SameRegionPen, CrossRegionPen, HighlightedDefaultPen, HighlightedSameConstellationPen, HighlightedSameRegionPen, HighlightedCrossRegionPen)
                    : isRegionLink
                        ? GetHighlightedPen(basePen, LinksPen, SameConstellationPen, SameRegionPen, CrossRegionPen, RegionEmphasisDefaultPen, RegionEmphasisSameConstellationPen, RegionEmphasisSameRegionPen, RegionEmphasisCrossRegionPen)
                        : basePen;

            context.DrawLine(pen, from, to);
        }

        if (ShowAnsiblexNetwork &&
            ViewMode != MapViewMode.UniverseRegions &&
            AnsiblexLinks is not null)
        {
            foreach (var link in AnsiblexLinks)
            {
                if (!_nodeIndexById.TryGetValue(link.FromId, out var fromIndex) ||
                    !_nodeIndexById.TryGetValue(link.ToId, out var toIndex))
                {
                    continue;
                }

                var from = _screenPositions[fromIndex];
                var to = _screenPositions[toIndex];
                if (!IsSegmentPotentiallyVisible(from, to, bounds, 64))
                {
                    continue;
                }

                var isSelectedLink = drawInteractive &&
                                     SelectedNodeId is not null &&
                                     (link.FromId == SelectedNodeId.Value || link.ToId == SelectedNodeId.Value);
                var isHoveredLink = drawInteractive &&
                                    _hoveredNodeId is not null &&
                                    (link.FromId == _hoveredNodeId.Value || link.ToId == _hoveredNodeId.Value);
                DrawCurvedAnsiblexLink(context, from, to, link, highlighted: isSelectedLink || isHoveredLink, animate: false);
            }
        }

        var labelBudget = GetLabelBudget();
        var shouldShowInlineLabels = ShouldShowInlineLabels(plot);
        foreach (var (fromIdx, toIdx, legColor) in _jumpRouteLegs)
        {
            context.DrawLine(_penCache.Get(legColor, 2.5), _screenPositions[fromIdx], _screenPositions[toIdx]);
        }

        var labelsDrawn = 0;
        if (_universeBakeBaseOnly)
        {
            DrawUniverseNodeBatches(context);
        }

        for (var i = 0; i < Graph.Nodes.Count; i++)
        {
            var node = Graph.Nodes[i];
            var p = _screenPositions[i];

            if (!IsPointVisible(p, bounds, 24))
            {
                continue;
            }

            var isSelected = drawInteractive && !_universeBakeBaseOnly && (SelectedNodeId == node.Id || _additionalSelectedSet.Contains(node.Id));
            var isHovered = drawInteractive && !_universeBakeBaseOnly && _hoveredNodeId == node.Id;
            var isSearchHighlighted = drawInteractive && !_universeBakeBaseOnly && (_searchHighlightedNodeId == node.Id ||
                                      (_searchHighlightedConstellationId is not null && node.ConstellationId == _searchHighlightedConstellationId.Value) ||
                                      (_searchHighlightedRegionId is not null && node.RegionId == _searchHighlightedRegionId.Value));
            var activeRegionId = _selectedRegionId ?? _hoveredRegionId;
            var isInActiveRegion = activeRegionId is not null && node.RegionId == activeRegionId.Value;
            var isSelectedRegionNode = _selectedRegionId is not null && node.RegionId == _selectedRegionId.Value;
            var universeNodeScale = GetUniverseNodeZoomScale();
            var radius = (isSelected ? 9.3 : isHovered ? 8.4 : isSearchHighlighted ? 7.8 : 6.75) * universeNodeScale;
            var brush = isSelected
                ? SelectedBrush
                : isHovered
                    ? HoveredBrush
                    : isSearchHighlighted
                        ? SelectedBrush
                            : isSelectedRegionNode
                                ? SelectedBrush
                                : isInActiveRegion
                                    ? RegionSelectedBrush
                                : ShowEditorGrid && _crossRegionConnectorSet.Contains(node.Id)
                                        ? EditorCrossRegionConnectorBrush
                                        : GetCachedBrush(GetNodeBaseColor(node, NodeColorMode));

            if (ViewMode == MapViewMode.UniverseRegions)
            {
                var text = new FormattedText(
                    node.Name,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    RegionCardTypeface,
                    12.5,
                    Brushes.White);
                var baseColor = GetUniverseRegionOwnerColor(node.RegionName ?? node.Name);
                var fillColor = isSelected
                    ? BlendColors(baseColor, Color.Parse("#FFFFFF"), 0.24)
                    : isHovered
                        ? BlendColors(baseColor, Color.Parse("#FFFFFF"), 0.14)
                        : baseColor;
                var bgColor = BlendColors(fillColor, Color.Parse("#0B1220"), 0.68);
                var borderColor = isSelected
                    ? BlendColors(baseColor, Color.Parse("#FFFFFF"), 0.48)
                    : isHovered
                        ? BlendColors(baseColor, Color.Parse("#FFFFFF"), 0.32)
                        : BlendColors(baseColor, Color.Parse("#000000"), 0.16);
                var rect = GetUniverseRegionNodeRect(node, p, isHovered ? 1.04 : 1.0);

                var outer = new Rect(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2);
                context.FillRectangle(GetCachedBrush(Color.FromArgb(110, bgColor.R, bgColor.G, bgColor.B)), outer, 6);
                context.FillRectangle(GetCachedBrush(bgColor), rect, 5);
                context.DrawRectangle(new Pen(GetCachedBrush(borderColor), isSelected ? 1.9 : 1.35), rect, 5);
                context.DrawText(text, new Point(rect.X + ((rect.Width - text.Width) / 2), rect.Y + ((rect.Height - text.Height) / 2)));
                continue;
            }

            if (!_universeBakeBaseOnly)
            {
                context.DrawEllipse(brush, NodeOutlinePen, p, radius, radius);
            }

            var hostileScoreForNode = 0;
            var hasHostileScore = IntelHostileScoresByNodeId is not null &&
                                  IntelHostileScoresByNodeId.TryGetValue(node.Id, out hostileScoreForNode) &&
                                  hostileScoreForNode > 0;
            if (hasHostileScore)
            {
                IReadOnlyList<string> intelRingIcons = ["crosshair"];
                if (IntelIconKeysByNodeId is not null &&
                    IntelIconKeysByNodeId.TryGetValue(node.Id, out var configuredIcons) &&
                    configuredIcons.Count > 0)
                {
                    intelRingIcons = configuredIcons;
                }

                DrawIntelRingWithIcons(context, p, radius, intelRingIcons, hostileScoreForNode, shouldShowInlineLabels && !EnableIntelRingAnimations);
            }
            if (ShowMissingConnectionMarkers &&
                _missingConnectionSet.Contains(node.Id) &&
                (ViewMode == MapViewMode.Region || ShowEditorGrid))
            {
                var ringRadius = radius + 2.7;
                context.DrawEllipse(null, MissingConnectionRingPen, p, ringRadius, ringRadius);
            }
            if (_jumpRangeOverlapByNodeId.TryGetValue(node.Id, out var jumpRangeOverlapCount) &&
                jumpRangeOverlapCount > 0)
            {
                var inRangeRadius = radius + 3.6;
                if (JumpRangeMembershipByNodeId is not null &&
                    JumpRangeMembershipByNodeId.TryGetValue(node.Id, out var sourceOriginIds) &&
                    sourceOriginIds.Count > 0)
                {
                    DrawJumpRangeSegments(context, p, inRangeRadius, sourceOriginIds);
                }
                else
                {
                    context.DrawEllipse(null, JumpRangeInRangeRingPen, p, inRangeRadius, inRangeRadius);
                }
            }
            if (_jumpRangeOriginNodeIds.Contains(node.Id))
            {
                var originRadius = radius + 6.2;
                context.DrawEllipse(null, JumpRangeOriginRingPen, p, originRadius, originRadius);
            }
            if (ShowAnsiblexNetwork &&
                AnsiblexCapitalSystemId is long capitalId &&
                node.Id == capitalId)
            {
                context.DrawEllipse(null, AnsiblexCapitalRingPen, p, radius + 5.4, radius + 5.4);
            }
            if (_lyCoveredSet.Contains(node.Id))
            {
                var coveredRadius = radius + 7.8;
                context.DrawEllipse(null, LyCoverageCoveredRingPen, p, coveredRadius, coveredRadius);
            }
            if (_lyUncoveredSet.Contains(node.Id))
            {
                var uncoveredRadius = radius + 9.8;
                context.DrawEllipse(null, LyCoverageUncoveredRingPen, p, uncoveredRadius, uncoveredRadius);
            }
            if (_jumpRouteOrderByNodeId.ContainsKey(node.Id))
            {
                var routeRadius = radius + 20.2;
                var routeColor = _jumpRouteColorByNodeId.TryGetValue(node.Id, out var nodeRouteColor) ? nodeRouteColor : Color.Parse("#63D3FF");
                var routePen = _penCache.Get(routeColor, 1.9);
                context.DrawEllipse(null, routePen, p, routeRadius, routeRadius);
                if (_jumpRouteOrderByNodeId.TryGetValue(node.Id, out var order))
                {
                    var numberLabel = new FormattedText(
                        order.ToString(CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        NodeLabelTypeface,
                        9.5,
                        JumpRouteNumberTextBrush);
                    var bubbleRadius = 7.2;
                    var bubbleCenter = new Point(p.X + routeRadius + 5.0, p.Y - routeRadius - 4.0);
                    context.DrawEllipse(GetCachedBrush(routeColor), null, bubbleCenter, bubbleRadius, bubbleRadius);
                    context.DrawText(
                        numberLabel,
                        new Point(
                            bubbleCenter.X - (numberLabel.Width / 2),
                            bubbleCenter.Y - (numberLabel.Height / 2)));
                }
            }
            if (_jumpRouteSkippedSet.Contains(node.Id))
            {
                var skippedRadius = radius + 23.4;
                context.DrawEllipse(null, JumpRouteSkippedRingPen, p, skippedRadius, skippedRadius);
            }
        }

        if (ViewMode != MapViewMode.UniverseRegions)
        {
            DrawUserSystemMarks(context, bounds);
        }

        for (var i = 0; i < Graph.Nodes.Count; i++)
        {
            var node = Graph.Nodes[i];
            var p = _screenPositions[i];
            if (ViewMode == MapViewMode.UniverseRegions || !IsPointVisible(p, bounds, 24))
            {
                continue;
            }

            var isSelected = drawInteractive && !_universeBakeBaseOnly && (SelectedNodeId == node.Id || _additionalSelectedSet.Contains(node.Id));
            var isHovered = drawInteractive && !_universeBakeBaseOnly && _hoveredNodeId == node.Id;
            var universeNodeScale = GetUniverseNodeZoomScale();
            var radius = (isSelected ? 9.3 : isHovered ? 8.4 : 6.75) * universeNodeScale;
            var badgeY = p.Y - radius - 9.0;
            if (AlwaysShowHubWormholes && node.HubWormholeConnections.Count > 0)
            {
                switch (HubWormholeMarkerMode)
                {
                    case HubWormholeMarkerMode.Ring:
                        DrawHubWormholeRing(context, p, node, radius);
                        break;
                    case HubWormholeMarkerMode.Halo:
                        DrawHubWormholeHalo(context, p, node, radius);
                        break;
                    default:
                    {
                        var wormholeSize = MeasureHubWormholeBadge(node);
                        var wormholeTopLeft = new Point(p.X - radius - NodeBadgeGap - wormholeSize.Width, badgeY);
                        DrawHubWormholeBeacon(context, wormholeTopLeft, node);
                        break;
                    }
                }
            }
            var hasHubWormholeBadge = AlwaysShowHubWormholes &&
                                      node.HubWormholeConnections.Count > 0 &&
                                      HubWormholeMarkerMode == HubWormholeMarkerMode.Badge;
            if (AlwaysShowIncursions && node.HasActiveIncursion)
            {
                var incursionSize = 12.0;
                var incursionX = p.X - radius - NodeBadgeGap - incursionSize;
                if (hasHubWormholeBadge)
                {
                    var wormholeSize = MeasureHubWormholeBadge(node);
                    incursionX -= NodeBadgeGap + wormholeSize.Width;
                }

                DrawIncursionBeacon(context, new Point(incursionX, badgeY));
            }
            if (ShowIndicatorCharacterPresence &&
                CharacterPresenceCountsByNodeId is not null &&
                CharacterPresenceCountsByNodeId.TryGetValue(node.Id, out var localCharacterCount) &&
                localCharacterCount > 0)
            {
                DrawCharacterPresenceBadge(context, p, radius, localCharacterCount);
            }

            var labelVisibilityMargin = ViewMode == MapViewMode.Universe ? 180 : 96;
            var suppressInlineLabel =
                (SelectedNodeId is not null && node.Id == SelectedNodeId.Value) ||
                (_hoveredNodeId is not null && node.Id == _hoveredNodeId.Value);
            if (drawInteractive &&
                !suppressInlineLabel &&
                (shouldShowInlineLabels || isSelected || isHovered) &&
                labelsDrawn < labelBudget &&
                IsPointVisible(p, bounds, labelVisibilityMargin))
            {
                var labelOrigin = GetNodeLabelOrigin(p);
                DrawIndicatorLabel(context, node, labelOrigin);
                labelsDrawn++;
            }
        }

        if (!drawInteractive || _universeBakeBaseOnly)
        {
            return;
        }

        if (ViewMode != MapViewMode.UniverseRegions)
        {
            if (SelectedNodeId is not null &&
                _nodeIndexById.TryGetValue(SelectedNodeId.Value, out var selectedIndex) &&
                _nodeById.TryGetValue(SelectedNodeId.Value, out var selectedNode))
            {
                DrawHoverOverlay(context, _screenPositions[selectedIndex], selectedNode);
            }

            if (_hoveredNodeId is not null &&
                _hoveredNodeId != SelectedNodeId &&
                _nodeIndexById.TryGetValue(_hoveredNodeId.Value, out var hoverIndex) &&
                _nodeById.TryGetValue(_hoveredNodeId.Value, out var hoverNode))
            {
                DrawHoverOverlay(context, _screenPositions[hoverIndex], hoverNode);
            }

            DrawHoveredSystemMarkClusterOverlay(context, bounds);
        }

        if (ShouldShowUniverseRegionLabels())
        {
            DrawUniverseRegionLabels(context);
        }
    }

    private void DrawCachedUniverseScene(
        DrawingContext context,
        Rect clearRect,
        Matrix worldToLocal,
        double originX,
        double originY,
        double viewCenterX,
        double viewCenterY)
    {
        context.FillRectangle(BackgroundBrush, clearRect);
        var plot = GetPlotMetrics();
        var worldScale = Math.Max(1e-9, ((plot.Width + plot.Height) * 0.5) * _zoom);
        EnsureUniverseVoronoiFills();
        var borderPen = ScaleWorldPen(new Pen(new ImmutableSolidColorBrush(Color.Parse("#AA0D131D")), 0.8), worldScale);
        using (context.PushTransform(worldToLocal))
        {
            foreach (var (fill, geometry) in _voronoiFillGeometries)
            {
                context.DrawGeometry(fill, borderPen, geometry);
            }
        }

        if (Graph is null)
        {
            return;
        }

        var nodeRadius = 6.75 * GetUniverseNodeZoomScale();
        var holeRadius = 10.8 * GetUniverseNodeZoomScale();
        var drawHoles = NodeBackgroundColorMode != MapNodeColorMode.None && _voronoiWorldPolygonsByNodeId.Count > 0;
        if (drawHoles)
        {
            for (var i = 0; i < Graph.Nodes.Count; i++)
            {
                var node = Graph.Nodes[i];
                if (!TryGetVoronoiFill(node, out _))
                {
                    continue;
                }

                var point = AtlasLocalPoint(node, plot, viewCenterX, viewCenterY, originX, originY);
                if (!IsPointVisible(point, clearRect, 24))
                {
                    continue;
                }

                context.DrawEllipse(NodeHoleBrush, null, point, holeRadius, holeRadius);
            }
        }

        foreach (var link in Graph.Links)
        {
            if (!_nodeById.TryGetValue(link.FromId, out var fromNode) ||
                !_nodeById.TryGetValue(link.ToId, out var toNode))
            {
                continue;
            }

            var from = AtlasLocalPoint(fromNode, plot, viewCenterX, viewCenterY, originX, originY);
            var to = AtlasLocalPoint(toNode, plot, viewCenterX, viewCenterY, originX, originY);
            if (!IsSegmentPotentiallyVisible(from, to, clearRect, 48))
            {
                continue;
            }

            var pen = GetLinkPen(LinksPen, SameConstellationPen, SameRegionPen, CrossRegionPen, link, _nodeById);
            context.DrawLine(pen, from, to);
        }

        for (var i = 0; i < Graph.Nodes.Count; i++)
        {
            var node = Graph.Nodes[i];
            var point = AtlasLocalPoint(node, plot, viewCenterX, viewCenterY, originX, originY);
            if (!IsPointVisible(point, clearRect, 24))
            {
                continue;
            }

            context.DrawEllipse(GetCachedBrush(GetNodeBaseColor(node, NodeColorMode)), NodeOutlinePen, point, nodeRadius, nodeRadius);
        }
    }

    private Point AtlasLocalPoint(
        MapNode node,
        PlotMetrics plot,
        double viewCenterX,
        double viewCenterY,
        double originX,
        double originY)
    {
        return new Point(
            PanFreeScreenX(node.X, plot, viewCenterX, _zoom) - originX,
            PanFreeScreenY(node.Y, plot, viewCenterY, _zoom) - originY);
    }

    private void EnsureUniverseNodeGeometries(double worldScale)
    {
        var plot = GetPlotMetrics();
        if (ReferenceEquals(_nodeGeometryGraph, Graph) &&
            _nodeGeometryColorMode == NodeColorMode &&
            _nodeGeometryBackgroundMode == NodeBackgroundColorMode &&
            _nodeGeometryVoronoiCount == _voronoiWorldPolygonsByNodeId.Count &&
            Math.Abs(_nodeGeometryZoom - _zoom) < 1e-6 &&
            Math.Abs(_nodeGeometryPlotWidth - plot.Width) < 0.5 &&
            Math.Abs(_nodeGeometryPlotHeight - plot.Height) < 0.5)
        {
            return;
        }

        _nodeGeometryGraph = Graph;
        _nodeGeometryColorMode = NodeColorMode;
        _nodeGeometryBackgroundMode = NodeBackgroundColorMode;
        _nodeGeometryVoronoiCount = _voronoiWorldPolygonsByNodeId.Count;
        _nodeGeometryZoom = _zoom;
        _nodeGeometryPlotWidth = plot.Width;
        _nodeGeometryPlotHeight = plot.Height;
        _nodeGeometries.Clear();
        _voronoiHoleGeometry = null;
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            return;
        }

        var radius = (6.75 * GetUniverseNodeZoomScale()) / worldScale;
        var count = Graph.Nodes.Count;
        var distinct = new HashSet<uint>();
        var colors = new Color[count];
        for (var i = 0; i < count; i++)
        {
            colors[i] = GetNodeBaseColor(Graph.Nodes[i], NodeColorMode);
            distinct.Add(UniverseSceneBatch.PackColor(colors[i].R, colors[i].G, colors[i].B));
        }

        var quantize = UniverseSceneBatch.ShouldQuantize(distinct.Count);
        var groups = new Dictionary<uint, (Color Color, StreamGeometry Geometry, StreamGeometryContext Context)>();
        for (var i = 0; i < count; i++)
        {
            var color = colors[i];
            var key = quantize
                ? UniverseSceneBatch.QuantizeColorKey(color.R, color.G, color.B)
                : UniverseSceneBatch.PackColor(color.R, color.G, color.B);
            if (!groups.TryGetValue(key, out var group))
            {
                var geometry = new StreamGeometry();
                group = (color, geometry, geometry.Open());
                groups[key] = group;
            }

            UniverseSceneBatch.AddCircle(group.Context, new Point(Graph.Nodes[i].X, Graph.Nodes[i].Y), radius);
        }

        foreach (var group in groups.Values)
        {
            group.Context.Dispose();
            _nodeGeometries.Add((group.Color, group.Geometry));
        }

        if (NodeBackgroundColorMode == MapNodeColorMode.None || _voronoiWorldPolygonsByNodeId.Count == 0)
        {
            return;
        }

        var holeRadius = (10.8 * GetUniverseNodeZoomScale()) / worldScale;
        var holes = new StreamGeometry();
        var drewHole = false;
        using (var holeContext = holes.Open())
        {
            for (var i = 0; i < count; i++)
            {
                if (!TryGetVoronoiFill(Graph.Nodes[i], out _))
                {
                    continue;
                }

                UniverseSceneBatch.AddCircle(holeContext, new Point(Graph.Nodes[i].X, Graph.Nodes[i].Y), holeRadius);
                drewHole = true;
            }
        }

        if (drewHole)
        {
            _voronoiHoleGeometry = holes;
        }
    }

    private void EnsureUniverseVoronoiFills()
    {
        var polygonCount = _voronoiWorldPolygonsByNodeId.Count;
        if (ReferenceEquals(_voronoiFillGraph, Graph) &&
            _voronoiFillMode == NodeBackgroundColorMode &&
            _voronoiFillPolygonCount == polygonCount)
        {
            return;
        }

        _voronoiFillGraph = Graph;
        _voronoiFillMode = NodeBackgroundColorMode;
        _voronoiFillPolygonCount = polygonCount;
        _voronoiFillGeometries.Clear();
        if (Graph is null ||
            NodeBackgroundColorMode == MapNodeColorMode.None ||
            polygonCount == 0)
        {
            return;
        }

        var quantizeFills = NodeBackgroundColorMode is MapNodeColorMode.SystemJumps
            or MapNodeColorMode.ShipKills
            or MapNodeColorMode.PodKills
            or MapNodeColorMode.NpcKills
            or MapNodeColorMode.NullsecTrueSec
            or MapNodeColorMode.Hostiles;
        var groups = new Dictionary<uint, (IBrush Fill, StreamGeometry Geometry, StreamGeometryContext Context)>();
        foreach (var node in Graph.Nodes)
        {
            if (!TryGetVoronoiFill(node, out var fill) ||
                fill is not ISolidColorBrush solidFill ||
                !_voronoiWorldPolygonsByNodeId.TryGetValue(node.Id, out var polygon) ||
                polygon.Count < 3)
            {
                continue;
            }

            var color = solidFill.Color;
            var key = quantizeFills
                ? UniverseSceneBatch.QuantizeColorKey(color.R, color.G, color.B)
                : UniverseSceneBatch.PackColor(color.R, color.G, color.B);
            if (!groups.TryGetValue(key, out var group))
            {
                var geometry = new StreamGeometry();
                group = (fill, geometry, geometry.Open());
                groups[key] = group;
            }

            group.Context.BeginFigure(new Point(polygon[0].X, polygon[0].Y), true);
            for (var p = 1; p < polygon.Count; p++)
            {
                group.Context.LineTo(new Point(polygon[p].X, polygon[p].Y));
            }

            group.Context.EndFigure(true);
        }

        foreach (var group in groups.Values)
        {
            group.Context.Dispose();
            _voronoiFillGeometries.Add((group.Fill, group.Geometry));
        }
    }

    private void DrawUniverseBakedLabels(DrawingContext context, Rect bounds)
    {
        if (Graph is null)
        {
            return;
        }

        var plot = GetPlotMetrics();
        var viewCenterX = bounds.Width / 2.0;
        var viewCenterY = bounds.Height / 2.0;
        var labelBudget = GetLabelBudget();
        var labelsDrawn = 0;
        const double labelVisibilityMargin = 180;
        for (var i = 0; i < Graph.Nodes.Count; i++)
        {
            if (labelsDrawn >= labelBudget)
            {
                break;
            }

            var node = Graph.Nodes[i];
            var point = ToScreenPointFast(node.X, node.Y, plot, viewCenterX, viewCenterY);
            if (!IsPointVisible(point, bounds, labelVisibilityMargin))
            {
                continue;
            }

            if ((SelectedNodeId is not null && node.Id == SelectedNodeId.Value) ||
                (_hoveredNodeId is not null && node.Id == _hoveredNodeId.Value))
            {
                continue;
            }

            DrawIndicatorLabel(context, node, GetNodeLabelOrigin(point));
            labelsDrawn++;
        }
    }

    private void DrawUniverseLinkBatches(DrawingContext context, PlotMetrics plot)
    {
        EnsureUniverseLinkGeometries();
        var worldScale = Math.Max(1e-9, ((plot.Width + plot.Height) * 0.5) * _zoom);
        using (context.PushTransform(GetWorldToScreenMatrix(plot)))
        {
            DrawWorldLinkGeometry(context, _linkSameConstellationGeometry, SameConstellationPen, worldScale);
            DrawWorldLinkGeometry(context, _linkSameRegionGeometry, SameRegionPen, worldScale);
            DrawWorldLinkGeometry(context, _linkCrossRegionGeometry, CrossRegionPen, worldScale);
        }
    }

    private void DrawWorldLinkGeometry(DrawingContext context, StreamGeometry? geometry, Pen screenPen, double worldScale)
    {
        if (geometry is null)
        {
            return;
        }

        context.DrawGeometry(null, ScaleWorldPen(screenPen, worldScale), geometry);
    }

    private Pen ScaleWorldPen(Pen screenPen, double worldScale)
    {
        var thickness = Math.Max(1e-6, screenPen.Thickness / Math.Max(1e-9, worldScale));
        var color = screenPen.Brush is ISolidColorBrush solid ? solid.Color : Colors.White;
        var micro = (int)Math.Round(thickness * 1_000_000.0);
        var key = ((long)color.ToUInt32() << 32) ^ micro;
        if (_worldPenCache.TryGetValue(key, out var pen))
        {
            return pen;
        }

        pen = new Pen(new ImmutableSolidColorBrush(color), thickness);
        _worldPenCache[key] = pen;
        return pen;
    }

    private void EnsureUniverseLinkGeometries()
    {
        if (ReferenceEquals(_linkBatchGraph, Graph) &&
            (_linkSameConstellationGeometry is not null ||
             _linkSameRegionGeometry is not null ||
             _linkCrossRegionGeometry is not null ||
             Graph is null ||
             Graph.Links.Count == 0))
        {
            return;
        }

        _linkBatchGraph = Graph;
        _linkSameConstellationGeometry = null;
        _linkSameRegionGeometry = null;
        _linkCrossRegionGeometry = null;
        if (Graph is null)
        {
            return;
        }

        var sameConstellation = new List<UniverseLinkSegment>();
        var sameRegion = new List<UniverseLinkSegment>();
        var crossRegion = new List<UniverseLinkSegment>();
        UniverseSceneBatch.AppendLinks(Graph.Links, _nodeById, sameConstellation, sameRegion, crossRegion);
        _linkSameConstellationGeometry = UniverseSceneBatch.CreateSegmentGeometry(sameConstellation);
        _linkSameRegionGeometry = UniverseSceneBatch.CreateSegmentGeometry(sameRegion);
        _linkCrossRegionGeometry = UniverseSceneBatch.CreateSegmentGeometry(crossRegion);
    }

    private void DrawUniverseNodeBatches(DrawingContext context)
    {
        if (Graph is null || _screenPositions.Length < Graph.Nodes.Count)
        {
            return;
        }

        var radius = 6.75 * GetUniverseNodeZoomScale();
        var count = Graph.Nodes.Count;
        var distinct = new HashSet<uint>();
        var colors = new Color[count];
        for (var i = 0; i < count; i++)
        {
            colors[i] = GetNodeBaseColor(Graph.Nodes[i], NodeColorMode);
            distinct.Add(UniverseSceneBatch.PackColor(colors[i].R, colors[i].G, colors[i].B));
        }

        var quantize = UniverseSceneBatch.ShouldQuantize(distinct.Count);
        var groups = new Dictionary<uint, (Color Color, StreamGeometry Geometry, StreamGeometryContext Context)>();
        for (var i = 0; i < count; i++)
        {
            var color = colors[i];
            var key = quantize
                ? UniverseSceneBatch.QuantizeColorKey(color.R, color.G, color.B)
                : UniverseSceneBatch.PackColor(color.R, color.G, color.B);
            if (!groups.TryGetValue(key, out var group))
            {
                var geometry = new StreamGeometry();
                group = (color, geometry, geometry.Open());
                groups[key] = group;
            }

            UniverseSceneBatch.AddCircle(group.Context, _screenPositions[i], radius);
        }

        foreach (var group in groups.Values)
        {
            group.Context.Dispose();
            context.DrawGeometry(GetCachedBrush(group.Color), NodeOutlinePen, group.Geometry);
        }
    }

    private void DrawUniverseVoronoiBatches(DrawingContext context, PlotMetrics plot)
    {
        if (Graph is null)
        {
            return;
        }

        var worldScale = Math.Max(1e-9, ((plot.Width + plot.Height) * 0.5) * _zoom);
        var borderPen = ScaleWorldPen(new Pen(new ImmutableSolidColorBrush(Color.Parse("#AA0D131D")), 0.8), worldScale);
        var groups = new Dictionary<uint, (IBrush Fill, StreamGeometry Geometry, StreamGeometryContext Context)>();
        var holeGeometry = new StreamGeometry();
        var holeRadius = 10.8 * GetUniverseNodeZoomScale();
        using (var holes = holeGeometry.Open())
        {
            for (var i = 0; i < Graph.Nodes.Count; i++)
            {
                var node = Graph.Nodes[i];
                if (!TryGetVoronoiFill(node, out var fill))
                {
                    continue;
                }

                if (!_voronoiWorldPolygonsByNodeId.TryGetValue(node.Id, out var polygon) || polygon.Count < 3)
                {
                    continue;
                }

                if (fill is not ISolidColorBrush solidFill)
                {
                    continue;
                }

                var color = solidFill.Color;
                var quantizeFills = NodeBackgroundColorMode is MapNodeColorMode.SystemJumps
                    or MapNodeColorMode.ShipKills
                    or MapNodeColorMode.PodKills
                    or MapNodeColorMode.NpcKills
                    or MapNodeColorMode.NullsecTrueSec
                    or MapNodeColorMode.Hostiles;
                var key = quantizeFills
                    ? UniverseSceneBatch.QuantizeColorKey(color.R, color.G, color.B)
                    : UniverseSceneBatch.PackColor(color.R, color.G, color.B);
                if (!groups.TryGetValue(key, out var group))
                {
                    var geometry = new StreamGeometry();
                    group = (fill, geometry, geometry.Open());
                    groups[key] = group;
                }

                group.Context.BeginFigure(new Point(polygon[0].X, polygon[0].Y), true);
                for (var p = 1; p < polygon.Count; p++)
                {
                    group.Context.LineTo(new Point(polygon[p].X, polygon[p].Y));
                }

                group.Context.EndFigure(true);
                if (i < _screenPositions.Length)
                {
                    UniverseSceneBatch.AddCircle(holes, _screenPositions[i], holeRadius);
                }
            }
        }

        using (context.PushTransform(GetWorldToScreenMatrix(plot)))
        {
            foreach (var group in groups.Values)
            {
                group.Context.Dispose();
                context.DrawGeometry(group.Fill, borderPen, group.Geometry);
            }
        }

        context.DrawGeometry(NodeHoleBrush, null, holeGeometry);
    }

    private bool TryGetVoronoiFill(MapNode node, out IBrush fill)
    {
        fill = NodeHoleBrush;
        if (!_voronoiWorldGeometriesByNodeId.ContainsKey(node.Id))
        {
            return false;
        }

        if (NodeBackgroundColorMode == MapNodeColorMode.JoveObservatory && !node.HasJoveObservatory)
        {
            return false;
        }

        if (NodeBackgroundColorMode == MapNodeColorMode.IceBelts && node.IceFieldCount <= 0)
        {
            return false;
        }

        if (NodeBackgroundColorMode == MapNodeColorMode.Storms && node.StormEffects.Count == 0)
        {
            return false;
        }

        if (NodeBackgroundColorMode == MapNodeColorMode.Wormholes && node.HubWormholeConnections.Count == 0)
        {
            return false;
        }

        if (NodeBackgroundColorMode == MapNodeColorMode.SovUpgrades &&
            !GetVisibleSovUpgrades(node.SovUpgrades, IndicatorSovUpgradeFilterKeys).Any())
        {
            return false;
        }

        if (NodeBackgroundColorMode == MapNodeColorMode.Incursions && !node.HasActiveIncursion)
        {
            return false;
        }

        if (NodeBackgroundColorMode == MapNodeColorMode.SystemJumps && node.SystemJumps <= 0)
        {
            return false;
        }

        if (NodeBackgroundColorMode == MapNodeColorMode.ShipKills && node.ShipKills <= 0)
        {
            return false;
        }

        if (NodeBackgroundColorMode == MapNodeColorMode.PodKills && node.PodKills <= 0)
        {
            return false;
        }

        if (NodeBackgroundColorMode == MapNodeColorMode.NpcKills && node.NpcKills <= 0)
        {
            return false;
        }

        if (NodeBackgroundColorMode == MapNodeColorMode.Hostiles &&
            (IntelHostileScoresByNodeId is null ||
             !IntelHostileScoresByNodeId.TryGetValue(node.Id, out var hostileScore) ||
             hostileScore <= 0))
        {
            return false;
        }

        var color = GetNodeBaseColor(node, NodeBackgroundColorMode);
        fill = GetCachedBrush(BrightenForBackground(color), 0.40);
        return true;
    }

    private void DrawUniverseDragDecorations(DrawingContext context, Rect bounds)
    {
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            return;
        }

        var plot = GetPlotMetrics();
        var viewCenterX = bounds.Width / 2.0;
        var viewCenterY = bounds.Height / 2.0;
        UpdateScreenPositions(plot, viewCenterX, viewCenterY);
        var shouldShowInlineLabels = ShouldShowInlineLabels(plot);
        var universeNodeScale = GetUniverseNodeZoomScale();

        if (ShowAnsiblexNetwork &&
            ViewMode != MapViewMode.UniverseRegions &&
            AnsiblexLinks is not null)
        {
            foreach (var link in AnsiblexLinks)
            {
                if (!_nodeIndexById.TryGetValue(link.FromId, out var fromIndex) ||
                    !_nodeIndexById.TryGetValue(link.ToId, out var toIndex) ||
                    fromIndex >= _screenPositions.Length ||
                    toIndex >= _screenPositions.Length)
                {
                    continue;
                }

                var from = _screenPositions[fromIndex];
                var to = _screenPositions[toIndex];
                if (!IsSegmentPotentiallyVisible(from, to, bounds, 64))
                {
                    continue;
                }

                var highlighted = (SelectedNodeId is not null &&
                                   (link.FromId == SelectedNodeId.Value || link.ToId == SelectedNodeId.Value)) ||
                                  (_hoveredNodeId is not null &&
                                   (link.FromId == _hoveredNodeId.Value || link.ToId == _hoveredNodeId.Value));
                DrawCurvedAnsiblexLink(context, from, to, link, highlighted, animate: false);
            }
        }

        foreach (var (fromIdx, toIdx, legColor) in _jumpRouteLegs)
        {
            if (fromIdx < 0 || toIdx < 0 ||
                fromIdx >= _screenPositions.Length ||
                toIdx >= _screenPositions.Length)
            {
                continue;
            }

            context.DrawLine(_penCache.Get(legColor, 2.5), _screenPositions[fromIdx], _screenPositions[toIdx]);
        }

        for (var i = 0; i < Graph.Nodes.Count; i++)
        {
            var node = Graph.Nodes[i];
            var p = _screenPositions[i];
            if (!IsPointVisible(p, bounds, 48))
            {
                continue;
            }

            var radius = 6.75 * universeNodeScale;
            var hostileScoreForNode = 0;
            var hasHostileScore = IntelHostileScoresByNodeId is not null &&
                                  IntelHostileScoresByNodeId.TryGetValue(node.Id, out hostileScoreForNode) &&
                                  hostileScoreForNode > 0;
            if (hasHostileScore)
            {
                IReadOnlyList<string> intelRingIcons = ["crosshair"];
                if (IntelIconKeysByNodeId is not null &&
                    IntelIconKeysByNodeId.TryGetValue(node.Id, out var configuredIcons) &&
                    configuredIcons.Count > 0)
                {
                    intelRingIcons = configuredIcons;
                }

                DrawIntelRingWithIcons(context, p, radius, intelRingIcons, hostileScoreForNode, shouldShowInlineLabels && !EnableIntelRingAnimations);
            }

            if (_jumpRangeOverlapByNodeId.TryGetValue(node.Id, out var jumpRangeOverlapCount) &&
                jumpRangeOverlapCount > 0)
            {
                var inRangeRadius = radius + 3.6;
                if (JumpRangeMembershipByNodeId is not null &&
                    JumpRangeMembershipByNodeId.TryGetValue(node.Id, out var sourceOriginIds) &&
                    sourceOriginIds.Count > 0)
                {
                    DrawJumpRangeSegments(context, p, inRangeRadius, sourceOriginIds);
                }
                else
                {
                    context.DrawEllipse(null, JumpRangeInRangeRingPen, p, inRangeRadius, inRangeRadius);
                }
            }

            if (_jumpRangeOriginNodeIds.Contains(node.Id))
            {
                context.DrawEllipse(null, JumpRangeOriginRingPen, p, radius + 6.2, radius + 6.2);
            }

            if (ShowAnsiblexNetwork &&
                AnsiblexCapitalSystemId is long capitalId &&
                node.Id == capitalId)
            {
                context.DrawEllipse(null, AnsiblexCapitalRingPen, p, radius + 5.4, radius + 5.4);
            }

            if (_lyCoveredSet.Contains(node.Id))
            {
                context.DrawEllipse(null, LyCoverageCoveredRingPen, p, radius + 7.8, radius + 7.8);
            }

            if (_lyUncoveredSet.Contains(node.Id))
            {
                context.DrawEllipse(null, LyCoverageUncoveredRingPen, p, radius + 9.8, radius + 9.8);
            }

            if (_jumpRouteOrderByNodeId.ContainsKey(node.Id))
            {
                var routeRadius = radius + 20.2;
                var routeColor = _jumpRouteColorByNodeId.TryGetValue(node.Id, out var nodeRouteColor) ? nodeRouteColor : Color.Parse("#63D3FF");
                var routePen = _penCache.Get(routeColor, 1.9);
                context.DrawEllipse(null, routePen, p, routeRadius, routeRadius);
                if (_jumpRouteOrderByNodeId.TryGetValue(node.Id, out var order))
                {
                    var numberLabel = new FormattedText(
                        order.ToString(CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        NodeLabelTypeface,
                        9.5,
                        JumpRouteNumberTextBrush);
                    var bubbleRadius = 7.2;
                    var bubbleCenter = new Point(p.X + routeRadius + 5.0, p.Y - routeRadius - 4.0);
                    context.DrawEllipse(GetCachedBrush(routeColor), null, bubbleCenter, bubbleRadius, bubbleRadius);
                    context.DrawText(
                        numberLabel,
                        new Point(
                            bubbleCenter.X - (numberLabel.Width / 2),
                            bubbleCenter.Y - (numberLabel.Height / 2)));
                }
            }

            if (_jumpRouteSkippedSet.Contains(node.Id))
            {
                context.DrawEllipse(null, JumpRouteSkippedRingPen, p, radius + 23.4, radius + 23.4);
            }

            var badgeY = p.Y - radius - 9.0;
            if (AlwaysShowHubWormholes && node.HubWormholeConnections.Count > 0)
            {
                switch (HubWormholeMarkerMode)
                {
                    case HubWormholeMarkerMode.Ring:
                        DrawHubWormholeRing(context, p, node, radius);
                        break;
                    case HubWormholeMarkerMode.Halo:
                        DrawHubWormholeHalo(context, p, node, radius);
                        break;
                    default:
                    {
                        var wormholeSize = MeasureHubWormholeBadge(node);
                        var wormholeTopLeft = new Point(p.X - radius - NodeBadgeGap - wormholeSize.Width, badgeY);
                        DrawHubWormholeBeacon(context, wormholeTopLeft, node);
                        break;
                    }
                }
            }

            var hasHubWormholeBadge = AlwaysShowHubWormholes &&
                                      node.HubWormholeConnections.Count > 0 &&
                                      HubWormholeMarkerMode == HubWormholeMarkerMode.Badge;
            if (AlwaysShowIncursions && node.HasActiveIncursion)
            {
                var incursionSize = 12.0;
                var incursionX = p.X - radius - NodeBadgeGap - incursionSize;
                if (hasHubWormholeBadge)
                {
                    var wormholeSize = MeasureHubWormholeBadge(node);
                    incursionX -= NodeBadgeGap + wormholeSize.Width;
                }

                DrawIncursionBeacon(context, new Point(incursionX, badgeY));
            }

            if (ShowIndicatorCharacterPresence &&
                CharacterPresenceCountsByNodeId is not null &&
                CharacterPresenceCountsByNodeId.TryGetValue(node.Id, out var localCharacterCount) &&
                localCharacterCount > 0)
            {
                DrawCharacterPresenceBadge(context, p, radius, localCharacterCount);
            }
        }

        if (ViewMode != MapViewMode.UniverseRegions)
        {
            DrawUserSystemMarks(context, bounds);
        }
    }

    private void DrawUniverseInteractiveOverlay(DrawingContext context, Rect bounds)
    {
        if (Graph is null)
        {
            return;
        }

        _intelOverlayLinks.Clear();
        var plot = GetPlotMetrics();
        var viewCenterX = Bounds.Width / 2.0;
        var viewCenterY = Bounds.Height / 2.0;
        EnsureUniverseHighlightOverlay();
        var worldScale = Math.Max(1e-9, ((plot.Width + plot.Height) * 0.5) * _zoom);
        using (context.PushTransform(GetWorldToScreenMatrix(plot)))
        {
            foreach (var (geometry, pen) in _highlightLinkBatches)
            {
                context.DrawGeometry(null, ScaleWorldPen(pen, worldScale), geometry);
            }
        }

        var universeNodeScale = GetUniverseNodeZoomScale();
        foreach (var index in _highlightNodeIndexes)
        {
            if (index < 0 || index >= Graph.Nodes.Count)
            {
                continue;
            }

            var node = Graph.Nodes[index];
            var point = ToScreenPointFast(node.X, node.Y, plot, viewCenterX, viewCenterY);
            if (!IsPointVisible(point, bounds, 24))
            {
                continue;
            }

            var isSelected = SelectedNodeId == node.Id || _additionalSelectedSet.Contains(node.Id);
            var isHovered = _hoveredNodeId == node.Id;
            var isSearchHighlighted = _searchHighlightedNodeId == node.Id ||
                                      (_searchHighlightedConstellationId is not null && node.ConstellationId == _searchHighlightedConstellationId.Value) ||
                                      (_searchHighlightedRegionId is not null && node.RegionId == _searchHighlightedRegionId.Value);
            var activeRegionId = _selectedRegionId ?? _hoveredRegionId;
            var isInActiveRegion = activeRegionId is not null && node.RegionId == activeRegionId.Value;
            var isSelectedRegionNode = _selectedRegionId is not null && node.RegionId == _selectedRegionId.Value;
            var radius = (isSelected ? 9.3 : isHovered ? 8.4 : isSearchHighlighted ? 7.8 : 6.75) * universeNodeScale;
            var brush = isSelected
                ? SelectedBrush
                : isHovered
                    ? HoveredBrush
                    : isSearchHighlighted
                        ? SelectedBrush
                        : isSelectedRegionNode
                            ? SelectedBrush
                            : isInActiveRegion
                                ? RegionSelectedBrush
                                : GetCachedBrush(GetNodeBaseColor(node, NodeColorMode));
            context.DrawEllipse(brush, NodeOutlinePen, point, radius, radius);
        }

        if (SelectedNodeId is not null &&
            _nodeIndexById.TryGetValue(SelectedNodeId.Value, out var selectedIndex) &&
            _nodeById.TryGetValue(SelectedNodeId.Value, out var selectedNode))
        {
            var selectedPoint = ToScreenPointFast(selectedNode.X, selectedNode.Y, plot, viewCenterX, viewCenterY);
            DrawHoverOverlay(context, selectedPoint, selectedNode);
        }

        if (_hoveredNodeId is not null &&
            _hoveredNodeId != SelectedNodeId &&
            _nodeIndexById.TryGetValue(_hoveredNodeId.Value, out _) &&
            _nodeById.TryGetValue(_hoveredNodeId.Value, out var hoverNode))
        {
            var hoverPoint = ToScreenPointFast(hoverNode.X, hoverNode.Y, plot, viewCenterX, viewCenterY);
            DrawHoverOverlay(context, hoverPoint, hoverNode);
        }

        if (_lastPanPoint is null)
        {
            DrawHoveredSystemMarkClusterOverlay(context, bounds);
        }

        if (ShouldShowInlineLabels(plot))
        {
            DrawUniverseBakedLabels(context, bounds);
        }

        if (ShouldShowUniverseRegionLabels())
        {
            DrawUniverseRegionLabels(context);
        }
    }

    private void EnsureUniverseHighlightOverlay()
    {
        var activeRegionId = _selectedRegionId ?? _hoveredRegionId;
        if (_highlightBuiltSelected == SelectedNodeId &&
            _highlightBuiltHovered == _hoveredNodeId &&
            _highlightBuiltSearchNode == _searchHighlightedNodeId &&
            _highlightBuiltSearchConstellation == _searchHighlightedConstellationId &&
            _highlightBuiltSearchRegion == _searchHighlightedRegionId &&
            _highlightBuiltActiveRegion == activeRegionId &&
            ReferenceEquals(_highlightBuiltAdditional, AdditionalSelectedNodeIds) &&
            ReferenceEquals(_linkBatchGraph, Graph))
        {
            return;
        }

        _highlightBuiltSelected = SelectedNodeId;
        _highlightBuiltHovered = _hoveredNodeId;
        _highlightBuiltSearchNode = _searchHighlightedNodeId;
        _highlightBuiltSearchConstellation = _searchHighlightedConstellationId;
        _highlightBuiltSearchRegion = _searchHighlightedRegionId;
        _highlightBuiltActiveRegion = activeRegionId;
        _highlightBuiltAdditional = AdditionalSelectedNodeIds;
        _highlightLinkBatches.Clear();
        _highlightNodeIndexes.Clear();
        if (Graph is null)
        {
            return;
        }

        var groups = new Dictionary<Pen, (StreamGeometry Geometry, StreamGeometryContext Context)>();
        var seen = new HashSet<(long Lo, long Hi)>();
        foreach (var link in Graph.Links)
        {
            var pen = GetUniverseOverlayLinkPen(link);
            if (pen is null ||
                !_nodeById.TryGetValue(link.FromId, out var from) ||
                !_nodeById.TryGetValue(link.ToId, out var to))
            {
                continue;
            }

            var lo = Math.Min(link.FromId, link.ToId);
            var hi = Math.Max(link.FromId, link.ToId);
            if (!seen.Add((lo, hi)))
            {
                continue;
            }

            if (!groups.TryGetValue(pen, out var group))
            {
                var geometry = new StreamGeometry();
                group = (geometry, geometry.Open());
                groups[pen] = group;
            }

            group.Context.BeginFigure(new Point(from.X, from.Y), false);
            group.Context.LineTo(new Point(to.X, to.Y));
            group.Context.EndFigure(false);
        }

        foreach (var (pen, group) in groups)
        {
            group.Context.Dispose();
            _highlightLinkBatches.Add((group.Geometry, pen));
        }

        for (var i = 0; i < Graph.Nodes.Count; i++)
        {
            if (IsUniverseHighlightNode(Graph.Nodes[i]))
            {
                _highlightNodeIndexes.Add(i);
            }
        }
    }

    private bool IsUniverseHighlightNode(MapNode node)
    {
        if (SelectedNodeId == node.Id || _additionalSelectedSet.Contains(node.Id) || _hoveredNodeId == node.Id)
        {
            return true;
        }

        if (_searchHighlightedNodeId == node.Id)
        {
            return true;
        }

        if (_searchHighlightedConstellationId is not null && node.ConstellationId == _searchHighlightedConstellationId.Value)
        {
            return true;
        }

        if (_searchHighlightedRegionId is not null && node.RegionId == _searchHighlightedRegionId.Value)
        {
            return true;
        }

        var activeRegionId = _selectedRegionId ?? _hoveredRegionId;
        return activeRegionId is not null && node.RegionId == activeRegionId.Value;
    }

    private Pen? GetUniverseOverlayLinkPen(MapLink link)
    {
        if (!_nodeById.TryGetValue(link.FromId, out var fromNode) ||
            !_nodeById.TryGetValue(link.ToId, out var toNode))
        {
            return null;
        }

        var isSelectedLink = SelectedNodeId is not null &&
                             (link.FromId == SelectedNodeId.Value || link.ToId == SelectedNodeId.Value);
        var isHoveredLink = _hoveredNodeId is not null &&
                            (link.FromId == _hoveredNodeId.Value || link.ToId == _hoveredNodeId.Value);
        var isSearchConstellationLink = _searchHighlightedConstellationId is not null &&
                                        fromNode.ConstellationId == _searchHighlightedConstellationId.Value &&
                                        toNode.ConstellationId == _searchHighlightedConstellationId.Value;
        var isSearchRegionLink = _searchHighlightedRegionId is not null &&
                                 (fromNode.RegionId == _searchHighlightedRegionId.Value ||
                                  toNode.RegionId == _searchHighlightedRegionId.Value);
        var activeRegionId = _selectedRegionId ?? _hoveredRegionId;
        var isRegionLink = activeRegionId is not null &&
                           (fromNode.RegionId == activeRegionId.Value || toNode.RegionId == activeRegionId.Value);
        if (!isSelectedLink && !isHoveredLink && !isSearchConstellationLink && !isSearchRegionLink && !isRegionLink)
        {
            return null;
        }

        var basePen = GetLinkPen(LinksPen, SameConstellationPen, SameRegionPen, CrossRegionPen, link, _nodeById);
        if (isSearchConstellationLink)
        {
            return HighlightedSearchConstellationPen;
        }

        if (isSelectedLink || isHoveredLink || isSearchRegionLink)
        {
            return GetHighlightedPen(basePen, LinksPen, SameConstellationPen, SameRegionPen, CrossRegionPen, HighlightedDefaultPen, HighlightedSameConstellationPen, HighlightedSameRegionPen, HighlightedCrossRegionPen);
        }

        return GetHighlightedPen(basePen, LinksPen, SameConstellationPen, SameRegionPen, CrossRegionPen, RegionEmphasisDefaultPen, RegionEmphasisSameConstellationPen, RegionEmphasisSameRegionPen, RegionEmphasisCrossRegionPen);
    }

    private void DrawAnimatedOverlays(DrawingContext context, Rect bounds)
    {
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            return;
        }

        var plot = GetPlotMetrics();
        var shouldShowInlineLabels = ShouldShowInlineLabels(plot);
        if (EnableLinkAnimations)
        {
            var anchorNodeId = _hoveredNodeId ?? SelectedNodeId;
            if (anchorNodeId is not null)
            {
                foreach (var link in Graph.Links)
                {
                    if (link.FromId != anchorNodeId.Value && link.ToId != anchorNodeId.Value)
                    {
                        continue;
                    }

                    if (!_nodeIndexById.TryGetValue(link.FromId, out var fromIndex) ||
                        !_nodeIndexById.TryGetValue(link.ToId, out var toIndex))
                    {
                        continue;
                    }

                    var from = _screenPositions[fromIndex];
                    var to = _screenPositions[toIndex];
                    if (!IsSegmentPotentiallyVisible(from, to, bounds, 48))
                    {
                        continue;
                    }

                    var basePen = GetLinkPen(LinksPen, SameConstellationPen, SameRegionPen, CrossRegionPen, link, _nodeById);
                    DrawAnimatedRegularLinkEffect(context, from, to, GetAnimatedRegularLinkColor(basePen), link);
                }

                if (ShowAnsiblexNetwork &&
                    ViewMode != MapViewMode.UniverseRegions &&
                    AnsiblexLinks is not null)
                {
                    foreach (var link in AnsiblexLinks)
                    {
                        if (link.FromId != anchorNodeId.Value && link.ToId != anchorNodeId.Value)
                        {
                            continue;
                        }

                        if (!_nodeIndexById.TryGetValue(link.FromId, out var fromIndex) ||
                            !_nodeIndexById.TryGetValue(link.ToId, out var toIndex))
                        {
                            continue;
                        }

                        var from = _screenPositions[fromIndex];
                        var to = _screenPositions[toIndex];
                        if (!IsSegmentPotentiallyVisible(from, to, bounds, 64))
                        {
                            continue;
                        }

                        DrawCurvedAnsiblexLink(context, from, to, link, highlighted: true, animate: true);
                    }
                }
            }
        }

        if (!EnableIntelRingAnimations || !shouldShowInlineLabels || IntelHostileScoresByNodeId is null)
        {
            return;
        }

        for (var i = 0; i < Graph.Nodes.Count; i++)
        {
            var node = Graph.Nodes[i];
            var p = _screenPositions[i];
            if (!IsPointVisible(p, bounds, 24))
            {
                continue;
            }

            if (!IntelHostileScoresByNodeId.TryGetValue(node.Id, out var hostileScore) || hostileScore <= 0)
            {
                continue;
            }

            IReadOnlyList<string> intelRingIcons = ["crosshair"];
            if (IntelIconKeysByNodeId is not null &&
                IntelIconKeysByNodeId.TryGetValue(node.Id, out var configuredIcons) &&
                configuredIcons.Count > 0)
            {
                intelRingIcons = configuredIcons;
            }

            var isSelected = SelectedNodeId == node.Id || _additionalSelectedSet.Contains(node.Id);
            var isHovered = _hoveredNodeId == node.Id;
            var isSearchHighlighted = _searchHighlightedNodeId == node.Id ||
                                      (_searchHighlightedConstellationId is not null && node.ConstellationId == _searchHighlightedConstellationId.Value) ||
                                      (_searchHighlightedRegionId is not null && node.RegionId == _searchHighlightedRegionId.Value);
            var universeNodeScale = GetUniverseNodeZoomScale();
            var radius = (isSelected ? 9.3 : isHovered ? 8.4 : isSearchHighlighted ? 7.8 : 6.75) * universeNodeScale;
            DrawIntelOrbitIcons(context, p, radius, intelRingIcons);
        }
    }

    private double GetLabelZoomThreshold()
    {
        if (ShowEditorGrid)
        {
            return 1.5;
        }

        return ViewMode switch
        {
            MapViewMode.Universe => 5.4,
            MapViewMode.UniverseRegions => 0.5,
            MapViewMode.Region => 0.6,
            _ => 1.0
        };
    }

    private bool ShouldShowInlineLabels(PlotMetrics plot)
    {
        if (ShowEditorGrid)
        {
            return _zoom >= GetLabelZoomThreshold();
        }

        if (ViewMode == MapViewMode.Region)
        {
            // use screen-space spacing instead of raw zoom threshold.
            var worldScale = Math.Max(1e-9, ((plot.Width + plot.Height) * 0.5) * _zoom);
            var typicalSpacingPx = _typicalLinkSpacing * worldScale;
            var density01 = GetRegionDensity01();
            var requiredSpacingPx = RegionLabelSpacingPxSparse + ((RegionLabelSpacingPxDense - RegionLabelSpacingPxSparse) * density01);
            return typicalSpacingPx >= requiredSpacingPx;
        }

        return _zoom >= GetLabelZoomThreshold();
    }

    private int GetLabelBudget()
    {
        return ViewMode switch
        {
            MapViewMode.Universe => 620,
            MapViewMode.UniverseRegions => 180,
            MapViewMode.Region => 1240,
            _ => 300
        };
    }

    private double GetUniverseNodeZoomScale()
    {
        if (ViewMode != MapViewMode.Universe && ViewMode != MapViewMode.Region)
        {
            return 1.0;
        }

        var threshold = GetLabelZoomThreshold();
        var effectiveThreshold = ViewMode == MapViewMode.Region
            ? Math.Min(GetMaxZoom(), threshold * 1.45)
            : threshold;
        if (_zoom >= effectiveThreshold)
        {
            return 1.0;
        }

        const double minZoom = 0.4;
        var progress = (_zoom - minZoom) / (effectiveThreshold - minZoom);
        progress = Math.Clamp(progress, 0.0, 1.0);
        return UniverseMinNodeScale + ((1.0 - UniverseMinNodeScale) * progress);
    }

    private double GetVoronoiZoomThreshold()
    {
        return ViewMode switch
        {
            MapViewMode.Universe => 0.4,
            MapViewMode.UniverseRegions => 0.4,
            MapViewMode.Region => 0.4,
            _ => 0.4
        };
    }

    private bool ShouldShowUniverseRegionLabels()
    {
        if (ViewMode != MapViewMode.Universe)
        {
            return false;
        }

        const double minVisibleZoom = 1.0;
        return _zoom >= minVisibleZoom && _zoom < GetLabelZoomThreshold();
    }

    private void DrawUniverseRegionLabels(DrawingContext context)
    {
        if (!ShouldShowUniverseRegionLabels() || Graph is null || Graph.Nodes.Count == 0)
        {
            return;
        }

        const double minVisibleZoom = 1.0;
        var threshold = GetLabelZoomThreshold();
        var progress = Math.Clamp((_zoom - minVisibleZoom) / (threshold - minVisibleZoom), 0.0, 1.0);
        var scale = 0.74 + (0.26 * progress);
        var idleOpacity = 0.40 + (0.28 * progress);
        var activeOpacity = 0.60 + (0.24 * progress);

        foreach (var layout in BuildUniverseRegionLabelLayouts())
        {
            var isSelected = _selectedRegionId == layout.RegionId;
            var isHovered = _hoveredRegionId == layout.RegionId;
            var opacity = isSelected || isHovered ? activeOpacity : idleOpacity;
            var rect = new Rect(
                layout.Center.X - ((layout.Rect.Width * scale) / 2.0),
                layout.Center.Y - ((layout.Rect.Height * scale) / 2.0),
                layout.Rect.Width * scale,
                layout.Rect.Height * scale);

            using var _ = context.PushOpacity(opacity);
            context.FillRectangle(
                new SolidColorBrush(Color.Parse(isSelected ? "#2A3A52" : isHovered ? "#243347" : "#172333")),
                rect,
                4);
            context.DrawRectangle(
                new Pen(new SolidColorBrush(Color.Parse(isSelected ? "#74B0E5" : isHovered ? "#638FB9" : "#33506F")), 0.9),
                rect,
                4);

            var origin = new Point(layout.Center.X - ((layout.Label.Width * scale) / 2.0), layout.Center.Y - ((layout.Label.Height * scale) / 2.0));
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y)))
            {
                context.DrawText(layout.Label, new Point(0, 0));
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Focus();
        var point = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed && TryOpenIntelOverlayLink(point))
        {
            return;
        }

        if (!UseBuiltInSelection)
        {
            return;
        }

        if (e.ClickCount >= 2 &&
            ViewMode == MapViewMode.UniverseRegions &&
            props.IsLeftButtonPressed &&
            TrySelectNodeAt(point) &&
            SelectedNodeId is long selectedId)
        {
            UniverseRegionNodeDoubleClicked?.Invoke(this, (int)selectedId);
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            if (TryToggleRegionSelectionFromLabel(point))
            {
                SelectedNodeId = null;
                ClearSearchHighlight();
                InvalidateInteractive();
                return;
            }

            if (TrySelectNodeAt(point))
            {
                _selectedRegionId = null;
                ClearSearchHighlight();
                InvalidateInteractive();
                return;
            }

            _leftPressPoint = point;
            _pendingClearSelectionOnLeftRelease = true;
            _leftDragPanned = false;
            _lastPanPoint = point;
            e.Pointer.Capture(this);
            return;
        }

        if (props.IsMiddleButtonPressed || props.IsRightButtonPressed)
        {
            _lastPanPoint = point;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        _lastPointerPosition = point;

        if (_lastPanPoint is null)
        {
            UpdateHover(point);
            return;
        }

        var delta = point - _lastPanPoint.Value;
        if ((delta.X * delta.X) + (delta.Y * delta.Y) < 0.25)
        {
            return;
        }

        _leftDragPanned = true;
        _panOffset += delta;
        _lastPanPoint = point;
        UpdateAnimationTimerState();
        InvalidateCamera();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var dragged = _leftDragPanned;
        if (_pendingClearSelectionOnLeftRelease && !_leftDragPanned)
        {
            SelectedNodeId = null;
            _selectedRegionId = null;
            ClearSearchHighlight();
            InvalidateInteractive();
        }

        _pendingClearSelectionOnLeftRelease = false;
        _leftDragPanned = false;
        _leftPressPoint = null;
        _lastPanPoint = null;
        if (dragged && UseUniverseFastPath() && Graph is not null)
        {
            var plot = GetPlotMetrics();
            UpdateScreenPositions(plot, Bounds.Width / 2.0, Bounds.Height / 2.0);
            _hitTestGrid.Rebuild(_screenPositions, Graph.Nodes.Count, Bounds.Size);
            InvalidateVisual();
        }

        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var delta = e.Delta.Y;
        var factor = delta > 0 ? 1.1 : 0.9;
        var mouse = e.GetPosition(this);
        var oldZoom = _zoom;
        var newZoom = Math.Clamp(_zoom * factor, GetMinZoom(), GetMaxZoom());
        if (Math.Abs(newZoom - oldZoom) < 1e-9)
        {
            return;
        }

        // Keep the world point under the cursor stable while zooming.
        var plot = GetPlotMetrics();
        var viewCenterX = Bounds.Width / 2.0;
        var viewCenterY = Bounds.Height / 2.0;
        var baseX = ((mouse.X - viewCenterX - _panOffset.X) / oldZoom) + viewCenterX;
        var baseY = ((mouse.Y - viewCenterY - _panOffset.Y) / oldZoom) + viewCenterY;
        var worldX = (baseX - plot.OriginX) / plot.Width;
        var worldY = (baseY - plot.OriginY) / plot.Height;
        var newBaseX = plot.OriginX + (worldX * plot.Width);
        var newBaseY = plot.OriginY + (worldY * plot.Height);

        _zoom = newZoom;
        _panOffset = new Point(
            mouse.X - (((newBaseX - viewCenterX) * _zoom) + viewCenterX),
            mouse.Y - (((newBaseY - viewCenterY) * _zoom) + viewCenterY));

        _universeWheelIdleUntil = DateTime.UtcNow.AddMilliseconds(120);
        UpdateAnimationTimerState();
        InvalidateCamera(zoomChanged: true);
    }

    private bool TrySelectNodeAt(Point point)
    {
        var closestId = FindClosestNodeAt(point, 8.0);
        if (closestId is null)
        {
            return false;
        }

        SelectedNodeId = closestId.Value;
        return true;
    }


    private void UpdateHover(Point point)
    {
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            if (_hoveredNodeId is not null)
            {
                _hoveredNodeId = null;
                InvalidateMap();
            }
            if (_hoveredSystemMarkClusterIndex >= 0)
            {
                _hoveredSystemMarkClusterIndex = -1;
                InvalidateMap();
            }
            if (_hoveredRegionId is not null)
            {
                _hoveredRegionId = null;
                InvalidateMap();
            }

            return;
        }

        var hoverId = FindClosestNodeAt(point, 10.0);
        int? hoveredRegionId = null;
        if (hoverId is null &&
            ShouldShowUniverseRegionLabels())
        {
            hoveredRegionId = TryGetHoveredRegionFromRegionLabel(point);
        }

        var hoveredCluster = -1;
        for (var i = _systemMarkClusterHits.Count - 1; i >= 0; i--)
        {
            if (_systemMarkClusterHits[i].Bounds.Contains(point))
            {
                hoveredCluster = i;
                break;
            }
        }

        var changed = false;
        if (_hoveredNodeId != hoverId)
        {
            _hoveredNodeId = hoverId;
            changed = true;
        }

        if (_hoveredRegionId != hoveredRegionId)
        {
            _hoveredRegionId = hoveredRegionId;
            changed = true;
        }

        if (_hoveredSystemMarkClusterIndex != hoveredCluster)
        {
            _hoveredSystemMarkClusterIndex = hoveredCluster;
            changed = true;
        }

        if (changed)
        {
            UpdateAnimationTimerState();
            InvalidateInteractive();
        }
    }


    public void FocusOnSearch(MapSearchFocus focus)
    {
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            return;
        }

        ClearSearchHighlight();

        if (ViewMode == MapViewMode.Region)
        {
            if (focus.Kind == MapSearchKind.SolarSystem && focus.SolarSystemId is not null)
            {
                _searchHighlightedNodeId = focus.SolarSystemId.Value;
                SelectedNodeId = focus.SolarSystemId.Value;

                if (focus.PreferPreserveZoom)
                {
                    FocusOnNode(focus.SolarSystemId.Value);
                    InvalidateMap();
                    return;
                }
            }
            else if (focus.Kind == MapSearchKind.Constellation && focus.ConstellationId is not null)
            {
                _searchHighlightedConstellationId = focus.ConstellationId.Value;
                SelectedNodeId = null;

                if (focus.PreferPreserveZoom)
                {
                    CenterOnConstellation(focus.ConstellationId.Value);
                    InvalidateMap();
                    return;
                }
            }
            else
            {
                SelectedNodeId = null;
            }

            FitToView();
            InvalidateMap();
            return;
        }

        if (ViewMode == MapViewMode.UniverseRegions)
        {
            if (focus.Kind == MapSearchKind.Region && focus.RegionId is not null)
            {
                _searchHighlightedRegionId = focus.RegionId.Value;
                SelectedNodeId = null;
                InvalidateMap();
            }

            return;
        }

        if (focus.Kind == MapSearchKind.SolarSystem && focus.SolarSystemId is not null)
        {
            _searchHighlightedNodeId = focus.SolarSystemId.Value;
            _selectedRegionId = null;
            FocusOnNode(focus.SolarSystemId.Value);
            return;
        }

        if (focus.Kind == MapSearchKind.Constellation && focus.ConstellationId is not null)
        {
            _searchHighlightedConstellationId = focus.ConstellationId.Value;
            _selectedRegionId = null;
            SelectedNodeId = null;
            FocusOnConstellation(focus.ConstellationId.Value);
            return;
        }

        if (focus.Kind == MapSearchKind.Region && focus.RegionId is not null)
        {
            _searchHighlightedRegionId = focus.RegionId.Value;
            _selectedRegionId = ViewMode == MapViewMode.Universe ? focus.RegionId.Value : null;
            SelectedNodeId = null;
            FocusOnRegion(focus.RegionId.Value);
            InvalidateMap();
        }
    }

    private void FocusOnNode(long nodeId)
    {
        if (Graph is null)
        {
            return;
        }

        if (!_nodeById.TryGetValue(nodeId, out var node))
        {
            return;
        }

        CenterOnWorld(node.X, node.Y, _zoom);
    }

    public bool FocusOnNodeWithZoomPercent(long nodeId, double zoomPercent)
    {
        if (Graph is null)
        {
            return false;
        }

        if (!_nodeById.TryGetValue(nodeId, out var node))
        {
            return false;
        }

        var minZoom = GetMinZoom();
        var maxZoom = GetMaxZoom();
        var t = Math.Clamp(zoomPercent, 0.0, 1.0);
        var targetZoom = minZoom + ((maxZoom - minZoom) * t);
        CenterOnWorld(node.X, node.Y, targetZoom);
        return true;
    }

    public bool FocusOnNodeWithZoom(long nodeId, double zoom)
    {
        if (Graph is null || !double.IsFinite(zoom) || zoom <= 0)
        {
            return false;
        }

        if (!_nodeById.TryGetValue(nodeId, out var node))
        {
            return false;
        }

        CenterOnWorld(node.X, node.Y, zoom);
        return true;
    }

    private void FocusOnConstellation(int constellationId)
    {
        if (Graph is null)
        {
            return;
        }

        var nodes = Graph.Nodes.Where(n => n.ConstellationId == constellationId).ToList();
        if (nodes.Count == 0)
        {
            return;
        }

        FocusOnNodes(nodes, 130);
    }

    private void CenterOnConstellation(int constellationId)
    {
        if (Graph is null)
        {
            return;
        }

        var nodes = Graph.Nodes.Where(n => n.ConstellationId == constellationId).ToList();
        if (nodes.Count == 0)
        {
            return;
        }

        CenterOnNodes(nodes);
    }

    private void FocusOnRegion(int regionId)
    {
        if (Graph is null)
        {
            return;
        }

        var nodes = Graph.Nodes.Where(n => n.RegionId == regionId).ToList();
        if (nodes.Count == 0)
        {
            return;
        }

        FocusOnNodes(nodes, 150);
    }

    private void FocusOnNodes(IReadOnlyList<MapNode> nodes, double padding)
    {
        if (nodes.Count == 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        var plot = GetPlotMetrics();
        var plotWidth = plot.Width;
        var plotHeight = plot.Height;

        var minX = nodes.Min(n => n.X);
        var maxX = nodes.Max(n => n.X);
        var minY = nodes.Min(n => n.Y);
        var maxY = nodes.Max(n => n.Y);

        var graphWidthPx = Math.Max(1e-9, (maxX - minX) * plotWidth);
        var graphHeightPx = Math.Max(1e-9, (maxY - minY) * plotHeight);

        var availableWidth = Math.Max(1.0, plotWidth - (padding * 2));
        var availableHeight = Math.Max(1.0, plotHeight - (padding * 2));

        var zoomX = availableWidth / graphWidthPx;
        var zoomY = availableHeight / graphHeightPx;
        _zoom = Math.Clamp(Math.Min(zoomX, zoomY), GetMinZoom(), GetMaxZoom());

        var centerX = (minX + maxX) * 0.5;
        var centerY = (minY + maxY) * 0.5;
        CenterOnWorld(centerX, centerY, _zoom);
    }

    private void CenterOnNodes(IReadOnlyList<MapNode> nodes)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        var minX = nodes.Min(n => n.X);
        var maxX = nodes.Max(n => n.X);
        var minY = nodes.Min(n => n.Y);
        var maxY = nodes.Max(n => n.Y);

        var centerX = (minX + maxX) * 0.5;
        var centerY = (minY + maxY) * 0.5;
        CenterOnWorld(centerX, centerY, _zoom);
    }

    private void CenterOnWorld(double worldX, double worldY, double zoom)
    {
        var previousZoom = _zoom;
        _zoom = Math.Clamp(zoom, GetMinZoom(), GetMaxZoom());
        var plot = GetPlotMetrics();
        var baseX = plot.OriginX + (worldX * plot.Width);
        var baseY = plot.OriginY + (worldY * plot.Height);
        var cx = Bounds.Width * 0.5;
        var cy = Bounds.Height * 0.5;
        _panOffset = new Point(
            cx - (((baseX - cx) * _zoom) + cx),
            cy - (((baseY - cy) * _zoom) + cy));
        InvalidateCamera(Math.Abs(_zoom - previousZoom) > 1e-6);
    }

    private Point ToScreenPoint(MapNode node)
    {
        var plot = GetPlotMetrics();

        var x = plot.OriginX + (node.X * plot.Width);
        var y = plot.OriginY + (node.Y * plot.Height);

        var centeredX = ((x - Bounds.Width / 2.0) * _zoom) + (Bounds.Width / 2.0) + _panOffset.X;
        var centeredY = ((y - Bounds.Height / 2.0) * _zoom) + (Bounds.Height / 2.0) + _panOffset.Y;

        return new Point(centeredX, centeredY);
    }

    private Point ToScreenPoint(double worldX, double worldY)
    {
        var plot = GetPlotMetrics();
        return ToScreenPointFast(worldX, worldY, plot, Bounds.Width / 2.0, Bounds.Height / 2.0);
    }

    private Point ToScreenPointFast(
        double worldX,
        double worldY,
        PlotMetrics plot,
        double viewCenterX,
        double viewCenterY)
    {
        var x = plot.OriginX + (worldX * plot.Width);
        var y = plot.OriginY + (worldY * plot.Height);

        return new Point(
            ((x - viewCenterX) * _zoom) + viewCenterX + _panOffset.X,
            ((y - viewCenterY) * _zoom) + viewCenterY + _panOffset.Y);
    }

    private Matrix GetWorldToScreenMatrix(PlotMetrics plot)
    {
        var viewCenterX = Bounds.Width / 2.0;
        var viewCenterY = Bounds.Height / 2.0;

        var scaleX = plot.Width * _zoom;
        var scaleY = plot.Height * _zoom;
        var offsetX = ((plot.OriginX - viewCenterX) * _zoom) + viewCenterX + _panOffset.X;
        var offsetY = ((plot.OriginY - viewCenterY) * _zoom) + viewCenterY + _panOffset.Y;

        return new Matrix(
            scaleX, 0,
            0, scaleY,
            offsetX, offsetY);
    }

    private void DrawEditorGrid(DrawingContext context, PlotMetrics plot, double viewCenterX, double viewCenterY)
    {
        var step = Math.Max(0.0001, EditorGridStep);
        if (!TryScreenToWorld(new Point(0, 0), out var worldTopLeft) ||
            !TryScreenToWorld(new Point(Bounds.Width, Bounds.Height), out var worldBottomRight))
        {
            return;
        }

        var minX = Math.Min(worldTopLeft.X, worldBottomRight.X);
        var maxX = Math.Max(worldTopLeft.X, worldBottomRight.X);
        var minY = Math.Min(worldTopLeft.Y, worldBottomRight.Y);
        var maxY = Math.Max(worldTopLeft.Y, worldBottomRight.Y);
        const int maxLinesPerAxis = 160;
        var spanX = Math.Max(step, maxX - minX);
        var spanY = Math.Max(step, maxY - minY);
        var needed = Math.Max(spanX / step, spanY / step);
        if (needed > maxLinesPerAxis)
        {
            step *= Math.Ceiling(needed / maxLinesPerAxis);
        }

        var worldScale = Math.Max(1e-9, ((plot.Width + plot.Height) * 0.5) * _zoom);
        var lineWidth = 1.0 / worldScale;
        if (lineWidth * worldScale < 0.35)
        {
            return;
        }

        var minorPen = _penCache.Get(Color.Parse("#1E6A8A9E"), 1.0 / worldScale);
        var majorPen = _penCache.Get(Color.Parse("#32AFD5F2"), 1.25 / worldScale);

        var firstX = Math.Floor(minX / step) * step;
        var firstY = Math.Floor(minY / step) * step;

        using (context.PushTransform(GetWorldToScreenMatrix(plot)))
        {
            for (var wx = firstX; wx <= maxX + (step * 0.5); wx += step)
            {
                var ix = (long)Math.Round(wx / step, MidpointRounding.AwayFromZero);
                var isMajor = Math.Abs(ix % 6) == 0;
                context.DrawLine(
                    isMajor ? majorPen : minorPen,
                    new Point(wx, minY),
                    new Point(wx, maxY));
            }

            for (var wy = firstY; wy <= maxY + (step * 0.5); wy += step)
            {
                var iy = (long)Math.Round(wy / step, MidpointRounding.AwayFromZero);
                var isMajor = Math.Abs(iy % 6) == 0;
                context.DrawLine(
                    isMajor ? majorPen : minorPen,
                    new Point(minX, wy),
                    new Point(maxX, wy));
            }
        }
    }


    private double GetMaxZoom()
    {
        if (MaxZoomOverride > 0)
        {
            return Math.Max(GetMinZoom() + 0.01, MaxZoomOverride);
        }

        return ViewMode switch
        {
            MapViewMode.Universe => 60.0,
            MapViewMode.Region => GetAdaptiveRegionMaxZoom(),
            _ => 12.0
        };
    }

    private double GetAdaptiveRegionMaxZoom()
    {
        // denser/larger regions (smaller typical spacing) get higher max zoom.
        var density01 = GetRegionDensity01();
        var maxZoom = RegionMaxZoomSparse + ((RegionMaxZoomDense - RegionMaxZoomSparse) * density01);
        return Math.Max(GetMinZoom() + 0.01, maxZoom);
    }

    private double GetRegionDensity01()
    {
        var spacing = Math.Max(1e-6, _typicalLinkSpacing);
        return Math.Clamp((DenseSpacingHigh - spacing) / Math.Max(1e-9, DenseSpacingHigh - DenseSpacingLow), 0.0, 1.0);
    }

    private double GetMinZoom()
    {
        return Math.Clamp(MinZoom, 0.01, 1000.0);
    }

    private PlotMetrics GetPlotMetrics()
    {
        var baseWidth = Math.Max(1.0, Bounds.Width - (BasePadding * 2));
        var baseHeight = Math.Max(1.0, Bounds.Height - (BasePadding * 2));

        if (StretchToWindow)
        {
            return new PlotMetrics(BasePadding, BasePadding, baseWidth, baseHeight);
        }

        var side = Math.Max(1.0, Math.Min(baseWidth, baseHeight));
        var originX = BasePadding + ((baseWidth - side) * 0.5);
        var originY = BasePadding + ((baseHeight - side) * 0.5);
        return new PlotMetrics(originX, originY, side, side);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private bool IsPointVisible(Point p, Rect bounds, double margin)
    {
        var pad = margin + (_universeBakeBaseOnly ? _bakeMargin : 0);
        return p.X >= -pad &&
               p.Y >= -pad &&
               p.X <= bounds.Width + pad &&
               p.Y <= bounds.Height + pad;
    }

    private bool IsSegmentPotentiallyVisible(Point a, Point b, Rect bounds, double margin)
    {
        var pad = margin + (_universeBakeBaseOnly ? _bakeMargin : 0);
        if (a.X < -pad && b.X < -pad) return false;
        if (a.Y < -pad && b.Y < -pad) return false;
        if (a.X > bounds.Width + pad && b.X > bounds.Width + pad) return false;
        if (a.Y > bounds.Height + pad && b.Y > bounds.Height + pad) return false;
        return true;
    }

    private int? TryGetHoveredRegionFromRegionLabel(Point point)
    {
        if (!ShouldShowUniverseRegionLabels())
        {
            return null;
        }

        foreach (var layout in BuildUniverseRegionLabelLayouts())
        {
            if (layout.Rect.Contains(point))
            {
                return layout.RegionId;
            }
        }

        return null;
    }

    private bool TryToggleRegionSelectionFromLabel(Point point)
    {
        if (!ShouldShowUniverseRegionLabels())
        {
            return false;
        }

        var regionId = TryGetHoveredRegionFromRegionLabel(point);
        if (regionId is null)
        {
            return false;
        }

        _selectedRegionId = _selectedRegionId == regionId ? null : regionId;
        return true;
    }

    private void ClearSearchHighlight()
    {
        _searchHighlightedNodeId = null;
        _searchHighlightedConstellationId = null;
        _searchHighlightedRegionId = null;
    }

    private void EnsureRegionCentroids()
    {
        if (ReferenceEquals(_regionCentroidGraph, Graph))
        {
            return;
        }

        _regionCentroidGraph = Graph;
        _regionCentroids = Graph is null || Graph.Nodes.Count == 0
            ? []
            : UniverseSceneBatch.BuildRegionCentroids(Graph.Nodes);
    }

    private IReadOnlyList<UniverseRegionLabelLayout> BuildUniverseRegionLabelLayouts()
    {
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            return [];
        }

        EnsureRegionCentroids();
        var result = new List<UniverseRegionLabelLayout>(_regionCentroids.Count);
        foreach (var centroid in _regionCentroids)
        {
            var center = ToScreenPoint(centroid.X, centroid.Y);
            var label = GetRegionLabel(centroid.RegionId, centroid.Name);
            var padX = 6.0;
            var padY = 3.0;
            var rect = new Rect(
                center.X - (label.Width / 2.0) - padX,
                center.Y - (label.Height / 2.0) - padY,
                label.Width + (padX * 2),
                label.Height + (padY * 2));

            result.Add(new UniverseRegionLabelLayout(centroid.RegionId, centroid.Name, center, rect, label));
        }

        return result;
    }

    private Pen GetLinkPen(
        Pen defaultPen,
        Pen sameConstellationPen,
        Pen sameRegionPen,
        Pen crossRegionPen,
        MapLink link,
        IReadOnlyDictionary<long, MapNode> nodeById)
    {
        if (ViewMode == MapViewMode.UniverseRegions)
        {
            if (!nodeById.TryGetValue(link.FromId, out var fromRegionNode) || !nodeById.TryGetValue(link.ToId, out var toRegionNode))
            {
                return defaultPen;
            }

            var fromColor = GetUniverseRegionOwnerColor(fromRegionNode.RegionName ?? fromRegionNode.Name);
            var toColor = GetUniverseRegionOwnerColor(toRegionNode.RegionName ?? toRegionNode.Name);
            var finalColor = fromColor == toColor
                ? fromColor
                : BlendColors(fromColor, toColor, 0.5);
            var linkColor = Color.FromArgb(196, finalColor.R, finalColor.G, finalColor.B);
            return new Pen(GetCachedBrush(linkColor), 1.15);
        }

        if (!nodeById.TryGetValue(link.FromId, out var fromNode) || !nodeById.TryGetValue(link.ToId, out var toNode))
        {
            return defaultPen;
        }

        var sameConstellation = fromNode.ConstellationId is not null &&
                                toNode.ConstellationId is not null &&
                                fromNode.ConstellationId == toNode.ConstellationId;
        if (sameConstellation)
        {
            return sameConstellationPen;
        }

        var sameRegion = fromNode.RegionId is not null &&
                         toNode.RegionId is not null &&
                         fromNode.RegionId == toNode.RegionId;
        return sameRegion ? sameRegionPen : crossRegionPen;
    }

    private static Pen GetHighlightedPen(
        Pen basePen,
        Pen defaultPen,
        Pen sameConstellationPen,
        Pen sameRegionPen,
        Pen crossRegionPen,
        Pen highlightedDefaultPen,
        Pen highlightedSameConstellationPen,
        Pen highlightedSameRegionPen,
        Pen highlightedCrossRegionPen)
    {
        if (ReferenceEquals(basePen, sameConstellationPen))
        {
            return highlightedSameConstellationPen;
        }

        if (ReferenceEquals(basePen, sameRegionPen))
        {
            return highlightedSameRegionPen;
        }

        if (ReferenceEquals(basePen, crossRegionPen))
        {
            return highlightedCrossRegionPen;
        }

        return highlightedDefaultPen;
    }

    private void DrawCurvedAnsiblexLink(DrawingContext context, Point from, Point to, MapLink link, bool highlighted, bool animate)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        var dashOffset = animate ? (_linkAnimationPhase * 22.0) + ((link.FromId + link.ToId) % 13) : 0;
        var pen = CreateAnsiblexLinkPen(link, from, to, highlighted || animate, dashOffset);
        if (length < 2.0)
        {
            context.DrawLine(pen, from, to);
            if (animate)
            {
                DrawAnimatedRegularLinkEffect(context, from, to, GetAnsiblexLinkColor(link.FromId), link);
            }
            return;
        }

        var nx = -dy / length;
        var ny = dx / length;
        var sign = (((link.FromId ^ link.ToId) & 1) == 0) ? 1.0 : -1.0;
        var offset = Math.Clamp(length * 0.13, 16.0, 62.0);

        // Stronger curvature for short/medium links, where node overlap is most common.
        if (length < 120)
        {
            offset = Math.Max(offset, 34.0);
        }
        else if (length < 220)
        {
            offset = Math.Max(offset, 28.0);
        }

        // If a third node sits close to the midpoint corridor, push the curve farther out.
        var midpoint = new Point((from.X + to.X) * 0.5, (from.Y + to.Y) * 0.5);
        var nearestCenterDistance = double.MaxValue;
        for (var i = 0; i < _screenPositions.Length; i++)
        {
            var p = _screenPositions[i];
            var dpX = p.X - midpoint.X;
            var dpY = p.Y - midpoint.Y;
            var d = Math.Sqrt((dpX * dpX) + (dpY * dpY));
            if (d < nearestCenterDistance)
            {
                nearestCenterDistance = d;
            }
        }

        if (nearestCenterDistance < 34.0)
        {
            offset = Math.Min(76.0, offset + 16.0);
        }

        offset *= sign;
        var control = new Point((from.X + to.X) * 0.5 + (nx * offset), (from.Y + to.Y) * 0.5 + (ny * offset));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(from, false);
            ctx.QuadraticBezierTo(control, to);
            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private Pen CreateAnsiblexLinkPen(MapLink link, Point from, Point to, bool highlighted, double dashOffset)
    {
        var fromZone = TryGetAnsiblexZone(link.FromId);
        var toZone = TryGetAnsiblexZone(link.ToId);
        if (fromZone is null || toZone is null)
        {
            return highlighted ? AnsiblexHighlightedLinkPen : AnsiblexLinkPen;
        }

        if (fromZone.Value == toZone.Value && dashOffset == 0)
        {
            return highlighted ? AnsiblexZoneHighlightPens[fromZone.Value] : AnsiblexZonePens[fromZone.Value];
        }

        var start = Color.Parse(AnsiblexZones.ColorHex(fromZone.Value));
        var end = Color.Parse(AnsiblexZones.ColorHex(toZone.Value));
        if (highlighted)
        {
            start = BlendColors(start, Colors.White, 0.35);
            end = BlendColors(end, Colors.White, 0.35);
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(from, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(to, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(start, 0),
                new GradientStop(end, 1)
            }
        };
        return new Pen(brush, highlighted ? 3.2 : 2.2, dashStyle: new DashStyle([4.2, 2.8], dashOffset));
    }

    private static Pen[] CreateAnsiblexZonePens(double thickness, double brighten)
    {
        var pens = new Pen[6];
        for (var zone = 1; zone <= 5; zone++)
        {
            var color = Color.Parse(AnsiblexZones.ColorHex(zone));
            if (brighten > 0)
            {
                color = BlendColors(color, Colors.White, brighten);
            }

            pens[zone] = new Pen(
                new ImmutableSolidColorBrush(color),
                thickness,
                dashStyle: new DashStyle([4.2, 2.8], 0));
        }

        return pens;
    }

    private Color GetAnsiblexZoneColor(MapNode node)
    {
        var zone = TryGetAnsiblexZone(node.Id);
        return zone is null ? Color.Parse("#98A6B8") : Color.Parse(AnsiblexZones.ColorHex(zone.Value));
    }

    private Color GetAnsiblexLinkColor(long nodeId)
    {
        var zone = TryGetAnsiblexZone(nodeId);
        return zone is null ? AnimatedAnsiblexColor : Color.Parse(AnsiblexZones.ColorHex(zone.Value));
    }

    private int? TryGetAnsiblexZone(long nodeId)
    {
        if (AnsiblexZonesByNodeId is not null &&
            AnsiblexZonesByNodeId.TryGetValue(nodeId, out var zone))
        {
            return zone.Zone;
        }

        return null;
    }

    private bool TryGetAnsiblexZoneInfo(long nodeId, out AnsiblexSystemZone zone)
    {
        if (AnsiblexZonesByNodeId is not null &&
            AnsiblexZonesByNodeId.TryGetValue(nodeId, out var found) &&
            found is not null)
        {
            zone = found;
            return true;
        }

        zone = null!;
        return false;
    }

    private sealed class AnsiblexHoverLine
    {
        public required int Zone { get; init; }
        public string SortName { get; init; } = string.Empty;
        public required FormattedText Label { get; init; }
        public FormattedText? Meta { get; init; }
        public FormattedText? Ship { get; init; }
        public bool ShowCapitalMark { get; init; }
        public FormattedText? Outbound { get; init; }
        public int OutboundZone { get; init; }
        public FormattedText? Inbound { get; init; }
        public int InboundZone { get; init; }
    }

    private List<AnsiblexHoverLine> BuildAnsiblexHoverLines(MapNode node)
    {
        if (!ShowAnsiblexNetwork ||
            !InfoBoxShowAnsiblexZone ||
            ViewMode == MapViewMode.UniverseRegions ||
            !TryGetAnsiblexZoneInfo(node.Id, out var ownZone))
        {
            return [];
        }

        var ink = new ImmutableSolidColorBrush(Color.Parse("#0D131D"));
        var partners = new List<AnsiblexHoverLine>();
        if (AnsiblexLinks is not null)
        {
            foreach (var link in AnsiblexLinks)
            {
                long? otherId = link.FromId == node.Id
                    ? link.ToId
                    : link.ToId == node.Id
                        ? link.FromId
                        : null;
                if (otherId is null ||
                    !_nodeById.TryGetValue(otherId.Value, out var otherNode) ||
                    !TryGetAnsiblexZoneInfo(otherNode.Id, out var otherZone))
                {
                    continue;
                }

                var toCost = AnsiblexZones.CostTj(AnsiblexShipBaseCostTj, otherZone.Zone);
                var backCost = AnsiblexZones.CostTj(AnsiblexShipBaseCostTj, ownZone.Zone);
                partners.Add(new AnsiblexHoverLine
                {
                    Zone = otherZone.Zone,
                    SortName = otherNode.Name,
                    Label = CreateHoverText(otherNode.Name, Brushes.White, 12),
                    Outbound = CreateHoverText(CompactAnsiblexCost(toCost) + " TJ", ink, 11, FontWeight.SemiBold),
                    OutboundZone = otherZone.Zone,
                    Inbound = CreateHoverText(CompactAnsiblexCost(backCost) + " TJ", ink, 11, FontWeight.SemiBold),
                    InboundZone = ownZone.Zone
                });
            }
        }

        partners.Sort((a, b) => string.Compare(a.SortName, b.SortName, StringComparison.OrdinalIgnoreCase));
        var capitalName = string.IsNullOrWhiteSpace(AnsiblexCapitalName) ? "capital" : AnsiblexCapitalName;
        var lines = new List<AnsiblexHoverLine>
        {
            new()
            {
                Zone = ownZone.Zone,
                Label = CreateHoverText($"{ownZone.DistanceLy:0.#} ly", Brushes.White, 12),
                Meta = CreateHoverText(capitalName, Brushes.White, 12),
                Ship = partners.Count > 0 && !string.IsNullOrWhiteSpace(AnsiblexShipClassName)
                    ? CreateHoverText(AnsiblexShipClassName, Brushes.White, 12)
                    : null,
                ShowCapitalMark = true
            }
        };
        lines.AddRange(partners);
        return lines;
    }

    private static FormattedText CreateHoverText(string text, IBrush brush, double size, FontWeight weight = FontWeight.Normal) =>
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, weight),
            size,
            brush);

    private static string CompactAnsiblexCost(double costTj) =>
        costTj <= 0 ? "0" : costTj.ToString("0.#", CultureInfo.InvariantCulture);

    private static FormattedText CreateZoneChipText(int zone) =>
        new(
            $"Z{zone}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            NodeLabelTypeface,
            9,
            new ImmutableSolidColorBrush(Color.Parse("#0D131D")));

    private static double MeasureAnsiblexHoverLine(AnsiblexHoverLine line)
    {
        var width = CreateZoneChipText(line.Zone).Width + 8 + 4 + line.Label.Width;
        if (line.ShowCapitalMark)
        {
            width += 6 + 8;
        }

        if (line.Meta is not null)
        {
            width += 4 + line.Meta.Width;
        }

        if (line.Ship is not null)
        {
            width += 8 + line.Ship.Width;
        }

        if (line.Outbound is not null)
        {
            width += 8 + AnsiblexCostPillWidth(line.Outbound);
        }

        if (line.Inbound is not null)
        {
            width += 4 + AnsiblexCostPillWidth(line.Inbound);
        }

        return width;
    }

    private void DrawAnsiblexHoverLine(DrawingContext context, Point origin, AnsiblexHoverLine line, double height)
    {
        var chipText = CreateZoneChipText(line.Zone);
        var chipWidth = chipText.Width + 8;
        var chipRect = new Rect(origin.X, origin.Y, chipWidth, height);
        context.FillRectangle(GetCachedBrush(Color.Parse(AnsiblexZones.ColorHex(line.Zone))), chipRect, 3);
        context.DrawText(chipText, new Point(chipRect.X + 4, chipRect.Y + ((height - chipText.Height) / 2)));

        var x = chipRect.Right + 4;
        var labelY = origin.Y + ((height - line.Label.Height) / 2);
        context.DrawText(line.Label, new Point(x, labelY));
        x += line.Label.Width;
        if (line.ShowCapitalMark)
        {
            x += 6;
            context.DrawEllipse(null, AnsiblexCapitalRingPen, new Point(x + 4, origin.Y + (height / 2)), 3.5, 3.5);
            x += 8;
        }

        if (line.Meta is not null)
        {
            x += 3;
            context.DrawText(line.Meta, new Point(x, origin.Y + ((height - line.Meta.Height) / 2)));
            x += line.Meta.Width;
        }

        if (line.Ship is not null)
        {
            x += 8;
            context.DrawText(line.Ship, new Point(x, origin.Y + ((height - line.Ship.Height) / 2)));
            x += line.Ship.Width;
        }

        if (line.Outbound is not null)
        {
            x += 8;
            x += DrawAnsiblexCostPill(context, new Point(x, origin.Y), line.Outbound, line.OutboundZone, height, pointsRight: true);
        }

        if (line.Inbound is not null)
        {
            x += 4;
            DrawAnsiblexCostPill(context, new Point(x, origin.Y), line.Inbound, line.InboundZone, height, pointsRight: false);
        }
    }

    private const double AnsiblexDirectionArrowWidth = 12;
    private const double AnsiblexDirectionArrowHeight = 10;
    private static readonly StreamGeometry AnsiblexRightArrow = CreateAnsiblexDirectionArrow(pointsRight: true);
    private static readonly StreamGeometry AnsiblexLeftArrow = CreateAnsiblexDirectionArrow(pointsRight: false);
    private static readonly IBrush AnsiblexDirectionArrowBrush = new ImmutableSolidColorBrush(Color.Parse("#F7FAFF"));
    private static readonly Pen AnsiblexDirectionArrowPen = new(new ImmutableSolidColorBrush(Color.Parse("#0D131D")), 1.15);

    private static double AnsiblexCostPillWidth(FormattedText text) =>
        5 + AnsiblexDirectionArrowWidth + 5 + text.Width + 6;

    private double DrawAnsiblexCostPill(DrawingContext context, Point origin, FormattedText text, int zone, double height, bool pointsRight)
    {
        var width = AnsiblexCostPillWidth(text);
        var rect = new Rect(origin.X, origin.Y, width, height);
        context.FillRectangle(GetCachedBrush(Color.Parse(AnsiblexZones.ColorHex(zone))), rect, 3);
        var arrowTop = origin.Y + ((height - AnsiblexDirectionArrowHeight) / 2);
        using (context.PushTransform(Matrix.CreateTranslation(origin.X + 5, arrowTop)))
        {
            context.DrawGeometry(AnsiblexDirectionArrowBrush, AnsiblexDirectionArrowPen, pointsRight ? AnsiblexRightArrow : AnsiblexLeftArrow);
        }

        context.DrawText(text, new Point(origin.X + 5 + AnsiblexDirectionArrowWidth + 5, origin.Y + ((height - text.Height) / 2)));
        return width;
    }

    private static StreamGeometry CreateAnsiblexDirectionArrow(bool pointsRight)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            // Thick shaft with a full-height head, in a 12×10 box.
            var figure = pointsRight
                ? new Point[]
                {
                    new(0, 3),
                    new(6, 3),
                    new(6, 0),
                    new(12, 5),
                    new(6, 10),
                    new(6, 7),
                    new(0, 7)
                }
                : new Point[]
                {
                    new(12, 3),
                    new(6, 3),
                    new(6, 0),
                    new(0, 5),
                    new(6, 10),
                    new(6, 7),
                    new(12, 7)
                };
            ctx.BeginFigure(figure[0], true);
            for (var i = 1; i < figure.Length; i++)
            {
                ctx.LineTo(figure[i]);
            }

            ctx.EndFigure(true);
        }

        return geometry;
    }

    private bool IsAnsiblexZoneBadgeNode(long nodeId)
    {
        if (!ReferenceEquals(_ansiblexBadgeSource, AnsiblexLinks) || _ansiblexBadgeCapitalId != AnsiblexCapitalSystemId)
        {
            _ansiblexBadgeSource = AnsiblexLinks;
            _ansiblexBadgeCapitalId = AnsiblexCapitalSystemId;
            _ansiblexBadgeNodeIds = [];
            if (AnsiblexCapitalSystemId is long capitalId)
            {
                _ansiblexBadgeNodeIds.Add(capitalId);
            }

            if (AnsiblexLinks is not null)
            {
                foreach (var link in AnsiblexLinks)
                {
                    _ansiblexBadgeNodeIds.Add(link.FromId);
                    _ansiblexBadgeNodeIds.Add(link.ToId);
                }
            }
        }

        return _ansiblexBadgeNodeIds!.Contains(nodeId);
    }

    private void DrawAnimatedRegularLinkEffect(DrawingContext context, Point from, Point to, Color baseColor, MapLink link)
    {
        var glowPen = new Pen(GetCachedBrush(Color.FromArgb(176, baseColor.R, baseColor.G, baseColor.B)), 2.9);
        context.DrawLine(glowPen, from, to);

        var baseT = (_linkAnimationPhase + (((link.FromId * 17) + (link.ToId * 31)) % 100) / 100.0) % 1.0;
        for (var i = 0; i < 3; i++)
        {
            var t = (baseT + (i * 0.27)) % 1.0;
            var p = new Point(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));
            var alpha = (byte)(235 - (i * 35));
            var radius = 3.15 - (i * 0.38);
            context.DrawEllipse(GetCachedBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B)), null, p, radius, radius);
        }
    }

    private static Color GetAnimatedRegularLinkColor(Pen basePen)
    {
        if (ReferenceEquals(basePen, SameConstellationPen))
        {
            return AnimatedSameConstellationColor;
        }

        if (ReferenceEquals(basePen, SameRegionPen))
        {
            return AnimatedSameRegionColor;
        }

        if (ReferenceEquals(basePen, CrossRegionPen))
        {
            return AnimatedCrossRegionColor;
        }

        return AnimatedDefaultLinkColor;
    }

    private bool ShouldAnimateAnyLink()
    {
        if (!EnableLinkAnimations || Graph is null)
        {
            return false;
        }

        var anchorNodeId = _hoveredNodeId ?? SelectedNodeId;
        if (anchorNodeId is null)
        {
            return false;
        }

        var id = anchorNodeId.Value;
        if (Graph.Links.Any(l => l.FromId == id || l.ToId == id))
        {
            return true;
        }

        if (ShowAnsiblexNetwork &&
            ViewMode != MapViewMode.UniverseRegions &&
            AnsiblexLinks is not null &&
            AnsiblexLinks.Any(l => l.FromId == id || l.ToId == id))
        {
            return true;
        }

        return false;
    }

    private bool HasAnimatedIntelRings()
    {
        if (!EnableIntelRingAnimations)
        {
            return false;
        }

        if (Graph is null ||
            Bounds.Width <= 1 ||
            Bounds.Height <= 1 ||
            IntelHostileScoresByNodeId is null ||
            IntelHostileScoresByNodeId.Count == 0)
        {
            return false;
        }

        // When zoomed out we render static rings without orbit icons.
        // In that mode there is no visual state to animate, so avoid frame-by-frame invalidation.
        var plot = GetPlotMetrics();
        if (!ShouldShowInlineLabels(plot))
        {
            return false;
        }

        if (_screenPositions.Length != Graph.Nodes.Count)
        {
            UpdateScreenPositions(plot, Bounds.Width / 2.0, Bounds.Height / 2.0);
        }

        var bounds = Bounds;
        foreach (var (nodeId, score) in IntelHostileScoresByNodeId)
        {
            if (score <= 0)
            {
                continue;
            }

            if (!_nodeIndexById.TryGetValue(nodeId, out var index) ||
                index < 0 ||
                index >= _screenPositions.Length)
            {
                continue;
            }

            if (IsPointVisible(_screenPositions[index], bounds, 40))
            {
                return true;
            }
        }

        return false;
    }

    private FormattedText GetNodeLabel(long nodeId, string name)
    {
        var fontSize = ShowEditorGrid ? EditorNodeLabelFontSize : NodeLabelFontSize;
        var key = $"{nodeId}:{name}:{fontSize:F1}";
        if (_nodeLabelCache.TryGetValue(key, out var text))
        {
            return text;
        }

        text = new FormattedText(
            name,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            NodeLabelTypeface,
            fontSize,
            new SolidColorBrush(Color.Parse("#EEF6FF")));
        _nodeLabelCache[key] = text;
        return text;
    }

    private FormattedText GetNodeLabelHalo(long nodeId, string name)
    {
        var fontSize = ShowEditorGrid ? EditorNodeLabelFontSize : NodeLabelFontSize;
        var key = $"{nodeId}:{name}:{fontSize:F1}";
        if (_nodeLabelHaloCache.TryGetValue(key, out var text))
        {
            return text;
        }

        text = new FormattedText(
            name,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            NodeLabelTypeface,
            fontSize,
            new ImmutableSolidColorBrush(Color.Parse("#AA0A111A")));
        _nodeLabelHaloCache[key] = text;
        return text;
    }

    private FormattedText GetNodeSecondaryLabel(long nodeId, string name)
    {
        var fontSize = ShowEditorGrid ? EditorNodeLabelFontSize : NodeLabelFontSize;
        var key = $"{nodeId}:{name}:{fontSize:F1}";
        if (_nodeSecondaryLabelCache.TryGetValue(key, out var text))
        {
            return text;
        }

        text = new FormattedText(
            name,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            NodeLabelTypeface,
            fontSize,
            new SolidColorBrush(Color.Parse("#E6F0FF")));
        _nodeSecondaryLabelCache[key] = text;
        return text;
    }

    private FormattedText GetNodeSecondaryLabelHalo(long nodeId, string name)
    {
        var fontSize = ShowEditorGrid ? EditorNodeLabelFontSize : NodeLabelFontSize;
        var key = $"{nodeId}:{name}:{fontSize:F1}";
        if (_nodeSecondaryLabelHaloCache.TryGetValue(key, out var text))
        {
            return text;
        }

        text = new FormattedText(
            name,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            NodeLabelTypeface,
            fontSize,
            new ImmutableSolidColorBrush(Color.Parse("#AA0A111A")));
        _nodeSecondaryLabelHaloCache[key] = text;
        return text;
    }

    private void EnsureVoronoiWorldPolygons()
    {
        if (Graph is null)
        {
            _voronoiWorldPolygonsByNodeId.Clear();
            _voronoiWorldGeometriesByNodeId.Clear();
            _lastGraphForVoronoi = null;
            _lastVoronoiCacheKey = null;
            CancelVoronoiBuild();
            return;
        }

        var cacheKey = ComputeVoronoiCacheKey(Graph);
        if (ReferenceEquals(_lastGraphForVoronoi, Graph) && _lastVoronoiCacheKey == cacheKey && _voronoiWorldPolygonsByNodeId.Count > 0)
        {
            return;
        }

        _voronoiWorldPolygonsByNodeId.Clear();
        _voronoiWorldGeometriesByNodeId.Clear();
        _lastGraphForVoronoi = Graph;
        _lastVoronoiCacheKey = cacheKey;

        if (TryLoadVoronoiFromCache(cacheKey, out var cachedPolygons))
        {
            foreach (var kvp in cachedPolygons)
            {
                _voronoiWorldPolygonsByNodeId[kvp.Key] = kvp.Value;
                _voronoiWorldGeometriesByNodeId[kvp.Key] = BuildWorldGeometry(kvp.Value);
            }
            return;
        }

        StartVoronoiBuild(cacheKey, Graph);
    }

    private void CancelVoronoiBuild()
    {
        _voronoiBuildCts?.Cancel();
        _voronoiBuildCts?.Dispose();
        _voronoiBuildCts = null;
        _voronoiBuildTask = null;
        _voronoiBuildKey = null;
    }

    private void StartVoronoiBuild(string cacheKey, MapGraph graph)
    {
        if (_voronoiBuildTask is { IsCompleted: false } && _voronoiBuildKey == cacheKey)
        {
            return;
        }

        CancelVoronoiBuild();
        _voronoiBuildCts = new CancellationTokenSource();
        _voronoiBuildKey = cacheKey;
        var token = _voronoiBuildCts.Token;

        _voronoiBuildTask = Task.Run(
            () => BuildVoronoiPolygons(graph, _graphMinX, _graphMaxX, _graphMinY, _graphMaxY, _typicalLinkSpacing, token),
            token);

        _ = _voronoiBuildTask.ContinueWith(task =>
        {
            if (task.IsCanceled || task.IsFaulted || token.IsCancellationRequested)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (Graph is null || _lastVoronoiCacheKey != cacheKey || _voronoiBuildKey != cacheKey)
                {
                    return;
                }

                _voronoiWorldPolygonsByNodeId.Clear();
                _voronoiWorldGeometriesByNodeId.Clear();
                foreach (var kvp in task.Result)
                {
                    _voronoiWorldPolygonsByNodeId[kvp.Key] = kvp.Value;
                    _voronoiWorldGeometriesByNodeId[kvp.Key] = BuildWorldGeometry(kvp.Value);
                }

                SaveVoronoiToCache(cacheKey, _voronoiWorldPolygonsByNodeId);
                InvalidateMap();
            }, DispatcherPriority.Background);
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private static Dictionary<long, IReadOnlyList<(double X, double Y)>> BuildVoronoiPolygons(
        MapGraph graph,
        double graphMinX,
        double graphMaxX,
        double graphMinY,
        double graphMaxY,
        double typicalLinkSpacing,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, IReadOnlyList<(double X, double Y)>>();
        if (graph.Nodes.Count < 2 || cancellationToken.IsCancellationRequested)
        {
            return result;
        }

        var geometryFactory = new NtsGeometryFactory();
        var minX = graphMinX;
        var maxX = graphMaxX;
        var minY = graphMinY;
        var maxY = graphMaxY;
        var spacing = typicalLinkSpacing > 0 ? typicalLinkSpacing : EstimateTypicalLinkSpacing(graph.Nodes, graph.Links);
        var nodeMaskRadius = Math.Clamp(spacing * 1.95, 0.012, 0.095);
        var linkMaskRadius = Math.Clamp(spacing * 0.72, 0.006, 0.044);
        var maxBufferedLinkLength = spacing * 3.5;
        var envelopePad = Math.Clamp(spacing * 5.0, 0.04, 0.20);

        var nodeIdsByKey = graph.Nodes
            .GroupBy(n => $"{Math.Round(n.X, 8)}:{Math.Round(n.Y, 8)}")
            .ToDictionary(g => g.Key, g => g.Select(n => n.Id).Distinct().ToList());

        var sites = graph.Nodes
            .Select(n => new NtsCoordinate(n.X, n.Y))
            .ToList();

        var territoryMask = BuildVoronoiTerritoryMask(
            graph.Nodes,
            graph.Links,
            geometryFactory,
            nodeMaskRadius,
            linkMaskRadius,
            maxBufferedLinkLength);

        if (territoryMask is null || territoryMask.IsEmpty || cancellationToken.IsCancellationRequested)
        {
            return result;
        }

        var builder = new VoronoiDiagramBuilder();
        builder.SetSites(sites);
        builder.ClipEnvelope = new NtsEnvelope(
            minX - envelopePad,
            maxX + envelopePad,
            minY - envelopePad,
            maxY + envelopePad);

        var diagram = builder.GetDiagram(geometryFactory) as NtsGeometryCollection;
        if (diagram is null)
        {
            return result;
        }

        for (var i = 0; i < diagram.NumGeometries; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return result;
            }

            if (diagram.GetGeometryN(i) is not NtsPolygon polygon ||
                polygon.ExteriorRing is null ||
                polygon.ExteriorRing.NumPoints < 4)
            {
                continue;
            }

            var nodeId = ResolveVoronoiOwnerNodeId(polygon, nodeIdsByKey, graph);
            if (nodeId is null)
            {
                continue;
            }

            var clipped = SafeIntersection(polygon, territoryMask);
            if (clipped is null || clipped.IsEmpty)
            {
                continue;
            }
            var points = ExtractLargestPolygonCoordinates(clipped);

            if (points.Count < 3)
            {
                continue;
            }

            result[nodeId.Value] = points;
        }
        return result;
    }

    private static NtsGeometry? SafeIntersection(NtsGeometry a, NtsGeometry b)
    {
        try
        {
            return a.Intersection(b);
        }
        catch
        {
            // Robust fallback for occasional non-noded intersections from Voronoi edges.
            try
            {
                var precision = new NetTopologySuite.Geometries.PrecisionModel(1_000_000d);
                var reducer = new GeometryPrecisionReducer(precision)
                {
                    ChangePrecisionModel = true,
                    Pointwise = false,
                    RemoveCollapsedComponents = true
                };

                var ra = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(reducer.Reduce(a)).Buffer(0);
                var rb = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(reducer.Reduce(b)).Buffer(0);
                return ra.Intersection(rb);
            }
            catch
            {
                return null;
            }
        }
    }

    private static string ComputeVoronoiCacheKey(MapGraph graph) => VoronoiCacheStore.ComputeKey(graph);

    private static bool TryLoadVoronoiFromCache(string key, out Dictionary<long, IReadOnlyList<(double X, double Y)>> polygons)
    {
        if (VoronoiMemoryCache.TryGet(key, out polygons))
        {
            return true;
        }

        polygons = [];
        try
        {
            var path = Path.Combine(VoronoiCacheDirectory, $"{key}.json");
            if (!File.Exists(path))
            {
                return false;
            }

            var json = File.ReadAllText(path);
            var model = JsonSerializer.Deserialize<VoronoiCacheModel>(json, VoronoiJsonOptions);
            if (model is null || model.Polygons.Count == 0)
            {
                return false;
            }

            foreach (var kvp in model.Polygons)
            {
                polygons[kvp.Key] = kvp.Value.Select(p => (p.X, p.Y)).ToList();
            }

            VoronoiMemoryCache.Set(key, polygons);
            return polygons.Count > 0;
        }
        catch
        {
            polygons = [];
            return false;
        }
    }

    private static void SaveVoronoiToCache(string key, IReadOnlyDictionary<long, IReadOnlyList<(double X, double Y)>> polygons)
    {
        if (polygons.Count == 0)
        {
            return;
        }

        var inMemory = new Dictionary<long, IReadOnlyList<(double X, double Y)>>(polygons.Count);
        foreach (var kvp in polygons)
        {
            inMemory[kvp.Key] = kvp.Value.ToList();
        }
        VoronoiMemoryCache.Set(key, inMemory);

        try
        {
            Directory.CreateDirectory(VoronoiCacheDirectory);
            var model = new VoronoiCacheModel
            {
                Key = key,
                Polygons = polygons.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Select(p => new PointDto { X = p.X, Y = p.Y }).ToList())
            };

            var json = JsonSerializer.Serialize(model, VoronoiJsonOptions);
            var path = Path.Combine(VoronoiCacheDirectory, $"{key}.json");
            File.WriteAllText(path, json);
        }
        catch
        {
            // Best-effort cache persistence only.
        }
    }

    private static NtsGeometry? BuildVoronoiTerritoryMask(
    IReadOnlyList<MapNode> nodes,
    IReadOnlyList<MapLink> links,
    NtsGeometryFactory geometryFactory,
    double nodeMaskRadius,
    double linkMaskRadius,
    double maxBufferedLinkLength)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        var parts = new List<NtsGeometry>();

        foreach (var node in nodes)
        {
            var point = geometryFactory.CreatePoint(new NtsCoordinate(node.X, node.Y));
            parts.Add(point.Buffer(nodeMaskRadius, 8));
        }

        foreach (var link in links)
        {
            if (!byId.TryGetValue(link.FromId, out var from) ||
                !byId.TryGetValue(link.ToId, out var to))
            {
                continue;
            }

            var dx = from.X - to.X;
            var dy = from.Y - to.Y;
            var linkLength = Math.Sqrt((dx * dx) + (dy * dy));

            // Skip long map-spanning links so they don't create huge territory ribbons.
            if (linkLength > maxBufferedLinkLength)
            {
                continue;
            }

            var line = geometryFactory.CreateLineString([
                new NtsCoordinate(from.X, from.Y),
    new NtsCoordinate(to.X, to.Y)
            ]);

            parts.Add(line.Buffer(linkMaskRadius, 6));
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var union = NtsUnaryUnionOp.Union(parts);

        // Small smoothing pass.
        var smoothOut = nodeMaskRadius * 0.28;
        var smoothIn = nodeMaskRadius * 0.07;

        return union
            .Buffer(smoothOut, 4)
            .Buffer(-smoothIn, 4);
    }

    private static long? ResolveVoronoiOwnerNodeId(
    NtsPolygon polygon,
    IReadOnlyDictionary<string, List<long>> nodeIdsByKey,
    MapGraph graph)
    {
        if (polygon.UserData is NtsCoordinate site)
        {
            var key = $"{Math.Round(site.X, 8)}:{Math.Round(site.Y, 8)}";
            if (nodeIdsByKey.TryGetValue(key, out var nodeIds) && nodeIds.Count > 0)
            {
                if (nodeIds.Count == 1)
                {
                    return nodeIds[0];
                }

                // Duplicate coordinates: pick deterministically by nearest to centroid,
                // with stable tie-break on node id.
                var centroidForDuplicate = polygon.Centroid;
                return nodeIds
                    .OrderBy(id =>
                    {
                        var node = graph.Nodes.FirstOrDefault(n => n.Id == id);
                        if (node is null)
                        {
                            return double.MaxValue;
                        }

                        var dx = node.X - centroidForDuplicate.X;
                        var dy = node.Y - centroidForDuplicate.Y;
                        return (dx * dx) + (dy * dy);
                    })
                    .ThenBy(id => id)
                    .First();
            }
        }

        var centroid = polygon.Centroid;

        var nearest = graph.Nodes
            .OrderBy(n =>
            {
                var dx = n.X - centroid.X;
                var dy = n.Y - centroid.Y;
                return (dx * dx) + (dy * dy);
            })
            .FirstOrDefault();

        return nearest?.Id;
    }

    private static IReadOnlyList<(double X, double Y)> ExtractLargestPolygonCoordinates(NtsGeometry geometry)
    {
        if (geometry.IsEmpty)
        {
            return [];
        }

        NtsPolygon? bestPolygon = null;
        var bestArea = 0.0;

        void Consider(NtsGeometry candidate)
        {
            if (candidate is NtsPolygon polygon &&
                polygon.ExteriorRing is not null &&
                polygon.ExteriorRing.NumPoints >= 4 &&
                polygon.Area > bestArea)
            {
                bestPolygon = polygon;
                bestArea = polygon.Area;
            }
        }

        if (geometry is NtsPolygon singlePolygon)
        {
            Consider(singlePolygon);
        }
        else if (geometry is NtsMultiPolygon multiPolygon)
        {
            for (var i = 0; i < multiPolygon.NumGeometries; i++)
            {
                Consider(multiPolygon.GetGeometryN(i));
            }
        }
        else if (geometry is NtsGeometryCollection collection)
        {
            for (var i = 0; i < collection.NumGeometries; i++)
            {
                Consider(collection.GetGeometryN(i));
            }
        }

        if (bestPolygon is null)
        {
            return [];
        }

        return bestPolygon.ExteriorRing.Coordinates
            .Take(bestPolygon.ExteriorRing.Coordinates.Length - 1)
            .Select(c => (c.X, c.Y))
            .ToList();
    }

    private static double EstimateTypicalLinkSpacing(
    IReadOnlyList<MapNode> nodes,
    IReadOnlyList<MapLink> links)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        var lengths = new List<double>(links.Count);

        foreach (var link in links)
        {
            if (!byId.TryGetValue(link.FromId, out var from) ||
                !byId.TryGetValue(link.ToId, out var to))
            {
                continue;
            }

            var dx = from.X - to.X;
            var dy = from.Y - to.Y;
            var d = Math.Sqrt((dx * dx) + (dy * dy));

            if (d > 0)
            {
                lengths.Add(d);
            }
        }

        if (lengths.Count == 0)
        {
            var minX = nodes.Min(n => n.X);
            var maxX = nodes.Max(n => n.X);
            var minY = nodes.Min(n => n.Y);
            var maxY = nodes.Max(n => n.Y);

            return Math.Max((maxX - minX + maxY - minY) * 0.015, 0.01);
        }

        lengths.Sort();
        return lengths[lengths.Count / 2];
    }

    private static StreamGeometry BuildWorldGeometry(IReadOnlyList<(double X, double Y)> polygon)
    {
        var geometry = new StreamGeometry();

        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(polygon[0].X, polygon[0].Y), true);
            for (var i = 1; i < polygon.Count; i++)
            {
                g.LineTo(new Point(polygon[i].X, polygon[i].Y));
            }
            g.EndFigure(true);
        }

        return geometry;
    }

    private void DrawVoronoiCellWorld(DrawingContext context, MapNode node, Pen borderPen)
    {
        if (!_voronoiWorldGeometriesByNodeId.TryGetValue(node.Id, out var geometry))
        {
            return;
        }

        if (NodeBackgroundColorMode == MapNodeColorMode.JoveObservatory && !node.HasJoveObservatory)
        {
            return;
        }

        if (NodeBackgroundColorMode == MapNodeColorMode.IceBelts && node.IceFieldCount <= 0)
        {
            return;
        }
        if (NodeBackgroundColorMode == MapNodeColorMode.Storms && node.StormEffects.Count == 0)
        {
            return;
        }
        if (NodeBackgroundColorMode == MapNodeColorMode.Wormholes && node.HubWormholeConnections.Count == 0)
        {
            return;
        }
        if (NodeBackgroundColorMode == MapNodeColorMode.SovUpgrades)
        {
            var visibleSov = GetVisibleSovUpgrades(node.SovUpgrades, IndicatorSovUpgradeFilterKeys);
            if (!visibleSov.Any())
            {
                return;
            }
        }
        if (NodeBackgroundColorMode == MapNodeColorMode.Incursions && !node.HasActiveIncursion)
        {
            return;
        }
        if (NodeBackgroundColorMode == MapNodeColorMode.SystemJumps && node.SystemJumps <= 0)
        {
            return;
        }
        if (NodeBackgroundColorMode == MapNodeColorMode.ShipKills && node.ShipKills <= 0)
        {
            return;
        }
        if (NodeBackgroundColorMode == MapNodeColorMode.PodKills && node.PodKills <= 0)
        {
            return;
        }
        if (NodeBackgroundColorMode == MapNodeColorMode.NpcKills && node.NpcKills <= 0)
        {
            return;
        }
        if (NodeBackgroundColorMode == MapNodeColorMode.Hostiles)
        {
            if (IntelHostileScoresByNodeId is null ||
                !IntelHostileScoresByNodeId.TryGetValue(node.Id, out var hostileScore) ||
                hostileScore <= 0)
            {
                // No active intel for this node: keep background transparent.
                return;
            }
        }

        var color = GetNodeBaseColor(node, NodeBackgroundColorMode);
        var tuned = BrightenForBackground(color);
        var baseFill = GetCachedBrush(tuned, 0.40);
        context.DrawGeometry(baseFill, borderPen, geometry);
    }


    private Color GetNodeBaseColor(MapNode node, MapNodeColorMode mode)
    {
        return mode switch
        {
            MapNodeColorMode.Security => GetSecurityColor(node),
            MapNodeColorMode.Hostiles => GetIntelHostileColorForNode(node.Id),
            MapNodeColorMode.Region => GetRegionColor(node.RegionId),
            MapNodeColorMode.Star => GetStarColor(node),
            MapNodeColorMode.NullsecTrueSec => GetNullsecTrueSecColor(node),
            MapNodeColorMode.JoveObservatory => node.HasJoveObservatory ? Color.Parse("#2ED436") : Color.Parse("#98A6B8"),
            MapNodeColorMode.IceBelts => node.IceFieldCount > 0 ? Color.Parse("#58B9FF") : Color.Parse("#98A6B8"),
            MapNodeColorMode.Storms => GetStormColor(node),
            MapNodeColorMode.Wormholes => GetHubWormholeColor(node),
            MapNodeColorMode.SovUpgrades => GetSovUpgradeColor(GetVisibleSovUpgrades(node.SovUpgrades, IndicatorSovUpgradeFilterKeys).ToList()),
            MapNodeColorMode.Incursions => node.HasActiveIncursion ? Color.Parse("#A77BFF") : Color.Parse("#98A6B8"),
            MapNodeColorMode.SystemJumps => GetActivityHeatColor(MapNodeColorMode.SystemJumps, node.SystemJumps),
            MapNodeColorMode.ShipKills => GetActivityHeatColor(MapNodeColorMode.ShipKills, node.ShipKills),
            MapNodeColorMode.PodKills => GetActivityHeatColor(MapNodeColorMode.PodKills, node.PodKills),
            MapNodeColorMode.NpcKills => GetActivityHeatColor(MapNodeColorMode.NpcKills, node.NpcKills),
            MapNodeColorMode.AnsiblexZone => GetAnsiblexZoneColor(node),
            _ => Color.Parse("#98A6B8")
        };
    }

    private Color GetActivityHeatColor(MapNodeColorMode mode, int value)
    {
        if (mode == MapNodeColorMode.NpcKills)
        {
            return GetNpcKillsBandColor(value);
        }

        EnsureActivityRanges();
        if (!_activityRangesByMode.TryGetValue(mode, out var range))
        {
            return Color.Parse("#98A6B8");
        }

        var min = range.Min;
        var max = range.Max;
        if (max <= min)
        {
            max = min + 1;
        }

        var t = Math.Clamp((value - min) / (double)(max - min), 0.0, 1.0);
        return GetFourBandRampColor(t);
    }

    private Color GetNpcKillsBandColor(int value)
    {
        if (value <= 10)
        {
            return Color.Parse("#98A6B8");
        }

        if (value <= 500)
        {
            var t = Math.Clamp((value - 11) / (double)(500 - 11), 0.0, 1.0);
            return BlendColors(Color.Parse("#DDF6E6"), Color.Parse("#3DBB67"), t);
        }

        if (value <= 1000)
        {
            var t = Math.Clamp((value - 501) / (double)(1000 - 501), 0.0, 1.0);
            return BlendColors(Color.Parse("#FFF6BF"), Color.Parse("#F3E66E"), t);
        }

        if (value <= 1900)
        {
            var t = Math.Clamp((value - 1001) / (double)(1900 - 1001), 0.0, 1.0);
            return BlendColors(Color.Parse("#FFD5A8"), Color.Parse("#F29B38"), t);
        }

        if (value <= 2400)
        {
            var t = Math.Clamp((value - 1901) / (double)(2400 - 1901), 0.0, 1.0);
            return BlendColors(Color.Parse("#FF8A8A"), Color.Parse("#FF2D2D"), t);
        }

        var maxUpper = 3000;
        var tRed = Math.Clamp((value - 2401) / (double)(maxUpper - 2401), 0.0, 1.0);
        return BlendColors(Color.Parse("#963b73"), Color.Parse("#860053"), tRed);
    }

    private static Color GetFourBandRampColor(double t)
    {
        var c0 = Color.Parse("#DDF6E6");
        var c1 = Color.Parse("#F3E66E");
        var c2 = Color.Parse("#F29B38");
        var c3 = Color.Parse("#FF2D2D");
        return t switch
        {
            <= 1.0 / 3.0 => BlendColors(c0, c1, t * 3.0),
            <= 2.0 / 3.0 => BlendColors(c1, c2, (t - (1.0 / 3.0)) * 3.0),
            _ => BlendColors(c2, c3, (t - (2.0 / 3.0)) * 3.0)
        };
    }

    private void EnsureActivityRanges()
    {
        var currentGraph = Graph;
        var nodeCount = currentGraph?.Nodes.Count ?? 0;
        if (ReferenceEquals(currentGraph, _activityRangeGraph) && nodeCount == _activityRangeNodeCount)
        {
            return;
        }

        _activityRangeGraph = currentGraph;
        _activityRangeNodeCount = nodeCount;
        _activityRangesByMode.Clear();
        if (currentGraph is null || nodeCount == 0)
        {
            return;
        }

        BuildActivityRange(currentGraph.Nodes, MapNodeColorMode.SystemJumps, static n => n.SystemJumps);
        BuildActivityRange(currentGraph.Nodes, MapNodeColorMode.ShipKills, static n => n.ShipKills);
        BuildActivityRange(currentGraph.Nodes, MapNodeColorMode.PodKills, static n => n.PodKills);
    }

    private void BuildActivityRange(IReadOnlyList<MapNode> nodes, MapNodeColorMode mode, Func<MapNode, int> selector)
    {
        var values = nodes
            .Select(selector)
            .Where(v => v > 0)
            .ToArray();
        if (values.Length == 0)
        {
            return;
        }

        _activityRangesByMode[mode] = (values.Min(), values.Max());
    }

    private static Color GetSovUpgradeColor(IReadOnlyList<SovUpgradeEntry> upgrades)
    {
        if (upgrades.Count == 0)
        {
            return Color.Parse("#98A6B8");
        }

        if (upgrades.Any(x => x.UpgradeName.Contains("Major Threat Detection Array", StringComparison.OrdinalIgnoreCase)))
        {
            return Color.Parse("#D06A4A");
        }

        if (upgrades.Any(x => x.UpgradeName.Contains("Minor Threat Detection Array", StringComparison.OrdinalIgnoreCase)))
        {
            return Color.Parse("#E7A95D");
        }

        if (upgrades.Any(x => x.UpgradeName.Contains("Exploration Detector", StringComparison.OrdinalIgnoreCase)))
        {
            return Color.Parse("#58B9FF");
        }

        return Color.Parse("#8CC8A5");
    }

    private static Color GetHubWormholeColor(MapNode node)
    {
        var hasThera = node.HubWormholeConnections.Any(c => c.HubType == WormholeHubType.Thera);
        var hasTurnur = node.HubWormholeConnections.Any(c => c.HubType == WormholeHubType.Turnur);
        if (!hasThera && !hasTurnur)
        {
            return Color.Parse("#98A6B8");
        }

        if (hasThera && hasTurnur)
        {
            return Color.Parse("#ff0000");
        }

        return hasThera ? Color.Parse("#00ff00") : Color.Parse("#ff9c1a");
    }

    private static Color GetStormColor(MapNode node)
    {
        var primary = GetPrimaryStormEffect(node);
        if (primary is null)
        {
            return Color.Parse("#98A6B8");
        }

        var baseColor = GetStormBaseColor(primary.Type);
        return primary.Strength switch
        {
            StormStrength.Center => baseColor,
            StormStrength.Strong => LerpColor(baseColor, Color.FromRgb(210, 210, 210), 0.25),
            _ => LerpColor(baseColor, Color.FromRgb(210, 210, 210), 0.50)
        };
    }

    private static Color GetStormBaseColor(StormType type)
    {
        return type switch
        {
            StormType.Electrical => Color.Parse("#4AA8FF"),
            StormType.Plasma => Color.Parse("#DE5B52"),
            StormType.Gamma => Color.Parse("#E69138"),
            StormType.Exotic => Color.Parse("#CFD4DC"),
            _ => Color.Parse("#A9B2BF")
        };
    }

    private static StormEffect? GetPrimaryStormEffect(MapNode node)
    {
        if (node.StormEffects.Count == 0)
        {
            return null;
        }

        return node.StormEffects
            .OrderByDescending(e => e.Strength)
            .ThenBy(e => e.Type)
            .FirstOrDefault();
    }

    private static Color WithAlpha(Color color, double alpha01)
    {
        var a = (byte)Math.Clamp((int)(alpha01 * 255), 0, 255);
        return Color.FromArgb(a, color.R, color.G, color.B);
    }

    private static Color GetSecurityColor(MapNode node)
    {
        var rounded = RoundSecurityForDisplay(node.Security ?? 0.0);
        return rounded switch
        {
            >= 1.0 => Color.Parse("#2C75E1"),
            >= 0.9 => Color.Parse("#399AEB"),
            >= 0.8 => Color.Parse("#4ECEF8"),
            >= 0.7 => Color.Parse("#60DBA3"),
            >= 0.6 => Color.Parse("#71E754"),
            >= 0.5 => Color.Parse("#F5FF83"),
            >= 0.4 => Color.Parse("#DC6C06"),
            >= 0.3 => Color.Parse("#CE440F"),
            >= 0.2 => Color.Parse("#BB1116"),
            >= 0.1 => Color.Parse("#731F1F"),
            _ => Color.Parse("#8D3163")
        };
    }

    private static double RoundSecurityForDisplay(double security)
    {
        if (security == 0.0d)
        {
            return 0.0d;
        }

        if (security > 0.0d && security < 0.05d)
        {
            return Math.Round((security * 10.0d) + 0.5d, MidpointRounding.AwayFromZero) / 10.0d;
        }

        return Math.Round(security * 10.0d, MidpointRounding.AwayFromZero) / 10.0d;
    }

    private string BuildIndicatorLabel(MapNode node) => node.Name;

    private static Color GetRegionColor(int? regionId)
    {
        if (regionId is null)
        {
            return Color.Parse("#8FB0D9");
        }

        var v = Math.Abs(regionId.Value);
        var r = 96 + (v * 37 % 120);
        var g = 96 + (v * 53 % 120);
        var b = 96 + (v * 71 % 120);
        return Color.FromRgb((byte)r, (byte)g, (byte)b);
    }

    private static Color GetStarColor(MapNode node)
    {
        var raw = (node.StarTypeName ?? node.SpectralClass ?? string.Empty).Trim();
        var type = raw.ToUpperInvariant();

        if (type.Length > 0)
        {
            // Explicit in-game type-name mapping first.
            // A0 Blue Small is intentionally the most visible blue.
            if (type == "SUN A0 (BLUE SMALL)")
            {
                return Color.Parse("#4AA3FF");
            }
            if (type == "SUN A0 (CAPTURED BLUE SMALL)")
            {
                return Color.Parse("#63AEFF");
            }
            if (type == "SUN A0 (DISRUPTED BLUE SMALL)")
            {
                return Color.Parse("#6DB6FF");
            }
            if (type == "SUN A0 (GLORY IMMANENCE)")
            {
                return Color.Parse("#5AA9FF");
            }
            if (type == "SUN A0IV (TURBULENT BLUE SUBGIANT)")
            {
                return Color.Parse("#7CC0FF");
            }
            if (type == "SUN B0 (BLUE)")
            {
                return Color.Parse("#79BCFF");
            }
            if (type == "SUN B0 (FRUITFUL IMMANENCE)")
            {
                return Color.Parse("#84C4FF");
            }
            if (type == "SUN B5 (WHITE DWARF)")
            {
                return Color.Parse("#EAF4FF");
            }
            if (type == "SUN F0 (WHITE)")
            {
                return Color.Parse("#F4F8FF");
            }
            if (type == "SUN G3 (PINK SMALL)")
            {
                return Color.Parse("#FFC6DA");
            }
            if (type == "SUN G5 (GOLD IMMANENCE)")
            {
                return Color.Parse("#FFD76B");
            }
            if (type == "SUN G5 (PINK)")
            {
                return Color.Parse("#FFB7D3");
            }
            if (type == "SUN G5 (YELLOW)")
            {
                return Color.Parse("#FFE08A");
            }
            if (type == "SUN K3 (YELLOW SMALL)")
            {
                return Color.Parse("#FFD98A");
            }
            if (type == "SUN K5 (ORANGE BRIGHT)")
            {
                return Color.Parse("#FFB35A");
            }
            if (type == "SUN K5 (RED GIANT)")
            {
                return Color.Parse("#FF8066");
            }
            if (type == "SUN K7 (ORANGE)")
            {
                return Color.Parse("#FFA55E");
            }
            if (type == "SUN M0 (ORANGE RADIANT)")
            {
                return Color.Parse("#FF9560");
            }
            if (type == "SUN O1 (BRIGHT BLUE)")
            {
                return Color.Parse("#66B7FF");
            }
            if (type == "SUN O1 (DIVINE IMMANENCE)")
            {
                return Color.Parse("#73BEFF");
            }

            // Fallback for compact type labels / spectral-like formats.
            var firstClassLetter = type.FirstOrDefault(c => c is >= 'A' and <= 'Z');
            switch (firstClassLetter)
            {
                case 'O': return Color.Parse("#6FA8FF");
                case 'B': return Color.Parse("#9CC4FF");
                case 'A': return Color.Parse("#E9F1FF");
                case 'F': return Color.Parse("#FFF4D6");
                case 'G': return Color.Parse("#FFE08A");
                case 'K': return Color.Parse("#FFB05A");
                case 'M': return Color.Parse("#FF6B5C");
                case 'L': return Color.Parse("#F45A46");
                case 'T': return Color.Parse("#C84B3A");
                case 'Y': return Color.Parse("#A74135");
            }
        }

        if (type.Contains("BLUE") || type.Contains("TYPE O"))
        {
            return Color.Parse("#8FB8FF");
        }
        if (type.Contains("TYPE B"))
        {
            return Color.Parse("#A5C7FF");
        }
        if (type.Contains("WHITE") || type.Contains("TYPE A"))
        {
            return Color.Parse("#EAF2FF");
        }
        if (type.Contains("TYPE F"))
        {
            return Color.Parse("#FFF6D1");
        }
        if (type.Contains("YELLOW") || type.Contains("TYPE G"))
        {
            return Color.Parse("#FFE08A");
        }
        if (type.Contains("ORANGE") || type.Contains("TYPE K"))
        {
            return Color.Parse("#FFB869");
        }
        if (type.Contains("RED") || type.Contains("TYPE M"))
        {
            return Color.Parse("#FF7E6B");
        }
        if (type.Contains("TYPE L"))
        {
            return Color.Parse("#FF985E");
        }
        if (type.Contains("TYPE T"))
        {
            return Color.Parse("#D45B4A");
        }
        if (type.Contains("TYPE Y"))
        {
            return Color.Parse("#B3433A");
        }
        if (type.Contains("DWARF"))
        {
            return Color.Parse("#E7F0FF");
        }
        if (type.Contains("CARBON"))
        {
            return Color.Parse("#FF9A87");
        }

        return Color.Parse("#C9D8EE");
    }

    private static Color GetNullsecTrueSecColor(MapNode node)
    {
        var security = node.Security ?? 0.0;
        if (security > 0.0)
        {
            return Color.Parse("#8FB0D9");
        }

        // Truesec gradient for null systems:
        // -0.1 => orange, mid => red, -1.0 => null purple.
        var clamped = Math.Clamp(security, -1.0, -0.1);
        var t = (-0.1 - clamped) / 0.9; // 0 at -0.1, 1 at -1.0

        var orange = Color.Parse("#FF9B4A");
        var red = Color.Parse("#D84545");
        var purple = Color.Parse("#8D3163");

        if (t < 0.55)
        {
            return LerpColor(orange, red, t / 0.55);
        }

        return LerpColor(red, purple, (t - 0.55) / 0.45);
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        byte Mix(byte x, byte y) => (byte)Math.Clamp((int)Math.Round(x + ((y - x) * t)), 0, 255);
        return Color.FromRgb(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }

    private static Color BrightenForBackground(Color color)
    {
        static byte Lift(byte c)
        {
            // Lift 16% toward white to make Voronoi/background fills read clearer.
            return (byte)Math.Clamp(c + ((255 - c) * 0.16), 0, 255);
        }

        return Color.FromRgb(Lift(color.R), Lift(color.G), Lift(color.B));
    }

    private static string? GetStarClassDisplayValue(MapNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.StarTypeName))
        {
            return node.StarTypeName;
        }

        if (!string.IsNullOrWhiteSpace(node.SpectralClass))
        {
            return node.SpectralClass;
        }

        return null;
    }

    private FormattedText GetRegionLabel(int regionId, string name)
    {
        if (_regionLabelCache.TryGetValue(regionId, out var text))
        {
            return text;
        }

        text = new FormattedText(
            name,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
            14,
            new SolidColorBrush(Color.Parse("#f1f7ff")));
        _regionLabelCache[regionId] = text;
        return text;
    }

    private FormattedText GetRegionLabelHalo(int regionId, string name)
    {
        if (_regionLabelHaloCache.TryGetValue(regionId, out var text))
        {
            return text;
        }

        text = new FormattedText(
            name,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
            14,
            new ImmutableSolidColorBrush(Color.Parse("#CC071018")));
        _regionLabelHaloCache[regionId] = text;
        return text;
    }

    private static void DrawLabelWithHalo(DrawingContext context, FormattedText label, FormattedText halo, Point origin)
    {
        context.DrawText(halo, new Point(origin.X - 1, origin.Y));
        context.DrawText(halo, new Point(origin.X + 1, origin.Y));
        context.DrawText(halo, new Point(origin.X, origin.Y - 1));
        context.DrawText(halo, new Point(origin.X, origin.Y + 1));
        context.DrawText(label, origin);
    }

    private static void DrawNodeLabel(DrawingContext context, FormattedText label, FormattedText halo, Point origin)
    {
        var rect = new Rect(
            origin.X - 3,
            origin.Y - 2,
            label.Width + 6,
            label.Height + 4);
        context.FillRectangle(NodeLabelBackgroundBrush, rect, 3);
        context.DrawText(label, origin);
    }

    private void DrawIndicatorLabel(DrawingContext context, MapNode node, Point origin)
    {
        const double gap = 7.0;
        const double lineGap = 1.0;
        var labelFontSize = ShowEditorGrid ? EditorNodeLabelFontSize : NodeLabelFontSize;
        var regionLabelFontSize = ShowEditorGrid ? EditorRegionConstellationFontSize : (NodeRegionConstellationFontSize - 1);
        var nameText = BuildIndicatorLabel(node);
        var name = GetNodeLabel(node.Id, nameText);
        var nameHalo = GetNodeLabelHalo(node.Id, nameText);
        FormattedText? sec = null;
        FormattedText? secHalo = null;

        if (ShowIndicatorSecurityStatus && node.Security is not null)
        {
            var rounded = RoundSecurityForDisplay(node.Security.Value);
            var securityLabel = rounded.ToString("0.0", CultureInfo.InvariantCulture);
            sec = new FormattedText(
                securityLabel,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                labelFontSize,
                GetCachedBrush(GetSecurityColor(node)));
            secHalo = new FormattedText(
                securityLabel,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                labelFontSize,
                new ImmutableSolidColorBrush(Color.Parse("#AA0A111A")));
        }

        FormattedText? region = null;
        FormattedText? regionHalo = null;
        if ((ShowIndicatorRegion || (ShowEditorGrid && ShowEditorRegionLabel)) && !string.IsNullOrWhiteSpace(node.RegionName))
        {
            region = new FormattedText(
                node.RegionName,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                regionLabelFontSize,
                new SolidColorBrush(Color.Parse("#E6F0FF")));
            regionHalo = new FormattedText(
                node.RegionName,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                regionLabelFontSize,
                new ImmutableSolidColorBrush(Color.Parse("#AA0A111A")));
        }

        FormattedText? constellation = null;
        FormattedText? constellationHalo = null;
        if (ShowIndicatorConstellation && !string.IsNullOrWhiteSpace(node.ConstellationName))
        {
            constellation = new FormattedText(
                node.ConstellationName,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                regionLabelFontSize,
                new SolidColorBrush(Color.Parse("#E6F0FF")));
            constellationHalo = new FormattedText(
                node.ConstellationName,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                regionLabelFontSize,
                new ImmutableSolidColorBrush(Color.Parse("#AA0A111A")));
        }

        var starClass = GetStarClassDisplayValue(node);
        FormattedText? starClassText = null;
        FormattedText? starClassHalo = null;
        if (ShowIndicatorStarClass && !string.IsNullOrWhiteSpace(starClass))
        {
            starClassText = GetNodeSecondaryLabel(node.Id, starClass);
            starClassHalo = GetNodeSecondaryLabelHalo(node.Id, starClass);
        }

        FormattedText? zoneChipText = null;
        Color zoneChipColor = default;
        var zoneChipWidth = 0.0;
        const double zoneChipGap = 6.0;
        if (ShowIndicatorAnsiblexZone && IsAnsiblexZoneBadgeNode(node.Id) && TryGetAnsiblexZoneInfo(node.Id, out var ansiblexZone))
        {
            zoneChipText = new FormattedText(
                $"Z{ansiblexZone.Zone}",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                Math.Max(9, labelFontSize - 1),
                new ImmutableSolidColorBrush(Color.Parse("#0D131D")));
            zoneChipColor = Color.Parse(AnsiblexZones.ColorHex(ansiblexZone.Zone));
            zoneChipWidth = zoneChipText.Width + 8;
        }

        var firstLineWidth = name.Width
            + (sec is null ? 0 : gap + sec.Width)
            + (zoneChipText is null ? 0 : zoneChipGap + zoneChipWidth);
        var width = firstLineWidth;
        if (region is not null)
        {
            width = Math.Max(width, region.Width);
        }
        if (constellation is not null)
        {
            width = Math.Max(width, constellation.Width);
        }
        if (starClassText is not null)
        {
            width = Math.Max(width, starClassText.Width);
        }

        var height = name.Height;
        if (region is not null)
        {
            height += lineGap + region.Height;
        }
        if (constellation is not null)
        {
            height += lineGap + constellation.Height;
        }
        if (starClassText is not null)
        {
            height += lineGap + starClassText.Height;
        }

        var rect = new Rect(origin.X - 3, origin.Y - 2, width + 6, height + 4);
        context.FillRectangle(NodeLabelBackgroundBrush, rect, 3);

        DrawLabelWithHalo(context, name, nameHalo, origin);
        var firstLineCursor = origin.X + name.Width;
        if (sec is not null && secHalo is not null)
        {
            firstLineCursor += gap;
            var secOrigin = new Point(firstLineCursor, origin.Y);
            DrawLabelWithHalo(context, sec, secHalo, secOrigin);
            firstLineCursor += sec.Width;
        }

        if (zoneChipText is not null)
        {
            firstLineCursor += zoneChipGap;
            var chipRect = new Rect(firstLineCursor, origin.Y, zoneChipWidth, name.Height);
            context.FillRectangle(GetCachedBrush(zoneChipColor), chipRect, 3);
            context.DrawText(
                zoneChipText,
                new Point(chipRect.X + 4, chipRect.Y + ((name.Height - zoneChipText.Height) / 2)));
        }

        var y = origin.Y + name.Height + lineGap;
        if (region is not null && regionHalo is not null)
        {
            DrawLabelWithHalo(context, region, regionHalo, new Point(origin.X, y));
            y += region.Height + lineGap;
        }

        if (constellation is not null && constellationHalo is not null)
        {
            DrawLabelWithHalo(context, constellation, constellationHalo, new Point(origin.X, y));
            y += constellation.Height + lineGap;
        }

        if (starClassText is not null && starClassHalo is not null)
        {
            DrawLabelWithHalo(context, starClassText, starClassHalo, new Point(origin.X, y));
        }

        var indicatorIconCursorX = rect.X + IndicatorIconLeftPadding;
        if (ShowIndicatorA0StarIcon && IsA0BlueSmall(node))
        {
            DrawA0Icon(context, new Point(indicatorIconCursorX, rect.Bottom), IconSize);
            indicatorIconCursorX += IconSize + IndicatorIconSlotGap;
        }
        if (ShowIndicatorJoveObservatoryIcon && node.HasJoveObservatory)
        {
            DrawJoveObservatoryIcon(context, new Point(indicatorIconCursorX, rect.Bottom), IconSize);
            indicatorIconCursorX += IconSize + IndicatorIconSlotGap;
        }
        if (ShowIndicatorIceBeltsIcon && node.IceFieldCount > 0)
        {
            DrawIceFieldIcon(context, new Point(indicatorIconCursorX, rect.Bottom), IconSize);
            indicatorIconCursorX += IconSize + IndicatorIconSlotGap;
        }
        if (ShowIndicatorStormIcon && node.StormEffects.Count > 0)
        {
            DrawStormIcon(context, node, new Point(indicatorIconCursorX, rect.Bottom), IconSize);
            indicatorIconCursorX += IconSize + IndicatorIconSlotGap;
        }
        if (ShowIndicatorWormholeIcon && node.HubWormholeConnections.Count > 0)
        {
            DrawHubWormholeIcon(context, node, new Point(indicatorIconCursorX, rect.Bottom), IconSize);
            indicatorIconCursorX += IconSize + IndicatorIconSlotGap;
        }
        if (ShowIndicatorIncursionIcon && node.HasActiveIncursion)
        {
            DrawIncursionIcon(context, new Point(indicatorIconCursorX, rect.Bottom), IconSize);
            indicatorIconCursorX += IconSize + IndicatorIconSlotGap;
        }
        if (ShowIndicatorSystemJumps && node.SystemJumps > 0)
        {
            indicatorIconCursorX += DrawCountIconBadge(context, SystemJumpsIcon.Value, node.SystemJumps, new Point(indicatorIconCursorX, rect.Bottom), IconSize);
        }
        if (ShowIndicatorShipKills && node.ShipKills > 0)
        {
            indicatorIconCursorX += DrawCountIconBadge(context, ShipKillsIcon.Value, node.ShipKills, new Point(indicatorIconCursorX, rect.Bottom), IconSize);
        }
        if (ShowIndicatorPodKills && node.PodKills > 0)
        {
            indicatorIconCursorX += DrawCountIconBadge(context, PodKillsIcon.Value, node.PodKills, new Point(indicatorIconCursorX, rect.Bottom), IconSize);
        }
        if (ShowIndicatorNpcKills && node.NpcKills > 0)
        {
            indicatorIconCursorX += DrawCountIconBadge(context, NpcKillsIcon.Value, node.NpcKills, new Point(indicatorIconCursorX, rect.Bottom), IconSize);
        }
        FormattedText? jumpRangeIndicatorText = null;
        Bitmap? jumpRangeIndicatorIcon = null;
        if (ShowIndicatorJumpRangeLy &&
            JumpRangeDistancesByNodeId is not null &&
            JumpRangeDistancesByNodeId.TryGetValue(node.Id, out var jumpDistancesForNode) &&
            jumpDistancesForNode.Count > 0)
        {
            var best = jumpDistancesForNode
                .OrderByDescending(x => x.IsInRange)
                .ThenBy(x => x.DistanceLy)
                .First();
            var overlapSuffix = jumpDistancesForNode.Count > 1 ? $" (+{jumpDistancesForNode.Count - 1})" : string.Empty;
            jumpRangeIndicatorText = new FormattedText(
                $"{best.DistanceLy:0.0} LY{overlapSuffix}",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                10.5,
                Brushes.White);
            jumpRangeIndicatorIcon = best.IsInRange ? JumpRangeInRangeIcon.Value : JumpRangeOutRangeIcon.Value;
        }

        // Keep SOV icons on a dedicated second row, but collapse up if first row is empty.
        var primaryRowHasIcons = indicatorIconCursorX > (rect.X + IndicatorIconLeftPadding);
        var sovIndicatorRowY = primaryRowHasIcons
            ? rect.Bottom + SovIconSize + 2
            : rect.Bottom;
        var sovIconSlot = 0;
        if (ShowIndicatorSovUpgradeIcon && node.SovUpgrades.Count > 0)
        {
            foreach (var sov in GetVisibleSovUpgrades(node.SovUpgrades, IndicatorSovUpgradeFilterKeys))
            {
                var iconX = rect.X + IndicatorIconLeftPadding + (sovIconSlot * (IconSize + IndicatorIconSlotGap));
                var iconY = sovIndicatorRowY;
                DrawSovUpgradeIcon(context, sov, new Point(iconX, iconY), SovIconSize, sov.MiningSiteStatus == MiningSiteStatus.Available ? 1.0 : 0.32);
                if (sov.UpgradeName.EndsWith(" Prospecting Array", StringComparison.OrdinalIgnoreCase))
                {
                    _sovUpgradeIconHitTargets.Add((new Rect(iconX, iconY, SovIconSize, SovIconSize), new MapSovUpgradeHit
                    {
                        SolarSystemId = node.Id,
                        UpgradeName = sov.UpgradeName,
                        Tier = sov.Tier
                    }));
                }
                sovIconSlot++;
            }
        }
        if (ShowIndicatorSovUpgradeIcon &&
            _indicatorExplorationOverlapByNodeId.TryGetValue(node.Id, out var overlapCount) &&
            overlapCount > 0)
        {
            var iconX = rect.X + IndicatorIconLeftPadding + (sovIconSlot * (IconSize + IndicatorIconSlotGap));
            var iconY = sovIndicatorRowY;
            var sourceUpgrade = GetNodeExplorationDetector(node, IndicatorSovUpgradeFilterKeys)
                ?? new SovUpgradeEntry { UpgradeName = "Exploration Detector", Tier = 1 };
            var isDirectSource = _indicatorExplorationSourceNodeIds.Contains(node.Id);
            DrawSovUpgradeIcon(context, sourceUpgrade, new Point(iconX, iconY), SovIconSize, isDirectSource ? 1.0 : 0.5);

            var counterText = new FormattedText(
                $"x{overlapCount}",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                10.5,
                Brushes.White);
            context.DrawText(counterText, new Point(iconX + SovIconSize + 1, iconY + ((SovIconSize - counterText.Height) / 2)));
            sovIconSlot += 2;
        }

        if (jumpRangeIndicatorText is not null)
        {
            var indicatorY = sovIconSlot > 0
                ? sovIndicatorRowY + SovIconSize + 2
                : (primaryRowHasIcons ? rect.Bottom + IconSize + 2 : rect.Bottom + 2);
            var textX = rect.X + IndicatorIconLeftPadding;
            if (jumpRangeIndicatorIcon is not null)
            {
                var iconSize = 13.5;
                DrawBitmap(context, jumpRangeIndicatorIcon, new Point(textX, indicatorY - 1), iconSize);
                textX += iconSize + 3;
            }

            context.DrawText(jumpRangeIndicatorText, new Point(textX, indicatorY));
        }
    }

    private static void DrawCenteredText(DrawingContext context, string message, Rect bounds)
    {
        var text = new FormattedText(
            message,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            14,
            EmptyTextBrush);

        var origin = new Point((bounds.Width - text.Width) / 2, (bounds.Height - text.Height) / 2);
        context.DrawText(text, origin);
    }

    private static Rect GetUniverseRegionNodeRect(MapNode node, Point center, double scale = 1.0)
    {
        var text = new FormattedText(
            node.Name,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            RegionCardTypeface,
            12.5,
            Brushes.White);
        const double padX = 11.5;
        const double padY = 5.8;
        var width = (text.Width + (padX * 2)) * scale;
        var height = (text.Height + (padY * 2)) * scale;
        return new Rect(center.X - (width / 2.0), center.Y - (height / 2.0), width, height);
    }

    private static Color BlendColors(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte Mix(byte x, byte y) => (byte)Math.Clamp((int)Math.Round((x * (1 - t)) + (y * t)), 0, 255);
        return Color.FromArgb(Mix(a.A, b.A), Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }

    private static Color GetUniverseRegionOwnerColor(string? regionName)
    {
        if (string.IsNullOrWhiteSpace(regionName))
        {
            return Color.Parse("#7A5BAA");
        }

        var name = regionName.Trim();
        if (name.Equals("Pochven", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Yasna Zakh", StringComparison.OrdinalIgnoreCase))
        {
            return Color.Parse("#C44A5A");
        }

        if (IsRegionNameIn(name, "The Forge", "Lonetrek", "The Citadel", "Black Rise"))
        {
            return Color.Parse("#4B87D9"); // Caldari
        }

        if (IsRegionNameIn(name, "Heimatar", "Metropolis", "Molden Heath"))
        {
            return Color.Parse("#D9823B"); // Minmatar
        }

        if (IsRegionNameIn(name, "Domain", "Tash-Murkon", "Kador", "Kor-Azor", "Devoid", "Khanid", "The Bleak Lands", "Aridia"))
        {
            return Color.Parse("#D6B94A"); // Amarr
        }

        if (IsRegionNameIn(name, "Essence", "Sinq Laison", "Verge Vendor", "Placid", "Solitude", "Everyshore"))
        {
            return Color.Parse("#4FAE67"); // Gallente
        }

        if (name.Equals("Genesis", StringComparison.OrdinalIgnoreCase))
        {
            return Color.Parse("#D6B94A"); // Amarr override
        }

        if (name.Equals("Derelik", StringComparison.OrdinalIgnoreCase))
        {
            return Color.Parse("#D9823B"); // Minmatar override
        }

        if (name.Equals("Exordium", StringComparison.OrdinalIgnoreCase))
        {
            return Color.Parse("#DCE6F5"); // White override
        }

        return Color.Parse("#7A5BAA"); // Nullsec/default
    }

    private static bool IsRegionNameIn(string value, params string[] names)
    {
        foreach (var n in names)
        {
            if (value.Equals(n, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void DrawTooltip(DrawingContext context, Point anchor, string text)
    {
        var content = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            12,
            Brushes.White);

        var padX = 8.0;
        var padY = 6.0;
        var rect = new Rect(
            anchor.X + 12,
            anchor.Y + 12,
            content.Width + (padX * 2),
            content.Height + (padY * 2));

        context.FillRectangle(TooltipBackgroundBrush, rect, 4);
        context.DrawRectangle(TooltipBorderPen, rect, 4);
        context.DrawText(content, new Point(rect.X + padX, rect.Y + padY));
    }

    private static Point GetNodeLabelOrigin(Point nodePoint)
    {
        return new Point(nodePoint.X + NodeLabelOffset.X, nodePoint.Y + NodeLabelOffset.Y);
    }

    private void DrawHoverOverlay(DrawingContext context, Point anchor, MapNode node)
    {
        var header = node.Name;
        var detailLines = new List<string>();
        var regionText = (InfoBoxShowRegion && !string.IsNullOrWhiteSpace(node.RegionName))
            ? $"{node.RegionName}"
            : null;
        var constellationText = (InfoBoxShowConstellation && !string.IsNullOrWhiteSpace(node.ConstellationName))
            ? $"{node.ConstellationName}"
            : null;
        var regionConstellationLine = regionText is not null || constellationText is not null
            ? (regionText is not null && constellationText is not null
                ? $"{regionText} | {constellationText}"
                : regionText ?? constellationText!)
            : null;
        var starClass = GetStarClassDisplayValue(node);
        if (InfoBoxShowStarClass && !string.IsNullOrWhiteSpace(starClass))
        {
            detailLines.Add(starClass);
        }
        if (InfoBoxShowIncursionIcon && node.HasActiveIncursion)
        {
            detailLines.Add("Incursion: Active");
        }
        if (ShowUserSystemMarks &&
            UserSystemMarksByNodeId is not null &&
            UserSystemMarksByNodeId.TryGetValue(node.Id, out var userMark))
        {
            var markSummary = userMark.ShowLabel && !string.IsNullOrWhiteSpace(userMark.Label)
                ? userMark.Label
                : userMark.IconKind is { } iconKind
                    ? SystemMarkIcons.GetDisplayName(iconKind)
                    : "Marked";
            detailLines.Add($"Mark: {markSummary}");
        }
        if (InfoBoxShowSystemJumps && node.SystemJumps > 0)
        {
            detailLines.Add($"Jumps: {node.SystemJumps}");
        }
        if (InfoBoxShowShipKills && node.ShipKills > 0)
        {
            detailLines.Add($"Ship Kills: {node.ShipKills}");
        }
        if (InfoBoxShowPodKills && node.PodKills > 0)
        {
            detailLines.Add($"Pod Kills: {node.PodKills}");
        }
        if (InfoBoxShowNpcKills && node.NpcKills > 0)
        {
            detailLines.Add($"NPC Kills: {node.NpcKills}");
        }
        var ansiblexHoverLines = BuildAnsiblexHoverLines(node);
        if (node.StormEffects.Count > 0)
        {
            foreach (var storm in node.StormEffects.OrderByDescending(e => e.Strength).ThenBy(e => e.Type))
            {
                detailLines.Add($"Storm: {storm.Strength} {storm.Type}");
            }
        }
        var visibleOverlaySovUpgrades = InfoBoxShowSovUpgradeIcon
            ? GetVisibleSovUpgrades(node.SovUpgrades, OverlaySovUpgradeFilterKeys)
                .OrderBy(x => x.UpgradeName, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
        var wormholes = node.HubWormholeConnections
            .OrderBy(c => c.HubType)
            .ToList();
        var headerText = new FormattedText(
            header,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            12,
            Brushes.White);
        FormattedText? securityText = null;
        if (InfoBoxShowSecurityStatus && node.Security is not null)
        {
            var rounded = RoundSecurityForDisplay(node.Security.Value);
            securityText = new FormattedText(
                rounded.ToString("0.0", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter"),
                12,
                GetCachedBrush(GetSecurityColor(node)));
        }
        var detailsText = detailLines.Count == 0
            ? null
            : new FormattedText(
                string.Join('\n', detailLines),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter"),
                12,
                Brushes.White);
        var jumpRangeLineTexts = new List<(Bitmap? Icon, FormattedText Text, IBrush Brush)>();
        if (InfoBoxShowJumpRangeLy &&
            JumpRangeDistancesByNodeId is not null &&
            JumpRangeDistancesByNodeId.TryGetValue(node.Id, out var jumpDistances) &&
            jumpDistances.Count > 0)
        {
            foreach (var distance in jumpDistances.OrderBy(x => x.DistanceLy))
            {
                var isInRange = distance.IsInRange;
                var color = _jumpRangeOriginColorByNodeId.TryGetValue(distance.OriginNodeId, out var originColor)
                    ? GetCachedBrush(originColor)
                    : Brushes.White;
                var label = distance.DistanceLy <= 0
                    ? $"{distance.OriginSystemName}: 0.00 LY (origin)"
                    : $"{distance.OriginSystemName}: {distance.DistanceLy:0.00} LY / {distance.MaxLy:0.0} LY{(isInRange ? " (in)" : " (out)")}";
                jumpRangeLineTexts.Add((
                    isInRange ? JumpRangeInRangeIcon.Value : JumpRangeOutRangeIcon.Value,
                    new FormattedText(
                        label,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Inter"),
                        12,
                        color),
                    color));
            }
        }
        var sovLineTexts = visibleOverlaySovUpgrades
            .Select(sov => new
            {
                Upgrade = sov,
                Opacity = sov.MiningSiteStatus == MiningSiteStatus.Available ? 1.0 : 0.38,
                Text = new FormattedText(
                    IsSingleLevelSovUpgrade(sov.UpgradeName) ? sov.UpgradeName : $"{sov.UpgradeName} {sov.Tier}",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter"),
                    12,
                    Brushes.White)
            })
            .ToList();
        if (InfoBoxShowSovUpgradeIcon &&
            _overlayExplorationOverlapByNodeId.TryGetValue(node.Id, out var overlayExplorationCount) &&
            overlayExplorationCount > 0)
        {
            var sourceUpgrade = GetNodeExplorationDetector(node, OverlaySovUpgradeFilterKeys)
                ?? new SovUpgradeEntry { UpgradeName = "Exploration Detector", Tier = 1 };
            var isDirectSource = _overlayExplorationSourceNodeIds.Contains(node.Id);
            sovLineTexts.Add(new
            {
                Upgrade = sourceUpgrade,
                Opacity = isDirectSource ? 1.0 : 0.5,
                Text = new FormattedText(
                    $"Exploration overlap x{overlayExplorationCount}",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter"),
                    12,
                    Brushes.White)
            });
        }
        var regionConstellationText = regionConstellationLine is null
            ? null
            : new FormattedText(
                regionConstellationLine,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                NodeRegionConstellationFontSize,
                Brushes.White);

        var wormholeLineHeight = 0.0;
        var wormholeMaxWidth = 0.0;
        foreach (var wh in wormholes)
        {
            var hub = new FormattedText($"{wh.HubType}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, Brushes.White);
            var sep = new FormattedText(" | ", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#8A96A8")));
            var inSig = new FormattedText($"[{(string.IsNullOrWhiteSpace(wh.InSignature) ? "-" : wh.InSignature!)}]", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#5EDC8A")));
            var outSig = new FormattedText($"[{(string.IsNullOrWhiteSpace(wh.OutSignature) ? "-" : wh.OutSignature!)}]", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#F07171")));
            var mass = new FormattedText($"[{GetWormholeMassShort(wh.MaxShipSize)}]", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#F5C06A")));
            var width = hub.Width + sep.Width + inSig.Width + sep.Width + outSig.Width + sep.Width + mass.Width;
            wormholeMaxWidth = Math.Max(wormholeMaxWidth, width);
            wormholeLineHeight = Math.Max(wormholeLineHeight, Math.Max(hub.Height, Math.Max(inSig.Height, outSig.Height)));
        }
        var sovLineHeight = 0.0;
        var sovMaxWidth = 0.0;
        foreach (var sovLine in sovLineTexts)
        {
            sovLineHeight = Math.Max(sovLineHeight, Math.Max(SovIconSize, sovLine.Text.Height));
            sovMaxWidth = Math.Max(sovMaxWidth, SovIconSize + 4 + sovLine.Text.Width);
        }
        var jumpLineHeight = 0.0;
        var jumpMaxWidth = 0.0;
        foreach (var jumpLine in jumpRangeLineTexts)
        {
            jumpLineHeight = Math.Max(jumpLineHeight, Math.Max(14, jumpLine.Text.Height));
            jumpMaxWidth = Math.Max(jumpMaxWidth, 14 + 4 + jumpLine.Text.Width);
        }
        var ansiblexHoverWidth = 0.0;
        const double ansiblexHoverRowHeight = 18.0;
        foreach (var line in ansiblexHoverLines)
        {
            ansiblexHoverWidth = Math.Max(ansiblexHoverWidth, MeasureAnsiblexHoverLine(line));
        }
        const double intelIdentityIconSize = 22.0;
        const double intelIdentityGap = 3.0;
        var intelRows = new List<(
            IntelMapHoverReport Report,
            FormattedText Age,
            FormattedText Hostiles,
            FormattedText Message,
            IReadOnlyList<(IntelMapHoverShip Ship, FormattedText Text)> Ships,
            IReadOnlyList<(IntelMapHoverHostile Hostile, FormattedText Name, FormattedText Membership)> Identities,
            FormattedText? Overflow,
            double TopWidth,
            double Width,
            double Height)>();
        var zkillRows = new List<(
            IntelMapHoverKillmail Report,
            FormattedText Age,
            FormattedText Isk,
            FormattedText Victim,
            FormattedText VictimMembership,
            IReadOnlyList<(IntelMapHoverHostile Hostile, FormattedText Name, FormattedText Membership)> Attackers,
            FormattedText? Overflow,
            FormattedText Message,
            double Width,
            double Height)>();
        if (IntelRecentReportsByNodeId is not null &&
            IntelRecentReportsByNodeId.TryGetValue(node.Id, out var reportsForNode) &&
            reportsForNode.Count > 0)
        {
            foreach (var report in reportsForNode.Take(1))
            {
                var age = new FormattedText(
                    FormatRelativeAge(report.TimestampUtc),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
                    10,
                    new ImmutableSolidColorBrush(Color.Parse("#BFD8FF")));
                var hostiles = new FormattedText(
                    $"{report.HostileCount} hostile{(report.HostileCount == 1 ? string.Empty : "s")}",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
                    10,
                    Brushes.White);
                var message = new FormattedText(
                    TrimIntelReportText(report.MessageText, 58),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter"),
                    10,
                    new ImmutableSolidColorBrush(Color.Parse("#D7E1EF")));
                var ships = report.Ships
                    .Where(s => !string.IsNullOrWhiteSpace(s.ShipDisplayName) &&
                                !string.Equals(s.ShipDisplayName, "Unknown", StringComparison.OrdinalIgnoreCase))
                    .Select(s => (s, new FormattedText(
                        $"{s.Count}x {s.ShipDisplayName}",
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
                        10,
                        new ImmutableSolidColorBrush(Color.Parse("#EAF2FF")))))
                    .ToList();
                var identities = report.Hostiles
                    .Take(4)
                    .Select(hostile =>
                    {
                        var name = new FormattedText(
                            hostile.Name,
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
                            10,
                            Brushes.White);
                        var membership = new FormattedText(
                            BuildIntelMembershipTickerSummary(hostile),
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Inter"),
                            9,
                            new ImmutableSolidColorBrush(Color.Parse("#9DB8D8")));
                        return (hostile, name, membership);
                    })
                    .ToList();
                var topWidth = hostiles.Width + age.Width + 22;
                var identitiesWidth = identities.Count == 0
                    ? 0
                    : identities.Max(x =>
                        intelIdentityIconSize
                        + (x.hostile.CorporationId is null ? 0 : intelIdentityIconSize + intelIdentityGap)
                        + (x.hostile.AllianceId is null ? 0 : intelIdentityIconSize + intelIdentityGap)
                        + 6
                        + Math.Max(x.name.Width, x.membership.Width));
                var shipsWidth = ships.Count == 0 ? 0 : ships.Max(x => intelIdentityIconSize + 4 + x.Item2.Width);
                var shipsHeight = ships.Sum(x => Math.Max(intelIdentityIconSize, x.Item2.Height) + 2);
                var identitiesHeight = identities.Count * (intelIdentityIconSize + intelIdentityGap);
                var overflowParts = new List<string>();
                if (report.HiddenShipCount > 0)
                {
                    overflowParts.Add($"+{report.HiddenShipCount} ships");
                }
                if (report.HiddenHostileCount > 0)
                {
                    overflowParts.Add($"+{report.HiddenHostileCount} hostiles");
                }

                var overflow = overflowParts.Count > 0
                    ? new FormattedText(
                        string.Join("  ", overflowParts),
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
                        10,
                        new ImmutableSolidColorBrush(Color.Parse("#BFD8FF")))
                    : null;
                intelRows.Add((
                    report,
                    age,
                    hostiles,
                    message,
                    ships,
                    identities,
                    overflow,
                    topWidth,
                    Math.Max(Math.Max(Math.Max(topWidth, shipsWidth), identitiesWidth), Math.Max(message.Width, overflow?.Width ?? 0)),
                    Math.Max(age.Height + 4, hostiles.Height + 4) + shipsHeight + identitiesHeight + (overflow is null ? 0 : overflow.Height + 5) + message.Height + 7));
            }
        }
        if (ZkillRecentReportsByNodeId is not null &&
            ZkillRecentReportsByNodeId.TryGetValue(node.Id, out var zkillForNode) &&
            zkillForNode.Count > 0)
        {
            foreach (var report in zkillForNode.Take(1))
            {
                var age = new FormattedText(
                    FormatRelativeAge(report.TimestampUtc),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
                    10,
                    new ImmutableSolidColorBrush(Color.Parse("#BFD8FF")));
                var isk = new FormattedText(
                    report.IskLostLabel,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
                    10,
                    Brushes.White);
                var victim = new FormattedText(
                    report.VictimName,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
                    10,
                    Brushes.White);
                var victimMembership = new FormattedText(
                    report.VictimMembership,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter"),
                    9,
                    new ImmutableSolidColorBrush(Color.Parse("#9DB8D8")));
                var attackers = report.Attackers
                    .Take(3)
                    .Select(hostile =>
                    {
                        var name = new FormattedText(
                            hostile.Name,
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
                            10,
                            Brushes.White);
                        var membership = new FormattedText(
                            BuildIntelMembershipTickerSummary(hostile),
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Inter"),
                            9,
                            new ImmutableSolidColorBrush(Color.Parse("#9DB8D8")));
                        return (Hostile: hostile, Name: name, Membership: membership);
                    })
                    .ToList<(IntelMapHoverHostile Hostile, FormattedText Name, FormattedText Membership)>();
                var overflow = report.HiddenAttackerCount > 0
                    ? new FormattedText(
                        $"+{report.HiddenAttackerCount}",
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
                        10,
                        new ImmutableSolidColorBrush(Color.Parse("#BFD8FF")))
                    : null;
                var message = new FormattedText(
                    TrimIntelReportText(report.MessageText, 58),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter"),
                    10,
                    new ImmutableSolidColorBrush(Color.Parse("#D7E1EF")));
                var attackersWidth = attackers.Count == 0
                    ? 0
                    : attackers.Max(x =>
                        intelIdentityIconSize
                        + intelIdentityGap
                        + intelIdentityIconSize
                        + (x.Hostile.CorporationId is null ? 0 : intelIdentityIconSize + intelIdentityGap)
                        + (x.Hostile.AllianceId is null ? 0 : intelIdentityIconSize + intelIdentityGap)
                        + 6
                        + Math.Max(x.Name.Width, x.Membership.Width));
                var width = Math.Max(
                    Math.Max(isk.Width + age.Width + 24, message.Width),
                    Math.Max(victim.Width + victimMembership.Width + 18 + (intelIdentityIconSize * 3), attackersWidth));
                var height = Math.Max(age.Height + 4, isk.Height + 4)
                    + intelIdentityIconSize
                    + (string.IsNullOrWhiteSpace(report.VictimMembership) ? 0 : victimMembership.Height)
                    + (attackers.Count * (intelIdentityIconSize + intelIdentityGap))
                    + (overflow is null ? 0 : overflow.Height + 6)
                    + message.Height
                    + 12;
                zkillRows.Add((report, age, isk, victim, victimMembership, attackers, overflow, message, width, height));
            }
        }
        var intelMaxWidth = intelRows.Count == 0 ? 0.0 : intelRows.Max(x => x.Width) + 8;
        var intelHeight = intelRows.Count == 0 ? 0.0 : intelRows.Sum(x => x.Height + 5);
        var zkillMaxWidth = zkillRows.Count == 0 ? 0.0 : zkillRows.Max(x => x.Width) + 8;
        var zkillHeight = zkillRows.Count == 0 ? 0.0 : zkillRows.Sum(x => x.Height + 5);
        var intelSectionTitle = new FormattedText("Intel Reports", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter", FontStyle.Normal, FontWeight.Bold), 10, new ImmutableSolidColorBrush(Color.Parse("#9DB8D8")));
        var zkillSectionTitle = new FormattedText("zKillmails", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter", FontStyle.Normal, FontWeight.Bold), 10, new ImmutableSolidColorBrush(Color.Parse("#9DB8D8")));
        var hasOverlaySections = intelRows.Count > 0 || zkillRows.Count > 0;
        var overlaySectionsHeight = 0.0;
        if (hasOverlaySections)
        {
            // Extra spacer before the section splitter and first section.
            overlaySectionsHeight += 2;
            if (intelRows.Count > 0)
            {
                overlaySectionsHeight += intelSectionTitle.Height + 2;
            }

            if (zkillRows.Count > 0)
            {
                overlaySectionsHeight += zkillSectionTitle.Height + 2;
            }
        }
        IReadOnlyList<int>? presentCharacterIds = null;
        IReadOnlyList<string>? presentCharacterNames = null;
        var characterPortraitSize = 28.0;
        var characterPortraitGap = 4.0;
        var characterRowHeight = 0.0;
        var characterRowWidth = 0.0;
        FormattedText? characterOverflowText = null;
        if (ShowInfoBoxCharacterPresence &&
            CharacterPresenceCharacterIdsByNodeId is not null &&
            CharacterPresenceCharacterIdsByNodeId.TryGetValue(node.Id, out var idsForNode) &&
            idsForNode.Count > 0)
        {
            presentCharacterIds = idsForNode;
            if (CharacterPresenceNamesByNodeId is not null &&
                CharacterPresenceNamesByNodeId.TryGetValue(node.Id, out var namesForNode))
            {
                presentCharacterNames = namesForNode;
            }

            var maxNames = Math.Clamp(CharacterPresenceHoverMaxNames, 1, 12);
            var visibleCount = Math.Min(maxNames, idsForNode.Count);
            characterRowWidth = visibleCount * characterPortraitSize +
                                Math.Max(0, visibleCount - 1) * characterPortraitGap;
            var overflow = idsForNode.Count - visibleCount;
            if (overflow > 0)
            {
                characterOverflowText = new FormattedText(
                    $"+{overflow}",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter"),
                    12,
                    Brushes.White);
                characterRowWidth += 6 + characterOverflowText.Width;
            }
            characterRowHeight = Math.Max(characterPortraitSize, characterOverflowText?.Height ?? 0);
        }

        var start = GetNodeLabelOrigin(anchor);
        var padX = 8.0;
        var padY = 6.0;
        var headerWidth = headerText.Width + (securityText is null ? 0 : (8 + securityText.Width));
        var bodyWidth = Math.Max(Math.Max(Math.Max(Math.Max(Math.Max(Math.Max(Math.Max(Math.Max(regionConstellationText?.Width ?? 0, detailsText?.Width ?? 0), wormholeMaxWidth), sovMaxWidth), jumpMaxWidth), ansiblexHoverWidth), characterRowWidth), intelMaxWidth), zkillMaxWidth);
        var contentWidth = Math.Max(headerWidth, bodyWidth);
        var contentHeight = headerText.Height
            + (regionConstellationText is null ? 0 : regionConstellationText.Height + 2)
            + (detailsText is null ? 0 : detailsText.Height + 2)
            + (ansiblexHoverLines.Count == 0 ? 0 : (ansiblexHoverLines.Count * (ansiblexHoverRowHeight + 2)) + 2)
            + intelHeight
            + zkillHeight
            + overlaySectionsHeight
            + (jumpRangeLineTexts.Count == 0 ? 0 : (jumpRangeLineTexts.Count * (jumpLineHeight + 1)))
            + (sovLineTexts.Count == 0 ? 0 : (sovLineTexts.Count * (sovLineHeight + 1)))
            + (wormholes.Count == 0 ? 0 : (wormholes.Count * (wormholeLineHeight + 1)))
            + (presentCharacterIds is null ? 0 : characterRowHeight + 2);
        var rect = new Rect(
            start.X - 2,
            start.Y - 2,
            contentWidth + (padX * 2),
            contentHeight + (padY * 2));

        context.FillRectangle(HoverOverlayBackgroundBrush, rect, 4);
        context.DrawRectangle(HoverOverlayBorderPen, rect, 4);
        var headerOrigin = new Point(rect.X + padX, rect.Y + padY);
        context.DrawText(headerText, headerOrigin);
        if (securityText is not null)
        {
            var secOrigin = new Point(headerOrigin.X + headerText.Width + 8, headerOrigin.Y);
            context.DrawText(securityText, secOrigin);
        }
        var detailsStartY = headerOrigin.Y + headerText.Height + 2;
        if (regionConstellationText is not null)
        {
            context.DrawText(regionConstellationText, new Point(headerOrigin.X, detailsStartY));
            detailsStartY += regionConstellationText.Height + 2;
        }
        if (detailsText is not null)
        {
            context.DrawText(detailsText, new Point(headerOrigin.X, detailsStartY));
            detailsStartY += detailsText.Height;
        }
        if (ansiblexHoverLines.Count > 0)
        {
            detailsStartY += 2;
            foreach (var line in ansiblexHoverLines)
            {
                DrawAnsiblexHoverLine(context, new Point(headerOrigin.X, detailsStartY), line, ansiblexHoverRowHeight);
                detailsStartY += ansiblexHoverRowHeight + 2;
            }
        }
        foreach (var jumpLine in jumpRangeLineTexts)
        {
            if (jumpLine.Icon is not null)
            {
                DrawBitmap(context, jumpLine.Icon, new Point(headerOrigin.X, detailsStartY + ((jumpLineHeight - 14) / 2)), 14);
            }

            context.DrawText(jumpLine.Text, new Point(headerOrigin.X + 18, detailsStartY + ((jumpLineHeight - jumpLine.Text.Height) / 2)));
            detailsStartY += jumpLineHeight + 1;
        }

        foreach (var sovLine in sovLineTexts)
        {
            DrawSovUpgradeIcon(context, sovLine.Upgrade, new Point(headerOrigin.X, detailsStartY), SovIconSize, sovLine.Opacity);
            context.DrawText(sovLine.Text, new Point(headerOrigin.X + SovIconSize + 4, detailsStartY + ((sovLineHeight - sovLine.Text.Height) / 2)));
            detailsStartY += sovLineHeight + 1;
        }

        var wormholeStartY = detailsStartY;
        foreach (var wh in wormholes)
        {
            var hub = new FormattedText($"{wh.HubType}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, Brushes.White);
            var sep = new FormattedText(" | ", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#8A96A8")));
            var inSig = new FormattedText($"[{(string.IsNullOrWhiteSpace(wh.InSignature) ? "-" : wh.InSignature!)}]", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#5EDC8A")));
            var outSig = new FormattedText($"[{(string.IsNullOrWhiteSpace(wh.OutSignature) ? "-" : wh.OutSignature!)}]", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#F07171")));
            var mass = new FormattedText($"[{GetWormholeMassShort(wh.MaxShipSize)}]", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#F5C06A")));

            var lineX = headerOrigin.X;
            context.DrawText(hub, new Point(lineX, wormholeStartY));
            lineX += hub.Width;
            context.DrawText(sep, new Point(lineX, wormholeStartY));
            lineX += sep.Width;
            context.DrawText(inSig, new Point(lineX, wormholeStartY));
            lineX += inSig.Width;
            context.DrawText(sep, new Point(lineX, wormholeStartY));
            lineX += sep.Width;
            context.DrawText(outSig, new Point(lineX, wormholeStartY));
            lineX += outSig.Width;
            context.DrawText(sep, new Point(lineX, wormholeStartY));
            lineX += sep.Width;
            context.DrawText(mass, new Point(lineX, wormholeStartY));
            wormholeStartY += wormholeLineHeight + 1;
        }
        if (presentCharacterIds is { Count: > 0 })
        {
            var maxNames = Math.Clamp(CharacterPresenceHoverMaxNames, 1, 12);
            var visibleCount = Math.Min(maxNames, presentCharacterIds.Count);
            var charStartY = wormholeStartY + 2;
            var charX = headerOrigin.X;
            string? hoveredCharacterName = null;
            for (var i = 0; i < visibleCount; i++)
            {
                var characterId = presentCharacterIds[i];
                var currentName = presentCharacterNames is not null && i < presentCharacterNames.Count ? presentCharacterNames[i] : null;
                var portraitRect = new Rect(charX, charStartY, characterPortraitSize, characterPortraitSize);
                if (portraitRect.Contains(_lastPointerPosition))
                {
                    hoveredCharacterName = currentName;
                }

                DrawCharacterPortraitChip(
                    context,
                    charX,
                    charStartY,
                    characterPortraitSize,
                    characterId,
                    currentName);
                charX += characterPortraitSize + characterPortraitGap;
            }

            if (characterOverflowText is not null)
            {
                context.DrawText(characterOverflowText, new Point(charX + 6, charStartY + ((characterPortraitSize - characterOverflowText.Height) / 2)));
            }

            if (!string.IsNullOrWhiteSpace(hoveredCharacterName))
            {
                DrawCompactTooltip(context, _lastPointerPosition, hoveredCharacterName!);
            }
        }

        if (intelRows.Count > 0 || zkillRows.Count > 0)
        {
            var intelStartY = wormholeStartY + (presentCharacterIds is { Count: > 0 } ? characterRowHeight + 4 : 2);
            var splitterY = intelStartY - 3;
            var splitterPen = new Pen(new ImmutableSolidColorBrush(Color.Parse("#5A6B82")), 1);
            context.DrawLine(splitterPen, new Point(headerOrigin.X, splitterY), new Point(rect.Right - padX, splitterY));
            string? hoveredIntelHostileName = null;
            var drawIntelFirst = intelRows.Count > 0 && (zkillRows.Count == 0 || intelRows[0].Report.TimestampUtc >= zkillRows[0].Report.TimestampUtc);

            void DrawIntelSection()
            {
                var sectionTitle = new FormattedText("Intel Reports", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter", FontStyle.Normal, FontWeight.Bold), 10, new ImmutableSolidColorBrush(Color.Parse("#9DB8D8")));
                context.DrawText(sectionTitle, new Point(headerOrigin.X + 2, intelStartY));
                intelStartY += sectionTitle.Height + 2;

                foreach (var intelRow in intelRows)
                {
                    var rowRect = new Rect(headerOrigin.X, intelStartY, intelMaxWidth, intelRow.Height);
                context.FillRectangle(new ImmutableSolidColorBrush(Color.Parse("#8A172234")), rowRect, 3);
                context.DrawRectangle(new Pen(new ImmutableSolidColorBrush(Color.Parse("#4A2A3C58")), 1), rowRect, 3);

                var chipY = intelStartY + 2;
                var ageRect = new Rect(rowRect.Right - intelRow.Age.Width - 10, chipY, intelRow.Age.Width + 6, intelRow.Age.Height + 4);
                context.FillRectangle(new ImmutableSolidColorBrush(Color.Parse("#3A241C35")), ageRect, 3);
                context.DrawRectangle(new Pen(new ImmutableSolidColorBrush(Color.Parse("#5D83B5")), 1), ageRect, 3);
                context.DrawText(intelRow.Age, new Point(ageRect.X + 3, ageRect.Y + 2));

                var hostilesRect = new Rect(headerOrigin.X + 4, chipY, intelRow.Hostiles.Width + 6, intelRow.Hostiles.Height + 4);
                var hostileColor = GetIntelHostileColor(intelRow.Report.HostileCount);
                context.FillRectangle(new ImmutableSolidColorBrush(Color.FromArgb(130, hostileColor.R, hostileColor.G, hostileColor.B)), hostilesRect, 3);
                context.DrawRectangle(new Pen(new ImmutableSolidColorBrush(hostileColor), 1), hostilesRect, 3);
                context.DrawText(intelRow.Hostiles, new Point(hostilesRect.X + 3, hostilesRect.Y + 2));

                var identityY = chipY + Math.Max(ageRect.Height, hostilesRect.Height) + 3;
                foreach (var ship in intelRow.Ships)
                {
                    DrawIntelShipIcon(context, ship.Ship, new Point(headerOrigin.X + 4, identityY), intelIdentityIconSize);
                    context.DrawText(ship.Text, new Point(headerOrigin.X + 4 + intelIdentityIconSize + 4, identityY + ((intelIdentityIconSize - ship.Text.Height) / 2)));
                    identityY += Math.Max(intelIdentityIconSize, ship.Text.Height) + 2;
                }

                foreach (var identity in intelRow.Identities)
                {
                    var intelX = headerOrigin.X + 4;
                    var portraitRect = new Rect(intelX, identityY, intelIdentityIconSize, intelIdentityIconSize);
                    if (identity.Hostile.CharacterId is { } characterId)
                    {
                        _intelOverlayLinks.Add((portraitRect, $"https://zkillboard.com/character/{characterId}/"));
                    }
                    if (portraitRect.Contains(_lastPointerPosition))
                    {
                        hoveredIntelHostileName = identity.Hostile.Name;
                    }

                    DrawCharacterPortraitChip(
                        context,
                        intelX,
                        identityY,
                        intelIdentityIconSize,
                        identity.Hostile.CharacterId ?? 0,
                        identity.Hostile.Name);
                    intelX += intelIdentityIconSize + intelIdentityGap;
                    if (identity.Hostile.CorporationId is { } corporationId)
                    {
                        _intelOverlayLinks.Add((new Rect(intelX, identityY, intelIdentityIconSize, intelIdentityIconSize), $"https://zkillboard.com/corporation/{corporationId}/"));
                        DrawOrganizationLogoChip(context, intelX, identityY, intelIdentityIconSize, "corporations", corporationId);
                        intelX += intelIdentityIconSize + intelIdentityGap;
                    }
                    if (identity.Hostile.AllianceId is { } allianceId)
                    {
                        _intelOverlayLinks.Add((new Rect(intelX, identityY, intelIdentityIconSize, intelIdentityIconSize), $"https://zkillboard.com/alliance/{allianceId}/"));
                        DrawOrganizationLogoChip(context, intelX, identityY, intelIdentityIconSize, "alliances", allianceId);
                        intelX += intelIdentityIconSize + intelIdentityGap;
                    }

                    context.DrawText(identity.Name, new Point(intelX + 3, identityY));
                    context.DrawText(identity.Membership, new Point(intelX + 3, identityY + identity.Name.Height));
                    identityY += intelIdentityIconSize + intelIdentityGap;
                }

                if (intelRow.Overflow is not null)
                {
                    var overflowRect = new Rect(headerOrigin.X + 4, identityY, intelRow.Overflow.Width + 10, intelRow.Overflow.Height + 4);
                    context.FillRectangle(new ImmutableSolidColorBrush(Color.Parse("#3A241C35")), overflowRect, 3);
                    context.DrawRectangle(new Pen(new ImmutableSolidColorBrush(Color.Parse("#5D83B5")), 1), overflowRect, 3);
                    context.DrawText(intelRow.Overflow, new Point(overflowRect.X + 5, overflowRect.Y + 2));
                    identityY += overflowRect.Height + 1;
                }

                var messageY = identityY + 1;
                context.DrawText(intelRow.Message, new Point(headerOrigin.X + 4, messageY));
                intelStartY += intelRow.Height + 5;
            }
            }

            void DrawZkillSection()
            {
                var sectionTitle = new FormattedText("zKillmails", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter", FontStyle.Normal, FontWeight.Bold), 10, new ImmutableSolidColorBrush(Color.Parse("#9DB8D8")));
                context.DrawText(sectionTitle, new Point(headerOrigin.X + 2, intelStartY));
                intelStartY += sectionTitle.Height + 2;

                foreach (var zkillRow in zkillRows)
                {
                    var rowRect = new Rect(headerOrigin.X, intelStartY, zkillMaxWidth, zkillRow.Height);
                    context.FillRectangle(new ImmutableSolidColorBrush(Color.Parse("#8A172234")), rowRect, 3);
                    context.DrawRectangle(new Pen(new ImmutableSolidColorBrush(Color.Parse("#4A2A3C58")), 1), rowRect, 3);

                    var chipY = intelStartY + 2;
                    var ageRect = new Rect(rowRect.Right - zkillRow.Age.Width - 10, chipY, zkillRow.Age.Width + 6, zkillRow.Age.Height + 4);
                    context.FillRectangle(new ImmutableSolidColorBrush(Color.Parse("#3A241C35")), ageRect, 3);
                    context.DrawRectangle(new Pen(new ImmutableSolidColorBrush(Color.Parse("#5D83B5")), 1), ageRect, 3);
                    context.DrawText(zkillRow.Age, new Point(ageRect.X + 3, ageRect.Y + 2));

                    var iskRect = new Rect(headerOrigin.X + 4, chipY, zkillRow.Isk.Width + 6, zkillRow.Isk.Height + 4);
                    context.FillRectangle(new ImmutableSolidColorBrush(Color.Parse("#3A2A2A")), iskRect, 3);
                    context.DrawRectangle(new Pen(new ImmutableSolidColorBrush(Color.Parse("#8A4C4C")), 1), iskRect, 3);
                    context.DrawText(zkillRow.Isk, new Point(iskRect.X + 3, iskRect.Y + 2));

                    var lineY = chipY + Math.Max(ageRect.Height, iskRect.Height) + 3;
                    var iconX = headerOrigin.X + 4;
                    var iconRect = new Rect(iconX, lineY, intelIdentityIconSize, intelIdentityIconSize);
                    if (!string.IsNullOrWhiteSpace(zkillRow.Report.KillmailUrl))
                    {
                        _intelOverlayLinks.Add((iconRect, zkillRow.Report.KillmailUrl));
                    }
                    var killmailIcon = KillmailIcon.Value;
                    if (killmailIcon is not null)
                    {
                        DrawBitmap(context, killmailIcon, new Point(iconRect.X, iconRect.Y), intelIdentityIconSize);
                    }

                    iconX += intelIdentityIconSize + intelIdentityGap;
                    DrawIntelShipIcon(
                        context,
                        new IntelMapHoverShip
                        {
                            ShipDisplayName = zkillRow.Report.VictimShipDisplayName,
                            ShipIconKey = "unknown",
                            ShipTypeId = zkillRow.Report.VictimShipTypeId
                        },
                        new Point(iconX, lineY),
                        intelIdentityIconSize);
                    iconX += intelIdentityIconSize + intelIdentityGap;
                    DrawCharacterPortraitChip(context, iconX, lineY, intelIdentityIconSize, zkillRow.Report.VictimCharacterId ?? 0, zkillRow.Report.VictimName);
                    if (zkillRow.Report.VictimCharacterId is { } victimCharacterId)
                    {
                        _intelOverlayLinks.Add((new Rect(iconX, lineY, intelIdentityIconSize, intelIdentityIconSize), $"https://zkillboard.com/character/{victimCharacterId}/"));
                    }

                    iconX += intelIdentityIconSize + intelIdentityGap;
                    if (zkillRow.Report.VictimCorporationId is { } victimCorporationId)
                    {
                        DrawOrganizationLogoChip(context, iconX, lineY, intelIdentityIconSize, "corporations", victimCorporationId);
                        _intelOverlayLinks.Add((new Rect(iconX, lineY, intelIdentityIconSize, intelIdentityIconSize), $"https://zkillboard.com/corporation/{victimCorporationId}/"));
                        iconX += intelIdentityIconSize + intelIdentityGap;
                    }
                    if (zkillRow.Report.VictimAllianceId is { } victimAllianceId)
                    {
                        DrawOrganizationLogoChip(context, iconX, lineY, intelIdentityIconSize, "alliances", victimAllianceId);
                        _intelOverlayLinks.Add((new Rect(iconX, lineY, intelIdentityIconSize, intelIdentityIconSize), $"https://zkillboard.com/alliance/{victimAllianceId}/"));
                        iconX += intelIdentityIconSize + intelIdentityGap;
                    }

                    context.DrawText(zkillRow.Victim, new Point(iconX + 3, lineY));
                    if (!string.IsNullOrWhiteSpace(zkillRow.Report.VictimMembership))
                    {
                        context.DrawText(zkillRow.VictimMembership, new Point(iconX + 3, lineY + zkillRow.Victim.Height));
                    }

                    lineY += intelIdentityIconSize + 2;
                    foreach (var attacker in zkillRow.Attackers)
                    {
                        var attackerX = headerOrigin.X + 4 + intelIdentityIconSize + intelIdentityGap;
                        DrawIntelShipIcon(
                            context,
                            new IntelMapHoverShip
                            {
                                ShipDisplayName = "Unknown",
                                ShipIconKey = "unknown",
                                ShipTypeId = attacker.Hostile.ShipTypeId
                            },
                            new Point(attackerX, lineY),
                            intelIdentityIconSize);
                        attackerX += intelIdentityIconSize + intelIdentityGap;
                        DrawCharacterPortraitChip(context, attackerX, lineY, intelIdentityIconSize, attacker.Hostile.CharacterId ?? 0, attacker.Hostile.Name);
                        if (attacker.Hostile.CharacterId is { } attackerCharacterId)
                        {
                            _intelOverlayLinks.Add((new Rect(attackerX, lineY, intelIdentityIconSize, intelIdentityIconSize), $"https://zkillboard.com/character/{attackerCharacterId}/"));
                        }
                        attackerX += intelIdentityIconSize + intelIdentityGap;

                        if (attacker.Hostile.CorporationId is { } attackerCorporationId)
                        {
                            DrawOrganizationLogoChip(context, attackerX, lineY, intelIdentityIconSize, "corporations", attackerCorporationId);
                            _intelOverlayLinks.Add((new Rect(attackerX, lineY, intelIdentityIconSize, intelIdentityIconSize), $"https://zkillboard.com/corporation/{attackerCorporationId}/"));
                            attackerX += intelIdentityIconSize + intelIdentityGap;
                        }
                        if (attacker.Hostile.AllianceId is { } attackerAllianceId)
                        {
                            DrawOrganizationLogoChip(context, attackerX, lineY, intelIdentityIconSize, "alliances", attackerAllianceId);
                            _intelOverlayLinks.Add((new Rect(attackerX, lineY, intelIdentityIconSize, intelIdentityIconSize), $"https://zkillboard.com/alliance/{attackerAllianceId}/"));
                            attackerX += intelIdentityIconSize + intelIdentityGap;
                        }

                        context.DrawText(attacker.Name, new Point(attackerX + 3, lineY));
                        context.DrawText(attacker.Membership, new Point(attackerX + 3, lineY + attacker.Name.Height));
                        lineY += intelIdentityIconSize + intelIdentityGap;
                    }

                    if (zkillRow.Overflow is not null)
                    {
                        var overflowRect = new Rect(headerOrigin.X + 4, lineY, zkillRow.Overflow.Width + 10, zkillRow.Overflow.Height + 4);
                        context.FillRectangle(new ImmutableSolidColorBrush(Color.Parse("#3A241C35")), overflowRect, 3);
                        context.DrawRectangle(new Pen(new ImmutableSolidColorBrush(Color.Parse("#5D83B5")), 1), overflowRect, 3);
                        context.DrawText(zkillRow.Overflow, new Point(overflowRect.X + 5, overflowRect.Y + 2));
                        lineY += overflowRect.Height + 1;
                    }

                    context.DrawText(zkillRow.Message, new Point(headerOrigin.X + 4, lineY + 1));
                    intelStartY += zkillRow.Height + 5;
                }
            }

            if (drawIntelFirst)
            {
                if (intelRows.Count > 0) DrawIntelSection();
                if (zkillRows.Count > 0) DrawZkillSection();
            }
            else
            {
                if (zkillRows.Count > 0) DrawZkillSection();
                if (intelRows.Count > 0) DrawIntelSection();
            }

            if (!string.IsNullOrWhiteSpace(hoveredIntelHostileName))
            {
                DrawCompactTooltip(context, _lastPointerPosition, hoveredIntelHostileName);
            }
        }

        var overlayIconCursorX = rect.X + IndicatorIconLeftPadding;
        if (InfoBoxShowA0StarIcon && IsA0BlueSmall(node))
        {
            DrawA0Icon(context, new Point(overlayIconCursorX, rect.Bottom + 3), IconSize);
            overlayIconCursorX += IconSize + IndicatorIconSlotGap;
        }
        if (InfoBoxShowJoveObservatoryIcon && node.HasJoveObservatory)
        {
            DrawJoveObservatoryIcon(context, new Point(overlayIconCursorX, rect.Bottom + 3), IconSize);
            overlayIconCursorX += IconSize + IndicatorIconSlotGap;
        }
        if (InfoBoxShowIceBeltsIcon && node.IceFieldCount > 0)
        {
            DrawIceFieldIcon(context, new Point(overlayIconCursorX, rect.Bottom + 3), IconSize);
            overlayIconCursorX += IconSize + IndicatorIconSlotGap;
        }
        if (InfoBoxShowStormIcon && node.StormEffects.Count > 0)
        {
            DrawStormIcon(context, node, new Point(overlayIconCursorX, rect.Bottom + 3), IconSize);
            overlayIconCursorX += IconSize + IndicatorIconSlotGap;
        }
        if (InfoBoxShowWormholeIcon && node.HubWormholeConnections.Count > 0)
        {
            DrawHubWormholeIcon(context, node, new Point(overlayIconCursorX, rect.Bottom + 3), IconSize);
            overlayIconCursorX += IconSize + IndicatorIconSlotGap;
        }
        if (InfoBoxShowIncursionIcon && node.HasActiveIncursion)
        {
            DrawIncursionIcon(context, new Point(overlayIconCursorX, rect.Bottom + 3), IconSize);
            overlayIconCursorX += IconSize + IndicatorIconSlotGap;
        }
        if (InfoBoxShowSystemJumps && node.SystemJumps > 0)
        {
            overlayIconCursorX += DrawCountIconBadge(context, SystemJumpsIcon.Value, node.SystemJumps, new Point(overlayIconCursorX, rect.Bottom + 3), IconSize);
        }
        if (InfoBoxShowShipKills && node.ShipKills > 0)
        {
            overlayIconCursorX += DrawCountIconBadge(context, ShipKillsIcon.Value, node.ShipKills, new Point(overlayIconCursorX, rect.Bottom + 3), IconSize);
        }
        if (InfoBoxShowPodKills && node.PodKills > 0)
        {
            overlayIconCursorX += DrawCountIconBadge(context, PodKillsIcon.Value, node.PodKills, new Point(overlayIconCursorX, rect.Bottom + 3), IconSize);
        }
        if (InfoBoxShowNpcKills && node.NpcKills > 0)
        {
            overlayIconCursorX += DrawCountIconBadge(context, NpcKillsIcon.Value, node.NpcKills, new Point(overlayIconCursorX, rect.Bottom + 3), IconSize);
        }
        // SOV upgrades are rendered inline in the overlay body with icon + label rows.
    }

    private static string GetWormholeMassShort(string? maxShipSize)
    {
        return maxShipSize?.Trim().ToLowerInvariant() switch
        {
            "xlarge" => "XL",
            "large" => "L",
            "medium" => "M",
            "small" => "S",
            _ => "?"
        };
    }

    private static Bitmap? LoadA0StarIcon()
    {
        return LoadIcon("a0-star.png");
    }

    private static Bitmap? LoadJoveObservatoryIcon()
    {
        return LoadIcon("jove_observatory.png");
    }

    private static Bitmap? LoadIceFieldIcon()
    {
        return LoadIcon("iceField.png");
    }

    private static Bitmap? LoadIcon(string fileName)
    {
        try
        {
            var uri = new Uri($"avares://HISA/Assets/Icons/{fileName}");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static void DrawSovUpgradeIcon(DrawingContext context, SovUpgradeEntry upgrade, Point topLeft, double size, double opacity = 1.0)
    {
        var icon = GetSovUpgradeIcon(upgrade);
        if (icon is null)
        {
            return;
        }

        var src = new Rect(0, 0, icon.Size.Width, icon.Size.Height);
        var dst = new Rect(topLeft.X, topLeft.Y, size, size);
        if (opacity >= 0.999)
        {
            context.DrawImage(icon, src, dst);
            return;
        }

        using (context.PushOpacity(Math.Clamp(opacity, 0.0, 1.0)))
        {
            context.DrawImage(icon, src, dst);
        }
    }

    private static Bitmap? GetSovUpgradeIcon(SovUpgradeEntry upgrade)
    {
        var fileName = BuildSovIconFileName(upgrade);
        if (!SovUpgradeIcons.TryGetValue(fileName, out var lazy))
        {
            lazy = new Lazy<Bitmap?>(() => LoadSovUpgradeIcon(fileName));
            SovUpgradeIcons[fileName] = lazy;
        }

        return lazy.Value;
    }

    private static IEnumerable<SovUpgradeEntry> GetVisibleSovUpgrades(
        IReadOnlyList<SovUpgradeEntry> upgrades,
        IEnumerable<string>? selectedKeys)
    {
        return SovUpgradeFilters.GetVisible(upgrades, selectedKeys);
    }

    private static SovUpgradeEntry? GetNodeExplorationDetector(MapNode node, IEnumerable<string>? selectedKeys)
    {
        return GetVisibleSovUpgrades(node.SovUpgrades, selectedKeys)
            .Where(u => u.UpgradeName.Equals("Exploration Detector", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(u => u.Tier)
            .FirstOrDefault();
    }

    private static string BuildSovFilterKey(SovUpgradeEntry upgrade) => SovUpgradeFilters.BuildFilterKey(upgrade);

    private static string BuildSovIconFileName(SovUpgradeEntry upgrade)
    {
        return SovUpgradeFilters.IsSingleLevel(upgrade.UpgradeName)
            ? $"{upgrade.UpgradeName}.png"
            : $"{upgrade.UpgradeName} {Math.Clamp(upgrade.Tier, 1, 3)}.png";
    }

    private static bool IsSingleLevelSovUpgrade(string upgradeName) => SovUpgradeFilters.IsSingleLevel(upgradeName);

    private static Bitmap? LoadSovUpgradeIcon(string fileName)
    {
        try
        {
            var uri = new Uri($"avares://HISA/Assets/Icons/SOV Upgrades/{fileName}");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static void DrawStormIcon(DrawingContext context, MapNode node, Point topLeft, double size)
    {
        var effect = GetPrimaryStormEffect(node);
        var icon = effect switch
        {
            null => StormUnknownIcon.Value,
            { Type: StormType.Electrical, Strength: StormStrength.Center } => StormElectricCenterIcon.Value,
            { Type: StormType.Electrical, Strength: StormStrength.Strong } => StormElectricStrongIcon.Value,
            { Type: StormType.Electrical, Strength: StormStrength.Weak } => StormElectricWeakIcon.Value,
            { Type: StormType.Gamma, Strength: StormStrength.Center } => StormGammaCenterIcon.Value,
            { Type: StormType.Gamma, Strength: StormStrength.Strong } => StormGammaStrongIcon.Value,
            { Type: StormType.Gamma, Strength: StormStrength.Weak } => StormGammaWeakIcon.Value,
            { Type: StormType.Exotic, Strength: StormStrength.Center } => StormExoticCenterIcon.Value,
            { Type: StormType.Exotic, Strength: StormStrength.Strong } => StormExoticStrongIcon.Value,
            { Type: StormType.Exotic, Strength: StormStrength.Weak } => StormExoticWeakIcon.Value,
            { Type: StormType.Plasma, Strength: StormStrength.Center } => StormPlasmaCenterIcon.Value,
            { Type: StormType.Plasma, Strength: StormStrength.Strong } => StormPlasmaStrongIcon.Value,
            { Type: StormType.Plasma, Strength: StormStrength.Weak } => StormPlasmaWeakIcon.Value,
            _ => StormUnknownIcon.Value
        };
        if (icon is null)
        {
            return;
        }

        var src = new Rect(0, 0, icon.Size.Width, icon.Size.Height);
        var dst = new Rect(topLeft.X, topLeft.Y, size, size);
        context.DrawImage(icon, src, dst);
    }

    private static void DrawHubWormholeIcon(DrawingContext context, MapNode node, Point topLeft, double size)
    {
        var icon = WormholeIcon.Value;
        if (icon is null)
        {
            return;
        }

        var src = new Rect(0, 0, icon.Size.Width, icon.Size.Height);
        var dst = new Rect(topLeft.X, topLeft.Y, size, size);
        context.DrawImage(icon, src, dst);
    }

    private static Dictionary<long, Color> BuildJumpRangeOriginColorMap(IReadOnlyList<JumpRangeOriginDisplay>? origins)
    {
        var result = new Dictionary<long, Color>();
        if (origins is null)
        {
            return result;
        }

        foreach (var origin in origins)
        {
            var a = (byte)((origin.ColorArgb >> 24) & 0xFF);
            var r = (byte)((origin.ColorArgb >> 16) & 0xFF);
            var g = (byte)((origin.ColorArgb >> 8) & 0xFF);
            var b = (byte)(origin.ColorArgb & 0xFF);
            result[origin.NodeId] = Color.FromArgb(a, r, g, b);
        }

        return result;
    }

    private void DrawJumpRangeSegments(DrawingContext context, Point center, double radius, IReadOnlyList<long> originIds)
    {
        var segments = originIds
            .Select(id => _jumpRangeOriginColorByNodeId.TryGetValue(id, out var c) ? c : Color.Parse("#6FD7F7"))
            .ToList();
        if (segments.Count == 0)
        {
            return;
        }

        if (segments.Count == 1)
        {
            context.DrawEllipse(null, _penCache.Get(segments[0], 3.2), center, radius, radius);
            return;
        }

        var gapDegrees = 3.5;
        var sweep = (360.0 / segments.Count) - gapDegrees;
        for (var i = 0; i < segments.Count; i++)
        {
            var start = (360.0 / segments.Count) * i + (gapDegrees * 0.5);
            DrawArcSegment(context, center, radius, start, sweep, _penCache.Get(segments[i], 3.2));
        }
    }

    private static void DrawArcSegment(DrawingContext context, Point center, double radius, double startDegrees, double sweepDegrees, Pen pen)
    {
        if (sweepDegrees <= 0)
        {
            return;
        }

        var startRad = startDegrees * (Math.PI / 180.0);
        var endRad = (startDegrees + sweepDegrees) * (Math.PI / 180.0);
        var start = new Point(center.X + (radius * Math.Cos(startRad)), center.Y + (radius * Math.Sin(startRad)));
        var end = new Point(center.X + (radius * Math.Cos(endRad)), center.Y + (radius * Math.Sin(endRad)));

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(start, false);
            g.ArcTo(
                end,
                new Size(radius, radius),
                0,
                sweepDegrees > 180,
                SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static Color GetJumpRouteStepColor(int stepIndex, int stepCount)
    {
        if (stepCount <= 1)
        {
            return Color.Parse("#63D3FF");
        }

        var t = Math.Clamp(stepIndex / (double)Math.Max(1, stepCount - 1), 0.0, 1.0);
        var hue = 195.0 - (t * 95.0); // cyan -> green/yellow for visual progression
        return ColorFromHsv(hue, 0.72, 0.98);
    }

    private static Color ColorFromHsv(double hue, double saturation, double value)
    {
        var h = ((hue % 360.0) + 360.0) % 360.0;
        var c = value * saturation;
        var x = c * (1.0 - Math.Abs(((h / 60.0) % 2.0) - 1.0));
        var m = value - c;

        double rp, gp, bp;
        if (h < 60.0) { rp = c; gp = x; bp = 0.0; }
        else if (h < 120.0) { rp = x; gp = c; bp = 0.0; }
        else if (h < 180.0) { rp = 0.0; gp = c; bp = x; }
        else if (h < 240.0) { rp = 0.0; gp = x; bp = c; }
        else if (h < 300.0) { rp = x; gp = 0.0; bp = c; }
        else { rp = c; gp = 0.0; bp = x; }

        byte r = (byte)Math.Clamp((rp + m) * 255.0, 0.0, 255.0);
        byte g = (byte)Math.Clamp((gp + m) * 255.0, 0.0, 255.0);
        byte b = (byte)Math.Clamp((bp + m) * 255.0, 0.0, 255.0);
        return Color.FromRgb(r, g, b);
    }

    private static void DrawIncursionIcon(DrawingContext context, Point topLeft, double size)
    {
        var icon = IncursionIcon.Value;
        if (icon is null)
        {
            return;
        }

        var src = new Rect(0, 0, icon.Size.Width, icon.Size.Height);
        var dst = new Rect(topLeft.X, topLeft.Y, size, size);
        context.DrawImage(icon, src, dst);
    }

    private Color GetIntelHostileColorForNode(long nodeId)
    {
        if (IntelHostileScoresByNodeId is null || !IntelHostileScoresByNodeId.TryGetValue(nodeId, out var score))
        {
            return Color.Parse("#98A6B8");
        }

        return GetIntelHostileColor(score);
    }

    private static readonly ConcurrentDictionary<string, Bitmap?> ShipClassIconCache = new(StringComparer.OrdinalIgnoreCase);

    private void DrawIntelRingWithIcons(
        DrawingContext context,
        Point center,
        double nodeRadius,
        IReadOnlyList<string> iconKeys,
        int hostileScore,
        bool showOrbitIcons)
    {
        var ringColor = GetIntelHostileColor(hostileScore);
        var scaledRingRadius = nodeRadius * 2.2;
        var corePen = _penCache.Get(Color.FromArgb(220, ringColor.R, ringColor.G, ringColor.B), Math.Max(1.8, nodeRadius * 0.67));
        context.DrawEllipse(null, corePen, center, scaledRingRadius, scaledRingRadius);

        if (!showOrbitIcons)
        {
            return;
        }

        var fadePen1 = _penCache.Get(Color.FromArgb(130, ringColor.R, ringColor.G, ringColor.B), Math.Max(1.2, nodeRadius * 0.47));
        var fadePen2 = _penCache.Get(Color.FromArgb(70, ringColor.R, ringColor.G, ringColor.B), Math.Max(0.9, nodeRadius * 0.31));
        var fadeOffset1 = Math.Max(0.5, nodeRadius * 0.13);
        var fadeOffset2 = Math.Max(0.9, nodeRadius * 0.25);
        context.DrawEllipse(null, fadePen1, center, scaledRingRadius + fadeOffset1, scaledRingRadius + fadeOffset1);
        context.DrawEllipse(null, fadePen2, center, scaledRingRadius + fadeOffset2, scaledRingRadius + fadeOffset2);
        DrawIntelOrbitIcons(context, center, nodeRadius, iconKeys);
    }

    private void DrawIntelOrbitIcons(
        DrawingContext context,
        Point center,
        double nodeRadius,
        IReadOnlyList<string> iconKeys)
    {
        var scaledRingRadius = nodeRadius * 2.2;
        var icons = iconKeys.Count <= 10 ? iconKeys : iconKeys.Take(10).ToArray();
        if (icons.Count == 0)
        {
            return;
        }

        var orbitRadius = scaledRingRadius;
        var iconScale = icons.Count switch
        {
            <= 6 => 3.1,
            <= 8 => 2.65,
            _ => 2.25
        };
        var iconSize = Math.Clamp(nodeRadius * iconScale, 11.0, 33.0);
        var rotationDegrees = EnableIntelRingAnimations ? (_linkAnimationPhase * 126.0) : 0.0;
        for (var i = 0; i < icons.Count; i++)
        {
            var angle = (-90.0 + rotationDegrees + ((360.0 / icons.Count) * i)) * (Math.PI / 180.0);
            var iconX = center.X + (orbitRadius * Math.Cos(angle)) - (iconSize * 0.5);
            var iconY = center.Y + (orbitRadius * Math.Sin(angle)) - (iconSize * 0.5);
            DrawIntelIcon(context, icons[i], new Point(iconX, iconY), iconSize);
        }
    }

    private Color GetIntelHostileColor(int hostileScore)
    {
        var settings = HostileColorSettings;
        var lowMax = Math.Max(1, settings.LowMaxHostiles);
        var mediumMax = Math.Max(lowMax + 1, settings.MediumMaxHostiles);
        var highMax = Math.Max(mediumMax + 1, settings.HighMaxHostiles);
        var low = ParseHostileColor(settings.LowColorHex, "#E6D86C");
        var medium = ParseHostileColor(settings.MediumColorHex, "#EE8639");
        var high = ParseHostileColor(settings.HighColorHex, "#D90F13");
        var aboveHigh = ParseHostileColor(settings.AboveHighColorHex, "#DD008C");

        if (hostileScore <= lowMax)
        {
            return low;
        }

        if (hostileScore <= mediumMax)
        {
            return BlendColors(low, medium, (hostileScore - lowMax) / (double)(mediumMax - lowMax));
        }

        if (hostileScore <= highMax)
        {
            return BlendColors(medium, high, (hostileScore - mediumMax) / (double)(highMax - mediumMax));
        }

        // Above High has no maximum. Use the preceding band width as a smooth
        // transition span, approaching the configured Above High color thereafter.
        var highBandWidth = Math.Max(1, highMax - mediumMax);
        var aboveHighProgress = 1.0 - Math.Exp(-(hostileScore - highMax) / (double)highBandWidth);
        return BlendColors(high, aboveHigh, aboveHighProgress);
    }

    private static Color ParseHostileColor(string? value, string fallback)
    {
        try
        {
            return Color.Parse(value ?? fallback);
        }
        catch (Exception)
        {
            return Color.Parse(fallback);
        }
    }

    private static void DrawIntelIcon(DrawingContext context, string iconKey, Point topLeft, double size)
    {
        Bitmap? icon;
        if (string.Equals(iconKey, "question-mark", StringComparison.OrdinalIgnoreCase))
        {
            icon = LoadIcon("crosshair.png");
        }
        else
        {
            icon = ShipClassIconCache.GetOrAdd(iconKey, static key => LoadIcon($"Ships/{key}.png"));
        }

        if (icon is null)
        {
            icon = LoadIcon("crosshair.png") ?? QuestionMarkIcon.Value;
        }

        if (icon is null)
        {
            return;
        }

        var src = new Rect(0, 0, icon.Size.Width, icon.Size.Height);
        var dst = new Rect(topLeft.X, topLeft.Y, size, size);
        context.DrawImage(icon, src, dst);
    }

    private void DrawIntelShipIcon(DrawingContext context, IntelMapHoverShip ship, Point topLeft, double size)
    {
        Bitmap? icon = null;
        if (ship.ShipTypeId is int typeId && typeId > 0)
        {
            icon = GetShipTypeIcon(typeId);
        }

        if (icon is null)
        {
            DrawIntelIcon(context, ship.ShipIconKey, topLeft, size);
            return;
        }

        var src = new Rect(0, 0, icon.Size.Width, icon.Size.Height);
        var dst = new Rect(topLeft.X, topLeft.Y, size, size);
        context.DrawImage(icon, src, dst);
    }

    private static double DrawCountIconBadge(DrawingContext context, Bitmap? icon, int value, Point topLeft, double size)
    {
        if (icon is null)
        {
            return size + IndicatorIconSlotGap;
        }

        DrawBitmap(context, icon, topLeft, size);
        var label = value.ToString(CultureInfo.InvariantCulture);
        var text = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            NodeLabelTypeface,
            9.5,
            Brushes.White);
        context.DrawText(text, new Point(topLeft.X + size + 1.5, topLeft.Y + ((size - text.Height) / 2) - 0.5));
        return size + 1.5 + text.Width + IndicatorIconSlotGap;
    }

    private const double SystemMarkIconSize = 14.0;
    private const double NodeBadgeGap = 3.0;

    private Point GetSystemMarkAnchor(MapNode node, Point nodePoint)
    {
        var isSelected = SelectedNodeId == node.Id;
        var isHovered = _hoveredNodeId == node.Id;
        var radius = (isSelected ? 9.3 : isHovered ? 8.4 : 6.75) * GetUniverseNodeZoomScale();
        var x = nodePoint.X + radius + NodeBadgeGap;
        var y = nodePoint.Y - radius - 9.0;
        if (TryGetCharacterPresenceCount(node.Id, out var characterCount))
        {
            x = GetCharacterPresenceRect(nodePoint, radius, characterCount).Right + NodeBadgeGap;
        }

        return new Point(x, y);
    }

    private bool TryGetCharacterPresenceCount(long nodeId, out int count)
    {
        count = 0;
        return ShowIndicatorCharacterPresence &&
               CharacterPresenceCountsByNodeId is not null &&
               CharacterPresenceCountsByNodeId.TryGetValue(nodeId, out count) &&
               count > 0;
    }

    private void DrawUserSystemMarks(DrawingContext context, Rect bounds)
    {
        _systemMarkClusterHits.Clear();
        if (!ShowUserSystemMarks ||
            UserSystemMarksByNodeId is null ||
            UserSystemMarksByNodeId.Count == 0 ||
            ViewMode == MapViewMode.UniverseRegions)
        {
            if (_hoveredSystemMarkClusterIndex >= 0)
            {
                _hoveredSystemMarkClusterIndex = -1;
            }

            return;
        }

        var visible = new List<(UserSystemMarkDisplay Mark, Point Anchor)>();
        foreach (var kvp in UserSystemMarksByNodeId)
        {
            if (!_nodeIndexById.TryGetValue(kvp.Key, out var index) ||
                index < 0 ||
                index >= _screenPositions.Length)
            {
                continue;
            }

            var nodePoint = _screenPositions[index];
            if (!IsPointVisible(nodePoint, bounds, 48) ||
                !_nodeById.TryGetValue(kvp.Key, out var markedNode))
            {
                continue;
            }

            visible.Add((kvp.Value, GetSystemMarkAnchor(markedNode, nodePoint)));
        }

        if (visible.Count == 0)
        {
            return;
        }

        var parent = new int[visible.Count];
        for (var i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        const double clusterDistanceSquared = 18.0 * 18.0;
        for (var i = 0; i < visible.Count; i++)
        {
            for (var j = i + 1; j < visible.Count; j++)
            {
                var dx = visible[i].Anchor.X - visible[j].Anchor.X;
                var dy = visible[i].Anchor.Y - visible[j].Anchor.Y;
                if ((dx * dx) + (dy * dy) > clusterDistanceSquared)
                {
                    continue;
                }

                var a = Find(i);
                var b = Find(j);
                if (a != b)
                {
                    parent[b] = a;
                }
            }
        }

        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < visible.Count; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var members))
            {
                members = [];
                groups[root] = members;
            }

            members.Add(i);
        }

        foreach (var members in groups.Values)
        {
            if (members.Count == 1)
            {
                var item = visible[members[0]];
                var hit = DrawUserSystemMark(context, item.Anchor, item.Mark);
                _systemMarkClusterHits.Add((hit, [item.Mark], item.Anchor));
                continue;
            }

            var marks = members.Select(index => visible[index].Mark).ToList();
            var anchor = new Point(
                members.Average(index => visible[index].Anchor.X),
                members.Average(index => visible[index].Anchor.Y));
            var clusterHit = DrawUserSystemMarkCluster(context, anchor, marks);
            _systemMarkClusterHits.Add((clusterHit, marks, anchor));
        }

        if (_hoveredSystemMarkClusterIndex >= _systemMarkClusterHits.Count)
        {
            _hoveredSystemMarkClusterIndex = -1;
        }
    }

    private Rect DrawUserSystemMark(DrawingContext context, Point anchor, UserSystemMarkDisplay mark)
    {
        var color = Color.FromUInt32(mark.ColorArgb);
        const double iconSize = SystemMarkIconSize;
        var cursorX = anchor.X;
        var top = anchor.Y;
        var bottom = anchor.Y;
        var hasIcon = mark.ShowIcon && mark.IconKind is not null;
        if (hasIcon && mark.IconKind is { } iconKind)
        {
            var iconRect = new Rect(cursorX, anchor.Y, iconSize, iconSize);
            context.FillRectangle(new ImmutableSolidColorBrush(Color.FromArgb(230, 12, 18, 28)), iconRect, 3);
            context.DrawRectangle(new Pen(GetCachedBrush(color), 1.2), iconRect, 3);
            DrawSystemMarkIcon(context, iconKind, new Point(iconRect.X + 2.0, iconRect.Y + 2.0), iconSize - 4.0, color);
            cursorX = iconRect.Right + NodeBadgeGap;
            bottom = iconRect.Bottom;
        }

        if (mark.ShowLabel && !string.IsNullOrWhiteSpace(mark.Label))
        {
            var text = new FormattedText(
                mark.Label.Trim(),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
                10,
                Brushes.White);
            var chipWidth = Math.Max(18.0, text.Width + 8.0);
            var chipHeight = Math.Max(iconSize, text.Height + 4.0);
            var chip = new Rect(cursorX, anchor.Y + ((iconSize - chipHeight) / 2.0), chipWidth, chipHeight);
            context.FillRectangle(GetCachedBrush(color, 0.9), chip, 3);
            context.DrawRectangle(new Pen(new ImmutableSolidColorBrush(Color.FromArgb(220, 8, 12, 18)), 1), chip, 3);
            context.DrawText(text, new Point(
                chip.X + ((chip.Width - text.Width) / 2.0),
                chip.Y + ((chip.Height - text.Height) / 2.0) - 0.5));
            cursorX = chip.Right;
            top = Math.Min(top, chip.Y);
            bottom = Math.Max(bottom, chip.Bottom);
        }

        return new Rect(anchor.X, top, Math.Max(iconSize, cursorX - anchor.X), Math.Max(iconSize, bottom - top));
    }

    private Rect DrawUserSystemMarkCluster(
        DrawingContext context,
        Point anchor,
        IReadOnlyList<UserSystemMarkDisplay> marks)
    {
        var first = marks[0];
        var color = Color.FromUInt32(first.ColorArgb);
        const double iconSize = SystemMarkIconSize;
        var countLabel = marks.Count.ToString(CultureInfo.InvariantCulture);
        var text = new FormattedText(
            countLabel,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
            10,
            Brushes.White);
        var width = iconSize + 4.0 + text.Width + 8.0;
        var rect = new Rect(anchor.X, anchor.Y, width, iconSize);
        context.FillRectangle(new ImmutableSolidColorBrush(Color.FromArgb(220, 12, 18, 28)), rect, 4);
        context.DrawRectangle(new Pen(GetCachedBrush(color), 1.2), rect, 4);
        if (first.ShowIcon && first.IconKind is { } iconKind)
        {
            DrawSystemMarkIcon(context, iconKind, new Point(rect.X + 2.0, rect.Y + 2.0), iconSize - 4.0, color);
        }
        else
        {
            context.FillRectangle(GetCachedBrush(color), new Rect(rect.X + 3, rect.Y + 3, 8, 8), 2);
        }

        context.DrawText(text, new Point(
            rect.X + iconSize + 2.0,
            rect.Y + ((rect.Height - text.Height) / 2.0) - 0.5));
        return rect;
    }

    private void DrawHoveredSystemMarkClusterOverlay(DrawingContext context, Rect bounds)
    {
        if (_hoveredSystemMarkClusterIndex < 0 ||
            _hoveredSystemMarkClusterIndex >= _systemMarkClusterHits.Count)
        {
            return;
        }

        var hit = _systemMarkClusterHits[_hoveredSystemMarkClusterIndex];
        if (hit.Marks.Count < 2)
        {
            return;
        }

        var lines = hit.Marks
            .Take(8)
            .Select(mark =>
            {
                var label = mark.ShowLabel && !string.IsNullOrWhiteSpace(mark.Label)
                    ? mark.Label
                    : mark.IconKind is { } iconKind
                        ? SystemMarkIcons.GetDisplayName(iconKind)
                        : "Mark";
                return string.IsNullOrWhiteSpace(mark.RegionName)
                    ? $"{mark.SystemName}  {label}"
                    : $"{mark.SystemName}  {label}  ({mark.RegionName})";
            })
            .ToList();
        if (hit.Marks.Count > 8)
        {
            lines.Add($"+{hit.Marks.Count - 8} more");
        }

        var body = new FormattedText(
            string.Join('\n', lines),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            11,
            Brushes.White);
        var header = new FormattedText(
            $"{hit.Marks.Count} marked systems",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
            12,
            Brushes.White);
        var width = Math.Max(header.Width, body.Width) + 16;
        var height = header.Height + body.Height + 16;
        var x = hit.Anchor.X + 18;
        var y = hit.Anchor.Y - height - 6;
        if (x + width > bounds.Width - 8)
        {
            x = hit.Anchor.X - width - 6;
        }

        if (y < 8)
        {
            y = hit.Anchor.Y + 18;
        }

        var rect = new Rect(x, y, width, height);
        context.FillRectangle(new ImmutableSolidColorBrush(Color.FromArgb(230, 16, 24, 36)), rect, 6);
        context.DrawRectangle(new Pen(new ImmutableSolidColorBrush(Color.FromArgb(180, 90, 130, 180)), 1), rect, 6);
        context.DrawText(header, new Point(rect.X + 8, rect.Y + 6));
        context.DrawText(body, new Point(rect.X + 8, rect.Y + 8 + header.Height));
    }

    private void DrawSystemMarkIcon(
        DrawingContext context,
        SystemMarkIconKind kind,
        Point topLeft,
        double size,
        Color color)
    {
        var geometry = SystemMarkIconGeometry.Get(kind);
        var scale = size / 16.0;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(topLeft.X, topLeft.Y)))
        {
            context.DrawGeometry(GetCachedBrush(color), null, geometry);
        }
    }

    private static void DrawIncursionBeacon(DrawingContext context, Point topLeft)
    {
        var icon = IncursionIcon.Value;
        if (icon is null)
        {
            return;
        }

        var size = 12.0;
        var rect = new Rect(topLeft.X, topLeft.Y, size, size);
        var bg = new ImmutableSolidColorBrush(Color.FromArgb(178, 74, 46, 120));
        var border = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(255, 58, 110, 168)), 1);
        context.FillRectangle(bg, rect, 3);
        context.DrawRectangle(border, rect, 3);
        DrawBitmap(context, icon, new Point(rect.X + 1, rect.Y + 1), size - 2);
    }

    private static Rect GetCharacterPresenceRect(Point nodePoint, double nodeRadius, int count)
    {
        var text = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
        var textLayout = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            10,
            Brushes.White);
        var width = Math.Max(14.0, textLayout.Width + 8.0);
        var height = Math.Max(14.0, textLayout.Height + 4.0);
        return new Rect(
            nodePoint.X + nodeRadius + NodeBadgeGap,
            nodePoint.Y - nodeRadius - 9.0,
            width,
            height);
    }

    private static void DrawCharacterPresenceBadge(DrawingContext context, Point nodePoint, double nodeRadius, int count)
    {
        var text = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
        var textLayout = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            10,
            Brushes.White);
        var rect = GetCharacterPresenceRect(nodePoint, nodeRadius, count);

        var bg = new ImmutableSolidColorBrush(GetCharacterPresenceBadgeColor(count));
        var border = new Pen(new ImmutableSolidColorBrush(Color.Parse("#0B2A1A")), 1.0);
        context.FillRectangle(bg, rect, 4);
        context.DrawRectangle(border, rect, 4);
        context.DrawText(textLayout, new Point(
            rect.X + ((rect.Width - textLayout.Width) / 2),
            rect.Y + ((rect.Height - textLayout.Height) / 2) - 0.5));
    }

    private void DrawCharacterPortraitChip(
        DrawingContext context,
        double x,
        double y,
        double size,
        int characterId,
        string? characterName)
    {
        var rect = new Rect(x, y, size, size);
        context.FillRectangle(new ImmutableSolidColorBrush(Color.Parse("#233248")), rect, 3);

        var portrait = GetCharacterPortrait(characterId);
        if (portrait is not null)
        {
            var src = new Rect(0, 0, portrait.Size.Width, portrait.Size.Height);
            context.DrawImage(portrait, src, rect);
        }
        else
        {
            var fallbackGlyph = string.IsNullOrWhiteSpace(characterName)
                ? "?"
                : characterName.Trim().Substring(0, 1).ToUpperInvariant();
            var fallbackText = new FormattedText(
                fallbackGlyph,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
                10,
                Brushes.White);
            context.DrawText(
                fallbackText,
                new Point(
                    rect.X + ((rect.Width - fallbackText.Width) / 2),
                    rect.Y + ((rect.Height - fallbackText.Height) / 2) - 0.5));
        }
    }

    private static string BuildIntelMembershipTickerSummary(IntelMapHoverHostile hostile)
    {
        var corporation = string.IsNullOrWhiteSpace(hostile.CorporationTicker) ? null : $"[{hostile.CorporationTicker}]";
        var alliance = string.IsNullOrWhiteSpace(hostile.AllianceTicker) ? null : $"[{hostile.AllianceTicker}]";
        return string.Join("  ", new[] { corporation, alliance }.Where(x => x is not null));
    }

    private bool TryOpenIntelOverlayLink(Point point)
    {
        var link = _intelOverlayLinks.LastOrDefault(x => x.Bounds.Contains(point));
        if (string.IsNullOrWhiteSpace(link.Url))
        {
            return false;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = link.Url,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void DrawOrganizationLogoChip(
        DrawingContext context,
        double x,
        double y,
        double size,
        string category,
        int organizationId)
    {
        var rect = new Rect(x, y, size, size);
        context.FillRectangle(new ImmutableSolidColorBrush(Color.Parse("#233248")), rect, 3);
        var logo = GetOrganizationLogo(category, organizationId);
        if (logo is null)
        {
            var fallbackGlyph = string.Equals(category, "alliances", StringComparison.OrdinalIgnoreCase) ? "A" : "C";
            var fallbackText = new FormattedText(
                fallbackGlyph,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
                10,
                Brushes.White);
            context.DrawText(
                fallbackText,
                new Point(
                    rect.X + ((rect.Width - fallbackText.Width) / 2),
                    rect.Y + ((rect.Height - fallbackText.Height) / 2) - 0.5));
            return;
        }

        var src = new Rect(0, 0, logo.Size.Width, logo.Size.Height);
        context.DrawImage(logo, src, rect);
    }

    private Bitmap? GetOrganizationLogo(string category, int organizationId)
    {
        if (organizationId <= 0)
        {
            return null;
        }

        var cacheKey = $"{category}:{organizationId}";
        if (OrganizationLogoCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }
        if (OrganizationLogoRetryAfterUtc.TryGetValue(cacheKey, out var retryAfterUtc) &&
            DateTime.UtcNow < retryAfterUtc)
        {
            return null;
        }

        if (!OrganizationLogoLoading.TryAdd(cacheKey, 0))
        {
            return null;
        }

        _ = Task.Run(async () =>
        {
            Bitmap? logo = null;
            try
            {
                var url = $"https://images.evetech.net/{category}/{organizationId}/logo?tenant=tranquility&size=64";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("HISA/1.0");
                using var response = await CharacterPortraitHttpClient.SendAsync(request).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    logo = new Bitmap(stream);
                }
            }
            catch
            {
                logo = null;
            }
            finally
            {
                if (logo is not null)
                {
                    OrganizationLogoCache[cacheKey] = logo;
                    OrganizationLogoRetryAfterUtc.TryRemove(cacheKey, out _);
                }
                else
                {
                    OrganizationLogoRetryAfterUtc[cacheKey] = DateTime.UtcNow + CharacterPortraitRetryDelay;
                }
                OrganizationLogoLoading.TryRemove(cacheKey, out _);
                Dispatcher.UIThread.Post(InvalidateMap);
            }
        });

        return null;
    }

    private Bitmap? GetShipTypeIcon(int typeId)
    {
        if (typeId <= 0)
        {
            return null;
        }

        if (ShipTypeIconCache.TryGetValue(typeId, out var cached))
        {
            return cached;
        }

        if (ShipTypeIconRetryAfterUtc.TryGetValue(typeId, out var retryAfterUtc) &&
            DateTime.UtcNow < retryAfterUtc)
        {
            return null;
        }

        if (!ShipTypeIconLoading.TryAdd(typeId, 0))
        {
            return null;
        }

        _ = Task.Run(async () =>
        {
            Bitmap? icon = null;
            try
            {
                var url = $"https://images.evetech.net/types/{typeId}/icon?tenant=tranquility&size=64";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("HISA/1.0");
                using var response = await CharacterPortraitHttpClient.SendAsync(request).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    icon = new Bitmap(stream);
                }
            }
            catch
            {
                icon = null;
            }
            finally
            {
                if (icon is not null)
                {
                    ShipTypeIconCache[typeId] = icon;
                    ShipTypeIconRetryAfterUtc.TryRemove(typeId, out _);
                }
                else
                {
                    ShipTypeIconRetryAfterUtc[typeId] = DateTime.UtcNow + CharacterPortraitRetryDelay;
                }

                ShipTypeIconLoading.TryRemove(typeId, out _);
                Dispatcher.UIThread.Post(InvalidateMap);
            }
        });

        return null;
    }

    private static void DrawCompactTooltip(DrawingContext context, Point anchor, string text)
    {
        var content = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            11,
            Brushes.White);

        var padX = 6.0;
        var padY = 3.0;
        var rect = new Rect(
            anchor.X + 10.0,
            anchor.Y + 10.0,
            content.Width + (padX * 2),
            content.Height + (padY * 2));

        context.FillRectangle(new ImmutableSolidColorBrush(Color.Parse("#D6111A28")), rect, 4);
        context.DrawRectangle(new Pen(new ImmutableSolidColorBrush(Color.Parse("#55739A")), 1), rect, 4);
        context.DrawText(content, new Point(rect.X + padX, rect.Y + padY));
    }

    private Bitmap? GetCharacterPortrait(int characterId)
    {
        if (characterId <= 0)
        {
            return null;
        }

        if (CharacterPortraitCache.TryGetValue(characterId, out var cached))
        {
            return cached;
        }

        if (CharacterPortraitRetryAfterUtc.TryGetValue(characterId, out var retryAfterUtc) &&
            DateTime.UtcNow < retryAfterUtc)
        {
            return null;
        }

        if (!CharacterPortraitLoading.TryAdd(characterId, 0))
        {
            return null;
        }

        _ = Task.Run(async () =>
        {
            Bitmap? portrait = null;
            try
            {
                var url = $"https://images.evetech.net/characters/{characterId}/portrait?tenant=tranquility&size=64";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("HISA/1.0");
                using var response = await CharacterPortraitHttpClient.SendAsync(request).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    portrait = new Bitmap(stream);
                }
            }
            catch
            {
                portrait = null;
            }
            finally
            {
                if (portrait is not null)
                {
                    CharacterPortraitCache[characterId] = portrait;
                    CharacterPortraitRetryAfterUtc.TryRemove(characterId, out _);
                }
                else
                {
                    CharacterPortraitRetryAfterUtc[characterId] = DateTime.UtcNow + CharacterPortraitRetryDelay;
                }

                CharacterPortraitLoading.TryRemove(characterId, out _);
                Dispatcher.UIThread.Post(InvalidateMap);
            }
        });

        return null;
    }

    private static string FormatRelativeAge(DateTime timestampUtc)
    {
        var utc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime();
        var elapsed = DateTime.UtcNow - utc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return $"{Math.Max(1, (int)elapsed.TotalSeconds)}s ago";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{(int)elapsed.TotalHours}h ago";
        }

        return $"{(int)elapsed.TotalDays}d ago";
    }

    private static string TrimIntelReportText(string text, int maxLength)
    {
        var compact = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= maxLength
            ? compact
            : $"{compact[..Math.Max(1, maxLength - 3)]}...";
    }

    private static Color GetCharacterPresenceBadgeColor(int count)
    {
        return Color.Parse("#2B8A58");
    }

    private static void DrawBitmap(DrawingContext context, Bitmap icon, Point topLeft, double size)
    {
        var src = new Rect(0, 0, icon.Size.Width, icon.Size.Height);
        var dst = new Rect(topLeft.X, topLeft.Y, size, size);
        context.DrawImage(icon, src, dst);
    }

    private static Size MeasureHubWormholeBadge(MapNode node)
    {
        var hasThera = node.HubWormholeConnections.Any(c => c.HubType == WormholeHubType.Thera);
        var hasTurnur = node.HubWormholeConnections.Any(c => c.HubType == WormholeHubType.Turnur);
        var label = hasThera && hasTurnur ? "T+U" : hasThera ? "T" : "U";
        var text = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            10,
            Brushes.Black);
        return new Size(Math.Max(14, text.Width + 6), 12);
    }

    private static void DrawHubWormholeBeacon(DrawingContext context, Point topLeft, MapNode node)
    {
        var hasThera = node.HubWormholeConnections.Any(c => c.HubType == WormholeHubType.Thera);
        var hasTurnur = node.HubWormholeConnections.Any(c => c.HubType == WormholeHubType.Turnur);
        var color = GetHubWormholeColor(node);
        var fill = new ImmutableSolidColorBrush(Color.FromArgb(230, color.R, color.G, color.B));
        var border = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(255, 15, 20, 28)), 1);
        var label = hasThera && hasTurnur ? "T+U" : hasThera ? "T" : "U";
        var text = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            10,
            Brushes.Black);

        var rect = new Rect(
            topLeft.X,
            topLeft.Y,
            Math.Max(14, text.Width + 6),
            12);
        context.FillRectangle(fill, rect, 3);
        context.DrawRectangle(border, rect, 3);
        context.DrawText(text, new Point(rect.X + ((rect.Width - text.Width) / 2), rect.Y + ((rect.Height - text.Height) / 2) - 0.5));
    }

    private static void DrawHubWormholeRing(DrawingContext context, Point nodePoint, MapNode node, double nodeRadius)
    {
        var color = GetHubWormholeColor(node);
        var pen = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(220, color.R, color.G, color.B)), 2.0);
        var ringRadius = nodeRadius + 3.4;
        context.DrawEllipse(null, pen, nodePoint, ringRadius, ringRadius);
    }

    private static void DrawHubWormholeHalo(DrawingContext context, Point nodePoint, MapNode node, double nodeRadius)
    {
        var color = GetHubWormholeColor(node);
        var halo = new ImmutableSolidColorBrush(Color.FromArgb(120, color.R, color.G, color.B));
        var haloRadius = nodeRadius + 4.2;
        context.DrawEllipse(halo, null, nodePoint, haloRadius, haloRadius);
    }

    private static bool IsA0BlueSmall(MapNode node)
    {
        if (node.SunTypeId == A0BlueSmallTypeId)
        {
            return true;
        }

        var name = node.StarTypeName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (string.Equals(name, A0BlueSmallName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Fallback for minor naming differences in imported datasets.
        return name.Contains("A0", StringComparison.OrdinalIgnoreCase)
            && name.Contains("Blue", StringComparison.OrdinalIgnoreCase)
            && name.Contains("Small", StringComparison.OrdinalIgnoreCase);
    }

    private static void DrawA0Icon(DrawingContext context, Point topLeft, double size)
    {
        var icon = A0StarIcon.Value;
        if (icon is null)
        {
            return;
        }

        var src = new Rect(0, 0, icon.Size.Width, icon.Size.Height);
        var dst = new Rect(topLeft.X, topLeft.Y, size, size);
        context.DrawImage(icon, src, dst);
    }

    private static void DrawJoveObservatoryIcon(DrawingContext context, Point topLeft, double size)
    {
        var icon = JoveObservatoryIcon.Value;
        if (icon is null)
        {
            return;
        }

        var src = new Rect(0, 0, icon.Size.Width, icon.Size.Height);
        var dst = new Rect(topLeft.X, topLeft.Y, size, size);
        context.DrawImage(icon, src, dst);
    }

    private static void DrawIceFieldIcon(DrawingContext context, Point topLeft, double size)
    {
        var icon = IceFieldIcon.Value;
        if (icon is null)
        {
            return;
        }

        var src = new Rect(0, 0, icon.Size.Width, icon.Size.Height);
        var dst = new Rect(topLeft.X, topLeft.Y, size, size);
        context.DrawImage(icon, src, dst);
    }

    private sealed record UniverseRegionLabelLayout(int RegionId, string RegionName, Point Center, Rect Rect, FormattedText Label);
    private readonly record struct PlotMetrics(double OriginX, double OriginY, double Width, double Height);

    private sealed class UniverseAtlasTile(RenderTargetBitmap bitmap, double originX, double originY) : IDisposable
    {
        public RenderTargetBitmap Bitmap { get; } = bitmap;
        public double OriginX { get; } = originX;
        public double OriginY { get; } = originY;

        public void Dispose() => Bitmap.Dispose();
    }
}

