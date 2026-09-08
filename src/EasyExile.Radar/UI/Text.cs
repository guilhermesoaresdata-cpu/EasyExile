using EasyExile.Radar.Settings.General;

namespace EasyExile.Radar.UI;

/// <summary>
/// The interface in whichever language is chosen.
/// </summary>
/// <remarks>
/// The Portuguese IS the key. Inventing three hundred identifiers and then
/// maintaining the mapping between them and the words on screen is work that
/// buys nothing here: there is one interface, written once, and a table from
/// what it says to what it should say in English is the whole job.
///
/// It also means a string nobody translated still renders - in Portuguese,
/// visibly wrong to a reader expecting English rather than blank or crashed.
/// The test is what makes that a temporary state instead of a permanent one: it
/// reads the panel's own source and fails on any line the table has not got.
///
/// ImGui identity is preserved. A label like "Ativado##loot" is two things at
/// once - the words and the widget's identity - and translating the whole
/// string would give the widget a new identity in each language, losing its
/// state on every switch.
/// </remarks>
public static class Text
{
    /// <summary>What the interface is currently speaking.</summary>
    public static Language Current { get; set; } = Language.PtBr;

    /// <summary>
    /// This text, translated, with any ImGui identity left intact.
    /// </summary>
    public static string T(string source)
    {
        if (Current == Language.PtBr) return source;

        var mark = source.IndexOf("##", StringComparison.Ordinal);

        if (mark < 0) return English.GetValueOrDefault(source, source);

        var words = source[..mark];
        var id = source[mark..];

        return words.Length == 0
            ? source
            : English.GetValueOrDefault(words, words) + id;
    }

    /// <summary>Whether this piece of text has an English form.</summary>
    public static bool Knows(string source)
    {
        var mark = source.IndexOf("##", StringComparison.Ordinal);
        var words = mark < 0 ? source : source[..mark];

        return words.Length == 0 || English.ContainsKey(words);
    }

