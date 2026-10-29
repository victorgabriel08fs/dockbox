# Releases do Dockbox

Cada pasta guarda o executável pronto de uma versão, para baixar sem compilar.

| Versão | Data | Arquivo | Notas |
|---|---|---|---|
| [v0.1.0](v0.1.0/) | 2026-10-29 | `Dockbox-v0.1.0-win-x64.zip` | MVP (Fase 1) |

O que mudou em cada versão está no [CHANGELOG](../CHANGELOG.md).
As mesmas versões também aparecem na página
[Releases](https://github.com/victorgabriel08fs/dockbox/releases) do GitHub.

## Como usar

1. Baixe o zip da versão desejada e extraia.
2. Dê dois cliques em `Dockbox.exe` (Windows 10/11 x64, não precisa instalar o .NET).
3. Se aparecer "O Windows protegeu o computador", clique em **Mais informações** e
   depois em **Executar assim mesmo** (o executável ainda não tem assinatura digital).

Para conferir se o download veio inteiro, compare o hash com o `SHA256SUMS.txt` da pasta:

```powershell
Get-FileHash .\Dockbox-v0.1.0-win-x64.zip -Algorithm SHA256
```

## Como lançar uma versão nova

1. Atualize `<Version>` em `src/Dockbox/Dockbox.csproj` e mova as notas de
   **[Não lançado]** para uma seção nova no `CHANGELOG.md`.
2. Faça o merge na `main` e crie a tag: `git tag v0.2.0 && git push origin v0.2.0`.
3. O workflow **release** compila o `Dockbox.exe`, cria a release no GitHub com o
   zip anexado e abre um PR adicionando o zip em `releases/v0.2.0/`.
