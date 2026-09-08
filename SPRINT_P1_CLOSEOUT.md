# P1 CLOSEOUT

> **Nota historica.** Este documento descreve o estado no fim do P1, quando os
> projetos ainda se chamavam `Poe2Client.*`. A fundacao foi migrada para
> `EasyExile.Core` sem reescrita de logica; o mapeamento esta em
> [../PROJECT_STATE_SNAPSHOT.md](../PROJECT_STATE_SNAPSHOT.md).

## RESULTADO: COMPLETE

As duas lacunas estruturais do P1 estão fechadas.

---

## 1 — AREA TRANSITION LIVE = **PASS**

Observado ao vivo, jogo em foco, transição real de área aos 11 s da janela.

```
                BEFORE                    AFTER
GameStateRoot   0x2CD1CDE47C0             0x2CD1CDE47C0
InGameState     0x2CD1CE92D10             0x2CD1CE92D10
AreaInstance    0x2B56F5D6000             0x2B56F5D1800
LocalPlayer     0x2B59AB4EB80             0x2B59B38E200
AwakeEntities   131                       330
SleepingEnt.    4095                      101
```

| Verificação | Resultado |
|---|---|
| area mudou | PASS |
| cadeia re-resolvida da raiz | PASS |
| entidades da área antiga sumiram | PASS — **0 de 131** ainda lêem como entidade |
| component resolver funciona | PASS — 13 componentes |
| Life funciona | PASS — 359/359, 120/120, 157/157 |
| Player funciona | PASS — gravataicity lvl 17 |
| Player é o mesmo personagem | PASS |
| Camera funciona | PASS — projeção válida |
| posição do mundo legível | PASS |
| novos mapas povoados | PASS |

38 amostras na janela, **0 falhas de resolução**.

O achado mais forte: o **`LocalPlayer` também foi realocado**
(`0x2B59AB4EB80` → `0x2B59B38E200`). Não é só a área que morre na transição —
a própria entidade do jogador é outro objeto. Qualquer endereço guardado
atravessando um portal aponta para memória já reutilizada.

Comando: `--watch --seconds 120`. Ele observa, não instrui: nada é pedido ao
jogador, o relatório descreve o que a cadeia fez.

---

## 2 — STDMAP NODE LAYOUT = **CONFIRMED**

### Descoberto ao vivo, não copiado

O Analyzer já tinha um comentário afirmando `Key +0x20 / Value +0x28`. Isso foi
**ignorado** e o layout foi determinado do zero, por propriedade matemática:

- **value** = o slot que dereferencia para uma entidade *parseável* (metadata
  válida + ComponentList válida + ComponentLookup válido) em praticamente todo
  nó da árvore;
- **key** = o slot cuja travessia **in-order é estritamente ascendente**, que é
  a propriedade que define uma árvore de busca — não um palpite sobre ordem de
  campos.

O resultado bate com o comentário. A diferença é que agora há prova.

### Evidência

| slot | AwakeEntities | SleepingEntities | leitura |
|---|---|---|---|
| 0x00 | 0/200 entidades | 0/200 | Left (já no contrato) |
| 0x08 | 0/200 | 0/200 | Parent (já no contrato) |
| 0x10 | 0/200 | 0/200 | Right (já no contrato) |
| 0x18 | 0/200 | 0/200 | byte 0/1 — cor rubro-negra |
| 0x20 | 0/200 | 0/200 | **Key** — 200/200 distintos, in-order ascendente |
| **0x28** | **200/200** | **200/200** | **EntityValue** |
| 0x30 | 0/200 | 0/200 | — |
| 0x38 | 0/200 | 0/200 | — |
| 0x40 | 0/200 | 0/200 | — |

Duas amostras temporais separadas, dois containers, 200 nós cada.
Vizinhos imediatos do slot de valor: **0 entidades**. Não há ambiguidade.

### Semântica verificada

```
node 0x2CD0172B900  key 2            Metadata/MiscellaneousObjects/Waypoint
node 0x2CCFFFDABF0  key 36           Metadata/MiscellaneousObjects/Checkpoint
node 0x2CD31D96DF0  key 758          Metadata/Monsters/RamGiant/RamGiantQuarry@17
node 0x2CCFFFDE160  key 1073         Metadata/Characters/Int/IntFourb
node 0x2B56F4FB660  key 3221231132   Metadata/Effects/PermanentEffect
```