    /// <summary>
    /// Every phrase the interface can say, and its English.
    /// </summary>
    /// <remarks>
    /// Public so the completeness test can hold it to the panel's own source.
    /// "One hundred percent" is a claim about three hundred strings, which is
    /// not a claim anyone can check by reading.
    /// </remarks>
    public static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        { "     pais, subarvore, irmaos, bytes e offsets - nada mais", "     parents, subtree, siblings, bytes and offsets - nothing else" },
        { "  **       ajuda a build de duas formas", "  **       helps the chosen build in two ways" },
        { "  +F +C    fecha resist que falta: F=Fire C=Cold L=Lightning X=Chaos", "  +F +C    fills a missing resist: F=Fire C=Cold L=Lightning X=Chaos" },
        { "  -  ", "  -  " },
        { "  1,18 ex  preco estimado", "  1,18 ex  estimated price" },
        { "  Mi Pj    ajuda a build escolhida (Minion, Projectile...)", "  Mi Pj    helps the chosen build (Minion, Projectile...)" },
        { "  P1 S1    melhor roll: P=prefixo S=sufixo, numero=tier", "  P1 S1    best roll: P=prefix S=suffix, number=tier" },
        { "  opt  ", "  opt  " },
        { "  zero etiquetas = nao ha loot no chao agora.", "  zero labels = there is no loot on the ground right now." },
        { "A taxa de renderizacao e independente da captura.", "The render rate is independent of the capture rate." },
        { "Alcance do que conta como visto", "How far counts as seen" },
        { "Altura", "Height" },
        { "Analises", "Analysis" },
        { "Ancoragem", "Anchor" },
        { "Anota o que cada zona REALMENTE tinha, para corrigir o guia depois.", "Records what each zone REALLY had, to correct the guide later." },
        { "Anuncios minimos (abaixo disso marca ?)", "Minimum listings (below this it marks ?)" },
        { "Apenas os inimigos. O mapa e o player nunca sao suavizados.", "Monsters only. The map and the player are never smoothed." },
        { "Apontador", "Picker" },
        { "Ativado", "Enabled" },
        { "Barras sobre os monstros, no mundo. Aparecem com o mapa aberto ou fechado.", "Bars over monsters, in the world. Shown with the map open or closed." },
        { "Baus", "Chests" },
        { "Captura ativa", "Capturing" },
        { "Capturas por segundo", "Captures per second" },
        { "Categorias", "Categories" },
        { "Categorias e estado", "Categories and status" },
        { "Centenas de pontos. Ferramenta de debug, nao apresentacao.", "Hundreds of dots. A debugging tool, not a display." },
        { "Componentes", "Components" },
        { "Cores", "Colours" },
        { "Debug", "Debug" },
        { "Debug: desenhar as caixas dos rotulos", "Debug: draw the caption boxes" },
        { "Desenha sobre o mapa do proprio jogo. Abra com Tab.", "Draws on the game's own map. Open it with Tab." },
        { "Desenhar rotas", "Draw routes" },
        { "Destacar acima de (ex)", "Highlight above (ex)" },
        { "Destacar ate T", "Highlight up to T" },
        { "Destacar itens valiosos nos paineis", "Highlight valuable items in panels" },
        { "DevTree", "DevTree" },
        { "Diagnostico", "Diagnostics" },
        { "Distancia", "Distance" },
        { "Distancia acima", "Distance above" },
        { "Distancia da skill", "Distance from the skill" },
        { "EasyExile", "EasyExile" },
        { "Entidades", "Entities" },
        { "Entidades cruas (debug)", "Raw entities (debug)" },
        { "EntityId", "EntityId" },
        { "Escolha um destino. A rota aparece no mapa do jogo.", "Pick a destination. The route appears on the game's map." },
        { "Escrever o nome do unique junto do preco", "Write the unique's name next to the price" },
        { "F11  grava so o que se relaciona com o que esta sob o cursor:", "F11  records only what relates to what is under the cursor:" },
        { "F12  grava a arvore inteira, inclusive oculta  (arquivo grande)", "F12  records the whole tree, hidden included  (large file)" },
        { "F8   contorna o que esta sob o cursor", "F8   outlines what is under the cursor" },
        { "F8 liga e desliga dentro do jogo.", "F8 toggles it from inside the game." },
        { "Gemas", "Gems" },
        { "Geral", "General" },
        { "Gravar a rota enquanto joga", "Record the route while playing" },
        { "Gravar campaign-journal.txt", "Write campaign-journal.txt" },
        { "HUD", "HUD" },
        { "Horizontal", "Horizontal" },
        { "Incluir os opcionais", "Include the optional ones" },
        { "Inimigos", "Monsters" },
        { "Inimigos mortos", "Dead monsters" },
        { "Intensidade do vermelho", "Strength of the red" },
        { "Legenda das marcas no canto do item:", "Legend for the marks in the item's corner:" },
        { "Leveling", "Levelling" },
        { "Limite de escudo %", "Energy shield threshold %" },
        { "Limite de mana %", "Mana threshold %" },
        { "Limite de vida %", "Life threshold %" },
        { "Limpar tudo", "Clear everything" },
        { "Locais nomeados", "Named places" },
        { "Loot", "Loot" },
        { "Magicos", "Magic" },
        { "Mana", "Mana" },
        { "Mapa", "Map" },
        { "Marca itens com mods que ajudam o que voce esta montando.", "Marks items with mods that help what you are building." },
        { "Marca itens que fecham uma resist que ainda nao esta capada.", "Marks items that fill a resistance that is not capped yet." },
        { "Marca o slot quando o item tem um roll bom. Sem passar o mouse.", "Marks the slot when an item has a good roll. Without hovering." },
        { "Marcador", "Marker" },
        { "Marcar mods perigosos", "Mark dangerous mods" },
        { "Marcar o chao onde voce ainda nao foi", "Mark ground you have not walked yet" },
        { "Marcar o que falta", "Mark what is missing" },
        { "Marcar os slots", "Mark the slots" },
        { "Marcas nos itens", "Marks on items" },
        { "Maximo de entidades", "Maximum entities" },
        { "Mecanicas de liga", "League mechanics" },
        { "Metadata", "Metadata" },
        { "Minimo moedas (ex)", "Minimum currency (ex)" },
        { "Minimo resto (ex)", "Minimum for the rest (ex)" },
        { "Minimo uniques (ex)", "Minimum uniques (ex)" },
        { "Minions", "Minions" },
        { "Moedas, essencias e runas", "Currency, essences and runes" },
        { "Mostra na tela do jogo o que uma analise precisa que voce faca,", "Shows on the game screen what an analysis needs you to do," },
        { "Mostrar com o jogo fora de foco", "Show while the game is not focused" },
        { "Mostrar no inicio da linha do item", "Show at the start of the item's line" },
        { "Mostrar o destino", "Show the destination" },
        { "Mostrar o valor dentro do slot", "Show the value inside the slot" },
        { "Mostrar os passos por cima do jogo", "Show the steps over the game" },
        { "Mostrar suportes", "Show supports" },
        { "Movimento", "Movement" },
        { "NPCs", "NPCs" },
        { "Nada foi reescalado. A causa precisa ser entendida antes.", "Nothing was rescaled. The cause has to be understood first." },
        { "Nenhum destino nesta area ainda.", "No destination in this area yet." },
        { "Nivel", "Level" },
        { "Nome", "Name" },
        { "Nome do destino na trilha", "Destination name on the trail" },
        { "Nome real acima da tag do chao", "Real name above the ground label" },
        { "Normais", "Normal" },
        { "O guia usa o mesmo sistema de rota: desenha o caminho ate a saida certa.", "The guide uses the same route system: it draws the path to the right exit." },
        { "Observa", "Watching" },
        { "Onde", "Where" },
        { "Opacidade do fundo", "Background opacity" },
        { "Opacidade do terrain", "Terrain opacity" },
        { "Outros", "Other" },
        { "Outros jogadores", "Other players" },
        { "Passe o mouse numa skill para ver os suportes recomendados.", "Hover a skill to see the recommended supports." },
        { "Passos na tela", "Steps on screen" },
        { "Pedidos na tela", "Requests on screen" },
        { "Player", "Player" },
        { "Pontos de interesse", "Points of interest" },
        { "Posicao dos valores", "Position of the values" },
        { "Poção", "Flask" },
        { "Preco na tag do jogo", "Price on the game's own label" },
        { "Preco poe.ninja sobre os drops. A liga vem do proprio jogo.", "poe.ninja prices over drops. The league comes from the game itself." },
        { "Preco sob o cursor (inventario/stash)", "Price under the cursor (inventory/stash)" },
        { "Precos", "Prices" },
        { "Procurar", "Search" },
        { "Quadros por segundo do overlay", "Overlay frames per second" },
        { "Quantos", "How many" },
        { "Quantos passos", "How many steps" },
        { "Raros", "Rare" },
        { "Recarga de mana (ms)", "Mana recharge (ms)" },
        { "Recarga de vida (ms)", "Life recharge (ms)" },
        { "Resistencias", "Resistances" },
        { "Rotas", "Routes" },
        { "Rotear a mecanica da liga ao chegar", "Route to the league mechanic on arrival" },
        { "Rotear automaticamente para o proximo passo", "Route automatically to the next step" },
        { "Rotear saidas ao chegar", "Route to the exits on arrival" },
        { "Salvar print agora", "Save a screenshot now" },
        { "Sao recomendacoes do poe2db, nao contagem de builds reais.", "They are poe2db recommendations, not a count of real builds." },
        { "Simulacao (nao aperta tecla)", "Simulation (does not press the key)" },
        { "So baus que o jogo marca (esconde vasos e caixotes)", "Only chests the game marks (hides vases and crates)" },
        { "Some sozinho quando tudo estiver em 75.", "It disappears on its own once everything is at 75." },
        { "Suavizar movimento dos inimigos", "Smooth monster movement" },
        { "Suportes da skill", "Skill supports" },
        { "Tamanho", "Size" },
        { "Tamanho do texto", "Text size" },
        { "Terrain", "Terrain" },
        { "Tier dos mods", "Mod tiers" },
        { "Transicoes", "Transitions" },
        { "Trilha no mundo com o mapa fechado", "Trail in the world with the map closed" },
        { "Uma barra em cada lixo é a tela, não informação.", "A bar on every scrap is clutter, not information." },
        { "Unicos", "Uniques" },
        { "Unicos / chefes", "Uniques / bosses" },
        { "Vertical", "Vertical" },
        { "Vida", "Life" },
        { "Vida / mana / ES", "Life / mana / ES" },
        { "Waypoints, checkpoints, portais — marcados pelo proprio jogo.", "Waypoints, checkpoints, portals - marked by the game itself." },
        { "aplica ao reiniciar o EasyExile", "applies when EasyExile restarts" },
        { "categorias", "categories" },
        { "com a contagem do tempo restante.", "with the remaining time." },
        { "estado", "status" },
        { "fast lane", "fast lane" },
        { "mapa nativo", "native map" },
        { "ou F9 a qualquer momento", "or F9 at any time" },
        { "parado:        ", "stopped:       " },
        { "preco no chao", "price on the ground" },
        { "procura texto, inteiro ou float; grava o caminho de cada acerto", "searches text, integer or float; records the path of every hit" },
        { "saida: .jsonl em 'trees', uma linha por elemento", "output: .jsonl in 'trees', one line per element" },
        { "voce esta em:  ", "you are in:    " },
        { "world hud", "world hud" },
        { "inferior esquerdo", "bottom left" },
        { "inferior direito", "bottom right" },
        { "superior esquerdo", "top left" },
        { "superior direito", "top right" },
        { "acima", "above" },
        { "abaixo", "below" },
        { "Ao lado da skill", "Beside the skill" },
        { "Livre (voce escolhe)", "Free (you choose)" },
        { "Centro (topo)", "Centre (top)" },
        { "Canto superior esquerdo", "Top left corner" },
        { "Canto superior direito", "Top right corner" },
        { "Canto inferior esquerdo", "Bottom left corner" },
        { "Canto inferior direito", "Bottom right corner" },
        { "Idioma", "Language" },
        { "Itens para a build", "Items for the build" },
        { "Resistencias que faltam", "Missing resistances" },
        { "Preco sob o cursor", "Price under the cursor" },
        { "Pedidos da analise", "Analysis requests" },
        { "Guia de leveling", "Levelling guide" },
        { "Ir para ", "Go to " },
        { " - via ", " - via " },
        { " - saida: ", " - exit: " },
        { "Proximo: ", "Next: " },
        { " - caminho ainda desconhecido", " - path not known yet" },
        { "Explorar: ", "Explore: " },
        { "Aqui: ", "Here: " },
        { "Matar: ", "Kill: " },
        { "Pegar: ", "Take: " },
        { "Falar: ", "Talk: " },
        { "Ir: ", "Go: " },
        { "Va para ", "Go to " },
        { "Volte para a cidade pelo waypoint ou portal", "Return to town by waypoint or portal" },
        { "Mate ", "Kill " },
        { "Pegue ", "Take " },
        { "Fale com ", "Talk to " },

