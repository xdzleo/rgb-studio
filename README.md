# RGB Studio

Crie, coloque e ative efeitos de iluminação nas luzes ASUS Aura, nas memórias RGB e no sensor de fluxo Aqua Computer high flow NEXT, sem Armoury Crate e sem aquasuite abertos. São 264 efeitos, com uma tela própria e efeito separado para cada zona.

![Aba Gradientes](docs/gradientes.png)

> **Projeto não oficial, sem vínculo com a ASUS nem com a Aqua Computer.** Ele foi testado em **uma única máquina** e fala direto com o controlador de LED da placa, com as memórias pelo SMBus e com o sensor pelo USB. Use por sua conta e risco.

## O que tem

- **264 efeitos em abas:** Gradientes (91), RGB puro (17), Orgulho (24), Países (74), Motion (25), Ciência (12), Sinais (9) e Clássicos (12).
- **Efeito por zona:** placa, cada conector ARGB, cada pente de memória e o anel do high flow NEXT podem rodar um efeito diferente, ou um só atravessando todos (Encadear).
- **Seus próprios efeitos:** em Meus efeitos, escolha as cores e como elas andam (correr, faixas, cometas, blocos, vai e vem, espelho, respirar, piscar ou parado), ou crie uma cor estática com um clique.
- **Contar LEDs:** os LEDs não informam quantos são, então uma aba ajuda a contar no olho e grava o tamanho real de cada fita.
- **Correção de cor:** gama que não derruba o brilho, mistura em Oklab e calibração R/G/B por zona.
- **Limites de segurança:** teto de brilho e limite estimado de corrente por conector.

Alguns efeitos rodam a matemática de verdade: Bubble Sort, conjunto de Mandelbrot, autômatos celulares (regras 30, 90, 110 e 150) e Morse no tempo internacional.

![Aba RGB puro](docs/rgb-puro.png)

## Requisitos

- Windows 11 (é o único testado).
- Placa com controlador Aura USB `0B05:19AF` e firmware `AULA3-AR32`. **Com qualquer outro firmware, o app para sem enviar nada à placa.**
- Permissão de administrador: o app pede ao abrir, porque o SMBus das memórias exige.
- Memórias: chipset AMD (módulo PIIX4) e controlador ENE com firmware `AUDA0-E6K5-0101`. Com outro firmware, o pente fica como está. Se o Armoury Crate estiver instalado, o app usa o Aura SDK dele.
- Opcional: Aqua Computer high flow NEXT (`0C70:F012`).

Testado em ROG Crosshair X870E Apex, com memórias ADATA XPG Lancer RGB, um reservatório ARGB e um high flow NEXT. Outras placas com o mesmo firmware podem funcionar, mas não foram testadas.

## Instalar

Baixe em [Releases](../../releases):

