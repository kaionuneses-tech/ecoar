# Ecoar

Soundboard pessoal para Windows: toca sons **no seu microfone** (Discord, jogos) e nos seus fones,
com teclas de atalho. Funciona sem cabo de áudio virtual, porque instala um efeito de áudio
próprio no microfone.

## Baixar

**[⬇ Baixar a última versão (Ecoar-amigos.zip)](https://github.com/kaionuneses-tech/ecoar/releases/latest/download/Ecoar-amigos.zip)**

Extraia o zip e siga o `LEIA-ME.txt`. Resumo:

1. Abra o `Ecoar.exe`. Se o Windows mostrar "O Windows protegeu o computador", clique em
   **Mais informações → Executar assim mesmo** (o programa não é assinado).
2. Escolha o microfone e clique em **Instalar no microfone** (pede administrador uma vez).
3. Adicione seus sons, defina atalhos e, se quiser, use **Recortar...** para tocar só um trecho.

Para remover: `Desinstalar efeito do microfone.bat`.

### Discord: supressão de ruído

A supressão de ruído **Krisp** do Discord apaga sons que não são voz (batidas, buzinas, risadas),
então seus amigos podem não ouvir os sons do Ecoar. Em **Configurações → Voz e vídeo → Supressão de
ruído**, troque **Krisp** por **Padrão** (filtro mais leve) ou **Nenhum** (o som passa inteiro, mas o
ruído do ambiente também). Isso acontece depois que o som sai do microfone, então nenhum soundboard
consegue contornar. Durante a call, o botão de supressão no painel de voz liga e desliga o filtro.

## Funcionalidades

- Sons no microfone e nos fones ao mesmo tempo, com volumes separados.
- Atalhos globais, inclusive de uma tecla só (F9, NumPad1...).
- Recorte de trecho com forma de onda e prévia, sem alterar o arquivo original.
- Funciona com apps que capturam em modo RAW (como o Discord).

## Limitações

- Testado em um PC (Windows 10 22H2, headset USB).
- Microfones que já têm efeitos do fabricante (comum em Realtek) não recebem o Ecoar:
  o instalador se recusa a sobrescrever efeitos de outros programas.

## Como funciona

- `apo/` — `EcoarApo.dll` (C++): efeito de áudio (APO) carregado pelo `audiodg.exe`. Ele é registrado
  como efeito de stream (SFX, modo DEFAULT) e como efeito de modo (MFX, modo RAW), e soma ao microfone
  o áudio que lê de uma memória compartilhada.
- `app/` — `Ecoar.exe` (C#/.NET 8, WinForms, NAudio): interface, atalhos, decodificação e instalação
  (`--setup <endpoint>` / `--uninstall`).
- `tests/` — testes xUnit; `apo/tests/` — testes do leitor e do mixer em C++.
- `tools/` — `captest` (grava o microfone e detecta um tom), `ringprobe`, scripts de desenvolvimento.

## Compilar

Requisitos: Visual Studio 2022 Build Tools (C++) e .NET 8 SDK.

```powershell
./package.ps1        # compila, testa e gera dist/Ecoar-amigos.zip
```

Cada push na branch `main` gera automaticamente uma nova versão em **Releases**.