        // Names in the feature list, and the status lines a feature shows when
        // it is switched on but has nothing to draw yet - which is exactly the
        // moment somebody reads them.
        { "Barras de vida", "Health bars" },
        { "Valores de loot", "Loot values" },
        { "Destaque de itens valiosos", "Valuable item highlight" },
        { "Rota no mundo", "World route" },
        { "Debug (HUD)", "Debug (HUD)" },
        { "Player (HUD)", "Player (HUD)" },
        { "sem mundo", "no world" },
        { "sem frame de mapa", "no map frame" },
        { "mapa nativo fechado", "game map closed" },
        { "sem terrain", "no terrain" },
        { "terrain sem textura", "terrain has no texture" },
        { "sem objetivo", "no objective" },
        { "rota automatica desligada", "automatic routing is off" },
        { "pausado (fora de area)", "paused (not in an area)" },
        { "pausado (vitais ilegiveis)", "paused (vitals unreadable)" },
        { "pausado (PoE2 sem foco)", "paused (PoE2 is not focused)" },

        // What the radar says about itself: refusals, waiting, and the toast
        // after a key that saved something.
        { "PathOfExile nao esta rodando", "PathOfExile is not running" },
        { "OFFSETS BUILD MISMATCH", "OFFSETS BUILD MISMATCH" },
        { "RECUSANDO EXECUTAR", "REFUSING TO RUN" },
        { "aguardando o primeiro snapshot", "waiting for the first snapshot" },
        { "encerrado", "stopped" },
        { "aguardando snapshot", "waiting for a snapshot" },
        { "carregando / fora de area", "loading / not in an area" },
        { "trocando de area", "changing area" },
        { "print salvo: ", "screenshot saved: " },
        { "salvo: ", "saved: " },
        { "nao salvou (o personagem esta numa area?)", "not saved (is the character in an area?)" },
    };
}
