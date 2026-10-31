namespace RgbStudio.Core;

public enum GradientMotion { Flow, Mirror, Drift, Pulse, Ripple, Collide, Zoom, Glitter, Sweep, Comets, Chunks, Bounce, Bands, CubeWalk }

/// <summary>
/// One palette and one way of moving. The palette loops seamlessly and is built once into a
/// table, so every frame is a lookup and any strip length stays cheap. Blended in Oklab,
/// unless <c>pure</c> (full-saturation HSV, for raw RGB) or the motion is hard-edged stripes.
/// </summary>
public sealed class PaletteGradientEffect : MotionEffect
{
    const int TableSize = 720;
    readonly Rgb[] _table = new Rgb[TableSize];
    readonly GradientMotion _motion;
    readonly double _repeats, _pace;

    public override string Id { get; }
    public override string Name { get; }
    public override string Category { get; }
    public override string Blurb { get; }
    public override bool UsesColor1 => false;
    public override bool UsesDirection => true;

    public PaletteGradientEffect(string id, string name, GradientMotion motion, string[] stops,
        string category = "Gradientes", double repeats = 1, double pace = 1, bool pure = false)
    {
        Id = "grad-" + id;
        Name = name;
        Category = category;
        _motion = motion;
        _repeats = repeats;
        _pace = pace;
        Blurb = motion switch
        {
            GradientMotion.Flow => "As cores correm pela fita em fluxo contínuo.",
            GradientMotion.Mirror => "Nasce no centro e se abre para as duas pontas, em simetria.",
            GradientMotion.Drift => "As cores vagam sem pressa, sem repetir o mesmo desenho.",
            GradientMotion.Pulse => "Fluxo lento que respira, acendendo e recuando devagar.",
            GradientMotion.Ripple => "As cores balançam em ondas, como um tecido ao vento.",
            GradientMotion.Collide => "As cores vêm das pontas e se encontram no centro.",
            GradientMotion.Zoom => "O gradiente estica e encolhe, como uma lente aproximando.",
            GradientMotion.Glitter => "Fluxo de cor com pontos de brilho piscando por cima.",
            GradientMotion.Sweep => "A fita inteira atravessa as cores junto, quase de uma cor só.",
            GradientMotion.Comets => "Cometas coloridos cruzam o escuro deixando rastro.",
            GradientMotion.Chunks => "Blocos de cor bem definidos andando pela fita.",
            GradientMotion.Bounce => "O gradiente desliza para um lado e volta, em vai e vem.",
            GradientMotion.Bands => "Faixas nítidas, como uma bandeira passando pela fita.",
            _ => "Vermelho, verde e azul oscilam em ritmos sem repetição comum: passeia por todo o cubo RGB, sem repetir o ciclo.",
        };
        if (motion == GradientMotion.CubeWalk) return;

        if (pure)
        {
            for (int k = 0; k < TableSize; k++) _table[k] = Rgb.FromHsv(k * 360.0 / TableSize, 1, 1);
            return;
        }
        var colors = stops.Select(Rgb.FromHex).ToArray();
        for (int k = 0; k < TableSize; k++)
        {
            double at = (double)k / TableSize * colors.Length;
            int from = (int)at;
            double blend = at - from;
            // Stripes keep a hair of softness at the edge so they do not shimmer as they move.
            if (motion == GradientMotion.Bands) blend = blend < 0.94 ? 0 : (blend - 0.94) / 0.06;
            _table[k] = Rgb.MixPerceptual(colors[from], colors[(from + 1) % colors.Length], blend);
        }
    }

    Rgb At(double u) => _table[Math.Min(TableSize - 1, (int)(((u % 1 + 1) % 1) * TableSize))];

    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double s = t * p.Speed * _pace;
        int n = leds.Length;
        if (_motion == GradientMotion.CubeWalk) { CubeWalk(leds, s); return; }
        if (_motion == GradientMotion.Comets) { Comets(leds, s, p, seed); return; }

