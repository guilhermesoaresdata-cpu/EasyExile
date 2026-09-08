using Offsets = Poe2Analyzer.OffsetContract;

namespace EasyExile.Core.Contract;

/// <summary>
/// The single place that names the offset contract. Everything else asks this,
/// so a contract rename touches one file instead of the whole codebase — and no
/// offset is ever written as a literal outside GameOffsets.dll.
/// </summary>
/// <remarks>
/// Internal on purpose. EasyExile.Radar must not know an offset exists, and the
/// simplest way to guarantee that is to make the whole layout invisible outside
/// this assembly. Tests and Diagnostics see it through InternalsVisibleTo.
///
/// The contract is reached through the <c>Offsets</c> alias rather than a plain
/// <c>using</c>, because folder namespaces here (Camera, Spatial) collide by name
/// with contract classes. The alias makes every reference unambiguous.
/// </remarks>
internal static class GameLayout
{
    /// <summary>Size of a pointer in the client, which is always 64-bit.</summary>
    internal const int PointerSize = 8;

    internal static class Build
    {
        public static string Fingerprint => Offsets.BuildInfo.Fingerprint;
        public static uint PeTimestamp => Offsets.BuildInfo.PeTimestamp;
        public static uint ImageSize => Offsets.BuildInfo.ImageSize;
        public static string TextSha256 => Offsets.BuildInfo.TextSha256;
        public static int SchemaVersion => Offsets.BuildInfo.SchemaVersion;
        public static string ExportHash => Offsets.BuildInfo.ExportHash;
    }

    internal static class Roots
    {
        public static int GlobalSlotRva => Offsets.GameState.GlobalSlotRva;
        public static int CurrentStateVector => Offsets.GameState.CurrentStateVector;

        /// <summary>The inline state slots, used when the vector is empty.</summary>
        public static int StatesArray => Offsets.GameState.StatesArray;
        public static int StateSlotStride => Offsets.GameState.StateSlotStride;
        public static int StateSlotCount => Offsets.GameState.StateSlotCount;
        public static int AreaInstance => Offsets.InGameState.AreaInstance;
        public static int UiRoot => Offsets.InGameState.UiRoot;
        public static int Camera => Offsets.InGameState.Camera;
    }

    internal static class World
    {
        public static int LocalPlayer => Offsets.AreaInstance.LocalPlayer;
        public static int AwakeEntities => Offsets.AreaInstance.AwakeEntities;
        public static int SleepingEntities => Offsets.AreaInstance.SleepingEntities;

        /// <summary>
        /// How much of an AreaInstance has to be readable before the structure can
        /// be treated as a real area. Derived from the fields actually read rather
        /// than written as a round number: if a patch moves SleepingEntities past
        /// whatever constant someone picked, the chain would stop resolving with no
        /// error to explain why, and the build gate would not catch it because the
        /// contract would have been re-exported for that same patch.
        /// </summary>
        public static int RequiredReadableSpan =>
            Math.Max(LocalPlayer, Math.Max(AwakeEntities, SleepingEntities)) + PointerSize;
    }

    internal static class Components
    {
        public static int EntityDetails => Offsets.Entity.EntityDetails;
        public static int ComponentList => Offsets.Entity.ComponentList;
        public static int Metadata => Offsets.EntityDetails.Metadata;
        public static int ComponentLookup => Offsets.EntityDetails.ComponentLookup;
        public static int NameBucket => Offsets.ComponentLookup.NameBucket;
        public static int EntryName => Offsets.NameIndexEntry.Name;
        public static int EntryIndex => Offsets.NameIndexEntry.Index;
        public static int EntryStride => Offsets.NameIndexEntry.Stride;
        public static int Owner => Offsets.Component.Owner;
    }

    internal static class Vitals
    {
        public static int Health => Offsets.Life.Health;
        public static int Mana => Offsets.Life.Mana;
        public static int EnergyShield => Offsets.Life.EnergyShield;
        public static int BlockId => Offsets.VitalBlock.Id;
        public static int BlockMax => Offsets.VitalBlock.Max;
        public static int BlockCurrent => Offsets.VitalBlock.Current;
    }

    /// <summary>
    /// A monster's rank. Not the Mods component: monsters do not carry one —
    /// 1159 of them live without it. Mods.Rarity is the item field.
    /// </summary>
    internal static class Rank
    {
        public static int Rarity => Offsets.ObjectMagicProperties.Rarity;

        public const string ComponentName = Offsets.ComponentNames.ObjectMagicProperties;

