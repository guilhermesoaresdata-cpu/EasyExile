using EasyExile.Radar.Features.NativeMap;
using EasyExile.Radar.Features.World;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Runtime;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.General;
using EasyExile.Radar.Settings.Loot;
using EasyExile.Radar.Settings.NativeMap;
using EasyExile.Radar.Settings.Player;
using ImGuiNET;

namespace EasyExile.Radar.UI;

/// <summary>
/// The in-game control panel, shown only in interactive mode.
/// </summary>
/// <remarks>
/// One tab per settings category, and each tab shows only what belongs to it.
/// There is no search box: with three categories it would be furniture, and a
/// search that exists before the thing worth searching for teaches nobody
/// anything about the shape of the settings.
///
/// The settings records are immutable, so each control reads a value, and on
/// change swaps the whole category record. That makes every edit a single
/// reference assignment, which is what the capture thread reads.
/// </remarks>
internal sealed class SettingsWindow
{
    private readonly RadarSettings _settings;
    private readonly DebugWorldFeature _debug;
    private readonly NativeMapRadarFeature _map;

    /// <summary>
    /// The panel's rectangle in client pixels, or null when it is not drawn.
    /// </summary>
    /// <remarks>
    /// The overlay needs this to decide who owns the mouse. Interactive mode
    /// used to take the whole screen, so opening the panel meant the game could
    /// not be clicked at all until it was closed again.
    /// </remarks>
    public (float Left, float Top, float Right, float Bottom)? Bounds { get; private set; }

    /// <summary>Forgets where the panel was, for when it stops being drawn.</summary>
    public void Forget() => Bounds = null;

    /// <summary>True when the cursor is over the panel and the click is ours.</summary>
    public bool Contains(float x, float y) =>
        Bounds is { } b && x >= b.Left && x <= b.Right && y >= b.Top && y <= b.Bottom;
    private readonly Features.Navigation.Navigator? _navigator;

    /// <summary>
    /// The targets the current area offers, rebuilt when the snapshot changes.
    /// Kept here rather than in the Navigator because it is a view concern: the
    /// list is what the panel draws, not what navigation runs on.
    /// </summary>
    private IReadOnlyList<Features.Navigation.NavTarget> _targets =
        Array.Empty<Features.Navigation.NavTarget>();

    private RadarRenderLoop? _loop;

    /// <summary>The price book, so the panel can report what it knows.</summary>
    private Pricing.PriceBook? _prices;

    /// <summary>Hands the panel the price book. Set once, at composition.</summary>
    public void UsePrices(Pricing.PriceBook prices) => _prices = prices;

    private bool _placed;

    private long _targetsEpoch = -1;
    private int _targetsCount = -1;

    public SettingsWindow(
        RadarSettings settings, DebugWorldFeature debug, NativeMapRadarFeature map,
        Features.Navigation.Navigator? navigator = null,
        Features.Levelling.RouteGuideFeature? guide = null,
        Features.Levelling.AreaGraph? areas = null,
        Features.Levelling.LevelRoute? route = null)
    {
        _settings = settings;
        _debug = debug;
        _map = map;
        _navigator = navigator;
        _guide = guide;
        _areas = areas;
        _route = route;
    }

    private readonly Features.Levelling.RouteGuideFeature? _guide;
    private readonly Features.Levelling.AreaGraph? _areas;
    private readonly Features.Levelling.LevelRoute? _route;

    public void Draw(RadarRenderLoop loop, RadarStats stats)
    {
        _loop = loop;

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(420, 380), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(new System.Numerics.Vector2(60, 60), ImGuiCond.FirstUseEver);

        // Where it was left last time. Applied once per session, before Begin
        // decides for itself: ImGui would otherwise place it from imgui.ini,
        // which is written on a graceful shutdown an overlay rarely gets.
        if (!_placed)
        {
            _placed = true;

            var general = _settings.General;

            if (general.PanelX > 0f || general.PanelY > 0f)
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(general.PanelX, general.PanelY));
        }

        // Styled here rather than once at startup: the overlay shares an ImGui
        // context with nothing else, but pushing and popping around our own
        // window keeps the styling a property of this panel instead of a global
        // that some later feature has to remember not to fight.
        Style();

        if (!ImGui.Begin("EasyExile"))
        {
            Bounds = null;
            ImGui.End();
            Unstyle();
            return;
        }

        // Where the panel actually is, so the overlay can hand every click
        // outside it back to the game.
        var position = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();

        Bounds = (position.X, position.Y, position.X + size.X, position.Y + size.Y);

        // Dragged to a new spot: remember it. The store notices the change and
        // writes, so closing the overlay any way at all keeps the position.
        if (MathF.Abs(position.X - _settings.General.PanelX) > 0.5f ||
            MathF.Abs(position.Y - _settings.General.PanelY) > 0.5f)
            _settings.General = _settings.General with { PanelX = position.X, PanelY = position.Y };

        ImGui.TextDisabled($"{VirtualKey.Name(_settings.General.HudInteractiveHotkey)} volta para o modo HUD");
        ImGui.Separator();