- **`RgbStudio-<versão>-setup.exe`:** instalador, já inclui o .NET e o driver PawnIO, que só é instalado se faltar. Cria atalhos no Menu Iniciar e, se quiser, na Área de Trabalho. Desinstale por Configurações > Aplicativos; seus efeitos e ajustes ficam em `%APPDATA%\RgbStudio`, e o PawnIO fica, porque outros programas também o usam.
- **`RgbStudio-win-x64.zip`:** sem instalar, já inclui o .NET. Para as memórias, instale o [PawnIO](https://pawnio.eu) antes.
- **`RgbStudio-win-x64-requer-dotnet10.zip`:** menor, precisa do [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) e do PawnIO.

A primeira conexão leva cerca de 15 segundos. Nem o instalador nem o executável são assinados, então o Windows pode mostrar um aviso.

Ao atualizar ou desinstalar, o instalador pede ao app aberto que saia pelo caminho normal, devolvendo as luzes (o mesmo que `RgbStudio.exe --quit`).

## Como funciona

| Zona | Caminho |
|---|---|
| Placa e conectores ARGB | USB HID direto (relatórios de 65 bytes `EC 40`) |
| Memórias | SMBus direto, pelo driver [PawnIO](https://pawnio.eu); ou o Aura SDK, se o Armoury Crate estiver instalado |
| Anel do high flow NEXT | relatório de configuração `0x03`, reenviado a cada quadro |

**Memórias pelo SMBus.** Esse barramento também guarda os dados de configuração dos pentes (SPD), então o app só fala com os endereços `0x70` a `0x77`, onde fica o controlador de luz. Ele só grava registradores de cor, de modo direto e de remapeamento, e nunca o comando de salvar do controlador. Também pega o mutex `Access_SMBUS.HTP.Method`, compartilhado com programas de sensores. Depois de desligar o PC, os pentes acordam todos em `0x77`, e o app os remapeia como o Armoury Crate fazia.

**high flow NEXT.** O sensor roda os efeitos dele sozinho e não aceita cor por LED. Para rodar os efeitos do app, os 8 grupos de LED dele são espalhados pelas 11 posições do anel e a configuração é reenviada a no máximo 20 quadros por segundo, só na memória temporária dele. Nada é salvo, e só a parte de iluminação muda; calibração e alarmes ficam como estão. Os efeitos próprios do sensor ficam na aba **Do sensor** (com o anel selecionado), com a opção de salvar no aparelho. O layout da configuração veio das estruturas do próprio aquasuite e fecha com o checksum CRC-16/USB do aparelho.

## Limitações conhecidas

- **Corrente é estimativa:** o limite usa 60 mA por LED branco, sem medir nada. O padrão de 2 A por conector é conservador, não a especificação da placa. Confira o manual antes de aumentar, e fitas longas em branco merecem atenção.
- **high flow NEXT pode travar a configuração:** a 35-56 quadros por segundo, com o aquasuite também acessando, o sensor parou de aceitar configurações até ser reconectado. Agora o app manda no máximo 20 quadros por segundo e para depois de 3 recusas seguidas. A medição de vazão e temperatura não foi afetada.
- **Anel em 8 cores:** são 8 grupos para 10 LEDs, então dois pares de LEDs vizinhos dividem a mesma cor.
- **Remapeamento das memórias:** escrito seguindo a sequência documentada, ainda não testado num boot a frio sem Armoury Crate.
- **Azul nas memórias:** parece fraco nos pentes testados, e a causa não foi confirmada. A calibração por zona ajuda a equilibrar.
- **`EC 52 53 00 01`:** é enviado ao iniciar porque o OpenRGB faz o mesmo, mas seu efeito completo não foi verificado. Pode influenciar dispositivos ARGB Gen 2.
- **Flashes:** Sirene policial, Ambulância, Estroboscópio e o movimento Piscar piscam forte. Evite se tiver sensibilidade a luz piscando.

## Compilar

Requer o .NET 10 SDK.

```bash
dotnet test RgbStudio.Tests -c Release
dotnet publish RgbStudio/RgbStudio.csproj -c Release -o publish
```

O instalador sai de `installer\build.ps1`: ele roda os testes, publica, baixa o instalador oficial do PawnIO (conferido pelo SHA-256) e compila com o [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).

Sem hardware: `dotnet RgbStudio/bin/Release/net10.0-windows/RgbStudio.dll --simulate`.

## Créditos

- [OpenRGB](https://codeberg.org/OpenRGB/OpenRGB) e [liquidctl](https://github.com/liquidctl/liquidctl): documentação viva dos protocolos Aura USB e ENE SMBus. O código deste projeto foi escrito do zero, sem copiar o deles.
- [PawnIO](https://pawnio.eu), de namazso: o driver e o módulo de SMBus (`SmbusPIIX4.bin`, LGPL-2.1, em `RgbStudio/ThirdParty/PawnIO`).
- [Oklab](https://bottosson.github.io/posts/oklab/), de Björn Ottosson: mistura perceptual de cores.
- [WLED](https://kno.wled.ge/features/effects/) e [FastLED](https://github.com/FastLED/FastLED): ideias de efeitos, gama, calibração e limite de corrente.

ASUS, Aura e Armoury Crate são marcas da ASUSTeK. Aqua Computer, high flow NEXT e aquasuite são marcas da Aqua Computer GmbH & Co. KG. Este projeto não é afiliado nem endossado por elas.

## Licença

MIT. Veja [LICENSE](LICENSE). O módulo do PawnIO segue a LGPL-2.1 e o driver PawnIO, incluído no instalador sem modificação, a GPL-2.0.