        double level = _motion == GradientMotion.Pulse ? 0.62 + 0.38 * Math.Cos(s * 1.15) : 1;
        double back = (s * 0.09 % 2 + 2) % 2;
        if (back > 1) back = 2 - back;
        double zoom = 1 + 0.75 * Math.Sin(s * 0.45);
        for (int i = 0; i < n; i++)
        {
            double x = Pos(i, n, p.Reverse);
            double u = _motion switch
            {
                GradientMotion.Mirror => Math.Abs(x * 2 - 1) * _repeats - s * 0.13,
                GradientMotion.Drift => x * 0.8 * _repeats + Fbm(x * 1.8 + s * 0.05, s * 0.07, seed) * 1.5 - s * 0.03,
                GradientMotion.Pulse => x * _repeats - s * 0.05,
                GradientMotion.Ripple => x * _repeats + 0.2 * Math.Sin(x * 12.566 - s * 1.5) - s * 0.05,
                GradientMotion.Collide => Math.Abs(x * 2 - 1) * _repeats + s * 0.13,
                GradientMotion.Zoom => (x - 0.5) * zoom * _repeats - s * 0.05,
                GradientMotion.Glitter => x * _repeats - s * 0.07,
                GradientMotion.Sweep => x * 0.1 - s * 0.09,
                GradientMotion.Chunks => Math.Floor(x * 10 * _repeats) / (10 * _repeats) * _repeats - Math.Floor(s * 2.4) / 24,
                GradientMotion.Bounce => x * _repeats * 0.8 + back * 1.5,
                _ => x * _repeats - s * 0.11,      // Flow, Bands
            };
            var c = At(u);
            if (level < 1) c = c.Scale(level);
            if (_motion == GradientMotion.Glitter && Hash(i, (int)(s * 14) + seed) > 0.965)
                c = Rgb.Lerp(c, Rgb.White, 0.85);
            leds[i] = c;
        }
    }

    /// <summary>
    /// Rates 1, phi and sqrt(2) have no rational relation, so the walk never closes and fills the
    /// cube. (phi and sqrt(5) would not do: sqrt(5) = 2phi - 1 traps the walk on a surface.)
    /// </summary>
    static void CubeWalk(Span<Rgb> leds, double s)
    {
        for (int i = 0; i < leds.Length; i++)
        {
            double phase = (double)i / Math.Max(1, leds.Length) * 6.283;
            leds[i] = new Rgb(
                (byte)(127.5 + 127.5 * Math.Sin(s * 0.91 + phase)),
                (byte)(127.5 + 127.5 * Math.Sin(s * 0.91 * 1.618034 + phase * 1.3)),
                (byte)(127.5 + 127.5 * Math.Sin(s * 0.91 * 1.414214 + phase * 1.7)));
        }
    }

    void Comets(Span<Rgb> leds, double s, EffectParams p, int seed)
    {
        int n = leds.Length;
        leds.Clear();
        if (n == 0) return;
        double tail = Math.Max(2.5, n * 0.3);
        for (int m = 0; m < 4; m++)
        {
            double clock = s * (0.16 + Hash(m, seed) * 0.13) + Hash(seed, m);
            double f = clock - Math.Floor(clock);
            double head = f * (n + tail);
            for (int k = 0; k < n; k++)
            {
                double d = head - k;
                if (d < 0 || d > tail) continue;
                double fade = 1 - d / tail;
                int at = p.Reverse ? n - 1 - k : k;
                leds[at] = Add(leds[at], At(m * 0.25 + k * 0.004).Scale(fade * fade));
            }
        }
    }
}

