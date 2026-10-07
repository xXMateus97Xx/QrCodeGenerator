using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace QrCodeGenerator;

public partial class QrCode
{
    private void ApplyMask(int msk, ref ModuleState ptr)
    {
        var size = _size;

        if (msk == 0)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var apply = (x + y).IsEven() && !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction);
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
        else if (msk == 1)
        {
            for (var y = 0; y < size; y++)
            {
                var isEven = y.IsEven();
                for (var x = 0; x < size; x++)
                {
                    var apply = isEven && !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction);
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
        else if (msk == 2)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var apply = x % 3 == 0 && !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction);
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
        else if (msk == 3)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var apply = (x + y) % 3 == 0 && !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction);
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
        else if (msk == 4)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var apply = (x / 3 + y / 2).IsEven() && !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction);
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
        else if (msk == 5)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var apply = !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction) && ((x * y) & 1) + x * y % 3 == 0;
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
        else if (msk == 6)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var apply = !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction) && ((x * y & 1) + x * y % 3).IsEven();
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
        else
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var apply = !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction) && (((x + y) & 1) + x * y % 3).IsEven();
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
    }

    /// <summary>
    /// Versão vetorizada de <see cref="ApplyMask"/>: aplica o padrão de máscara <paramref name="msk"/> (0 a 7) na matriz,
    /// processando 32 módulos por iteração com <see cref="Vector256{T}"/> (ou 16 com <see cref="Vector128{T}"/>).
    /// <para>
    /// A matriz é tratada como um array linear de <c>size * size</c> bytes (<see cref="ModuleState"/>), onde a posição
    /// linear <c>pos</c> corresponde a <c>y = pos / size</c> (linha) e <c>x = pos % size</c> (coluna). Cada byte guarda
    /// <see cref="ModuleState.Module"/> (cor do módulo (x, y)) e <see cref="ModuleState.Reversed"/> (cor do módulo
    /// transposto, usado para varrer colunas como se fossem linhas).
    /// </para>
    /// <list type="number">
    /// <item><description>Valida a máscara e, sem aceleração SIMD, cai no <see cref="ApplyMask"/> escalar.</description></item>
    /// <item><description>Pré-calcula <c>versionMultipler = 1f / size</c>, usado para trocar divisões por multiplicações
    /// em <see cref="Utils.Div(Vector256{short}, float)"/> e <see cref="Utils.Mod(Vector256{short}, short, float)"/>.</description></item>
    /// <item><description>Carrega 32 bytes da matriz e os alarga (<c>Widen</c>) para dois vetores de 16 <c>short</c>
    /// (<c>modules</c> e <c>modules2</c>), pois as contas da máscara (ex.: <c>x * y</c>, até 176 * 176) não cabem em 8 bits.</description></item>
    /// <item><description>Monta as posições lineares das 16 lanes somando <c>pos</c> ao vetor de índices [0, 1, ..., 15]
    /// e calcula <c>y</c> e <c>x</c> de todas as lanes de uma vez com <c>Div</c>/<c>Mod</c>.</description></item>
    /// <item><description><c>apply</c> começa ligado (todos os bits 1) nas lanes que não são módulos de função
    /// (<see cref="ModuleState.IsFunction"/>), pois esses nunca são mascarados. Em seguida
    /// <see cref="CalculateMask(int, Vector256{short}, Vector256{short}, Vector256{short})"/> mantém ligadas apenas as lanes
    /// em que a fórmula da máscara é verdadeira.</description></item>
    /// <item><description>Repete os dois passos anteriores para a segunda metade (posições <c>pos + 16</c> até <c>pos + 31</c>).</description></item>
    /// <item><description><c>Narrow</c> junta os dois resultados em uma máscara de 32 bytes (0xFF = inverter, 0x00 = manter) e o XOR
    /// com o bit <see cref="ModuleState.Module"/> atual produz <c>finalApply</c>, que é a nova cor de cada módulo
    /// (aplicar a máscara é inverter a cor).</description></item>
    /// <item><description>Com <c>ConditionalSelect</c> são montados <c>toAdd</c> (bits a ligar via OR) e <c>toRemove</c>
    /// (bits a manter via AND), atualizando apenas o bit <see cref="ModuleState.Module"/> sem desvios condicionais: lanes com
    /// <c>finalApply</c> ligado recebem o bit <c>Module</c> e lanes com <c>finalApply</c> desligado o perdem. Os demais
    /// flags do byte não são alterados. O vetor resultante é gravado de volta na matriz.</description></item>
    /// <item><description>O bit <see cref="ModuleState.Reversed"/> fica na posição transposta <c>(x, y) -&gt; x * size + y</c>,
    /// que não é contígua, então não dá para gravá-lo com um store vetorial. <c>ExtractMostSignificantBits</c> converte
    /// <c>finalApply</c> em um inteiro de 32 bits (bit i = lane i), <c>x</c> e <c>y</c> são estreitados para bytes e
    /// <see cref="ApplyReverseMask(ref ModuleState, Vector256{byte}, Vector256{byte}, uint)"/> grava cada lane individualmente.</description></item>
    /// <item><description>Avança 32 posições. O que sobra é processado com o mesmo algoritmo em <see cref="Vector128{T}"/>
    /// (16 módulos por iteração) e, por fim, com o laço escalar usando <see cref="CalculateMask(int, int, int, ModuleState)"/>
    /// e <see cref="SetMask"/>.</description></item>
    /// </list>
    /// </summary>
    private void ApplyMaskFast(int msk, ref ModuleState ptr)
    {
        if (msk < 0 || msk > 7)
            throw new ArgumentException("Mask value out of range");

        if (!Vector256.IsHardwareAccelerated && !Vector128.IsHardwareAccelerated)
        {
            ApplyMask(msk, ref ptr);
            return;
        }

        var size = _size;
        var version = (size - 17) / 4;
        var sizeShort = (short)size;
        var versionMultipler = GetVersionMultiplier(version);

        ref var current = ref Unsafe.As<ModuleState, byte>(ref ptr);
        ref var end = ref Unsafe.Add(ref current, size * size);
        short pos = 0;

        if (Vector256.IsHardwareAccelerated)
        {
            Vector256<short> idx;
#if NET9_0_OR_GREATER
            idx = Vector256<short>.Indices;
#else
            idx = Vector256.Create(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);
#endif

            var isFunction = Vector256.Create((short)ModuleState.IsFunction);
            var module = Vector256.Create((byte)ModuleState.Module);
            var moduleReverse = ~module;
            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref current, Vector256<byte>.Count), ref end))
            {
                var allModules = Vector256.LoadUnsafe(ref current);
                var (modules, modules2) = Vector256.Widen(allModules.AsSByte());

                var posV = Vector256.Create(pos) + idx;
                var y = Utils.Div(posV, versionMultipler);
                var x = Utils.Mod(posV, sizeShort, versionMultipler);

                var apply = Vector256.Equals(modules & isFunction, Vector256<short>.Zero);
                apply = CalculateMask(msk, x, y, apply);

                posV = Vector256.Create((short)(pos + (short)Vector256<short>.Count)) + idx;
                var y2 = Utils.Div(posV, versionMultipler);
                var x2 = Utils.Mod(posV, sizeShort, versionMultipler);

                var apply2 = Vector256.Equals(modules2 & isFunction, Vector256<short>.Zero);
                apply2 = CalculateMask(msk, x2, y2, apply2);

                var finalApply = Vector256.Narrow(apply, apply2).AsByte();
                finalApply ^= Vector256.Equals(allModules & module, module);

                var toAdd = Vector256.ConditionalSelect(finalApply, module, Vector256<byte>.Zero);
                var toRemove = Vector256.ConditionalSelect(finalApply, Vector256<byte>.AllBitsSet, moduleReverse);

                allModules |= toAdd;
                allModules &= toRemove;

                allModules.StoreUnsafe(ref current);

                var mask = finalApply.ExtractMostSignificantBits();
                var xb = Vector256.Narrow(x, x2).AsByte();
                var yb = Vector256.Narrow(y, y2).AsByte();
                ApplyReverseMask(ref ptr, xb, yb, mask);

                current = ref Unsafe.Add(ref current, Vector256<byte>.Count);
                pos += (short)Vector256<byte>.Count;
            }
        }

        if (Vector128.IsHardwareAccelerated && Unsafe.IsAddressLessThan(ref Unsafe.Add(ref current, Vector128<byte>.Count), ref end))
        {
            Vector128<short> idx;
#if NET9_0_OR_GREATER
            idx = Vector128<short>.Indices;
#else
            idx = Vector128.Create(0, 1, 2, 3, 4, 5, 6, 7);
#endif

            var isFunction = Vector128.Create((short)ModuleState.IsFunction);
            var module = Vector128.Create((byte)ModuleState.Module);
            var moduleReverse = ~module;
            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref current, Vector128<byte>.Count), ref end))
            {
                var allModules = Vector128.LoadUnsafe(ref current).AsByte();
                var (modules, modules2) = Vector128.Widen(allModules.AsSByte());

                var posV = Vector128.Create(pos) + idx;
                var y = Utils.Div(posV, versionMultipler);
                var x = Utils.Mod(posV, sizeShort, versionMultipler);

                var apply = Vector128.Equals(modules & isFunction, Vector128<short>.Zero);
                apply = CalculateMask(msk, x, y, apply);

                posV = Vector128.Create((short)(pos + (short)Vector128<short>.Count)) + idx;
                var y2 = Utils.Div(posV, versionMultipler);
                var x2 = Utils.Mod(posV, sizeShort, versionMultipler);

                var apply2 = Vector128.Equals(modules2 & isFunction, Vector128<short>.Zero);
                apply2 = CalculateMask(msk, x2, y2, apply2);

                var finalApply = Vector128.Narrow(apply, apply2).AsByte();
                finalApply ^= Vector128.Equals(allModules & module, module);

                var toAdd = Vector128.ConditionalSelect(finalApply, module, Vector128<byte>.Zero);
                var toRemove = Vector128.ConditionalSelect(finalApply, Vector128<byte>.AllBitsSet, moduleReverse);

                allModules |= toAdd;
                allModules &= toRemove;
                allModules.StoreUnsafe(ref current);

                var mask = finalApply.ExtractMostSignificantBits();
                var xb = Vector128.Narrow(x, x2).AsByte();
                var yb = Vector128.Narrow(y, y2).AsByte();
                ApplyReverseMask(ref ptr, xb, yb, mask);

                current = ref Unsafe.Add(ref current, Vector128<byte>.Count);
                pos += (short)Vector128<byte>.Count;
            }
        }

        while (Unsafe.IsAddressLessThan(ref current, ref end))
        {
            var y = pos / size;
            var x = pos % size;
            var currentModule = Unsafe.As<byte, ModuleState>(ref current);
            var apply = CalculateMask(msk, x, y, currentModule);

            SetMask(x, y, apply, ref ptr, size);

            current = ref Unsafe.Add(ref current, 1);
            pos++;
        }
    }

    /// <summary>
    /// Complemento de <see cref="ApplyMaskFast"/> para o bloco de 32 módulos: atualiza o flag
    /// <see cref="ModuleState.Reversed"/> na posição transposta de cada módulo.
    /// <para>
    /// Como as posições transpostas <c>x * size + y</c> não são contíguas na memória, não existe um store vetorial
    /// que as atualize de uma vez, então este passo é escalar:
    /// </para>
    /// <list type="number">
    /// <item><description>Para cada lane i (0 a 31), lê a coluna <c>x[i]</c> e a linha <c>y[i]</c> do módulo.</description></item>
    /// <item><description>Lê o bit i de <paramref name="mask"/>, gerado por <c>ExtractMostSignificantBits</c>, que é a nova cor do módulo.</description></item>
    /// <item><description>Liga ou desliga <see cref="ModuleState.Reversed"/> no byte da posição <c>x[i] * size + y[i]</c>.</description></item>
    /// </list>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyReverseMask(ref ModuleState ptr, Vector256<byte> x, Vector256<byte> y, uint mask)
    {
        var size = _size;
        for (var i = 0; i < Vector256<byte>.Count; i++)
        {
            int xs = x[i],
                ys = y[i];
            var isSet = GetBit(mask, i);
            ref var p = ref Unsafe.Add(ref ptr, xs * size + ys);
            if (isSet)
                p |= ModuleState.Reversed;
            else
                p &= ~ModuleState.Reversed;
        }
    }

    /// <summary>
    /// Versão de 16 módulos de <see cref="ApplyReverseMask(ref ModuleState, Vector256{byte}, Vector256{byte}, uint)"/>:
    /// para cada lane i, liga ou desliga <see cref="ModuleState.Reversed"/> na posição transposta
    /// <c>x[i] * size + y[i]</c> conforme o bit i de <paramref name="mask"/> (a nova cor do módulo).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyReverseMask(ref ModuleState ptr, Vector128<byte> x, Vector128<byte> y, uint mask)
    {
        var size = _size;
        for (var i = 0; i < Vector128<byte>.Count; i++)
        {
            int xs = x[i],
                ys = y[i];
            var isSet = GetBit(mask, i);
            ref var p = ref Unsafe.Add(ref ptr, xs * size + ys);
            if (isSet)
                p |= ModuleState.Reversed;
            else
                p &= ~ModuleState.Reversed;
        }
    }

    private static bool CalculateMask(int msk, int x, int y, ModuleState currentModule)
    {
        bool apply;
        if (msk == 0)
            apply = (x + y).IsEven() && !currentModule.HasFlag(ModuleState.IsFunction);
        else if (msk == 1)
            apply = y.IsEven() && !currentModule.HasFlag(ModuleState.IsFunction);
        else if (msk == 2)
            apply = x % 3 == 0 && !currentModule.HasFlag(ModuleState.IsFunction);
        else if (msk == 3)
            apply = (x + y) % 3 == 0 && !currentModule.HasFlag(ModuleState.IsFunction);
        else if (msk == 4)
            apply = (x / 3 + y / 2).IsEven() && !currentModule.HasFlag(ModuleState.IsFunction);
        else if (msk == 5)
            apply = !currentModule.HasFlag(ModuleState.IsFunction) && ((x * y) & 1) + x * y % 3 == 0;
        else if (msk == 6)
            apply = !currentModule.HasFlag(ModuleState.IsFunction) && ((x * y & 1) + x * y % 3).IsEven();
        else
            apply = !currentModule.HasFlag(ModuleState.IsFunction) && (((x + y) & 1) + x * y % 3).IsEven();
        return apply;
    }

    /// <summary>
    /// Avalia a fórmula da máscara <paramref name="msk"/> para 8 módulos ao mesmo tempo e devolve <paramref name="apply"/>
    /// mantendo ligadas (todos os bits 1) apenas as lanes em que a máscara deve ser aplicada.
    /// <para>
    /// Cada fórmula da especificação tem a forma <c>expressão == 0</c>. O método calcula a expressão em <c>r</c> para todas
    /// as lanes, sem divisões (paridade com <c>&amp; 1</c>, metade com <c>&gt;&gt; 1</c>, divisão/resto por 3 com
    /// <see cref="Utils.Div3(Vector128{short})"/> e <see cref="Utils.Mod3(Vector128{short})"/>):
    /// </para>
    /// <list type="bullet">
    /// <item><description>0: <c>(x + y) % 2</c></description></item>
    /// <item><description>1: <c>y % 2</c></description></item>
    /// <item><description>2: <c>x % 3</c></description></item>
    /// <item><description>3: <c>(x + y) % 3</c></description></item>
    /// <item><description>4: <c>(x / 3 + y / 2) % 2</c></description></item>
    /// <item><description>5: <c>(x * y) % 2 + (x * y) % 3</c></description></item>
    /// <item><description>6: <c>((x * y) % 2 + (x * y) % 3) % 2</c></description></item>
    /// <item><description>7: <c>((x + y) % 2 + (x * y) % 3) % 2</c></description></item>
    /// </list>
    /// <para>
    /// Por fim, <c>Vector128.Equals(r, 0)</c> gera uma máscara com todos os bits ligados nas lanes em que <c>r == 0</c>,
    /// e o AND com <paramref name="apply"/> descarta as lanes que já estavam desligadas (módulos de função).
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> CalculateMask(int msk, Vector128<short> x, Vector128<short> y, Vector128<short> apply)
    {
        var one = Vector128<short>.One;
        Vector128<short> r;
        if (msk == 0)
            r = (x + y) & one;
        else if (msk == 1)
            r = y & one;
        else if (msk == 2)
            r = Utils.Mod3(x);
        else if (msk == 3)
            r = Utils.Mod3(x + y);
        else if (msk == 4)
            r = (Utils.Div3(x) + (y >> 1)) & one;
        else
        {
            var m = x * y;
            var modM = Utils.Mod3(m);
            if (msk == 5)
                r = (m & one) + modM;
            else if (msk == 6)
                r = ((x * y & one) + modM) & one;
            else
                r = (((x + y) & one) + modM) & one;
        }
        return apply & Vector128.Equals(r, Vector128<short>.Zero);
    }

    /// <summary>
    /// Versão de 256 bits (16 lanes) de <see cref="CalculateMask(int, Vector128{short}, Vector128{short}, Vector128{short})"/>.
    /// Calcula a expressão da máscara <paramref name="msk"/> em <c>r</c> para todas as lanes usando apenas AND, shifts,
    /// somas, multiplicações e <see cref="Utils.Div3(Vector256{short})"/>/<see cref="Utils.Mod3(Vector256{short})"/>,
    /// compara <c>r</c> com zero (lanes iguais ficam com todos os bits ligados) e faz AND com <paramref name="apply"/>,
    /// para que módulos de função nunca sejam mascarados.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<short> CalculateMask(int msk, Vector256<short> x, Vector256<short> y, Vector256<short> apply)
    {
        var one = Vector256<short>.One;
        Vector256<short> r;
        if (msk == 0)
            r = (x + y) & one;
        else if (msk == 1)
            r = y & one;
        else if (msk == 2)
            r = Utils.Mod3(x);
        else if (msk == 3)
            r = Utils.Mod3(x + y);
        else if (msk == 4)
            r = (Utils.Div3(x) + (y >> 1)) & one;
        else
        {
            var m = x * y;
            var modM = Utils.Mod3(m);
            if (msk == 5)
                r = (m & one) + modM;
            else if (msk == 6)
                r = ((x * y & one) + modM) & one;
            else
                r = (((x + y) & one) + modM) & one;
        }
        return apply & Vector256.Equals(r, Vector256<short>.Zero);
    }

    private static void SetMask(int x, int y, bool apply, ref ModuleState ptr, int size)
    {
        ref var p = ref Unsafe.Add(ref ptr, y * size + x);
        if (apply ^ p.HasFlag(ModuleState.Module))
        {
            p |= ModuleState.Module;
            Unsafe.Add(ref ptr, x * size + y) |= ModuleState.Reversed;
        }
        else
        {
            p &= ~ModuleState.Module;
            Unsafe.Add(ref ptr, x * size + y) &= ~ModuleState.Reversed;
        }
    }

    private int GetPenaltyScoreFast(ref ModuleState ptr, int currentScore)
    {
        var result = 0;
        var size = _size;

        ReadOnlySpan<ModuleState> current = MemoryMarshal.CreateReadOnlySpan(ref ptr, size), next;

        var black = 0;

        black += SumBlack(current);
        ptr = ref Unsafe.Add(ref ptr, size);

        for (int i = 1; i < size; i++)
        {
            next = MemoryMarshal.CreateReadOnlySpan(ref ptr, size);

            black += SumBlack(next);

            result += FindLinePatterns(current, ModuleState.Module);

            result += FindLinePatterns(current, ModuleState.Reversed);

            result += FindSquarePattern(current, next);

            if (result >= currentScore)
                return -1;

            ptr = ref Unsafe.Add(ref ptr, size);

            current = next;
        }

        result += FindLinePatterns(current, ModuleState.Module);
        result += FindLinePatterns(current, ModuleState.Reversed);

        var total = size * size;

        var k = ((black * 20 - total * 10).SimpleAbs() + total - 1) / total - 1;
        result += k * PENALTY_N4;

        return result < currentScore ? result : -1;
    }

    /// <summary>
    /// Regra de penalidade N2: soma <c>PENALTY_N2</c> para cada bloco 2x2 de módulos da mesma cor formado pelas linhas
    /// consecutivas <paramref name="line1"/> e <paramref name="line2"/>, comparando 32 colunas por vez.
    /// <list type="number">
    /// <item><description>Carrega 32 bytes de cada linha (<c>vec1</c> e <c>vec2</c>) a partir da mesma coluna.</description></item>
    /// <item><description>Para os módulos escuros: isola o bit <see cref="ModuleState.Module"/> com AND, compara com
    /// <c>Equals</c> nas duas linhas e faz AND dos resultados. A lane i fica ligada quando a coluna i é escura nas duas
    /// linhas, ou seja, forma um "dominó" vertical 2x1.</description></item>
    /// <item><description><c>ExtractMostSignificantBits</c> transforma esse resultado em uma máscara de bits (bit i = coluna i).
    /// Um bloco 2x2 existe onde dois bits vizinhos estão ligados (colunas i e i + 1).</description></item>
    /// <item><description>Para contar os pares de bits vizinhos: <c>TrailingZeroCount</c> pula as colunas que não formam dominó;
    /// se os dois bits mais baixos forem <c>11</c>, soma a penalidade; depois desloca 1 bit e repete. Uma sequência de n bits
    /// ligados gera n - 1 blocos 2x2, igual à contagem escalar.</description></item>
    /// <item><description>Repete o mesmo processo para os módulos claros, comparando o bit <see cref="ModuleState.Module"/>
    /// isolado com zero.</description></item>
    /// <item><description>Avança só <c>Count - 1</c> colunas, para que a última coluna de um bloco seja a primeira do próximo e
    /// o par que cruza a fronteira entre blocos também seja avaliado.</description></item>
    /// <item><description>Para as colunas que sobram, carrega os últimos 32 bytes da linha (terminando exatamente no fim, para
    /// não ler fora do array) e desloca a máscara para a direita descartando as colunas já processadas.</description></item>
    /// <item><description>Se só houver aceleração de 128 bits, executa o mesmo algoritmo com 16 colunas por vez; sem SIMD, usa o laço escalar.</description></item>
    /// </list>
    /// </summary>
    private static int FindSquarePattern(ReadOnlySpan<ModuleState> line1, ReadOnlySpan<ModuleState> line2)
    {
        var result = 0;

        ref var l1ptr = ref Unsafe.As<ModuleState, byte>(ref MemoryMarshal.GetReference(line1));
        ref var l1end = ref Unsafe.Add(ref l1ptr, line1.Length);
        ref var l2ptr = ref Unsafe.As<ModuleState, byte>(ref MemoryMarshal.GetReference(line2));
        ref var l2end = ref Unsafe.Add(ref l2ptr, line2.Length);

        if (Vector256.IsHardwareAccelerated && line1.Length >= Vector256<byte>.Count)
        {
            var mvec = Vector256.Create((byte)ModuleState.Module);
            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref l1ptr, Vector256<byte>.Count), ref l1end))
            {
                var vec1 = Vector256.LoadUnsafe(ref l1ptr);
                var vec2 = Vector256.LoadUnsafe(ref l2ptr);

                var eq = Vector256.Equals(vec1 & mvec, mvec) & Vector256.Equals(vec2 & mvec, mvec);
                var mask = eq.ExtractMostSignificantBits();

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }

                eq = Vector256.Equals(vec1 & mvec, Vector256<byte>.Zero) & Vector256.Equals(vec2 & mvec, Vector256<byte>.Zero);
                mask = eq.ExtractMostSignificantBits();

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }

                l1ptr = ref Unsafe.Add(ref l1ptr, Vector256<byte>.Count - 1);
                l2ptr = ref Unsafe.Add(ref l2ptr, Vector256<byte>.Count - 1);
            }

            if (Unsafe.IsAddressLessThan(ref l1ptr, ref l1end))
            {
                var vec1 = Vector256.LoadUnsafe(ref Unsafe.Subtract(ref l1end, Vector256<byte>.Count));
                var vec2 = Vector256.LoadUnsafe(ref Unsafe.Subtract(ref l2end, Vector256<byte>.Count));

                var eq = Vector256.Equals(vec1 & mvec, mvec) & Vector256.Equals(vec2 & mvec, mvec);
                var mask = eq.ExtractMostSignificantBits();

                mask >>= Vector256<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref l1ptr, ref l1end);

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }

                eq = Vector256.Equals(vec1 & mvec, Vector256<byte>.Zero) & Vector256.Equals(vec2 & mvec, Vector256<byte>.Zero);
                mask = eq.ExtractMostSignificantBits();

                mask >>= Vector256<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref l1ptr, ref l1end);

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }
            }
        }
        else if (Vector128.IsHardwareAccelerated && line1.Length >= Vector128<byte>.Count)
        {
            var mvec = Vector128.Create((byte)ModuleState.Module);
            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref l1ptr, Vector128<byte>.Count), ref l1end))
            {
                var vec1 = Vector128.LoadUnsafe(ref l1ptr);
                var vec2 = Vector128.LoadUnsafe(ref l2ptr);

                var eq = Vector128.Equals(vec1 & mvec, mvec) & Vector128.Equals(vec2 & mvec, mvec);
                var mask = eq.ExtractMostSignificantBits();

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }

                eq = Vector128.Equals(vec1 & mvec, Vector128<byte>.Zero) & Vector128.Equals(vec2 & mvec, Vector128<byte>.Zero);
                mask = eq.ExtractMostSignificantBits();

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }

                l1ptr = ref Unsafe.Add(ref l1ptr, Vector128<byte>.Count - 1);
                l2ptr = ref Unsafe.Add(ref l2ptr, Vector128<byte>.Count - 1);
            }

            if (Unsafe.IsAddressLessThan(ref l1ptr, ref l1end))
            {
                var vec1 = Vector128.LoadUnsafe(ref Unsafe.Subtract(ref l1end, Vector128<byte>.Count));
                var vec2 = Vector128.LoadUnsafe(ref Unsafe.Subtract(ref l2end, Vector128<byte>.Count));

                var eq = Vector128.Equals(vec1 & mvec, mvec) & Vector128.Equals(vec2 & mvec, mvec);
                var mask = eq.ExtractMostSignificantBits();

                mask >>= Vector128<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref l1ptr, ref l1end);

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }

                eq = Vector128.Equals(vec1 & mvec, Vector128<byte>.Zero) & Vector128.Equals(vec2 & mvec, Vector128<byte>.Zero);
                mask = eq.ExtractMostSignificantBits();

                mask >>= Vector128<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref l1ptr, ref l1end);

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }
            }
        }
        else
        {
            // only the color matters, the other flags must be ignored
            const byte module = (byte)ModuleState.Module;
            var p1 = l1ptr & module;
            var p2 = l2ptr & module;
            l1ptr = ref Unsafe.Add(ref l1ptr, 1);
            l2ptr = ref Unsafe.Add(ref l2ptr, 1);
            while (Unsafe.IsAddressLessThan(ref l1ptr, ref l1end))
            {
                var n1 = l1ptr & module;
                var n2 = l2ptr & module;
                if (p1 == p2 && n1 == n2 && p1 == n1)
                    result += PENALTY_N2;

                p1 = n1;
                p2 = n2;

                l1ptr = ref Unsafe.Add(ref l1ptr, 1);
                l2ptr = ref Unsafe.Add(ref l2ptr, 1);
            }
        }

        return result;
    }

    /// <summary>
    /// Regras de penalidade N1 e N3 para uma linha, com o mesmo resultado da implementação de referência
    /// (<see cref="PenaltyIteration"/> e <see cref="FinderPenaltyTerminateAndCount"/>), mas processando uma sequência
    /// (run) de módulos da mesma cor por vez em vez de um módulo por vez.
    /// <para>
    /// <paramref name="flag"/> escolhe o que é analisado: <see cref="ModuleState.Module"/> varre a linha em si e
    /// <see cref="ModuleState.Reversed"/> varre a coluna correspondente (o flag guarda a matriz transposta).
    /// </para>
    /// <list type="number">
    /// <item><description><see cref="GetDarkBits"/> converte a linha em um bitmap (bit i = módulo i escuro) usando
    /// comparações SIMD de 32 ou 16 módulos por vez.</description></item>
    /// <item><description><see cref="NextColorChange"/> encontra o fim de cada sequência com <c>TrailingZeroCount</c> sobre o
    /// bitmap (invertido quando a sequência é escura), pulando até 64 módulos por instrução. Como as cores sempre se
    /// alternam, o laço processa pares (clara, escura) sem precisar testar a cor de cada sequência; só a primeira sequência
    /// clara pode ter comprimento zero (quando a linha começa escura).</description></item>
    /// <item><description>N1: cada sequência com 5 ou mais módulos soma <c>PENALTY_N1 + (comprimento - 5)</c>.</description></item>
    /// <item><description>N3: os comprimentos das sequências entram em um histórico de 7 posições
    /// (<see cref="RunHistory.Add"/>). A primeira sequência clara recebe <c>size</c> extra, porque a borda do QR conta como
    /// clara. Sempre que termina uma sequência clara, <see cref="RunHistory.CountPatterns"/> verifica se as últimas
    /// sequências formam o padrão <c>n:n:3n:n:n</c> com uma margem clara de pelo menos <c>4n</c> de um dos lados.</description></item>
    /// <item><description>No fim da linha a última sequência é fechada com a borda clara (mais <c>size</c>) e o histórico é
    /// verificado uma última vez.</description></item>
    /// </list>
    /// </summary>
    private static int FindLinePatterns(ReadOnlySpan<ModuleState> modules, ModuleState flag)
    {
        const int words = (MAX_VERSION * 4 + 17 + 63) >> 6;

        var size = modules.Length;
        Span<ulong> darkBits = stackalloc ulong[words];
        GetDarkBits(modules, flag, darkBits);
        ref var dark = ref MemoryMarshal.GetReference(darkBits);

        var history = new RunHistory();
        var result = 0;

        // runs always alternate, so the line is processed as (light, dark) pairs; the first light run may be empty
        var pos = NextColorChange(ref dark, words, 0, false, size);
        var lightLength = pos;
        result += RunPenalty(lightLength);

        while (pos < size)
        {
            // a light run ended: check if it closes a finder-like pattern
            history.Add(lightLength, size);
            result += history.CountPatterns() * PENALTY_N3;

            var end = NextColorChange(ref dark, words, pos, true, size);
            var darkLength = end - pos;
            result += RunPenalty(darkLength);
            history.Add(darkLength, size);
            pos = end;

            if (pos == size)
            {
                // line ended on a dark run, the border after it counts as a light run
                history.Add(size, size);
                return result + history.CountPatterns() * PENALTY_N3;
            }

            end = NextColorChange(ref dark, words, pos, false, size);
            lightLength = end - pos;
            result += RunPenalty(lightLength);
            pos = end;
        }

        // line ended on a light run, which is extended by the light border
        history.Add(lightLength + size, size);
        return result + history.CountPatterns() * PENALTY_N3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int RunPenalty(int length) => length >= 5 ? PENALTY_N1 + (length - 5) : 0;

    /// <summary>
    /// Preenche <paramref name="bits"/> com o bitmap da linha: bit i ligado quando o módulo i tem <paramref name="flag"/>.
    /// <list type="number">
    /// <item><description>Carrega 32 bytes, isola <paramref name="flag"/> com AND e compara com <c>Equals</c>;
    /// <c>ExtractMostSignificantBits</c> transforma o resultado em 32 bits (bit i = lane i).</description></item>
    /// <item><description>Os bits são gravados na posição do bloco. Como o deslocamento é sempre múltiplo do tamanho do
    /// vetor, os bits de um bloco nunca cruzam a fronteira entre duas palavras de 64 bits.</description></item>
    /// <item><description>Os módulos que sobram usam uma última carga terminando no fim da linha (sobrepondo módulos já
    /// lidos, para não ler fora do array) e a máscara é deslocada para descartar a parte já gravada.</description></item>
    /// <item><description>Com apenas 128 bits o processo é o mesmo com 16 módulos por vez; sem SIMD, bit a bit.</description></item>
    /// </list>
    /// </summary>
    private static void GetDarkBits(ReadOnlySpan<ModuleState> modules, ModuleState flag, Span<ulong> bits)
    {
        ref var bitsRef = ref MemoryMarshal.GetReference(bits);
        ref var start = ref Unsafe.As<ModuleState, byte>(ref MemoryMarshal.GetReference(modules));
        ref var ptr = ref start;
        ref var end = ref Unsafe.Add(ref ptr, modules.Length);

        if (Vector256.IsHardwareAccelerated && modules.Length >= Vector256<byte>.Count)
        {
            var flagv = Vector256.Create((byte)flag);

            while (!Unsafe.IsAddressGreaterThan(ref Unsafe.Add(ref ptr, Vector256<byte>.Count), ref end))
            {
                var mask = Vector256.Equals(Vector256.LoadUnsafe(ref ptr) & flagv, flagv).ExtractMostSignificantBits();
                SetDarkBits(ref bitsRef, (int)(nuint)Unsafe.ByteOffset(ref start, ref ptr), mask);
                ptr = ref Unsafe.Add(ref ptr, Vector256<byte>.Count);
            }

            if (Unsafe.IsAddressLessThan(ref ptr, ref end))
            {
                var vec = Vector256.LoadUnsafe(ref Unsafe.Subtract(ref end, Vector256<byte>.Count));
                var mask = Vector256.Equals(vec & flagv, flagv).ExtractMostSignificantBits();
                var shift = Vector256<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref ptr, ref end);
                SetDarkBits(ref bitsRef, (int)(nuint)Unsafe.ByteOffset(ref start, ref ptr), mask >> shift);
                ptr = ref end;
            }
        }
        else if (Vector128.IsHardwareAccelerated && modules.Length >= Vector128<byte>.Count)
        {
            var flagv = Vector128.Create((byte)flag);

            while (!Unsafe.IsAddressGreaterThan(ref Unsafe.Add(ref ptr, Vector128<byte>.Count), ref end))
            {
                var mask = Vector128.Equals(Vector128.LoadUnsafe(ref ptr) & flagv, flagv).ExtractMostSignificantBits();
                SetDarkBits(ref bitsRef, (int)(nuint)Unsafe.ByteOffset(ref start, ref ptr), mask);
                ptr = ref Unsafe.Add(ref ptr, Vector128<byte>.Count);
            }

            if (Unsafe.IsAddressLessThan(ref ptr, ref end))
            {
                var vec = Vector128.LoadUnsafe(ref Unsafe.Subtract(ref end, Vector128<byte>.Count));
                var mask = Vector128.Equals(vec & flagv, flagv).ExtractMostSignificantBits();
                var shift = Vector128<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref ptr, ref end);
                SetDarkBits(ref bitsRef, (int)(nuint)Unsafe.ByteOffset(ref start, ref ptr), mask >> shift);
                ptr = ref end;
            }
        }

        while (Unsafe.IsAddressLessThan(ref ptr, ref end))
        {
            if (((ModuleState)ptr & flag) != 0)
                SetDarkBits(ref bitsRef, (int)(nuint)Unsafe.ByteOffset(ref start, ref ptr), 1);

            ptr = ref Unsafe.Add(ref ptr, 1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetDarkBits(ref ulong bits, int bit, uint mask)
    {
        // bit is always aligned to the vector size, so mask never crosses a word boundary
        Unsafe.Add(ref bits, bit >> 6) |= (ulong)mask << (bit & 63);
    }

    /// <summary>
    /// Retorna a posição do primeiro módulo a partir de <paramref name="pos"/> cuja cor é diferente de
    /// <paramref name="color"/>, ou <paramref name="size"/> se a sequência for até o fim da linha.
    /// <list type="number">
    /// <item><description>Se a sequência é escura, a palavra é invertida, assim o próximo módulo claro vira o próximo bit ligado.</description></item>
    /// <item><description>Os bits antes de <paramref name="pos"/> são zerados e <c>TrailingZeroCount</c> devolve a posição
    /// do primeiro bit ligado; se a palavra inteira for zero, passa para a próxima.</description></item>
    /// <item><description>Os bits depois do fim da linha são zero no bitmap (viram 1 quando invertidos), então o resultado é
    /// limitado a <paramref name="size"/>.</description></item>
    /// </list>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NextColorChange(ref ulong dark, int words, int pos, bool color, int size)
    {
        var invert = color ? ulong.MaxValue : 0UL;
        var w = pos >> 6;
        var bits = (Unsafe.Add(ref dark, w) ^ invert) & (ulong.MaxValue << (pos & 63));

        while (bits == 0)
        {
            if (++w == words)
                return size;
            bits = Unsafe.Add(ref dark, w) ^ invert;
        }

        return Math.Min((w << 6) + BitOperations.TrailingZeroCount(bits), size);
    }

    /// <summary>
    /// Histórico dos comprimentos das 7 últimas sequências de uma linha (<c>H0</c> é a mais recente), usado pela regra N3.
    /// Os campos são variáveis locais comuns, então inserir uma sequência é só uma cadeia de atribuições, sem cópia de memória.
    /// </summary>
    private struct RunHistory
    {
        private int H0, H1, H2, H3, H4, H5, H6;

        /// <summary>
        /// Insere o comprimento de uma sequência como a mais recente, deslocando as anteriores. Se o histórico ainda estiver
        /// vazio, soma <paramref name="size"/> ao comprimento, porque a borda do QR conta como uma sequência clara.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int length, int size)
        {
            if (H0 == 0)
                length += size;

            H6 = H5;
            H5 = H4;
            H4 = H3;
            H3 = H2;
            H2 = H1;
            H1 = H0;
            H0 = length;
        }

        /// <summary>
        /// Conta quantos padrões parecidos com o finder pattern terminam no histórico (0, 1 ou 2).
        /// <list type="number">
        /// <item><description><c>n = H1</c> é a unidade do padrão (a última sequência escura).</description></item>
        /// <item><description>O núcleo exige que <c>H2..H5</c> seja <c>n, 3n, n, n</c>.</description></item>
        /// <item><description>Conta um padrão se a sequência clara mais recente (<c>H0</c>) tiver pelo menos <c>4n</c> e a
        /// mais antiga (<c>H6</c>) pelo menos <c>n</c>, e outro no caso simétrico.</description></item>
        /// </list>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int CountPatterns()
        {
            var n = H1;
            if (n <= 0 || H2 != n || H3 != n * 3 || H4 != n || H5 != n)
                return 0;

            return (H0 >= n * 4 && H6 >= n ? 1 : 0)
                + (H6 >= n * 4 && H0 >= n ? 1 : 0);
        }
    }

    /// <summary>
    /// Conta quantos módulos escuros (<see cref="ModuleState.Module"/>) existem na linha <paramref name="mptr"/>, usado pela
    /// regra de penalidade N4 (proporção de escuros), verificando 32 módulos por vez.
    /// <list type="number">
    /// <item><description>Carrega 32 bytes e isola o bit <see cref="ModuleState.Module"/> com AND.</description></item>
    /// <item><description><c>Equals</c> com o vetor de <c>Module</c> deixa ligadas as lanes escuras e
    /// <c>ExtractMostSignificantBits</c> as transforma em uma máscara de 32 bits.</description></item>
    /// <item><description><c>BitOperations.PopCount</c> conta os bits ligados, que são os módulos escuros do bloco.</description></item>
    /// <item><description>Para o restante, carrega os últimos 32 bytes da linha (sobrepondo módulos já contados) e desloca a
    /// máscara para a direita descartando os já contados antes do <c>PopCount</c>.</description></item>
    /// <item><description>Com apenas 128 bits usa o mesmo algoritmo com 16 módulos; sem SIMD, conta um a um.</description></item>
    /// </list>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SumBlack(ReadOnlySpan<ModuleState> mptr)
    {
        var count = 0;

        if (Vector256.IsHardwareAccelerated || Vector128.IsHardwareAccelerated)
        {
            ref var ptr = ref Unsafe.As<ModuleState, byte>(ref MemoryMarshal.GetReference(mptr));
            ref var end = ref Unsafe.Add(ref ptr, mptr.Length);

            if (Vector256.IsHardwareAccelerated && mptr.Length >= Vector256<byte>.Count)
            {
                var module = Vector256.Create((byte)ModuleState.Module);
                while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref ptr, Vector256<byte>.Count), ref end))
                {
                    var vec = Vector256.LoadUnsafe(ref ptr);
                    var mVec = vec & module;

                    var mask = Vector256.Equals(mVec, module).ExtractMostSignificantBits();
                    count += BitOperations.PopCount(mask);

                    ptr = ref Unsafe.Add(ref ptr, Vector256<byte>.Count);
                }

                if (Unsafe.IsAddressLessThan(ref ptr, ref end))
                {
                    var vec = Vector256.LoadUnsafe(ref Unsafe.Subtract(ref end, Vector256<byte>.Count));
                    var mVec = vec & module;

                    var mask = Vector256.Equals(mVec, module).ExtractMostSignificantBits();
                    count += BitOperations.PopCount(mask >> (Vector256<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref ptr, ref end)));
                }
            }
            else if (Vector128.IsHardwareAccelerated && mptr.Length >= Vector128<byte>.Count)
            {
                var module = Vector128.Create((byte)ModuleState.Module);
                while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref ptr, Vector128<byte>.Count), ref end))
                {
                    var vec = Vector128.LoadUnsafe(ref ptr);
                    var mVec = vec & module;

                    var mask = Vector128.Equals(mVec, module).ExtractMostSignificantBits();
                    count += BitOperations.PopCount(mask);

                    ptr = ref Unsafe.Add(ref ptr, Vector128<byte>.Count);
                }

                if (Unsafe.IsAddressLessThan(ref ptr, ref end))
                {
                    var vec = Vector128.LoadUnsafe(ref Unsafe.Subtract(ref end, Vector128<byte>.Count));
                    var mVec = vec & module;

                    var mask = Vector128.Equals(mVec, module).ExtractMostSignificantBits();
                    count += BitOperations.PopCount(mask >> (Vector128<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref ptr, ref end)));
                }
            }
        }
        else
        {
            for (var i = 0; i < mptr.Length; i++)
            {
                var mi = mptr[i];
                var m = mi & ModuleState.Module;
                if (m == ModuleState.Module)
                    count++;
            }
        }

        return count;
    }

    private int GetPenaltyScore(ref ModuleState ptr, int currentScore)
    {
        if (Vector128.IsHardwareAccelerated || Vector256.IsHardwareAccelerated)
            return GetPenaltyScoreFast(ref ptr, currentScore);

        var result = 0;
        var size = _size;

        Span<int> history = size * 2 <= 256 ? stackalloc int[256] : new int[size * 2];

        ref var xPtr = ref MemoryMarshal.GetReference(history);
        ref var yPtr = ref Unsafe.Add(ref xPtr, size);

        for (int y = 0; y < size; y++)
        {
            xPtr = 0;
            yPtr = 0;

            PenaltyState xState = new() { RunHistory = ref xPtr }, yState = new() { RunHistory = ref yPtr };

            for (int x = 0; x < size; x++)
            {
                var mod = Unsafe.Add(ref ptr, y * size + x);
                xState.Current = mod.HasFlag(ModuleState.Module);
                yState.Current = mod.HasFlag(ModuleState.Reversed);

                result += PenaltyIteration(ref xState);
                result += PenaltyIteration(ref yState);

                if (x < size - 1 && y < size - 1)
                {
                    if (xState.Current == Unsafe.Add(ref ptr, y * size + x + 1).HasFlag(ModuleState.Module) &&
                        xState.Current == Unsafe.Add(ref ptr, (y + 1) * size + x).HasFlag(ModuleState.Module) &&
                        xState.Current == Unsafe.Add(ref ptr, (y + 1) * size + x + 1).HasFlag(ModuleState.Module))
                        result += PENALTY_N2;
                }
            }
            result += FinderPenaltyTerminateAndCount(ref xState) * PENALTY_N3;
            result += FinderPenaltyTerminateAndCount(ref yState) * PENALTY_N3;

            if (result >= currentScore)
                return -1;
        }

        if (result >= currentScore)
            return -1;

        var black = CountModules(ref ptr);

        var total = size * size;
        var k = ((black * 20 - total * 10).SimpleAbs() + total - 1) / total - 1;
        result += k * PENALTY_N4;

        return result < currentScore ? result : -1;
    }

    private int PenaltyIteration(ref PenaltyState state)
    {
        if (state.Current == state.RunColor)
        {
            state.RunCordinate++;

            if (state.RunCordinate < 5)
                return 0;

            if (state.RunCordinate == 5)
                return PENALTY_N1;

            return 1;
        }

        var result = 0;

        FinderPenaltyAddHistory(ref state);
        if (!state.RunColor)
            result = FinderPenaltyCountPatterns(ref state.RunHistory, state.HistoryPosition) * PENALTY_N3;
        state.RunColor = state.Current;
        state.RunCordinate = 1;
        return result;
    }

    /// <summary>
    /// Conta os módulos escuros (<see cref="ModuleState.Module"/>) de toda a matriz (<c>size * size</c> bytes), usado pela
    /// regra de penalidade N4 no caminho escalar.
    /// <list type="number">
    /// <item><description>Percorre a matriz linear em blocos de 32 bytes: isola o bit <c>Module</c> com AND, compara com
    /// <c>Equals</c>, converte em máscara de bits com <c>ExtractMostSignificantBits</c> e soma <c>PopCount</c> da máscara.</description></item>
    /// <item><description>O que sobrar é processado em blocos de 16 bytes com <see cref="Vector128{T}"/>, do mesmo jeito.</description></item>
    /// <item><description>Os últimos bytes (menos que um vetor) são contados um a um.</description></item>
    /// </list>
    /// </summary>
    private int CountModules(ref ModuleState modulesPtr)
    {
        var size = _size;
        ref var ptr = ref Unsafe.As<ModuleState, byte>(ref modulesPtr);
        ref var end = ref Unsafe.Add(ref ptr, size * size);
        var result = 0;

        if (Vector256.IsHardwareAccelerated)
        {
            var black = Vector256.Create((byte)ModuleState.Module);
            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref ptr, Vector256<byte>.Count), ref end))
            {
                var vec = Vector256.LoadUnsafe(ref ptr);
                vec &= black;
                var isBlack = Vector256.Equals(vec, black);
                var mask = isBlack.ExtractMostSignificantBits();
                result += BitOperations.PopCount(mask);
                ptr = ref Unsafe.Add(ref ptr, Vector256<byte>.Count);
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            var black = Vector128.Create((byte)ModuleState.Module);
            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref ptr, Vector128<byte>.Count), ref end))
            {
                var vec = Vector128.LoadUnsafe(ref ptr);
                vec &= black;
                var isBlack = Vector128.Equals(vec, black);
                var mask = isBlack.ExtractMostSignificantBits();
                result += BitOperations.PopCount(mask);
                ptr = ref Unsafe.Add(ref ptr, Vector128<byte>.Count);
            }
        }

        while (Unsafe.IsAddressLessThan(ref ptr, ref end))
        {
            if (((ModuleState)ptr).HasFlag(ModuleState.Module))
                result++;

            ptr = ref Unsafe.Add(ref ptr, 1);
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FinderPenaltyAddHistory(ref PenaltyState state)
    {
        var currentRunLength = state.RunCordinate;

        var position = Math.Min(state.HistoryPosition - 1, 0);

        if (Unsafe.Add(ref state.RunHistory, position) == 0)
            currentRunLength += _size;

        Unsafe.Add(ref state.RunHistory, state.HistoryPosition) = currentRunLength;
        state.HistoryPosition++;
    }

    /// <summary>
    /// Conta quantos padrões parecidos com o finder pattern (1:1:3:1:1 com 4 módulos claros antes ou depois) terminam no
    /// histórico de sequências <paramref name="runHistory"/> (comprimentos alternados claro/escuro), retornando 0, 1 ou 2.
    /// <list type="number">
    /// <item><description><c>n</c> é o comprimento da sequência em <c>position - 2</c>, a unidade do padrão. Se for 0, não há padrão.</description></item>
    /// <item><description>O núcleo do padrão exige que as 4 sequências em <c>position - 6</c> a <c>position - 3</c> tenham
    /// comprimentos <c>n, n, 3n, n</c>. Com SIMD, as 4 são carregadas de uma vez em um <c>Vector128&lt;int&gt;</c> e comparadas
    /// com <c>n * (1, 1, 3, 1)</c>; o operador <c>==</c> só é verdadeiro se todas as lanes forem iguais, substituindo
    /// as 4 comparações escalares por uma só.</description></item>
    /// <item><description>Com o núcleo válido, conta um padrão se a sequência clara anterior (<c>n6</c>) tiver pelo menos <c>n</c>
    /// e a posterior (<c>n0</c>) pelo menos <c>4n</c>, e outro no caso simétrico.</description></item>
    /// </list>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FinderPenaltyCountPatterns(ref int runHistory, nuint position)
    {
        var n = Unsafe.Add(ref runHistory, position - 2);

        bool core = n > 0;
        if (!core)
            return 0;

        var n6 = Unsafe.Add(ref runHistory, position - 7);
        var n0 = Unsafe.Add(ref runHistory, position - 1);

        if (Vector128.IsHardwareAccelerated)
        {
            var hstVec = Vector128.LoadUnsafe(ref runHistory, position - 6);
            var nVec = Vector128.Create(n) * Vector128.Create(1, 1, 3, 1);
            core = hstVec == nVec;
        }
        else
        {
            core = Unsafe.Add(ref runHistory, position - 3) == n &&
            Unsafe.Add(ref runHistory, position - 4) == n * 3 &&
            Unsafe.Add(ref runHistory, position - 5) == n &&
            Unsafe.Add(ref runHistory, position - 6) == n;
        }

        return (core && n6 >= n && n0 >= n * 4 ? 1 : 0)
            + (core && n0 >= n && n6 >= n * 4 ? 1 : 0);
    }

    private int FinderPenaltyTerminateAndCount(ref PenaltyState state)
    {
        if (state.RunColor)
        {
            FinderPenaltyAddHistory(ref state);
            state.RunCordinate = 0;
        }
        state.RunCordinate += _size;
        FinderPenaltyAddHistory(ref state);
        return FinderPenaltyCountPatterns(ref state.RunHistory, state.HistoryPosition);
    }
}
