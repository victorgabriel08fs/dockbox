# Changelog

Todas as mudanças relevantes do Dockbox ficam registradas aqui.

O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e o projeto usa [Versionamento Semântico](https://semver.org/lang/pt-BR/).
Enquanto a versão for `0.x`, qualquer versão nova pode mudar o comportamento.

## [Não lançado]

### Adicionado

- Visual estilo macOS: fundo de vidro com gradiente, borda clara e sombra; rótulo com o nome do item.
- Tema automático, claro, escuro ou personalizado; cor de fundo, cor de destaque, opacidade e cantos configuráveis.
- Posição na tela: embaixo, em cima, à esquerda ou à direita.
- Tamanho dos ícones e intensidade da ampliação configuráveis; ampliação pode ser desligada.
- Ponto sob apps abertos; clique traz a janela para frente ou minimiza; Ctrl + clique abre nova janela.
- Animação de pulo ao abrir um app.
- Janela "Gerenciar Dock": adicionar apps do Menu Iniciar, arquivos e pastas; remover, reordenar e editar.
- Widgets: relógio, calendário, clima, CPU/RAM, bateria, mídia tocando agora e Lixeira.
- Soltar arquivos sobre um app abre os arquivos com ele; soltar na Lixeira apaga.
- Opção de iniciar junto com o Windows.
- Reservar espaço na tela: as janelas maximizadas param antes do Dock em qualquer borda; opção em Comportamento (#4).
- Ocultar automaticamente: o Dock sai da tela e volta ao encostar o mouse na borda, com tempo configurável (#5).
- Reordenar arrastando um ícone no Dock e remover arrastando para fora, com indicador de onde o item entra, também ao arrastar arquivos de fora (#6).
- Menu próprio de cada app: janelas abertas (clique traz a janela), Nova janela, Mostrar no Explorador, Fechar/Fechar todas, Remover do Dock e submenu Opções do Dock; menus funcionam pelo teclado (#7).
- Apps abertos que não estão fixados aparecem depois dos fixados, com "Manter no Dock"; apps da Microsoft Store ganham o ponto de aberto e podem ser adicionados pelo Gerenciar Dock (#8).
- Desfazer remoção: aviso "Desfazer" no Dock por 6 segundos e Ctrl+Z no Gerenciar Dock; fixar algo que já está no Dock não duplica, o item existente é destacado (#9).
- Aviso quando não dá para salvar as configurações (somente leitura, disco cheio, arquivo em uso), com o motivo, o caminho e "Tentar salvar de novo"; antes de avisar, tenta de novo algumas vezes (#10).

### Segurança

- Scripts, protocolos desconhecidos e interpretadores com argumentos pedem confirmação antes de abrir (uma vez por item); protocolos usados em ataques, como `ms-msdt:` e `search-ms:`, são bloqueados (#13).

### Alterado

- Configurações agora ficam em `%APPDATA%\Dockbox\settings.json`; o `pinned.json` antigo é importado.
- Gravação atômica das configurações; um arquivo corrompido é copiado com data antes de voltar aos padrões.
- Requer Windows 10 versão 1809 ou mais recente.
- Item padrão "Terminal" abre o Windows Terminal quando instalado (senão PowerShell, por último cmd) (#11).
- Setas de reordenar no Gerenciar Dock falam "antes/depois" conforme a posição do Dock (#11).
- Mudar cor, opacidade ou cantos não recarrega mais os ícones no Gerenciar Dock nem recria os ícones do Dock (#11).

## [0.1.0] - 2026-10-29

Primeira versão (Fase 1, MVP).

### Adicionado

- Barra fixa na parte inferior da tela, sem borda, transparente e sempre no topo.
- A janela não rouba o foco e não aparece no Alt+Tab nem na barra de tarefas.
- Ícones de apps fixados; um clique abre o app.
- Ampliação suave ao passar o mouse, com o nome do app acima do ícone.
- Arrastar um `.exe`, atalho `.lnk` ou pasta para o Dock para fixá-lo.
- Menu do botão direito: editar apps fixados, recarregar e sair.
- Apps fixados salvos em `%APPDATA%\Dockbox\pinned.json`.

[Não lançado]: https://github.com/victorgabriel08fs/dockbox/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/victorgabriel08fs/dockbox/releases/tag/v0.1.0