        /// <summary>The monster's rolled affix mods: auras, resistances, extra damage.</summary>
        public static int Mods => Offsets.ObjectMagicProperties.Mods;

        public static int ModStride => Offsets.ObjectMagicProperties.ModStride;

        /// <summary>Element to its record.</summary>
        public static int ModRecord => Offsets.ModEntry.RecordPointer;

        /// <summary>
        /// Record to the id. A POINTER to the text, never the text — reading the
        /// record's own bytes as a string is the mistake that makes every
        /// monster look unmodded.
        /// </summary>
        public static int ModId => Offsets.ModRecord.IdString;
    }

    /// <summary>
    /// The game's own map-icon marker. Ported from POE2Radar (MIT)
    /// <c>Poe2Offsets.MinimapIcon</c>: the component's PRESENCE is the semantics
    /// — the client puts it on exactly the things it draws an icon for — and the
    /// flag fades a finished encounter rather than removing it.
    /// </summary>
    /// <summary>
    /// The area's own identifier, e.g. <c>G2_5_1</c>. Two dereferences from the
    /// area instance; the only key the curated landmark data has.
    /// </summary>
    internal static class Area
    {
        public static int Info => Offsets.AreaInstance.AreaInfo;
    }

    /// <summary>
    /// Where an exit goes. Its metadata path is <c>AreaTransition_Animate</c> for
    /// every exit in the game, so the name is only reachable here.
    /// </summary>
    internal static class Transition
    {
        public const string ComponentName = Offsets.ComponentNames.AreaTransition;

        public static int Destination => Offsets.AreaTransition.Destination;

        public static int AreaName => Offsets.WorldArea.Name;

        /// <summary>
        /// The destination's code, e.g. <c>Abyss_Hub</c>.
        /// </summary>
        /// <remarks>
        /// The join key a levelling route needs. The NAME beside it is
        /// localized — this client renders in Portuguese — so a route authored
        /// against community data in English can only match on the code.
        /// </remarks>
        public static int AreaId => Offsets.WorldArea.Id;
    }

    internal static class MinimapIcon
    {
        public static int CompletedState => Offsets.MinimapIcon.CompletedState;

        /// <summary>The icon definition, whose first field names it.</summary>
        public static int Definition => Offsets.MinimapIcon.Definition;

        public const string ComponentName = Offsets.ComponentNames.MinimapIcon;
    }

    /// <summary>
    /// What a drop on the ground actually is.
    /// </summary>
    /// <remarks>
    /// A drop is a wrapper; everything identifying lives on the item entity
    /// inside it. Art alone cannot price an item — currency tiers share one icon
    /// — so the rendered base name comes along too.
    /// </remarks>
    internal static class Item
    {
        public const string WorldItemComponent = Offsets.ComponentNames.WorldItem;
        public const string RenderComponent = Offsets.ComponentNames.RenderItem;
        public const string BaseComponent = Offsets.ComponentNames.Base;
        public const string ModsComponent = Offsets.ComponentNames.Mods;

        public static int Inner => Offsets.WorldItem.ItemEntity;
        public static int ArtPath => Offsets.RenderItem.ResourcePath;
        public static int NameRow => Offsets.Base.NameRow;
        public static int RowDisplayName => Offsets.Base.RowDisplayName;
        public static int Rarity => Offsets.Mods.Rarity;

        /// <summary>Zero means the game is hiding the name. The unique reveal hangs off this.</summary>
        public static int Identified => Offsets.Mods.Identified;

        /// <summary>How many are in the pile, so a chip can price the pile.</summary>
        public const string StackComponent = Offsets.ComponentNames.Stack;
        public static int StackCount => Offsets.Stack.Count;

        /// <summary>
        /// The item's rolled affixes, as internal ids.
        /// </summary>
        /// <remarks>
        /// The id carries its own tier: the game names them Strength1..Strength8
        /// and the trailing number IS the tier. Which is why a tier readout does
        /// not need any stat-range arithmetic — only the name, and a table of
        /// how many tiers that family has.
        /// </remarks>
        public static int ExplicitMods => Offsets.Mods.ExplicitMods;

        public static int ImplicitMods => Offsets.Mods.ImplicitMods;

        /// <summary>Element to element inside the mod vector.</summary>
        public static int ModStride => Offsets.ModRecord.Stride;

        /// <summary>
        /// From a mod entry to its record.
        /// </summary>
        /// <remarks>
        /// NOT the monster layout. A monster's entry reaches its record from
        /// 0x08 and an item's from here; reading an item's the monster way
        /// returns nothing at all, which is how this was found.
        /// </remarks>
        public static int ModRecord => Offsets.ItemModEntry.RecordPointer;

