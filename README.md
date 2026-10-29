# Dockbox

Um clone do Dock do macOS para Windows, feito em C# + WPF (.NET 8).

## Fase 1 (MVP), o que já existe

- Barra fixa na parte inferior da tela, sem borda, transparente e sempre no topo.
- A janela **não rouba o foco** e não aparece no Alt+Tab nem na barra de tarefas.
- Ícones de apps fixados; um clique abre o app.
- Ampliação suave ao passar o mouse, com o nome do app acima do ícone.
- Arraste um `.exe`, atalho `.lnk` ou pasta para cima do Dock para fixá-lo.
- Clique com o botão direito no Dock: editar apps fixados, recarregar, sair.

## Como rodar no Windows

Opção A, sem instalar nada: baixe o zip da versão mais recente na pasta
[`releases/`](releases/) ou na página
[Releases](https://github.com/victorgabriel08fs/dockbox/releases), extraia e abra o `Dockbox.exe`.
Builds de desenvolvimento ficam na aba **Actions** (workflow *build*, artefato `Dockbox-win-x64`).

Opção B, compilando:

1. Instale o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (x64).
2. No PowerShell, dentro da pasta do repositório:

   ```powershell
   dotnet run --project src/Dockbox
   ```

   Ou gere um executável:

   ```powershell
   dotnet publish src/Dockbox -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o out
   .\out\Dockbox.exe
   ```

Também dá para abrir `Dockbox.sln` no Visual Studio 2022 e apertar F5.

## Apps fixados

A lista fica em `%APPDATA%\Dockbox\pinned.json` (criada na primeira execução com alguns apps padrão):

```json
[
  { "Name": "Explorador de Arquivos", "Path": "C:\\Windows\\explorer.exe" },
  { "Name": "Bloco de Notas", "Path": "%SystemRoot%\\System32\\notepad.exe", "Arguments": null }
]
```

Depois de editar, use **Recarregar** no menu do botão direito.

## Limitações conhecidas do MVP

- Só aparece no monitor principal.
- Fica por cima das janelas maximizadas (ainda não reserva espaço na tela).
- A barra de tarefas do Windows continua visível.

Esses pontos estão previstos nas fases 2 e 3.

## Versões

O Dockbox usa [Versionamento Semântico](https://semver.org/lang/pt-BR/) com tags `vX.Y.Z`.
A versão atual fica em `src/Dockbox/Dockbox.csproj` (`<Version>`), o histórico no
[CHANGELOG.md](CHANGELOG.md) e os executáveis de cada versão em [`releases/`](releases/),
onde também está o passo a passo para lançar uma versão nova.

## Estrutura

| Arquivo | Função |
|---|---|
| `MainWindow.xaml(.cs)` | Janela do Dock, layout e animação de ampliação |
| `IconLoader.cs` | Extrai ícones de até 256px pelo Shell do Windows |
| `PinnedApps.cs` | Lê/grava a lista de apps fixados |
| `NativeMethods.cs` | Declarações Win32 (estilos de janela, cursor, ícones) |
