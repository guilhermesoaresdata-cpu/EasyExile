# Escolha do backend gráfico

Requisito: overlay **externo**, Win32 + Direct3D 11 + Dear ImGui, sem injeção,
sem hook do DirectX do jogo, sem tocar no processo do PoE2.

## Escolhido

| pacote | versão | licença | papel |
|---|---|---|---|
| `ClickableTransparentOverlay` | 11.1.0 | Apache-2.0 | janela Win32 transparente, swap chain D3D11, backends ImGui |
| `ImGui.NET` | 1.91.6.1 | MIT | binding + cimgui nativo |
| `SixLabors.ImageSharp` | 3.1.12 | Apache-2.0 | transitivo, fixado para frente (ver abaixo) |

Transitivos do primeiro: `Vortice.Direct3D11`, `Vortice.DXGI`,
`Vortice.D3DCompiler` (MIT), `SharpGen.Runtime` (MIT).

### Por quê

- Faz exatamente o que o requisito descreve: cria uma janela `WS_EX_LAYERED`
  própria e desenha nela com D3D11. **Não injeta e não faz hook** — o PoE2 nunca
  é tocado, o que é a mesma postura do resto do produto, onde a memória é só
  lida.
- Os backends de ImGui da biblioteca são ports diretos do `imgui_impl_dx11.cpp` e
  do `imgui_impl_win32.cpp` oficiais, e o README declara isso e a data do último
  changelog acompanhado. É o mesmo código que a comunidade ImGui mantém, não uma
  reimplementação paralela.
- `net8.0` nativo, sem `net472` remendado.
- Expõe `Position`, `Size` e o `Handle` da janela, que é o que permite alinhar o
  overlay à *client area* do jogo em vez de aceitar o tamanho que a biblioteca
  escolher.

### Vulnerabilidade encontrada e tratada

`ClickableTransparentOverlay` 11.1.0 depende de `SixLabors.ImageSharp` 3.1.6,
que tem advisory de severidade **alta**
([GHSA-2cmq-823j-5qj8](https://github.com/advisories/GHSA-2cmq-823j-5qj8)) e
outro moderado ([GHSA-rxmq-m78w-7wmc](https://github.com/advisories/GHSA-rxmq-m78w-7wmc)).

O EasyExile não carrega imagem nenhuma — o ImageSharp só aparece em
`AddOrGetImagePointer`, que não usamos —, mas o assembly é distribuído junto.
A correção é referenciar 3.1.12 diretamente: o NuGet promove a versão maior e o
build volta a 0 avisos. Verificado.

## Descartado

**Backend próprio (Vortice.Direct3D11 + ImGui.NET, janela e backends escritos
aqui).** Daria controle total e uma árvore de dependências menor. Custa portar
`imgui_impl_dx11` e `imgui_impl_win32` — shaders, atlas de fonte, gestão de
vertex/index buffer, scissor rects, mapeamento de input — na ordem de mil linhas
de código gráfico que só se valida olhando a tela. É o tipo de código onde um
erro sutil aparece como "nada desenha" sem diagnóstico. Fica registrado como
opção real caso a biblioteca deixe de ser mantida: a fronteira interna do Radar
(`IOverlayCanvas`) foi desenhada para que trocar o backend não toque nas
features.

**Silk.NET.Direct3D11.** Mantido pela .NET Foundation, MIT, dependências mais
limpas que o Vortice — mas é API COM crua, o que aumenta o custo do backend
próprio em vez de reduzir. Mesma objeção acima.

**Overlay em WPF/WinForms com `AllowsTransparency`.** Descartado: composição por
software, sem D3D, e o custo por frame de uma janela layered do tamanho da tela
é alto justamente no caso que interessa.

## Fronteira

Nada do stack gráfico aparece fora de `Overlay/` e `Rendering/`. As features
desenham através de `IOverlayCanvas` e recebem `RenderFrame`, que só carrega
`WorldSnapshot`. Isso é verificado por teste, e é o que permite testar feature
offline sem D3D.