        /// <summary>Record to the id, which is a POINTER to UTF-16 text.</summary>
        public static int ModId => Offsets.ModRecord.IdString;
    }

    /// <summary>The league the character is in, spelled as the price sources spell it.</summary>
    internal static class Server
    {
        public static int Root => Offsets.AreaInstance.ServerData;

        public static int League => Offsets.ServerData.League;
    }

    internal static class Identity
    {
        public static int Name => Offsets.Player.Name;
        public static int Level => Offsets.Player.Level;
    }

    internal static class Spatial
    {
        public static int WorldPosition => Offsets.Render.WorldPosition;

        /// <summary>Bytes occupied by a world position: three 32-bit floats.</summary>
        public const int WorldPositionSize = 12;

        /// <summary>
        /// World units per grid cell. There is no grid-position offset in the
        /// contract any more: the field that used to be exported read (0,0) for
        /// long stretches while the world position was perfectly valid, so the
        /// grid is derived from the world position instead of read from memory.
        /// </summary>
        /// <summary>
        /// Friend or foe. The reference's rule: friendly when
        /// <c>(value &amp; 0x7F) == 1</c>. Live, hostile monsters read 0 while
        /// the player, its summons and the companion read 1.
        /// </summary>
        public static int Reaction => Offsets.Positioned.Reaction;

        public static float WorldToGrid =>
            Offsets.Spatial.WorldToGridNumerator /
            (float)Offsets.Spatial.WorldToGridDenominator;
    }

    internal static class View
    {
        public static int ViewProjection => Offsets.Camera.ViewProjection;
        public static int Viewport => Offsets.Camera.Viewport;

        /// <summary>A 4x4 matrix of 32-bit floats.</summary>
        public const int ViewProjectionSize = 64;

        /// <summary>Two 32-bit integers, width then height.</summary>
        public const int ViewportSize = 8;

        /// <summary>
        /// How much of the camera structure has to be readable. Derived, for the
        /// same reason as <see cref="World.RequiredReadableSpan"/>.
        /// </summary>
        public static int RequiredReadableSpan =>
            Math.Max(ViewProjection + ViewProjectionSize, Viewport + ViewportSize);
    }

    /// <summary>
    /// The terrain block. It is an <b>inline</b> struct: the block starts at
    /// <c>AreaInstance + Root</c> and is never dereferenced. Reading a pointer
    /// there yields the struct's own vtable pointer, which is what made terrain
    /// look absent for a whole sprint.
    /// </summary>
    internal static class Terrain
    {
        public static int Root => Offsets.AreaInstance.TerrainMetadata;

        public static int TotalTiles => Offsets.Terrain.TotalTiles;
        public static int TileDetails => Offsets.Terrain.TileDetails;
        public static int WalkableGrid => Offsets.Terrain.WalkableGrid;
        public static int BytesPerRow => Offsets.Terrain.BytesPerRow;

        public static int TileStride => Offsets.Terrain.TileStride;
        public static int CellsPerTile => Offsets.Terrain.CellsPerTile;

        /// <summary>One tile entry to the asset it was stamped from.</summary>
        public static int TgtFile => Offsets.TileStructure.TgtFilePtr;

        /// <summary>That asset's own .tdt path — the only name a tile has.</summary>
        public static int TgtPath => Offsets.TgtFileStruct.TgtPath;
    }

    /// <summary>The game's own map element: where it is panned to and how far zoomed.</summary>
    internal static class MapUi
    {
        public static int Shift => Offsets.MapUiElement.Shift;
        public static int DefaultShift => Offsets.MapUiElement.DefaultShift;
        public static int Zoom => Offsets.MapUiElement.Zoom;
    }

    /// <summary>UiElement fields, for walking the tree and finding the map.</summary>
    internal static class Ui
    {
        public static int Self => Offsets.UiElement.Self;
        public static int Children => Offsets.UiElement.Children;
        public static int Parent => Offsets.UiElement.Parent;
        public static int Flags => Offsets.UiElement.Flags;
        public static int VisibleBit => Offsets.UiElement.VisibleBit;

        // Layout, for asking where an element actually sits on screen.
        public static int RelativeX => Offsets.UiElement.RelativePosition;
        public static int RelativeY => Offsets.UiElement.RelativePosition + 4;
        public static int PositionModifierX => Offsets.UiElement.PositionModifier;
        public static int PositionModifierY => Offsets.UiElement.PositionModifier + 4;
        public static int ModifyPositionBit => Offsets.UiElement.ModifyPositionBit;
        public static int SizeWidth => Offsets.UiElement.SizeWidth;
        public static int SizeHeight => Offsets.UiElement.SizeHeight;