### Cross map

| | Awake | Sleeping | |
|---|---|---|---|
| value | 0x28 | 0x28 | PASS |
| key | 0x20 | 0x20 | PASS |

Layouts idênticos. Um único tipo de nó.

---

## 3 — EVIDÊNCIA CIRCULAR ENCONTRADA E REMOVIDA

A primeira versão dos validadores filtrava os nós irmãos usando o offset de
valor **já confirmado** (`0x28`) para decidir o que era um nó de verdade. Sob
simulação de drift isso entrega ao validador a resposta que ele deveria estar
estabelecendo — qualquer offset se confirmaria sozinho.

Removido. Os validadores agora identificam o sentinela por propriedade de
árvore, sem nunca ler o slot de valor:

- numa árvore, um nó real é filho `Left`/`Right` de **exatamente um** pai;
  toda folha aponta ambos os links para o sentinela;
- e a raiz e o sentinela se apontam mutuamente por `Parent`, o que fixa o
  sentinela com exatidão quando a âncora é a raiz.

O teste `The_validators_do_not_consult_the_confirmed_value_offset` monta a
mesma árvore com o layout deslocado (`value 0x30`, `key 0x18`) e exige que os
dois validadores aceitem. Se algum ainda buscasse `0x28` por dentro, ele
falharia ali e passaria no layout real — que é a assinatura da circularidade.

Efeito colateral: depois de remover o filtro, a recuperação sob drift aleatório
passou de 0/2 para 2/2. A evidência circular não estava só inflando confiança —
estava **impedindo** a recuperação.

---

## 4 — KNOWLEDGE NODE

Grupo novo: `Native.StdMapNodeBlock`.

| | `StdMapNode.EntityValue` | `StdMapNode.Key` |
|---|---|---|
| offset | 0x28 | 0x20 |
| tipo | `Entity*` | `uint32` |
| criticidade | High | Medium |
| âncora | `EntityMapNode` (nó vivo, re-vinculado a cada rebind) | idem |
| depende de | `AreaInstance.AwakeEntities` | `StdMapNode.EntityValue` |
| receita | structural-search, raio 0x40, **volátil** | structural-search, raio 0x40, **volátil** |
| gate | unanimidade dos irmãos | invariante da BST em ≥8 pares |

Voláteis porque nós são alocados e liberados conforme entidades entram e saem
da área; a âncora envelhece rápido.

---

## 5 — RECOVERY

```
SCENARIO  Native.StdMapNodeBlock

 members 2, gradable 2
   Validated   StdMapNode.EntityValue   0x28
   Validated   StdMapNode.Key           0x20

 broken 6, recovered 6, false positives 0
 SCENARIO PASS
```

4 execuções: `6/6` recuperados em três, `3/6` em uma (jogador atravessando
áreas, âncora liberada no meio da varredura — o motivo de a receita ser
volátil). **False positives: 0 em todas.**

`--smoke` completo: **27/27**, 22 execuções, 21 limpas. A única regressão foi
em `AreaInstance.SleepingEntities`, de outro bloco — ver "não resolvido".

---

## 6 — EXPORT

```
exported     55  (era 53)
excluded     10
export hash  A0AC4BF739428615AF3942E3   (era F8CAA0FA3005AE3423D4405F)
build        EC958755D0F299CDED73BAA6   (inalterado)

CONTRACT VALIDATED AGAINST THE LIVE CLIENT   9/9 PASS
```

```csharp
public static class StdMapNode
{
    /// <summary>Entity* - holds a parsable entity in 200/200 nodes of both
    /// containers; neighbouring slots in none</summary>
    public const int EntityValue = 0x28;
    /// <summary>uint32 - unique per node and ascending in-order in both
    /// containers, over 200 nodes each</summary>
    public const int Key = 0x20;
    public const int Left = 0x0;
    public const int Parent = 0x8;
    public const int Right = 0x10;
}
```

---

## 7 — CONSUMER

**Probing removido.** Antes:

