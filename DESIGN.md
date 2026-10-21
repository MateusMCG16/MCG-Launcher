# Direção da interface MCG Launcher

## Brief e análise de origem
Launcher Windows para organizar e abrir jogos instalados, em português, com biblioteca offline, Steam, Epic, favoritos e títulos manuais. Stack mantida: WinUI 3, XAML e C#, pois o produto é um aplicativo nativo. Análise feita pelo código, sem observar o aplicativo em execução.

A interface anterior tinha hierarquia tímida, verde ácido sobre preto sem relação específica com o acervo, legendas de 11 px e filtros numa linha rígida. Preservar os fluxos reais e as capas locais é a prioridade.

## Conceitos considerados
Escala 1 a 5; portabilidade menor é melhor.

| Território | Conceito, assinatura, emoção e quadro | Âncora | Originalidade | Adequação | Viabilidade | Portabilidade |
| --- | --- | --- | --- | --- | --- | --- |
| Material | Acervo: capas em uma estante digital; ação avança e se encaixa; organização tranquila; título amplo sobre capas | Jogos locais e imagens encontradas no PC | 4 | 5 | 5 | 2 |
| Lugar | Sala de jogos: painel luminoso; entrada cinematográfica; expectativa; jogo em destaque | Abrir uma partida | 3 | 4 | 3 | 4 |
| Sistema | Catálogo técnico: índices e metadados; seleção por marcador; precisão; lista compacta | Descoberta Steam e Epic | 3 | 4 | 5 | 3 |

Escolha: Acervo, porque apresenta os jogos reais e mantém o acesso rápido sem exigir assets fictícios ou efeitos de vitrine.

## Concept card
Idea: seus jogos instalados como um acervo pessoal pronto para abrir.
Metaphor: estante de capas e etiquetas de coleção.
Signature: as ações avançam 3 px na interação e a seção selecionada recebe uma moldura âmbar.
Emotion: orientação imediata e vontade de escolher uma partida.
Motion verb: avançar e encaixar.
Screenshot: biblioteca com título amplo, navegação marcada e capas locais.

## Sistema
Paleta por papel (definida pelo usuário): ground #000000, surface #14213D, ink #E5E5E5, accent #FCA311. Derivações: raised #1C2C4F, line #2A3B63, muted #E5E5E5 a 65%. Item de navegação selecionado usa fundo #FCA311 com texto #000000. Layout: barra lateral flutuante arredondada (Jogos, Configurações), título "MCG Launcher" e grade de cards verticais 200 × 290. Acento aplicado às ações e seleção, com imagens reais como fonte principal de cor.

Tipografia: Segoe UI Variable do Windows, sem downloads. Display 40 px / 32 px, tracking -35; títulos de jogos 16 px; texto de apresentação 16 px; controles 14 px e metadados 12 px. Títulos e descrições quebram linha. Fonte nativa mantém fallback do sistema.

Grade: sidebar 208 px, margem 32 px, ritmo 24 px; janela compacta com sidebar 180 px e margem 20 px. Capas 242 × 142 px mantidas para respeitar o pipeline de imagens existente. Raio: 6 px, com 5 px nas bordas internas das capas.

Composição:

```text
Janela ampla
[ marca                                    controles do Windows ]
[ navegação ][ título expressivo                 adicionar jogo ]
[          ][ busca       plataforma     ordem          detectar ]
[          ][ avisos / progresso                                ]
[          ][ capa / capa / capa                                ]
[ offline  ][ contagem                           acervo local   ]

Janela compacta
[ navegação ][ título em quebra               adicionar jogo    ]
[          ][ busca em toda a largura                           ]
[          ][ plataforma       ordem                  detectar  ]
[          ][ capas em fluxo conforme o espaço                  ]

Sem resultados
[ navegação ][ título / filtros                                 ]
[          ][ símbolo + motivo específico + orientação          ]
```

## Interação e acessibilidade
Template compartilhado de botões: moldura no hover, deslocamento do conteúdo, pressão por ThemeAnimation nativa, estado desabilitado e foco do sistema. As animações de tema seguem a configuração de animações do Windows; o deslocamento de hover é instantâneo. Não existe loop, loader decorativo nem conteúdo dependente de animação. Repouso deliberadamente estático: esta é uma ferramenta de escolha, não uma apresentação promocional.

Seção atual indicada por superfície e moldura, além de ItemStatus para automação. HeadingLevel no título principal; imagens nomeadas pelo jogo; alvos de botões com mínimo 44 px. Cursor e interação por teclado são nativos. Contagem mantém dados reais.

Teste conceitual de genericidade: retirar verde ácido, rótulo decorativo de seção e selo MVP; enfatizar o acervo e seus dados reais. Removido o acessório visual de raios grandes sem relação com o conceito.

## Adaptação e limites
As regras web da skill (GSAP, Lenis, SEO, sitemap, URLs, página 404 e layout de telefone) não se aplicam ao aplicativo WinUI. Não há migração de framework, novos pacotes, downloads, jogos ou métricas inventados. O layout compacto atende ao mínimo nativo de 860 px; não representa um aplicativo móvel.

Compilação, execução, testes, screenshots, auditoria de acessibilidade e avaliação externa não foram executados por instrução explícita do usuário. Nenhuma nota Awwwards é atribuída sem evidência visual. Contraste, comportamento do template, estados de foco e layout ainda precisam de confirmação no aplicativo quando autorizada. Sem publicação ou exclusão de arquivos.