public static class GradientCatalog
{
    public static readonly IReadOnlyList<Effect> All =
    [
        // Céu e luz
        G("por-do-sol", "Pôr do sol", GradientMotion.Flow, "#FF4E00", "#FF9A00", "#FF2E63", "#7A1FA2"),
        G("aurora-boreal", "Aurora boreal", GradientMotion.Drift, "#00FFA3", "#00B8D9", "#5B2BFF", "#00E0A0"),
        G("amanhecer", "Amanhecer", GradientMotion.Mirror, "#FF7E5F", "#FEB47B", "#FFD6A5", "#FF8FA3"),
        G("crepusculo", "Crepúsculo", GradientMotion.Pulse, "#3A1C71", "#D76D77", "#FFAF7B", "#6A3093"),
        G("hora-dourada", "Hora dourada", GradientMotion.Flow, "#FFB300", "#FF8F00", "#FFD54F", "#FF6F00"),
        G("ceu-limpo", "Céu limpo", GradientMotion.Drift, "#00C6FF", "#0072FF", "#7FDBFF", "#0096FF"),
        G("noite-estrelada", "Noite profunda", GradientMotion.Ripple, "#1A1AFF", "#3B0CA3", "#7209B7", "#0A3CFF"),
        G("nebulosa", "Nebulosa", GradientMotion.Drift, "#FF00C8", "#6A00FF", "#00B2FF", "#B5179E"),
        G("via-lactea", "Via Láctea", GradientMotion.Flow, "#5D3FD3", "#B388FF", "#E0C3FC", "#3F37C9"),
        G("eclipse-solar", "Coroa solar", GradientMotion.Mirror, "#FFD166", "#FF7B00", "#D00000", "#FF9F1C"),
        // Água
        G("oceano-profundo", "Oceano profundo", GradientMotion.Ripple, "#00E5FF", "#0077B6", "#023E8A", "#00B4D8"),
        G("caribe", "Caribe", GradientMotion.Flow, "#00F5D4", "#00BBF9", "#48CAE4", "#00E5B0"),
        G("lagoa-azul", "Lagoa azul", GradientMotion.Drift, "#2DE2E6", "#118AB2", "#06D6A0", "#0AA5D6"),
        G("geleira", "Geleira", GradientMotion.Pulse, "#E0FBFC", "#98C1D9", "#3A86FF", "#C8F4FF"),
        G("mar-de-coral", "Mar de coral", GradientMotion.Ripple, "#FF6F61", "#FF9E80", "#00CFC1", "#FF4D6D"),
        G("bioluminescente", "Bioluminescente", GradientMotion.Drift, "#00FFF0", "#0047FF", "#00FF9D", "#0090FF"),
        // Fogo e terra
        G("brasa", "Brasa", GradientMotion.Drift, "#FF2A00", "#FF7A00", "#B30000", "#FF4500"),
        G("lava-viva", "Lava viva", GradientMotion.Ripple, "#FF0000", "#FF6A00", "#FFC300", "#C70039"),
        G("deserto", "Deserto", GradientMotion.Flow, "#E2A76F", "#D9622B", "#F4C95D", "#B5451F"),
        G("outono", "Outono", GradientMotion.Mirror, "#D62828", "#F77F00", "#FCBF49", "#A7501A"),
        G("ambar", "Âmbar", GradientMotion.Pulse, "#FFBF00", "#FF7F11", "#FFD27D", "#E65C00"),
        G("cobre", "Cobre", GradientMotion.Flow, "#E07A3F", "#FFB88C", "#B5541C", "#F4A261"),
        // Natureza
        G("floresta", "Floresta", GradientMotion.Drift, "#00A86B", "#2E8B57", "#7CFC00", "#008F5A"),
        G("primavera", "Primavera", GradientMotion.Mirror, "#FF8FAB", "#FFC8DD", "#A0E426", "#FFD60A"),
        G("jardim", "Jardim", GradientMotion.Flow, "#F15BB5", "#FEE440", "#00F5D4", "#9B5DE5"),
        G("cerejeira", "Cerejeira", GradientMotion.Pulse, "#FFB7C5", "#FF69B4", "#FFE4EC", "#FF8FB1"),
        G("lavanda", "Lavanda", GradientMotion.Drift, "#B497FF", "#D4BBFF", "#8A6BFF", "#E2CFFF"),
        G("menta", "Menta", GradientMotion.Ripple, "#7FFFD4", "#00E6A8", "#C1FFE8", "#30D5C8"),
        G("tropical", "Tropical", GradientMotion.Flow, "#FF5E7E", "#FFB800", "#00E6B8", "#00B3FF"),
        G("citrico", "Cítrico", GradientMotion.Mirror, "#C6FF00", "#FFEA00", "#FF9100", "#76FF03"),
        // Neon e digital
        G("cyberpunk", "Cyberpunk", GradientMotion.Flow, "#FF007F", "#7B00FF", "#00F0FF", "#FF00D4"),
        G("synthwave", "Synthwave", GradientMotion.Ripple, "#FF2E97", "#FF9E00", "#7A04EB", "#00E5FF"),
        G("vaporwave", "Vaporwave", GradientMotion.Drift, "#FF71CE", "#01CDFE", "#B967FF", "#05FFA1"),
        G("neon-tokyo", "Neon Tóquio", GradientMotion.Pulse, "#FF0090", "#00E1FF", "#8000FF", "#FF2CDF"),
        G("laser", "Laser", GradientMotion.Mirror, "#00FF85", "#00D1FF", "#FF00E5", "#39FF14"),
        G("hologram", "Holograma", GradientMotion.Drift, "#A0FFE6", "#B2A4FF", "#FFB2E6", "#9DE8FF"),
        G("arcade", "Fliperama", GradientMotion.Flow, "#FF1744", "#FFEA00", "#00E676", "#2979FF", "#D500F9"),
        G("plasma-roxo", "Plasma roxo", GradientMotion.Ripple, "#7F00FF", "#E100FF", "#3D00FF", "#FF00C8"),
        // Suaves
        G("algodao-doce", "Algodão-doce", GradientMotion.Drift, "#FFAFCC", "#BDE0FE", "#FFC8DD", "#A2D2FF"),
        G("pessego", "Pêssego", GradientMotion.Pulse, "#FFB4A2", "#FFCDB2", "#FF8E72", "#FFD7BA"),
        G("sorbet", "Sorbet", GradientMotion.Flow, "#FF9AA2", "#FFDAC1", "#B5EAD7", "#C7CEEA"),
        G("madreperola", "Madrepérola", GradientMotion.Mirror, "#FFE5F1", "#D7E3FF", "#E2FFF3", "#F6DFFF"),
        G("rose-gold", "Ouro rosé", GradientMotion.Ripple, "#F7B5A0", "#E8A0BF", "#FFD6C0", "#D98BA3"),
        // Joias e metais
        G("ametista", "Ametista", GradientMotion.Pulse, "#9B30FF", "#6A0DAD", "#D291FF", "#7B2FF7"),
        G("safira", "Safira", GradientMotion.Drift, "#0F52BA", "#1E90FF", "#0A2FFF", "#4FA8FF"),
        G("esmeralda", "Esmeralda", GradientMotion.Flow, "#00C176", "#00FF9F", "#008B57", "#2EE6A6"),
        G("rubi", "Rubi", GradientMotion.Mirror, "#E0115F", "#FF2052", "#9B111E", "#FF5C8A"),
        G("opala", "Opala", GradientMotion.Drift, "#A8FFF1", "#FFC2F0", "#B8C6FF", "#FFF0B3"),
        // Contrastes
        G("gelo-e-fogo", "Gelo e fogo", GradientMotion.Mirror, "#00C8FF", "#FFFFFF", "#FF3D00", "#FFB300"),
        G("noite-e-dia", "Noite e dia", GradientMotion.Pulse, "#1C1CFF", "#FFC400", "#FF6A00", "#3D1CFF"),
        G("arco-iris-suave", "Arco-íris suave", GradientMotion.Flow, "#FF5A5F", "#FFB347", "#FFF275", "#7BE495", "#5AB9EA", "#B388EB"),
        G("prisma", "Prisma", GradientMotion.Ripple, "#FF0040", "#FFB300", "#00FF80", "#00A6FF", "#BF00FF"),

        // RGB puro
        Raw("rgb", "RGB puro", GradientMotion.Flow),
        Raw("rgb-turbo", "RGB turbo", GradientMotion.Flow, pace: 3.5),
        Raw("rgb-cascata", "RGB cascata", GradientMotion.Flow, repeats: 4),
        Raw("rgb-denso", "RGB denso", GradientMotion.Flow, repeats: 9, pace: 1.6),
        Raw("rgb-espelho", "RGB espelhado", GradientMotion.Mirror, repeats: 2),
        Raw("rgb-colisao", "RGB colisão", GradientMotion.Collide, repeats: 2),
        Raw("rgb-solido", "RGB sólido", GradientMotion.Sweep, pace: 1.3),
        Raw("rgb-zoom", "RGB zoom", GradientMotion.Zoom, repeats: 2),
        Raw("rgb-onda", "RGB onda", GradientMotion.Ripple, repeats: 2, pace: 1.4),
        Raw("rgb-glitter", "RGB glitter", GradientMotion.Glitter, repeats: 2),
        Raw("rgb-cometas", "RGB cometas", GradientMotion.Comets, pace: 1.6),
        Raw("rgb-blocos", "RGB blocos", GradientMotion.Chunks),
        Raw("rgb-vaivem", "RGB vai e vem", GradientMotion.Bounce),
        new PaletteGradientEffect("rgb-canais", "R, G e B", GradientMotion.Bands, ["#FF0000", "#00FF00", "#0000FF"], Rgbs),
        new PaletteGradientEffect("rgb-secundarias", "Ciano, magenta, amarelo", GradientMotion.Bands, ["#00FFFF", "#FF00FF", "#FFFF00"], Rgbs),
        new PaletteGradientEffect("rgb-todas", "Todas as cores", GradientMotion.CubeWalk, [], Rgbs),
        new PaletteGradientEffect("rgb-todas-rapido", "Todas as cores turbo", GradientMotion.CubeWalk, [], Rgbs, pace: 3),

        // Orgulho: bandeiras em faixas
        Flag("pride", "Orgulho", "#E40303", "#FF8C00", "#FFED00", "#008026", "#24408E", "#732982"),
        Flag("progress", "Progress", "#FFFFFF", "#FFAFC8", "#74D7EE", "#613915", "#000000", "#E40303", "#FF8C00", "#FFED00", "#008026", "#24408E", "#732982"),
        Flag("trans", "Trans", "#5BCEFA", "#F5A9B8", "#FFFFFF", "#F5A9B8", "#5BCEFA"),
        Flag("bi", "Bissexual", "#D60270", "#D60270", "#9B4F96", "#0038A8", "#0038A8"),
        Flag("pan", "Pansexual", "#FF218C", "#FFD800", "#21B1FF"),
        Flag("lesbica", "Lésbica", "#D52D00", "#EF7627", "#FF9A56", "#FFFFFF", "#D162A4", "#B55690", "#A30262"),
        Flag("gay", "Gay (MLM)", "#078D70", "#26CEAA", "#98E8C1", "#FFFFFF", "#7BADE2", "#5049CC", "#3D1A78"),
        Flag("nao-binario", "Não binário", "#FCF434", "#FFFFFF", "#9C59D1", "#2C2C2C"),
        Flag("assexual", "Assexual", "#000000", "#A3A3A3", "#FFFFFF", "#800080"),
        Flag("arromantico", "Arromântico", "#3DA542", "#A7D379", "#FFFFFF", "#A9A9A9", "#000000"),
        Flag("genero-fluido", "Gênero fluido", "#FF76A4", "#FFFFFF", "#C011D7", "#000000", "#2F3CBE"),
        Flag("agenero", "Agênero", "#000000", "#B9B9B9", "#FFFFFF", "#B8F483", "#FFFFFF", "#B9B9B9", "#000000"),
        Flag("genderqueer", "Genderqueer", "#B57EDC", "#FFFFFF", "#4A8123"),
        Flag("intersexo", "Intersexo", "#FFD800", "#7902AA"),
        Flag("polissexual", "Polissexual", "#F61CB9", "#07D569", "#1C92F6"),
        Flag("demissexual", "Demissexual", "#000000", "#FFFFFF", "#6E0070", "#D2D2D2"),
        // Orgulho: as mesmas cores, em versão suave
        Soft("pride-suave", "Orgulho suave", GradientMotion.Flow, "#E40303", "#FF8C00", "#FFED00", "#008026", "#24408E", "#732982"),
        Soft("pride-espelho", "Orgulho espelhado", GradientMotion.Mirror, "#E40303", "#FF8C00", "#FFED00", "#008026", "#24408E", "#732982"),
        Soft("pride-glitter", "Orgulho glitter", GradientMotion.Glitter, "#E40303", "#FF8C00", "#FFED00", "#008026", "#24408E", "#732982"),
        Soft("trans-suave", "Trans suave", GradientMotion.Drift, "#5BCEFA", "#F5A9B8", "#FFFFFF", "#F5A9B8"),
        Soft("bi-suave", "Bissexual suave", GradientMotion.Ripple, "#D60270", "#9B4F96", "#0038A8"),
        Soft("pan-suave", "Pansexual suave", GradientMotion.Collide, "#FF218C", "#FFD800", "#21B1FF"),
        Soft("lesbica-suave", "Lésbica suave", GradientMotion.Pulse, "#D52D00", "#FF9A56", "#FFFFFF", "#D162A4", "#A30262"),
        Soft("nb-suave", "Não binário suave", GradientMotion.Zoom, "#FCF434", "#FFFFFF", "#9C59D1"),

        // Datas e festa
        G("natal", "Natal", GradientMotion.Bands, "#FF0018", "#FFFFFF", "#00B140"),
        G("ano-novo", "Ano-novo", GradientMotion.Glitter, "#FFD700", "#FFFFFF", "#C0C0FF", "#FFB300"),
        G("halloween", "Halloween", GradientMotion.Ripple, "#FF6A00", "#7A00CC", "#FF8C00", "#39FF14"),
        G("carnaval", "Carnaval", GradientMotion.Chunks, "#FF0090", "#FFD600", "#00E5FF", "#7CFC00", "#FF5500"),
        G("sao-joao", "São João", GradientMotion.Bands, "#FF3B00", "#FFD000", "#0090FF", "#FF0080"),
        G("balada", "Balada", GradientMotion.Bounce, "#FF00E0", "#2A00FF", "#00FFD5"),
        G("disco", "Discoteca", GradientMotion.Glitter, "#FF00AA", "#FFB300", "#7A00FF", "#00D5FF"),
        G("rave", "Rave", GradientMotion.Collide, "#39FF14", "#FF00FF", "#00FFFF", "#FFFF00"),
        G("confete", "Confete", GradientMotion.Chunks, "#FF595E", "#FFCA3A", "#8AC926", "#1982C4", "#6A4C93"),
        G("brasil", "Brasil", GradientMotion.Bands, "#009C3B", "#FFDF00", "#002776", "#FFDF00"),
        // Sabores
        G("melancia", "Melancia", GradientMotion.Bands, "#FF2D55", "#FF6B81", "#FFFFFF", "#00B050"),
        G("acai", "Açaí", GradientMotion.Drift, "#5B0E91", "#B5179E", "#3C096C", "#9D4EDD"),
        G("napolitano", "Napolitano", GradientMotion.Bands, "#FF8FAB", "#FFF3D6", "#7A3E12"),
        G("chiclete", "Chiclete", GradientMotion.Bounce, "#FF70C5", "#7DE2FF", "#FFB3E6"),
        G("caipirinha", "Caipirinha", GradientMotion.Pulse, "#B6FF00", "#F4FF8F", "#6BE300"),
        G("cafe", "Café com leite", GradientMotion.Drift, "#8B4A16", "#D29A5B", "#F4DEB8", "#5E2F0C"),
        G("uva", "Uva", GradientMotion.Zoom, "#6A00F4", "#B100E8", "#2D00F7"),
        G("morango", "Morango", GradientMotion.Ripple, "#FF1744", "#FF8A9E", "#00C853", "#FF3D6E"),
        // Bichos
        G("pavao", "Pavão", GradientMotion.Mirror, "#00E0A4", "#0066FF", "#7A00FF", "#00C2B8"),
        G("flamingo", "Flamingo", GradientMotion.Drift, "#FF7BAC", "#FFA6C9", "#FF4F8B"),
        G("arara", "Arara", GradientMotion.Chunks, "#FF1E00", "#FFD000", "#0066FF"),
        G("borboleta", "Borboleta azul", GradientMotion.Collide, "#00B7FF", "#0030FF", "#00F0FF", "#3000C8"),
        G("medusa", "Água-viva", GradientMotion.Pulse, "#FF6AD5", "#8C7BFF", "#6AF2FF"),
        G("tigre", "Tigre", GradientMotion.Bands, "#FF8000", "#1A0A00", "#FFA733", "#1A0A00"),
        // Espaço
        G("marte", "Marte", GradientMotion.Drift, "#C1440E", "#E77D11", "#8A2B0E"),
        G("jupiter", "Júpiter", GradientMotion.Bands, "#D8A36B", "#F2DDB8", "#A75E2A", "#EBC999"),
        G("netuno", "Netuno", GradientMotion.Ripple, "#1F4FFF", "#00A2FF", "#3B2BFF"),
        G("tempestade-solar", "Tempestade solar", GradientMotion.Comets, "#FFD000", "#FF6A00", "#FF0040", "#FFFFFF"),
        G("buraco-de-minhoca", "Buraco de minhoca", GradientMotion.Zoom, "#00F0FF", "#7A00FF", "#FF00C8", "#001AFF"),
        G("constelacao", "Constelação", GradientMotion.Glitter, "#0B1E8A", "#3A0CA3", "#102A9E"),
        // Materiais e efeitos
        G("gasolina", "Gasolina na água", GradientMotion.Drift, "#FF00C8", "#00FFB2", "#FFD000", "#3D5CFF"),
        G("bolha", "Bolha de sabão", GradientMotion.Zoom, "#FFB3F0", "#B3F5FF", "#D9FFB3", "#D3B3FF"),
        G("vitral", "Vitral", GradientMotion.Chunks, "#D50032", "#0047BB", "#FFC72C", "#00843D", "#6A1B9A"),
        G("radioativo", "Radioativo", GradientMotion.Pulse, "#7CFF00", "#00FF55", "#E6FF00"),
        G("acido", "Ácido", GradientMotion.Bounce, "#CCFF00", "#FF00AA", "#00FFDD"),
        G("toxico", "Lagoa tóxica", GradientMotion.Ripple, "#39FF14", "#7A00FF", "#00FFA2"),
        G("cromo", "Cromo", GradientMotion.Flow, "#FFFFFF", "#8FA3B8", "#E6EEF5", "#5F7385"),
        G("gelo-seco", "Gelo seco", GradientMotion.Drift, "#FFFFFF", "#CFE8FF", "#9FD3FF"),
        G("eletrico", "Elétrico", GradientMotion.Comets, "#00E5FF", "#FFFFFF", "#7A5CFF", "#00A6FF"),
        G("metronomo", "Pêndulo de cor", GradientMotion.Bounce, "#FF3D00", "#FFC400", "#00E676", "#00B0FF"),    ];

    static PaletteGradientEffect G(string id, string name, GradientMotion motion, params string[] stops) => new(id, name, motion, stops);

    const string Rgbs = "RGB puro", Pride = "Orgulho";

    /// <summary>The raw spectrum at full saturation: no palette, every hue in order.</summary>
    static PaletteGradientEffect Raw(string id, string name, GradientMotion motion, double repeats = 1, double pace = 1) =>
        new(id, name, motion, [], Rgbs, repeats, pace, pure: true);

    static PaletteGradientEffect Flag(string id, string name, params string[] stripes) =>
        new(id, name, GradientMotion.Bands, stripes, Pride, pace: 0.75);

    static PaletteGradientEffect Soft(string id, string name, GradientMotion motion, params string[] stops) =>
        new(id, name, motion, stops, Pride);
}
