# RGB Studio

Crie, coloque e ative efeitos de iluminação nas luzes ASUS Aura, sem a interface do Armoury Crate. São 190 efeitos, incluindo 133 gradientes, com uma tela própria e efeito separado para cada zona.

![Aba Gradientes](docs/gradientes.png)

> **Projeto não oficial, sem vínculo com a ASUS.** Ele foi testado em **uma única máquina** e fala direto com o controlador de LED da placa. Use por sua conta e risco.

## O que tem

- **190 efeitos em abas:** Gradientes (92), RGB puro (17), Orgulho (24), Motion (25), Ciência (12), Sinais (9) e Clássicos (11).
- **Efeito por zona:** placa, cada conector ARGB e cada pente de memória podem rodar um efeito diferente, ou um só atravessando todos (Encadear).
- **Seus próprios efeitos:** ajuste cores, velocidade, brilho e direção, e salve com um nome.
- **Contar LEDs:** os LEDs não informam quantos são, então uma aba ajuda a contar no olho e grava o tamanho real de cada fita.
- **Correção de cor:** gama, mistura em Oklab e calibração R/G/B por zona.
- **Limites de segurança:** teto de brilho e limite estimado de corrente por conector.

Alguns efeitos rodam a matemática de verdade: Bubble Sort, conjunto de Mandelbrot, autômatos celulares (regras 30, 90, 110 e 150) e Morse no tempo internacional.

![Aba RGB puro](docs/rgb-puro.png)

## Requisitos

- Windows 11 (é o único testado).
- Placa com controlador Aura USB `0B05:19AF` e firmware `AULA3-AR32`. **Com qualquer outro firmware, o app para sem enviar nada à placa.**
- Armoury Crate instalado, se quiser controlar as memórias: elas passam pelo Aura SDK e pelo `LightingService` da ASUS.
- Permissão de administrador: o app pede ao abrir, porque o SDK precisa dela para alcançar as memórias.

Testado em ROG Crosshair X870E Apex, com memórias ADATA XPG Lancer RGB e um reservatório ARGB. Outras placas com o mesmo firmware podem funcionar, mas não foram testadas.

## Instalar

Baixe em [Releases](../../releases):

- **`RgbStudio-<versão>-setup.exe`:** instalador, já inclui o .NET. Instala em Arquivos de Programas e cria atalhos no Menu Iniciar e, se quiser, na Área de Trabalho. Desinstale por Configurações > Aplicativos; seus efeitos e ajustes ficam em `%APPDATA%\RgbStudio`.
- **`RgbStudio-win-x64.zip`:** a mesma coisa sem instalar, já inclui o .NET.
- **`RgbStudio-win-x64-requer-dotnet10.zip`:** menor, precisa do [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

A primeira conexão leva cerca de 15 segundos. Nem o instalador nem o executável são assinados, então o Windows pode mostrar um aviso.

Ao atualizar ou desinstalar, o instalador pede ao app aberto que saia pelo caminho normal, devolvendo as luzes ao Armoury Crate (o mesmo que `RgbStudio.exe --quit`).

## Como funciona

| Zona | Caminho |
|---|---|
| Placa e conectores ARGB | USB HID direto (relatórios de 65 bytes `EC 40`) |
| Memórias | Aura SDK oficial (`aura.sdk.1`) |

O app reinicia o `LightingService` ao abrir: as memórias só responderam ao SDK depois disso. Com o SDK no controle, o serviço não escreveu por cima do USB nos testes. Se o SDK não encontrar nenhuma memória, o app para o serviço e segue só pelo USB, religando-o ao pausar ou sair.

As memórias **nunca** são escritas direto no SMBus, porque esse barramento também guarda os dados de configuração dos pentes.

## Limitações conhecidas

- **Corrente é estimativa:** o limite usa 60 mA por LED branco, sem medir nada. O padrão de 2 A por conector é conservador, não a especificação da placa. Confira o manual antes de aumentar, e fitas longas em branco merecem atenção.
- **Azul nas memórias:** parece fraco nos pentes testados, e a causa não foi confirmada. Roxos e rosas também podem variar. A calibração por zona ajuda a equilibrar.
- **Memória intermitente:** o SDK às vezes devolve só um pente. O app detecta isso quando já conhece os dois e reabre até 3 vezes.
- **`EC 52 53 00 01`:** é enviado ao iniciar porque o OpenRGB faz o mesmo, mas seu efeito completo não foi verificado. Pode influenciar dispositivos ARGB Gen 2.
- **Flashes:** Sirene policial, Ambulância e Estroboscópio piscam forte. Evite se tiver sensibilidade a luz piscando.

## Compilar

Requer o .NET 10 SDK.

```bash
dotnet test RgbStudio.Tests -c Release
dotnet publish RgbStudio/RgbStudio.csproj -c Release -o publish
```

O instalador sai de `installer\build.ps1`, que roda os testes, publica e compila com o [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).

Sem hardware: `dotnet RgbStudio/bin/Release/net10.0-windows/RgbStudio.dll --simulate`.

## Créditos

- [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) e [liquidctl](https://github.com/liquidctl/liquidctl): documentação viva do protocolo Aura USB. O código deste projeto foi escrito do zero, sem copiar o deles.
- [Oklab](https://bottosson.github.io/posts/oklab/), de Björn Ottosson: mistura perceptual de cores.
- [WLED](https://kno.wled.ge/features/effects/) e [FastLED](https://github.com/FastLED/FastLED): ideias de efeitos, gama, calibração e limite de corrente.

ASUS, Aura e Armoury Crate são marcas da ASUSTeK. Este projeto não é afiliado nem endossado por ela.

## Licença

MIT. Veja [LICENSE](LICENSE).
