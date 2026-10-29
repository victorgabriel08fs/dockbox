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

### Alterado

- Configurações agora ficam em `%APPDATA%\Dockbox\settings.json`; o `pinned.json` antigo é importado.
- Gravação atômica das configurações; um arquivo corrompido é copiado com data antes de voltar aos padrões.
- Requer Windows 10 versão 1809 ou mais recente.

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