        if (ImGui.BeginTabBar("categorias"))
        {
            if (ImGui.BeginTabItem("Geral"))
            {
                DrawGeneral();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Loot"))
            {
                DrawLoot();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Vida"))
            {
                DrawHpBars();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Poção"))
            {
                DrawAutoPotion();
                ImGui.EndTabItem();
            }

            if (_navigator is not null && ImGui.BeginTabItem("Rotas"))
            {
                DrawNavigation();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Leveling"))
            {
                DrawLevelling();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Mapa"))
            {
                DrawNativeMap();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("HUD"))
            {
                DrawPlayer();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Debug"))
            {
                DrawDebug(loop, stats);

                ImGui.Separator();
                ImGui.TextDisabled("DevTree");

                DrawDevTree(loop);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.End();

        Unstyle();
    }

    private void DrawGeneral()
    {
        // Applied on restart, because the window's frame limit is set when the
        // window is created. Lowering it saves nothing measurable — the cost is
        // the world walk — so it is here for a machine that needs it, not as a
        // default.
        var frames = _settings.General.RenderFps;
        if (ImGui.SliderInt("Quadros por segundo do overlay", ref frames, 30, 240))
            _settings.General = _settings.General with { RenderFps = frames };

        ImGui.TextDisabled("aplica ao reiniciar o EasyExile");
        ImGui.Separator();

        var general = _settings.General;

        var enabled = general.Enabled;
        if (ImGui.Checkbox("Captura ativa", ref enabled))
            _settings.General = general with { Enabled = enabled };

        var rate = general.UpdateRateHz;
        if (ImGui.SliderInt("Capturas por segundo", ref rate, 1, 60))
            _settings.General = general with { UpdateRateHz = rate };

        var max = general.MaxEntities;
        if (ImGui.SliderInt("Maximo de entidades", ref max, 16, 2048))
            _settings.General = general with { MaxEntities = max };

        var unfocused = general.ShowWhenGameUnfocused;
        if (ImGui.Checkbox("Mostrar com o jogo fora de foco", ref unfocused))
            _settings.General = general with { ShowWhenGameUnfocused = unfocused };

        ImGui.Spacing();
        ImGui.TextDisabled("A taxa de renderizacao e independente da captura.");
    }

    /// <summary>
    /// Pick a destination and a route appears on the game's own map.
    /// </summary>
    /// <remarks>
    /// The reference drives this from an on-overlay navigation menu with
    /// click-to-toggle rows. This is the same selection model — toggle a target,
    /// up to eight, each in its own colour — living in the panel that already
    /// exists rather than in a new widget.
    /// </remarks>
    private void DrawNavigation()
    {
        var navigator = _navigator!;

        ImGui.TextDisabled("Escolha um destino. A rota aparece no mapa do jogo.");
        ImGui.Separator();

        var routes = _settings.NativeMap.ShowRoutes;
        if (ImGui.Checkbox("Desenhar rotas", ref routes))
            _settings.NativeMap = _settings.NativeMap with { ShowRoutes = routes };

        var world = _settings.NativeMap.ShowWorldRoute;
        if (ImGui.Checkbox("Trilha no mundo com o mapa fechado", ref world))
            _settings.NativeMap = _settings.NativeMap with { ShowWorldRoute = world };

        var destination = _settings.NativeMap.ShowDestinationName;
        if (ImGui.Checkbox("Nome do destino na trilha", ref destination))
            _settings.NativeMap = _settings.NativeMap with { ShowDestinationName = destination };

        ImGui.Separator();

        var mechanic = _settings.NativeMap.AutoRouteLeagueMechanic;
        if (ImGui.Checkbox("Rotear a mecanica da liga ao chegar", ref mechanic))
            _settings.NativeMap = _settings.NativeMap with { AutoRouteLeagueMechanic = mechanic };

        var exits = _settings.NativeMap.AutoRouteExits;
        if (ImGui.Checkbox("Rotear saidas ao chegar", ref exits))
            _settings.NativeMap = _settings.NativeMap with { AutoRouteExits = exits };

        if (ImGui.Button("Limpar tudo")) navigator.Clear();

        ImGui.Separator();

        if (_targets.Count == 0)
        {
            ImGui.TextDisabled("Nenhum destino nesta area ainda.");
            return;
        }

        ImGui.TextDisabled($"{navigator.RouteCount} de {Features.Navigation.Navigator.MaxRoutes} rotas");
        ImGui.Spacing();

        for (var i = 0; i < _targets.Count; i++)
        {
            var target = _targets[i];
            var selected = navigator.IsSelected(target.Id);

            // The swatch is the route's colour, so a line on the map is
            // traceable back to the row that made it.
            if (selected)
            {
                var slot = navigator.Selected.ToList().IndexOf(target.Id);

                ImGui.PushStyleColor(ImGuiCol.Text, ToVec4(Palette.RouteColour(slot)));
            }

            var toggled = selected;

            if (ImGui.Checkbox($"{target.Label}##nav{i}", ref toggled)) navigator.Toggle(target.Id);

            if (selected) ImGui.PopStyleColor();
        }
    }

    /// <summary>Unpacks a packed RGBA colour for ImGui's float-per-channel style stack.</summary>
    private static System.Numerics.Vector4 ToVec4(uint colour) =>
        new(
            (colour & 0xFF) / 255f,
            ((colour >> 8) & 0xFF) / 255f,
            ((colour >> 16) & 0xFF) / 255f,
            ((colour >> 24) & 0xFF) / 255f);

    /// <summary>Refreshes the destination list when the area or the entity set changes.</summary>
    public void Observe(EasyExile.Core.Snapshots.WorldSnapshot snapshot)
    {
        if (snapshot.Epoch == _targetsEpoch && snapshot.Entities.Length == _targetsCount) return;

        _targetsEpoch = snapshot.Epoch;
        _targetsCount = snapshot.Entities.Length;
        _targets = Features.Navigation.NavTargets.From(snapshot);
    }

    /// <summary>
    /// The one feature that presses a key. Its state is shown plainly, because
    /// "is this armed right now" is the only question worth being sure about.
    /// </summary>
    /// <summary>Prices over drops.</summary>
    /// <summary>
    /// The levelling guide.
    /// </summary>
    /// <remarks>
    /// The counters are the point of this panel, not decoration. There is no
    /// published table of PoE2 area codes, so the graph starts empty and the
    /// only way a user can tell the tool is learning is to watch it grow.
    /// </remarks>
    private void DrawLevelling()
    {
        var levelling = _settings.Levelling;

        ImGui.TextColored(ToVec4(Palette.Warning), "BETA - em construcao");
        ImGui.TextDisabled("O guia usa o mesmo sistema de rota: desenha o caminho ate a saida certa.");
        ImGui.Separator();

        var enabled = levelling.Enabled;
        if (ImGui.Checkbox("Ativado##lvl", ref enabled))
            _settings.Levelling = levelling with { Enabled = enabled };

        var auto = levelling.AutoRoute;
        if (ImGui.Checkbox("Rotear automaticamente para o proximo passo", ref auto))
            _settings.Levelling = levelling with { AutoRoute = auto };

        var recording = levelling.Recording;
        if (ImGui.Checkbox("Gravar a rota enquanto joga", ref recording))
            _settings.Levelling = levelling with { Recording = recording };

        ImGui.Separator();
        ImGui.TextDisabled("Passos na tela");

        // The guide is useless inside this window: nobody plays with it open.
        var steps = levelling.ShowSteps;
        if (ImGui.Checkbox("Mostrar os passos por cima do jogo", ref steps))
            _settings.Levelling = levelling with { ShowSteps = steps };

        var corner = Corner("Canto##lvl", levelling.Corner);
        if (corner != levelling.Corner) _settings.Levelling = levelling with { Corner = corner };

        var visible = levelling.VisibleSteps;
        if (ImGui.SliderInt("Quantos passos##lvl", ref visible, 0, 10))
            _settings.Levelling = levelling with { VisibleSteps = visible };

        var optional = levelling.ShowOptional;
        if (ImGui.Checkbox("Incluir os opcionais", ref optional))
            _settings.Levelling = levelling with { ShowOptional = optional };

        var scale = levelling.TextScale;
        if (ImGui.SliderFloat("Tamanho do texto##lvl", ref scale, 0.6f, 2.5f, "%.2f"))
            _settings.Levelling = levelling with { TextScale = scale };

        var opacity = levelling.Opacity;
        if (ImGui.SliderFloat("Opacidade do fundo##lvl", ref opacity, 0f, 1f, "%.2f"))
            _settings.Levelling = levelling with { Opacity = opacity };

        ImGui.Separator();
        ImGui.TextColored(ToVec4(Palette.Warning), "Diario da campanha (BETA)");
        ImGui.TextDisabled("Anota o que cada zona REALMENTE tinha, para corrigir o guia depois.");

        var journal = levelling.Journal;
        if (ImGui.Checkbox("Gravar campaign-journal.txt", ref journal))
            _settings.Levelling = levelling with { Journal = journal };

        if (_guide is not null)
            ImGui.TextDisabled($"zonas anotadas nesta sessao: {_guide.Journalled}");

        ImGui.Separator();

        if (_guide is null) return;

        ImGui.Text($"areas conhecidas {_areas?.Areas ?? 0}   visitadas {_areas?.VisitedCount ?? 0}" +
            $"   ligacoes {_areas?.Edges ?? 0}");

        ImGui.Text($"rota gravada     passo {_guide.Step} de {_guide.Steps}");

        ImGui.Separator();

        ImGui.Text("voce esta em:  " + _guide.Here);

        // Whether a path is actually being drawn, said out loud. "It knows where
        // to go and does not do it" was true and invisible: the objective and
        // the drawing are different things, and the panel showed neither.
        if (_guide.Routing)
            ImGui.TextColored(ToVec4(Palette.Good), "desenhando:    " + _guide.Objective);
        else
            ImGui.TextDisabled("parado:        " + _guide.Objective);

        // What to DO here, not only where to go next. The route answers "which
        // door"; this answers the other half, which no tool that cannot see
        // your instance can pair with it.
        if (_guide.Zone is not { } zone) return;

        ImGui.Separator();

        ImGui.Text($"{zone.Act} — zona {_guide.ZoneNumber} de {_guide.ZoneCount}");
        ImGui.TextColored(ToVec4(Palette.Good), zone.Name + (zone.Optional ? "  (opcional)" : ""));

        ImGui.Spacing();

        foreach (var step in zone.Steps)
        {
            var line = step.Hint is { Length: > 0 } hint ? $"{step.Text} — {hint}" : step.Text;

            if (step.Optional)
                ImGui.TextDisabled("  opt  " + line);
            else
                ImGui.TextWrapped("  -  " + line);
        }
    }

    /// <summary>
    /// The panel's look: softer corners, room to breathe, and a palette that
    /// sits on top of a game rather than beside one.
    /// </summary>
    /// <remarks>
    /// An overlay is read in a hurry, over a busy, bright, moving background.
    /// The defaults are tuned for a desktop app on a flat grey: thin borders,
    /// low contrast, square corners. Every value here is chosen against the
    /// opposite problem.
    /// </remarks>
    /// <summary>
    /// The panel's palette. Pushed from a table so the count cannot drift.
    /// </summary>
    /// <remarks>
    /// The first version hand-counted the pushes and returned one more than it
    /// made. ImGui pops what it is told to pop, so the colour stack underflowed
    /// and the overlay died the moment the panel was opened with F10 — a
    /// hand-maintained tally beside a list that grows is a bug waiting for the
    /// next colour.
    /// </remarks>
    private static readonly (ImGuiCol Slot, uint Colour)[] Colours =
    [
        // Nearly opaque, not translucent: a settings panel the game shows
        // through is one you cannot read while anything is happening.
        (ImGuiCol.WindowBg, Palette.Rgba(16, 18, 24, 244)),
        (ImGuiCol.Border, Palette.Rgba(70, 90, 120, 200)),
        (ImGuiCol.TitleBg, Palette.Rgba(20, 24, 32)),
        (ImGuiCol.TitleBgActive, Palette.Rgba(28, 40, 60)),
        (ImGuiCol.FrameBg, Palette.Rgba(30, 36, 48)),
        (ImGuiCol.FrameBgHovered, Palette.Rgba(44, 56, 76)),
        (ImGuiCol.FrameBgActive, Palette.Rgba(56, 72, 98)),
        (ImGuiCol.Tab, Palette.Rgba(26, 32, 42)),
        (ImGuiCol.TabHovered, Palette.Rgba(60, 100, 150)),
        (ImGuiCol.TabSelected, Palette.Rgba(46, 78, 120)),
        (ImGuiCol.CheckMark, Palette.Player),
        (ImGuiCol.SliderGrab, Palette.Player),
        (ImGuiCol.SliderGrabActive, Palette.Rgba(150, 220, 255)),
        (ImGuiCol.Button, Palette.Rgba(36, 46, 62)),
        (ImGuiCol.ButtonHovered, Palette.Rgba(54, 76, 108)),
        (ImGuiCol.ButtonActive, Palette.Rgba(70, 100, 140)),
        (ImGuiCol.Separator, Palette.Rgba(60, 76, 100, 180)),
    ];

    /// <summary>
    /// Softer corners and room to breathe.
    /// </summary>
    /// <remarks>
    /// The ImGui defaults are tuned for a desktop app on flat grey — thin
    /// borders, low contrast, square corners — and an overlay is read in a hurry
    /// over something bright and moving.
    /// </remarks>
    private static readonly (ImGuiStyleVar Var, System.Numerics.Vector2 Value)[] Metrics =
    [
        (ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(12f, 10f)),
        (ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(8f, 4f)),
        (ImGuiStyleVar.ItemSpacing, new System.Numerics.Vector2(8f, 7f)),
    ];

    private static readonly (ImGuiStyleVar Var, float Value)[] Roundings =
    [
        (ImGuiStyleVar.WindowRounding, 8f),
        (ImGuiStyleVar.FrameRounding, 5f),
        (ImGuiStyleVar.TabRounding, 5f),
        (ImGuiStyleVar.GrabRounding, 5f),
        (ImGuiStyleVar.WindowBorderSize, 1f),
    ];

    private static void Style()
    {
        foreach (var (variable, value) in Roundings) ImGui.PushStyleVar(variable, value);
        foreach (var (variable, value) in Metrics) ImGui.PushStyleVar(variable, value);
        foreach (var (slot, colour) in Colours) ImGui.PushStyleColor(slot, ToVec4(colour));
    }

    private static void Unstyle()
    {
        ImGui.PopStyleColor(Colours.Length);
        ImGui.PopStyleVar(Roundings.Length + Metrics.Length);
    }

    /// <summary>One line of the colour legend: a swatch and what it means.</summary>
    private static void Legend(int packed, string meaning)
    {
        var at = ImGui.GetCursorScreenPos();
        var height = ImGui.GetTextLineHeight();

        ImGui.GetWindowDrawList().AddRectFilled(
            at,
            new System.Numerics.Vector2(at.X + height, at.Y + height),
            unchecked((uint)packed));

        ImGui.Dummy(new System.Numerics.Vector2(height + 6f, height));
        ImGui.SameLine();
        ImGui.TextDisabled(meaning);
    }

    private static readonly string[] CornerNames =
        ["inferior esquerdo", "inferior direito", "superior esquerdo", "superior direito", "acima", "abaixo"];

    /// <summary>
    /// Where the skill's support list goes.
    /// </summary>
    /// <remarks>
    /// "Ao lado da skill" is the obvious choice and the one that collides: the
    /// client draws its own skill tooltip over the same left-hand space, which
    /// is why the corners are here and why one of them is the default.
    /// </remarks>
    private static readonly string[] SkillAnchorNames =
    [
        "Ao lado da skill", "Livre (voce escolhe)", "Centro (topo)",
        "Canto superior esquerdo", "Canto superior direito",
        "Canto inferior esquerdo", "Canto inferior direito",
    ];

    private static ChipCorner Corner(string label, ChipCorner current)
    {
        var index = (int)current;

        return ImGui.Combo(label, ref index, CornerNames, CornerNames.Length)
            ? (ChipCorner)index
            : current;
    }

    /// <summary>
    /// A colour picker that round-trips through the packed int the settings
    /// file stores.
    /// </summary>
    /// <remarks>
    /// ImGui works in floats and the settings store carries ints, so a colour
    /// crosses two representations every time it is touched. Doing the
    /// conversion in one place is what keeps it from drifting a shade per save.
    /// </remarks>
    /// <summary>
    /// The colours worth reaching for, so a choice is a click.
    /// </summary>
    /// <remarks>
    /// A raw RGB picker asks you to build a colour when what you want is to
    /// pick one. These are the shades that read over a dark, busy, moving
    /// background; the full picker stays underneath for the case a swatch does
    /// not cover.
    /// </remarks>
    private static readonly (string Name, uint Colour)[] Swatches =
    [
        ("branco", Palette.Rgba(245, 245, 245)),
        ("pergaminho", Palette.Rgba(200, 220, 230)),
        ("azul", Palette.Rgba(90, 200, 255)),
        ("ciano", Palette.Rgba(70, 230, 255)),
        ("verde", Palette.Rgba(120, 220, 140)),
        ("amarelo", Palette.Rgba(255, 230, 70)),
        ("laranja", Palette.Rgba(255, 160, 40)),
        ("vermelho", Palette.Rgba(255, 90, 90)),
        ("rosa", Palette.Rgba(255, 120, 200)),
        ("roxo", Palette.Rgba(180, 130, 255)),
    ];

    private static int Colour(string label, int packed)
    {
        var chosen = packed;

        ImGui.TextDisabled(label);

        for (var i = 0; i < Swatches.Length; i++)
        {
            var (name, colour) = Swatches[i];

            if (i > 0) ImGui.SameLine();

            ImGui.PushID($"{label}#{i}");
            ImGui.PushStyleColor(ImGuiCol.Button, ToVec4(colour));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ToVec4(colour));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ToVec4(colour));

            if (ImGui.Button("##swatch", new System.Numerics.Vector2(20f, 20f)))
                chosen = unchecked((int)colour);

            ImGui.PopStyleColor(3);
            ImGui.PopID();

            if (ImGui.IsItemHovered()) ImGui.SetTooltip(name);
        }

        return chosen != packed ? chosen : Precise(label, packed);
    }

    private static int Precise(string label, int packed)
    {
        Span<float> rgba = stackalloc float[4];

        Palette.Unpack(unchecked((uint)packed), rgba);

        var value = new System.Numerics.Vector4(rgba[0], rgba[1], rgba[2], rgba[3]);

        // Collapsed by default: the swatches above answer the question most of
        // the time, and an always-open RGB panel per colour buries the rest of
        // the tab.
        if (!ImGui.ColorEdit4($"ajuste fino##{label}", ref value,
                ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.NoInputs)) return packed;

        rgba[0] = value.X;
        rgba[1] = value.Y;
        rgba[2] = value.Z;
        rgba[3] = value.W;

        return unchecked((int)Palette.Pack(rgba));
    }

    /// <summary>
    /// The dev tree, where it can be found rather than remembered.
    /// </summary>
    /// <remarks>
    /// Compact on purpose. Recording is done with a key while the mouse is on
    /// the thing being asked about - going to a panel to click a button means
    /// the mouse is on the button, which is never what anyone wanted to record.
    /// So this names the keys, says where the files go, and shows what the
    /// picker is on; it does not try to be the way the tool is used.
    /// </remarks>
    /// <summary>What to look for. Kept between frames so it can be typed.</summary>
    private static string _needle = string.Empty;

    private static void DrawDevTree(RadarRenderLoop loop)
    {
        // No modifiers anywhere: shift and control are the game's, and holding
        // one to record also does whatever the game does with it.
        ImGui.TextDisabled("F11  grava so o que se relaciona com o que esta sob o cursor:");
        ImGui.TextDisabled("     pais, subarvore, irmaos, bytes e offsets - nada mais");
        ImGui.TextDisabled("F12  grava a arvore inteira, inclusive oculta  (arquivo grande)");
        ImGui.TextDisabled("F8   contorna o que esta sob o cursor");
        ImGui.TextDisabled("saida: .jsonl em 'trees', uma linha por elemento");

        var aiming = loop.Inspecting;

        if (ImGui.Checkbox("Apontador##devtree", ref aiming)) loop.Inspecting = aiming;

        // Searching is the one thing that cannot be a key: it needs a word.
        // "I can see this on screen, where does it live" is the question that
        // found every text offset this project reads, done by hand each time.
        ImGui.SetNextItemWidth(180f);
        ImGui.InputText("##devtreefind", ref _needle, 60);

        ImGui.SameLine();

        if (ImGui.Button("Procurar##devtree") && _needle.Length > 0)
            loop.SaveDump(Core.Diagnostics.DumpOptions.Find(_needle));

        ImGui.TextDisabled("procura texto, inteiro ou float; grava o caminho de cada acerto");

        if (loop.LastDump is { Length: > 0 } last) ImGui.TextDisabled($"ultimo: {last}");

        if (loop.Picked is not { } probe) return;

        ImGui.Separator();

        foreach (var line in probe.Describe()) ImGui.TextUnformatted(line);
    }

    private void DrawLoot()
    {
        var loot = _settings.Loot;

        ImGui.TextDisabled("Preco poe.ninja sobre os drops. A liga vem do proprio jogo.");
        ImGui.Separator();

        var enabled = loot.Enabled;
        if (ImGui.Checkbox("Ativado##loot", ref enabled))
            _settings.Loot = loot with { Enabled = enabled };

        // One floor per bucket, because five Exalted is an unremarkable unique
        // and an extraordinary scroll.
        var uniqueFloor = loot.UniqueMinimumExalted;
        if (ImGui.SliderFloat("Minimo uniques (ex)", ref uniqueFloor, 0f, 100f))
            _settings.Loot = loot with { UniqueMinimumExalted = uniqueFloor };

        var currencyFloor = loot.CurrencyMinimumExalted;
        if (ImGui.SliderFloat("Minimo moedas (ex)", ref currencyFloor, 0f, 100f))
            _settings.Loot = loot with { CurrencyMinimumExalted = currencyFloor };

        var otherFloor = loot.OtherMinimumExalted;
        if (ImGui.SliderFloat("Minimo resto (ex)", ref otherFloor, 0f, 100f))
            _settings.Loot = loot with { OtherMinimumExalted = otherFloor };

        var highlight = loot.HighlightExalted;
        if (ImGui.SliderFloat("Destacar acima de (ex)", ref highlight, 1f, 500f))
            _settings.Loot = loot with { HighlightExalted = highlight };

        var anchored = loot.AnchorValuesToTags;
        if (ImGui.Checkbox("Preco na tag do jogo", ref anchored))
            _settings.Loot = loot with { AnchorValuesToTags = anchored };

        var hover = loot.ShowHoverPrice;
        if (ImGui.Checkbox("Preco sob o cursor (inventario/stash)", ref hover))
            _settings.Loot = loot with { ShowHoverPrice = hover };

        var highlightSlots = loot.HighlightSlots;
        if (ImGui.Checkbox("Destacar itens valiosos nos paineis", ref highlightSlots))
            _settings.Loot = loot with { HighlightSlots = highlightSlots };

        var slotValues = loot.ShowSlotValues;
        if (ImGui.Checkbox("Mostrar o valor dentro do slot", ref slotValues))
            _settings.Loot = loot with { ShowSlotValues = slotValues };

        var reveal = loot.RevealNames;
        if (ImGui.Checkbox("Nome real acima da tag do chao", ref reveal))
            _settings.Loot = loot with { RevealNames = reveal };

        var nameUniques = loot.NameUniques;
        if (ImGui.Checkbox("Escrever o nome do unique junto do preco", ref nameUniques))
            _settings.Loot = loot with { NameUniques = nameUniques };

        var minQuantity = loot.MinQuantity;
        if (ImGui.SliderInt("Anuncios minimos (abaixo disso marca ?)", ref minQuantity, 0, 50))
            _settings.Loot = loot with { MinQuantity = minQuantity };

        ImGui.Separator();
        ImGui.TextDisabled("Posicao dos valores");

        var slotCorner = Corner("Valor no slot", loot.SlotValueCorner);
        if (slotCorner != loot.SlotValueCorner) _settings.Loot = loot with { SlotValueCorner = slotCorner };

        ImGui.Separator();
        ImGui.TextDisabled("Suportes da skill");

        ImGui.TextDisabled("Passe o mouse numa skill para ver os suportes recomendados.");
        ImGui.TextDisabled("Sao recomendacoes do poe2db, nao contagem de builds reais.");

        var skillSupports = loot.ShowSkillSupports;
        if (ImGui.Checkbox("Mostrar suportes##skill", ref skillSupports))
            _settings.Loot = loot with { ShowSkillSupports = skillSupports };

        if (skillSupports)
        {
            var howMany = loot.SkillSupportCount;
            if (ImGui.SliderInt("Quantos##skill", ref howMany, 1, 10))
                _settings.Loot = loot with { SkillSupportCount = howMany };

            if (loot.SkillSupportAnchor == Settings.Loot.SkillSupportAnchor.BesideSkill)
            {
                var away = loot.SkillSupportGap;
                if (ImGui.SliderFloat("Distancia da skill##skillgap", ref away, 0f, 0.6f, "%.2f"))
                    _settings.Loot = loot with { SkillSupportGap = away };
            }

            if (loot.SkillSupportAnchor == Settings.Loot.SkillSupportAnchor.Free)
            {
                var px = loot.SkillSupportX;
                if (ImGui.SliderFloat("Horizontal##skillpos", ref px, 0f, 1f, "%.2f"))
                    _settings.Loot = loot with { SkillSupportX = px };

                var py = loot.SkillSupportY;
                if (ImGui.SliderFloat("Vertical##skillpos", ref py, 0f, 1f, "%.2f"))
                    _settings.Loot = loot with { SkillSupportY = py };
            }

            var boxes = loot.DebugSkillCaptions;
            if (ImGui.Checkbox("Debug: desenhar as caixas dos rotulos##skill", ref boxes))
                _settings.Loot = loot with { DebugSkillCaptions = boxes };

            var where = (int)loot.SkillSupportAnchor;
            if (ImGui.Combo("Onde##skill", ref where, SkillAnchorNames, SkillAnchorNames.Length))
                _settings.Loot = loot with
                {
                    SkillSupportAnchor = (Settings.Loot.SkillSupportAnchor)where,
                };
        }

        ImGui.Separator();
        ImGui.TextDisabled("Analises");

        ImGui.TextDisabled("Mostra na tela do jogo o que uma analise precisa que voce faca,");
        ImGui.TextDisabled("com a contagem do tempo restante.");

        var prompts = _settings.General.ShowProbePrompts;
        if (ImGui.Checkbox("Pedidos na tela##probe", ref prompts))
            _settings.General = _settings.General with { ShowProbePrompts = prompts };

        ImGui.Separator();
        ImGui.TextDisabled("Tier dos mods");

        ImGui.TextDisabled("Marca o slot quando o item tem um roll bom. Sem passar o mouse.");

        var tiers = loot.ShowModTiers;
        if (ImGui.Checkbox("Marcar os slots##tier", ref tiers))
            _settings.Loot = loot with { ShowModTiers = tiers };

        var onTooltip = loot.ModTierOnTooltip;
        if (ImGui.Checkbox("Mostrar no inicio da linha do item##tier", ref onTooltip))
            _settings.Loot = loot with { ModTierOnTooltip = onTooltip };

        var alert = loot.ModTierAlert;
        if (ImGui.SliderInt("Destacar ate T##tier", ref alert, 1, 8))
            _settings.Loot = loot with { ModTierAlert = alert };

        var tierCorner = Corner("Canto da marca##tier", loot.ModTierCorner);
        if (tierCorner != loot.ModTierCorner) _settings.Loot = loot with { ModTierCorner = tierCorner };

        var tierScale = loot.ModTierScale;
        if (ImGui.SliderFloat("Tamanho##tier", ref tierScale, 0.5f, 2.5f, "%.2f"))
            _settings.Loot = loot with { ModTierScale = tierScale };

        // A colour per tier, because which one earns a glance is personal and
        // depends on the tileset it is drawn over.
        var t1 = Colour("T1##tier", loot.ModTier1Colour);
        if (t1 != loot.ModTier1Colour) _settings.Loot = loot with { ModTier1Colour = t1 };

        var t2 = Colour("T2##tier", loot.ModTier2Colour);
        if (t2 != loot.ModTier2Colour) _settings.Loot = loot with { ModTier2Colour = t2 };

        var t3 = Colour("T3##tier", loot.ModTier3Colour);
        if (t3 != loot.ModTier3Colour) _settings.Loot = loot with { ModTier3Colour = t3 };

        ImGui.Separator();

        var hoverCorner = Corner("Valor sob o cursor", loot.HoverCorner);
        if (hoverCorner != loot.HoverCorner) _settings.Loot = loot with { HoverCorner = hoverCorner };

        ImGui.Separator();
        ImGui.TextDisabled("Cores dos destaques");

        // A legend, because the tiers answer different questions and a colour
        // that needs explaining in chat is a colour that needs explaining here.
        Legend(loot.RichColour, "acima do limite de destaque");
        Legend(loot.PricedColour, "tem preco, acima do piso");
        Legend(loot.UnknownColour, "unique sem preco — pode valer qualquer coisa");

        var uniqueName = Colour("Nome de unique no chao", loot.UniqueNameColour);
        if (uniqueName != loot.UniqueNameColour)
            _settings.Loot = loot with { UniqueNameColour = uniqueName };

        var revealColour = Colour("Nome sem raridade conhecida", loot.RevealColour);
        if (revealColour != loot.RevealColour) _settings.Loot = loot with { RevealColour = revealColour };

        ImGui.Spacing();

        var rich = Colour("Muito valioso", loot.RichColour);
        if (rich != loot.RichColour) _settings.Loot = loot with { RichColour = rich };

        var priced = Colour("Tem preco", loot.PricedColour);
        if (priced != loot.PricedColour) _settings.Loot = loot with { PricedColour = priced };

        var unknown = Colour("Unique sem preco", loot.UnknownColour);
        if (unknown != loot.UnknownColour) _settings.Loot = loot with { UnknownColour = unknown };

        ImGui.Separator();

        var scale = loot.TextScale;
        if (ImGui.SliderFloat("Tamanho do texto", ref scale, 1f, 4f))
            _settings.Loot = loot with { TextScale = scale };

        ImGui.Separator();
        ImGui.TextDisabled("Categorias");

        var currency = loot.ShowCurrency;
        if (ImGui.Checkbox("Moedas, essencias e runas", ref currency))
            _settings.Loot = loot with { ShowCurrency = currency };

        var uniques = loot.ShowUniques;
        if (ImGui.Checkbox("Unicos", ref uniques))
            _settings.Loot = loot with { ShowUniques = uniques };

        var gems = loot.ShowGems;
        if (ImGui.Checkbox("Gemas", ref gems))
            _settings.Loot = loot with { ShowGems = gems };

        var other = loot.ShowOther;
        if (ImGui.Checkbox("Outros", ref other))
            _settings.Loot = loot with { ShowOther = other };

        if (_prices is null) return;

        ImGui.Separator();
        ImGui.TextDisabled("estado");

        static void Row(string label, string value)
        {
            ImGui.TextDisabled(label);
            ImGui.SameLine(190);
            ImGui.TextUnformatted(value);
        }

        Row("liga", _prices.League is { Length: > 0 } ? _prices.League : "detectando");
        Row("precos", _prices.IsLoaded ? $"{_prices.ItemCount} itens" : _prices.Status);
        Row("ex por divine", $"{_prices.ExPerDivine:0.#}");
    }

    /// <summary>Health bars over monsters.</summary>
    private void DrawHpBars()
    {
        var bars = _settings.HpBars;

        ImGui.TextDisabled("Barras sobre os monstros, no mundo. Aparecem com o mapa aberto ou fechado.");
        ImGui.Separator();

        var enabled = bars.Enabled;
        if (ImGui.Checkbox("Ativado##hp", ref enabled))
            _settings.HpBars = bars with { Enabled = enabled };

        var normal = bars.ShowNormal;
        if (ImGui.Checkbox("Normais", ref normal))
            _settings.HpBars = bars with { ShowNormal = normal };

        if (normal) ImGui.TextDisabled("Uma barra em cada lixo é a tela, não informação.");

        var magic = bars.ShowMagic;
        if (ImGui.Checkbox("Magicos", ref magic))
            _settings.HpBars = bars with { ShowMagic = magic };

        var rare = bars.ShowRare;
        if (ImGui.Checkbox("Raros", ref rare))
            _settings.HpBars = bars with { ShowRare = rare };

        var unique = bars.ShowUnique;
        if (ImGui.Checkbox("Unicos", ref unique))
            _settings.HpBars = bars with { ShowUnique = unique };

        ImGui.Separator();

        var height = bars.Height;
        if (ImGui.SliderFloat("Altura", ref height, 2f, 14f))
            _settings.HpBars = bars with { Height = height };

        var offset = bars.OffsetY;
        if (ImGui.SliderFloat("Distancia acima", ref offset, -80f, 0f))
            _settings.HpBars = bars with { OffsetY = offset };
    }

    private void DrawAutoPotion()
    {
        var options = _settings.AutoPotion;
        var potion = _loop?.Potion;

        var armed = options.Enabled;

        ImGui.TextColored(
            armed ? new System.Numerics.Vector4(0.4f, 1f, 0.5f, 1f) : new System.Numerics.Vector4(1f, 0.5f, 0.4f, 1f),
            armed ? "AUTO POTION: ON" : "AUTO POTION: OFF");

        ImGui.TextDisabled("F8 liga e desliga dentro do jogo.");

        if (potion is not null)
        {
            ImGui.SameLine(240);
            ImGui.TextDisabled(potion.Status);
        }

        ImGui.Separator();

        var enabled = options.Enabled;
        if (ImGui.Checkbox("Ativado", ref enabled))
            _settings.AutoPotion = options with { Enabled = enabled };

        var dry = options.DryRun;
        if (ImGui.Checkbox("Simulacao (nao aperta tecla)", ref dry))
            _settings.AutoPotion = options with { DryRun = dry };

        ImGui.Separator();
        ImGui.TextDisabled("Vida");

        var life = options.LifeEnabled;
        if (ImGui.Checkbox("Ativado##life", ref life))
            _settings.AutoPotion = options with { LifeEnabled = life };

        var mode = (int)options.LifeMode;
        if (ImGui.Combo("Observa##life", ref mode, "Vida Escudo Qualquer um "))
            _settings.AutoPotion = options with { LifeMode = (Settings.AutoPotion.LifeFlaskMode)mode };

        var lifeThreshold = options.LifeThresholdPercent;
        if (ImGui.SliderFloat("Limite de vida %", ref lifeThreshold, 5f, 95f))
            _settings.AutoPotion = options with { LifeThresholdPercent = lifeThreshold };

        var esThreshold = options.EnergyShieldThresholdPercent;
        if (ImGui.SliderFloat("Limite de escudo %", ref esThreshold, 5f, 95f))
            _settings.AutoPotion = options with { EnergyShieldThresholdPercent = esThreshold };

        var lifeCooldown = options.LifeCooldownMs;
        if (ImGui.SliderInt("Recarga de vida (ms)", ref lifeCooldown, 250, 10000))
            _settings.AutoPotion = options with { LifeCooldownMs = lifeCooldown };

        ImGui.Separator();
        ImGui.TextDisabled("Mana");

        var mana = options.ManaEnabled;
        if (ImGui.Checkbox("Ativado##mana", ref mana))
            _settings.AutoPotion = options with { ManaEnabled = mana };

        var manaThreshold = options.ManaThresholdPercent;
        if (ImGui.SliderFloat("Limite de mana %", ref manaThreshold, 5f, 95f))
            _settings.AutoPotion = options with { ManaThresholdPercent = manaThreshold };

        var manaCooldown = options.ManaCooldownMs;
        if (ImGui.SliderInt("Recarga de mana (ms)", ref manaCooldown, 250, 10000))
            _settings.AutoPotion = options with { ManaCooldownMs = manaCooldown };

        if (potion is null) return;

        ImGui.Separator();
        ImGui.TextDisabled("estado");

        static void Row(string label, string value)
        {
            ImGui.TextDisabled(label);
            ImGui.SameLine(190);
            ImGui.TextUnformatted(value);
        }

        Row("frasco de vida", potion.LifeCooldownLeft > TimeSpan.Zero
            ? $"recarregando {potion.LifeCooldownLeft.TotalSeconds:0.0}s"
            : "pronto");

        Row("frasco de mana", potion.ManaCooldownLeft > TimeSpan.Zero
            ? $"recarregando {potion.ManaCooldownLeft.TotalSeconds:0.0}s"
            : "pronto");

        Row("disparos", $"{potion.LifePresses} vida, {potion.ManaPresses} mana");
    }

    private void DrawNativeMap()
    {
        var map = _settings.NativeMap;

        ImGui.TextDisabled("Desenha sobre o mapa do proprio jogo. Abra com Tab.");
        ImGui.Separator();

        var enabled = map.Enabled;
        if (ImGui.Checkbox("Ativado##nativemap", ref enabled))
            _settings.NativeMap = map with { Enabled = enabled };

        var terrain = map.ShowTerrain;
        if (ImGui.Checkbox("Terrain", ref terrain))
            _settings.NativeMap = map with { ShowTerrain = terrain };

        var player = map.ShowPlayer;
        if (ImGui.Checkbox("Player##nativemap", ref player))
            _settings.NativeMap = map with { ShowPlayer = player };

        if (player)
        {
            ImGui.Indent();

            var playerColour = Colour("Cor do seu ponto##mapplayer", map.PlayerColour);
            if (playerColour != map.PlayerColour)
                _settings.NativeMap = map with { PlayerColour = playerColour };

            ImGui.Unindent();
        }

        var entities = map.ShowEntities;
        if (ImGui.Checkbox("Entidades##nativemap", ref entities))
            _settings.NativeMap = map with { ShowEntities = entities };

        ImGui.Separator();
        ImGui.TextDisabled("Categorias");

        var monsters = map.ShowMonsters;
        if (ImGui.Checkbox("Inimigos", ref monsters))
            _settings.NativeMap = map with { ShowMonsters = monsters };

        if (monsters)
        {
            ImGui.Indent();

            // The rank names no longer carry a colour in brackets. They used to
            // read "Magicos (azul)", which was true only while the colour was
            // not yours to change.
            var normal = map.ShowNormalMonsters;
            if (ImGui.Checkbox("Normais", ref normal))
                _settings.NativeMap = map with { ShowNormalMonsters = normal };

            var normalColour = Colour("Cor##monnormal", map.MonsterNormalColour);
            if (normalColour != map.MonsterNormalColour)
                _settings.NativeMap = map with { MonsterNormalColour = normalColour };

            var magic = map.ShowMagicMonsters;
            if (ImGui.Checkbox("Magicos", ref magic))
                _settings.NativeMap = map with { ShowMagicMonsters = magic };

            var magicColour = Colour("Cor##monmagic", map.MonsterMagicColour);
            if (magicColour != map.MonsterMagicColour)
                _settings.NativeMap = map with { MonsterMagicColour = magicColour };

            var rare = map.ShowRareMonsters;
            if (ImGui.Checkbox("Raros", ref rare))
                _settings.NativeMap = map with { ShowRareMonsters = rare };

            var rareColour = Colour("Cor##monrare", map.MonsterRareColour);
            if (rareColour != map.MonsterRareColour)
                _settings.NativeMap = map with { MonsterRareColour = rareColour };

            var unique = map.ShowUniqueMonsters;
            if (ImGui.Checkbox("Unicos / chefes", ref unique))
                _settings.NativeMap = map with { ShowUniqueMonsters = unique };

            var uniqueColour = Colour("Cor##monunique", map.MonsterUniqueColour);
            if (uniqueColour != map.MonsterUniqueColour)
                _settings.NativeMap = map with { MonsterUniqueColour = uniqueColour };

            ImGui.Unindent();
        }

        var dead = map.ShowDeadMonsters;
        if (ImGui.Checkbox("Inimigos mortos", ref dead))
            _settings.NativeMap = map with { ShowDeadMonsters = dead };

        var allies = map.ShowAllies;
        if (ImGui.Checkbox("Minions", ref allies))
            _settings.NativeMap = map with { ShowAllies = allies };

        if (allies)
        {
            ImGui.Indent();

            var allyColour = Colour("Cor dos minions##mapally", map.AllyColour);
            if (allyColour != map.AllyColour)
                _settings.NativeMap = map with { AllyColour = allyColour };

            ImGui.Unindent();
        }

        var npcs = map.ShowNpcs;
        if (ImGui.Checkbox("NPCs", ref npcs))
            _settings.NativeMap = map with { ShowNpcs = npcs };

        var unvisited = map.ShowUnvisited;
        if (ImGui.Checkbox("Marcar o chao onde voce ainda nao foi", ref unvisited))
            _settings.NativeMap = map with { ShowUnvisited = unvisited };

        var visitedColour = Colour("Chao ja percorrido", map.VisitedColour);
        if (visitedColour != map.VisitedColour)
            _settings.NativeMap = map with { VisitedColour = visitedColour };

        var unvisitedColour = Colour("Chao nao percorrido", map.UnvisitedColour);
        if (unvisitedColour != map.UnvisitedColour)
            _settings.NativeMap = map with { UnvisitedColour = unvisitedColour };

        var strength = map.UnvisitedStrength;
        if (ImGui.SliderFloat("Intensidade do vermelho", ref strength, 0f, 1f))
            _settings.NativeMap = map with { UnvisitedStrength = strength };

        var radius = map.VisitRadius;
        if (ImGui.SliderInt("Alcance do que conta como visto", ref radius, 10, 120))
            _settings.NativeMap = map with { VisitRadius = radius };

        ImGui.Separator();

        var marked = map.OnlyMarkedChests;
        if (ImGui.Checkbox("So baus que o jogo marca (esconde vasos e caixotes)", ref marked))
            _settings.NativeMap = map with { OnlyMarkedChests = marked };

        var chests = map.ShowChests;
        if (ImGui.Checkbox("Baus", ref chests))
            _settings.NativeMap = map with { ShowChests = chests };

        var transitions = map.ShowTransitions;
        if (ImGui.Checkbox("Transicoes", ref transitions))
            _settings.NativeMap = map with { ShowTransitions = transitions };

        var pois = map.ShowPois;
        if (ImGui.Checkbox("Pontos de interesse", ref pois))
            _settings.NativeMap = map with { ShowPois = pois };

        if (pois) ImGui.TextDisabled("Waypoints, checkpoints, portais — marcados pelo proprio jogo.");

        if (transitions)
        {
            ImGui.Indent();

            var names = map.ShowTransitionNames;
            if (ImGui.Checkbox("Mostrar o destino", ref names))
                _settings.NativeMap = map with { ShowTransitionNames = names };

            ImGui.Unindent();
        }

        var others = map.ShowOtherPlayers;
        if (ImGui.Checkbox("Outros jogadores", ref others))
            _settings.NativeMap = map with { ShowOtherPlayers = others };

        var mechanics = map.ShowMechanics;
        if (ImGui.Checkbox("Mecanicas de liga", ref mechanics))
            _settings.NativeMap = map with { ShowMechanics = mechanics };

        var threats = map.ShowThreats;
        if (ImGui.Checkbox("Marcar mods perigosos", ref threats))
            _settings.NativeMap = map with { ShowThreats = threats };

        var landmarks = map.ShowLandmarks;
        if (ImGui.Checkbox("Locais nomeados", ref landmarks))
            _settings.NativeMap = map with { ShowLandmarks = landmarks };

        var raw = map.ShowRawEntities;
        if (ImGui.Checkbox("Entidades cruas (debug)", ref raw))
            _settings.NativeMap = map with { ShowRawEntities = raw };

        if (raw) ImGui.TextDisabled("Centenas de pontos. Ferramenta de debug, nao apresentacao.");

        ImGui.Separator();
        ImGui.TextDisabled("Movimento");

        var smooth = map.SmoothMovement;
        if (ImGui.Checkbox("Suavizar movimento dos inimigos", ref smooth))
            _settings.NativeMap = map with { SmoothMovement = smooth };

        ImGui.TextDisabled("Apenas os inimigos. O mapa e o player nunca sao suavizados.");

        var opacity = map.TerrainOpacity;
        if (ImGui.SliderFloat("Opacidade do terrain", ref opacity, 0.1f, 1f))
            _settings.NativeMap = map with { TerrainOpacity = opacity };

        ImGui.Separator();
        ImGui.TextDisabled("estado");
        ImGui.SameLine(190);
        ImGui.TextUnformatted(_map.Status);
    }

    private void DrawPlayer()
    {
        var player = _settings.Player;

        var enabled = player.Enabled;
        if (ImGui.Checkbox("Ativado##player", ref enabled))
            _settings.Player = player with { Enabled = enabled };

        var markerColour = Colour("Cor do seu marcador no mundo##playermarker", player.MarkerColour);
        if (markerColour != player.MarkerColour)
            _settings.Player = player with { MarkerColour = markerColour };

        var marker = player.ShowMarker;
        if (ImGui.Checkbox("Marcador", ref marker))
            _settings.Player = player with { ShowMarker = marker };

        var name = player.ShowName;
        if (ImGui.Checkbox("Nome", ref name))
            _settings.Player = player with { ShowName = name };

        var level = player.ShowLevel;
        if (ImGui.Checkbox("Nivel", ref level))
            _settings.Player = player with { ShowLevel = level };

        var vitals = player.ShowVitals;
        if (ImGui.Checkbox("Vida / mana / ES", ref vitals))
            _settings.Player = player with { ShowVitals = vitals };

        var anchor = (int)player.Anchor;
        if (ImGui.Combo("Ancoragem", ref anchor, "No personagem\0HUD fixo\0"))
            _settings.Player = player with { Anchor = (PlayerAnchor)anchor };
    }

    private void DrawDebug(RadarRenderLoop loop, RadarStats stats)
    {
        // A button as well as the hotkey. The capture itself is proven — it
        // returns a path and writes a file — so when F9 produces nothing the
        // question is whether the key was seen, and a button that works while
        // the key does not answers that in one press.
        ImGui.Text($"prints salvos nesta sessao: {stats.Screenshots}");

        if (ImGui.Button("Salvar print agora"))
        {
            var saved = Overlay.ScreenCapture.Save(
                Path.Combine(AppContext.BaseDirectory, "prints"));

            if (saved is not null) stats.Screenshots++;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("ou F9 a qualquer momento");

        ImGui.Separator();

        var debug = _settings.DebugEntities;

        var enabled = debug.Enabled;
        if (ImGui.Checkbox("Ativado##debug", ref enabled))
            _settings.DebugEntities = debug with { Enabled = enabled };

        var marker = debug.ShowMarker;
        if (ImGui.Checkbox("Marcador##debug", ref marker))
            _settings.DebugEntities = debug with { ShowMarker = marker };

        var distance = debug.ShowDistance;
        if (ImGui.Checkbox("Distancia", ref distance))
            _settings.DebugEntities = debug with { ShowDistance = distance };

        var metadata = debug.ShowMetadata;
        if (ImGui.Checkbox("Metadata", ref metadata))
            _settings.DebugEntities = debug with { ShowMetadata = metadata };

        var id = debug.ShowEntityId;
        if (ImGui.Checkbox("EntityId", ref id))
            _settings.DebugEntities = debug with { ShowEntityId = id };

        var components = debug.ShowComponentNames;
        if (ImGui.Checkbox("Componentes", ref components))
            _settings.DebugEntities = debug with { ShowComponentNames = components };

        ImGui.Separator();

        var diagnostics = debug.ShowDiagnostics;
        if (ImGui.Checkbox("Diagnostico", ref diagnostics))
            _settings.DebugEntities = debug with { ShowDiagnostics = diagnostics };

        if (!diagnostics) return;

        ImGui.Spacing();
        DrawDiagnostics(loop, stats);
    }

    private void DrawDiagnostics(RadarRenderLoop loop, RadarStats stats)
    {
        var window = loop.GameWindow;

        Row("render", $"{stats.RenderFps:0} fps   {stats.FrameMilliseconds:0.00} ms");
        Row("captura", $"{stats.CaptureHz:0.0} Hz");
        Row("idade do snapshot", stats.SnapshotAge == TimeSpan.MaxValue
            ? "-"
            : $"{stats.SnapshotAge.TotalMilliseconds:0} ms");
        Row("projecao", $"{stats.ProjectionMicroseconds:0} us");

        ImGui.Separator();

        ImGui.TextDisabled("mapa nativo");
        Row("terrain", stats.TerrainWidth > 0 ? $"{stats.TerrainWidth} x {stats.TerrainHeight}" : "-");
        Row("builds de textura", $"{_map.TerrainBuilds} ({_map.TerrainBuildMilliseconds:0} ms)");
        Row("escala", $"{stats.MapScale:0.000} px/celula");
        Row("centro", $"{stats.MapCentre.X:0}, {stats.MapCentre.Y:0}");
        Row("entidades no mapa", $"{stats.MapEntitiesDrawn} de {stats.MapEntitiesReceived}");
        Row("ignoradas", $"{stats.MapEntitiesIgnored} categoria, {stats.MapEntitiesOutside} fora da tela");
        Row("inimigos", $"{stats.MapNormal} normal, {stats.MapMagic} magico, {stats.MapRare} raro, {stats.MapUnique} unico");
        Row("movimento", $"{stats.MapInterpolated} interpolados, {stats.MapSnapped} diretos, {stats.MapTracked} rastreados");
        Row("pontos de interesse", $"{stats.MapPois} marcados, {stats.MapPoisDrawn} desenhados, {stats.MapPoisUnplaced} sem posicao");
        Row("locais nomeados", $"{stats.MapLandmarks} na area, {stats.MapLandmarksDrawn} na tela");
        Row("ameacas", stats.MapThreats.ToString());
        Row("rotas", stats.MapRoutes.ToString());
        Row("saidas lembradas", stats.MapRemembered.ToString());
        Row("trilha no mundo", $"{stats.WorldRouteWaypoints} pontos");
        Row("barras de vida", $"{stats.HpBars} ({stats.HpBarThreats} com ameaca)");
        Row("drops precificados", stats.LootPriced.ToString());
        Row("icones em cache", Rendering.IconCache.Count.ToString());

        ImGui.Spacing();
        ImGui.TextDisabled("fast lane");
        Row("map frame", $"{stats.MapFrameHz:0} Hz   {stats.MapFrameMilliseconds:0.00} ms");
        Row("falhas", stats.MapFrameFailures.ToString());

        ImGui.Spacing();
        ImGui.TextDisabled("preco no chao");
        Row("etiquetas do jogo", stats.LootTags.ToString());
        Row("casadas com item", stats.LootTagsMatched.ToString());
        Row("sem preco", stats.LootTagsUnpriced.ToString());
        Row("abaixo do minimo", stats.LootTagsBelowFloor.ToString());
        Row("categoria desligada", stats.LootTagsFiltered.ToString());
        Row("desenhadas", stats.LootPriced.ToString());

        if (stats.LootTags == 0)
            ImGui.TextDisabled("  zero etiquetas = nao ha loot no chao agora.");

        ImGui.Spacing();
        ImGui.TextDisabled("world hud");
        Row("entidades recebidas", stats.EntitiesReceived.ToString());
        Row("desenhadas", stats.EntitiesRendered.ToString());
        Row("fora da tela", stats.OffScreen.ToString());
        Row("atras da camera", stats.BehindCamera.ToString());
        Row("projecao invalida", stats.InvalidProjection.ToString());


        ImGui.Separator();

        Row("janela do jogo", window.Status.ToString());
        Row("client area", loop.Placement.Bounds.ToString());
        Row("dpi", $"{window.Dpi} ({window.DpiScale:0.00}x)");
        Row("cache de rotulos", $"{_debug.CachedLabels} entradas, {_debug.CacheInvalidations} trocas de epoch");

        if (loop.ViewportMismatch is { } mismatch)
        {
            ImGui.Spacing();
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.45f, 0.45f, 1f), mismatch);
            ImGui.TextDisabled("Nada foi reescalado. A causa precisa ser entendida antes.");
        }

        static void Row(string label, string value)
        {
            ImGui.TextDisabled(label);
            ImGui.SameLine(190);
            ImGui.TextUnformatted(value);
        }
    }
}