        /// <summary>The element's own text, when it has any.</summary>
        public static int Text => Offsets.UiElement.Text;

        /// <summary>
        /// One rendered line of text, as a bare pointer to its characters.
        /// </summary>
        /// <remarks>
        /// Not the same field as <see cref="Text"/> and not the same shape: a
        /// pointer to the characters rather than a std::wstring. Everything the
        /// client draws as a line of prose is here — a tooltip's mods one per
        /// element, each with its own rectangle.
        /// </remarks>
        public static int LineText => Offsets.UiElement.LineText;

        /// <summary>The whole item description, held by the item in the grid.</summary>
        public static int ItemDescription => Offsets.UiElement.ItemDescription;

        /// <summary>The universal item-slot field: every item UI hangs its entity here.</summary>
        public static int TileSlotItem => Offsets.UiElement.TileSlotItem;
    }

    /// <summary>
    /// The ground-label layer, found by role bits rather than child indices.
    /// </summary>
    /// <remarks>
    /// Child indices drift every patch; the flag bits that say what an element
    /// IS do not. Three hops from the UI root, each matching a fingerprint.
    /// </remarks>
    internal static class GroundLabels
    {
        public static int[] Fingerprints =>
        [
            Offsets.GroundLabels.Fingerprint0,
            Offsets.GroundLabels.Fingerprint1,
            Offsets.GroundLabels.Fingerprint2,
        ];
    }

    internal static class Native
    {
        public static int VectorFirst => Offsets.StdVector.First;
        public static int VectorLast => Offsets.StdVector.Last;
        public static int VectorEnd => Offsets.StdVector.End;

        public static int StringBuffer => Offsets.StdWString.Buffer;
        public static int StringSize => Offsets.StdWString.Size;
        public static int StringCapacity => Offsets.StdWString.Capacity;

        public static int MapLeft => Offsets.StdMapNode.Left;
        public static int MapParent => Offsets.StdMapNode.Parent;
        public static int MapRight => Offsets.StdMapNode.Right;
        public static int MapKey => Offsets.StdMapNode.Key;
        public static int MapEntityValue => Offsets.StdMapNode.EntityValue;
    }

    /// <summary>Names the client supplies. Indices are runtime and never stored.</summary>
    internal static class Names
    {
        public const string Life = Offsets.ComponentNames.Life;
        public const string Player = Offsets.ComponentNames.Player;
        public const string Render = Offsets.ComponentNames.Render;
        public const string Positioned = Offsets.ComponentNames.Positioned;
        public const string Stats = Offsets.ComponentNames.Stats;

        public const string Inventories = Offsets.ComponentNames.Inventories;
        public const string Actor = Offsets.ComponentNames.Actor;
        public const string Buffs = Offsets.ComponentNames.Buffs;
    }

/// <summary>
/// The character's stat table: a flat list of keyed numbers.
/// </summary>
/// <remarks>
/// The keys are the client's own and the contract is explicit that their
/// meanings are NOT established - one of them tracked a current value in one
/// session and a maximum in another. So nothing here names a stat; a reader has
/// to establish which key is which by matching values it can also see, and say
/// so rather than assume.
/// </remarks>
internal static class StatTable
{
    /// <summary>From the Stats component to the struct holding the table.</summary>
    public static int Struct => Offsets.Stats.StatsStructInternal;

    /// <summary>The vector of entries inside that struct.</summary>
    public static int Vector => Offsets.StatsStructInternal.StatVector;

    /// <summary>Each entry is a key and a value, side by side.</summary>
    public const int KeyOffset = 0;

    public const int ValueOffset = 4;

    public const int Stride = 8;

    /// <summary>
    /// The keys the four resistances live under.
    /// </summary>
    /// <remarks>
    /// Identities of the client's own, so they belong in the contract like any
    /// offset - and they were established the same way, by matching numbers
    /// that could also be seen on the character sheet.
    /// </remarks>
    public static int FireResistance => Offsets.Stats.FireResistanceKey;

    public static int ColdResistance => Offsets.Stats.ColdResistanceKey;

    public static int LightningResistance => Offsets.Stats.LightningResistanceKey;

    public static int ChaosResistance => Offsets.Stats.ChaosResistanceKey;
}
}
