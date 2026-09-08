# SPRINT P1 — GAMEOFFSETS CONSUMER FOUNDATION

> **Nota historica.** Este documento descreve o estado no fim do P1, quando os
> projetos ainda se chamavam `Poe2Client.*`. A fundacao foi migrada para
> `EasyExile.Core` sem reescrita de logica; o mapeamento esta em
> [../PROJECT_STATE_SNAPSHOT.md](../PROJECT_STATE_SNAPSHOT.md).

## RESULTADO: COMPLETE

> Fechado em definitivo pelo [P1 CLOSEOUT](SPRINT_P1_CLOSEOUT.md): a transicao
> de area foi observada LIVE (PASS) e o probing heuristico do EntityWorld foi
> removido, substituido por `StdMapNode.EntityValue = 0x28` confirmado pelo
> Analyzer. As duas lacunas listadas abaixo em "O que NAO foi provado" nao
> existem mais.

## O que foi construido

Projeto principal em `main/`, isolado do Analyzer.

```
main/
  Poe2Client.slnx
  libs/GameOffsets.dll          <- referencia de arquivo, nao de projeto
  src/Poe2Client.Core           ProcessMemory, NativeText, GameLayout, BuildGate
  src/Poe2Client.Game           GameSession, GameEntity, EntityWorld, GameCamera
  src/Poe2Client.App            relatorio de console + --watch
  src/Poe2Client.Tests          54 testes
```

## Isolamento (verificado por teste, nao por convencao)

| Regra | Como e garantida |
|---|---|
| Nao referenciar Analyzer.Core | `Nothing_references_the_analyzer` — reflete sobre as assemblies |
| Nao acessar knowledge.db | mesma verificacao (Sqlite / EntityFramework ausentes) |
| Consumir somente GameOffsets.dll | `The_consumer_carries_no_non_framework_dependency_at_all` |
| Nenhum numero magico no consumidor | `GameLayout.cs` e o unico arquivo que nomeia o contrato |

Resultado inesperado e bom: **nao existe nem sequer uma referencia de assembly ao
GameOffsets em runtime**. Tudo no contrato e `const`, entao o compilador embute os
valores. Nao ha nada para substituir em runtime, e trocar a DLL sem recompilar nao
muda comportamento silenciosamente — o fingerprint tambem esta embutido, entao o
gate recusa.

## Build gate — REFUSE TO RUN

`GameSession.RequireMatchingBuild` lanca `BuildMismatchException`. Nao existe flag,
bool, `Force`, `Ignore`, `Override` ou `Unsafe` em nenhuma assinatura — isso e
verificado por reflexao no teste `There_is_no_way_to_continue_past_a_mismatch`.

O teste `A_client_from_a_different_build_is_refused_outright` escreve uma imagem PE
real em disco (DOS header, COFF, optional header, section table, `.text`) e exige a
excecao. `A_patched_code_section_alone_is_enough_to_refuse` prova que header igual +
codigo diferente ja basta — o caso exato que uma checagem so-de-header deixaria passar.

## Testes — 54, todos passando (98 ms)

| Item exigido na fase 17 | Coberto por |
|---|---|
| build mismatch | `BuildMismatchTests` (5) |
| native vector | vetor invertido, contagem absurda, layout do contrato |
| native map | layout do contrato, ciclo de nos nao trava a enumeracao |
| wstring | round-trip, size > capacity rejeitado |
| component lookup | resolucao nome→indice→endereco, back-reference de owner |
| component index bounds | indice negativo e indice fora da lista sao descartados |
| entity lifetime | details liberados invalidam a entidade; enderecos relidos, nunca lembrados |
| area change | `AreaTransitionTests` (7) |
| Life | leitura dos blocos, entidade sem Life reporta vazio |
| WorldToScreen | centro, eixo Y, fora da tela, atras da camera, matriz invalida |
| contract version | fingerprint, schema, export hash, RVA de entrada |

Correcao feita durante os testes: `FakeMemory` tinha a sua **propria** copia da
decodificacao de wstring/utf8. Testar isso testava o dublê, nao o produto. A
decodificacao foi extraida para `Core/NativeText.cs` e agora o cliente live e os
testes passam pelo mesmo codigo.

## Live run

```
fingerprint    EC958755D0F299CDED73BAA6  MATCH
LocalPlayer    0x3C0ABD1A280
name           gravataicity        level 17
Health 359/359 (239)  Mana 120/120 (240)  EnergyShield 157/157 (241)
world (4114, 2625, -353)   grid (246, 374)
13 components, owner back-reference 13/13
viewport 1920x1080, projecao valida, player OnScreen (960,444) depth 0.983
awake 364, sleeping 80
NPCs, Terrain, Effects e QuestObjects projetados na tela
elapsed 0.1s
```

Observacao de 75 s (`--watch`), 38 amostras: **0 falhas de resolucao**. A contagem de
entidades acordadas oscilou entre 343 e 361 — spawn e despawn observados ao vivo, que
e a prova de lifetime em cliente real.

## O que NAO foi provado

**Transicao de area ao vivo.** Nas 38 amostras nao houve nenhuma: o cliente pausa
quando esta sem foco, entao o personagem ficou parado em `0x3C087CB4000` o tempo todo.
A transicao esta coberta por teste (`After_an_area_change_a_fresh_walk_lands_in_the_new_area`,
`An_entity_held_across_an_area_change_stops_validating`), mas nao ao vivo. Para fechar:
rodar `--watch --seconds 120` com o jogo em foco e atravessar um portal.

**Offset de valor do no do mapa.** O contrato nao carrega um — o Analyzer nunca
confirmou. `EntityWorld` sonda os slots apos os links e aceita o que le de volta como
entidade real. E o unico lugar do consumidor que procura em vez de ser informado, e
esta comentado como tal no codigo. Candidato natural para um sprint futuro do Analyzer.

## Restricoes respeitadas

Nada escreve na memoria. Nada injeta. Nada envia input. Nada desenha na tela.
`ProcessMemory` abre o processo com `PROCESS_VM_READ | PROCESS_QUERY_INFORMATION` —
sem `VM_WRITE`, sem `VM_OPERATION`.

## Parado aqui

Nenhuma funcionalidade final foi implementada, conforme a instrucao.