```csharp
// scan the slots that follow for something that reads back as an entity
for (int slot = 0x20; slot <= 0x40; slot += 8) { ... break; }
```

Agora:

```csharp
if (memory.TryReadPointer(node + GameLayout.Native.MapEntityValue, out var entity) && ...)
```

Verificado por teste, não por inspeção:

- `The_consumer_does_not_probe_for_the_mapped_entity` — lê o fonte de
  `EntityWorld.cs` e exige ausência de laço por slots e presença do offset do
  contrato;
- `No_consumer_file_outside_the_layout_wrapper_hardcodes_a_map_offset`;
- `An_entity_at_an_adjacent_slot_is_not_picked_up` — com o valor um slot ao
  lado, o consumidor não encontra **nada**. Com probing, encontrava tudo igual.

Ganho colateral: o `found.Contains()` linear virou `HashSet`.

---

## 8 — CONST WORKFLOW

Documentado em [CONTRACT_WORKFLOW.md](CONTRACT_WORKFLOW.md).

O ponto central: o contrato é só `const`, então o compilador embute tudo e
**não sobra referência de assembly em runtime**. Trocar a DLL sem recompilar
não muda nada. E isso é seguro, não perigoso: o fingerprint é `const` também e
sai da mesma compilação, então o par (offsets, fingerprint) é sempre atômico.
Não existe o estado "offsets de um build rodando contra cliente de outro".

Nenhum hot-swap foi criado, e o documento explica por que criar um quebraria
justamente essa garantia.

---

## RELATÓRIO FINAL

```
Area transition LIVE       PASS

StdMapNode
  Key offset               0x20
  EntityValue offset       0x28
  Cross-Awake              PASS   (200/200 nós)
  Cross-Sleeping           PASS   (200/200 nós)
  Discriminação            PASS   (vizinhos: 0/200)

Knowledge node             SemanticallyConfirmed / Strong
Recovery scenario          PASS
False positives            0

GameOffsets.dll            REGENERATED   A0AC4BF739428615AF3942E3
Consumer rebuilt           PASS
Heuristic map probing      REMOVED

Analyzer tests             178/178
Consumer tests             67/67
Contract surface tests     13/13
Build                      0W / 0E
```

### Checklist

- [x] area transition observada LIVE
- [x] StdMapNode EntityValue confirmado pelo Analyzer
- [x] novo contrato exportou esse offset
- [x] consumer não faz mais probing
- [x] consumer recompilado
- [x] tests PASS
- [x] 0W / 0E

---

## Corrigido de passagem

Dois testes que eu deixei quebrados no P1 e não reexecutei: ao introduzir
`ExportValueKind` (para o RVA do slot global caber sem ser confundido com
endereço de runtime), as asserções de `ExportTests` e `ContractSurfaceTests`
continuaram usando a faixa antiga. Não era regressão de produto — as
asserções é que estavam velhas. Corrigidas: agora cada valor é julgado contra
o seu próprio tipo, e o teste de superfície reconhece o sufixo `Rva`.

Também removi um laço `foreach (var candidate in new[] { node, node })` em
`ReachEntityMetadata`, que iterava o mesmo nó duas vezes sem efeito.

---

## NÃO RESOLVIDO

**`AreaInstance.SleepingEntities` intermitente** — 1 falha em 22 execuções de
`--smoke`, com o jogador atravessando áreas ativamente. Nota do engine:
`nothing passed within +/-0x0; at 0x6F0 score 86 >= 50`. O nó pontua acima do
limiar mas não fecha como único. Awake e Sleeping são estruturalmente idênticos
e só se distinguem por *membership* — se o jogador é realocado no meio da
varredura, o discriminador oscila.

Não é deste bloco e **não foi investigado a fundo**: entrar nisso aqui seria
sair do escopo do closeout. É o próximo candidato natural.

**O probing no Analyzer permanece — de propósito.** `ReachEntityMetadata` e
`TreeContains` continuam varrendo 0x20..0x40 para identificar um container.
Trocar por `0x28` criaria um ciclo: `StdMapNode.EntityValue` está ancorado nos
nós do container, então o validador do container passaria a depender do nó que
depende dele. Um único relayout derrubaria os dois sem caminho de recuperação.
Está comentado no código com essa razão.
