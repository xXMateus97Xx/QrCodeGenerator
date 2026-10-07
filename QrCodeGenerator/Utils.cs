using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace QrCodeGenerator;

public static class Utils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEven(this int n) => (n & 1) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SimpleAbs(this int n) => unchecked(n >= 0 ? n : -n);

    /// <summary>
    /// Calcula o resto <c>a % b</c> para as 16 lanes de <paramref name="a"/> ao mesmo tempo.
    /// <para>
    /// Não existe instrução SIMD de divisão inteira, então o resto é obtido pela identidade
    /// <c>a % b = a - (a / b) * b</c>:
    /// </para>
    /// <list type="number">
    /// <item><description>O quociente <c>a / b</c> é calculado por <see cref="Div(Vector256{short}, float)"/>,
    /// que multiplica pelo inverso <paramref name="multiplier"/> (= <c>1f / b</c>, pré-calculado pelo chamador).</description></item>
    /// <item><description>O quociente é multiplicado por <paramref name="b"/> (broadcast para todas as lanes).</description></item>
    /// <item><description>O produto é subtraído de <paramref name="a"/>, sobrando apenas o resto de cada lane.</description></item>
    /// </list>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<short> Mod(Vector256<short> a, short b, float multiplier)
    {
        return a - (Div(a, multiplier) * b);
    }

    /// <summary>
    /// Versão de 128 bits (8 lanes) de <see cref="Mod(Vector256{short}, short, float)"/>.
    /// Calcula <c>a % b</c> como <c>a - (a / b) * b</c>, onde o quociente vem de
    /// <see cref="Div(Vector128{short}, float)"/> usando o inverso pré-calculado <paramref name="multiplier"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Mod(Vector128<short> a, short b, float multiplier)
    {
        return a - (Div(a, multiplier) * b);
    }

    /// <summary>
    /// Calcula <c>a % 3</c> para as 16 lanes de <paramref name="a"/> sem usar divisão.
    /// <list type="number">
    /// <item><description>Obtém o quociente <c>a / 3</c> com <see cref="Div3(Vector256{short})"/> (multiplicação + shift).</description></item>
    /// <item><description>Multiplica o quociente por 3 e subtrai de <paramref name="a"/>, resultando no resto (0, 1 ou 2).</description></item>
    /// </list>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<short> Mod3(Vector256<short> a)
    {
        var v = Div3(a);
        return a - (v * 3);
    }

    /// <summary>
    /// Versão de 128 bits (8 lanes) de <see cref="Mod3(Vector256{short})"/>:
    /// <c>a % 3 = a - (a / 3) * 3</c>, com o quociente vindo de <see cref="Div3(Vector128{short})"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Mod3(Vector128<short> a)
    {
        var v = Div3(a);
        return a - (v * 3);
    }

    /// <summary>
    /// Calcula a divisão inteira <c>a / 3</c> para as 16 lanes de <paramref name="a"/> usando a técnica
    /// de "multiplicação pelo inverso mágico" (o mesmo truque que os compiladores usam para dividir por constantes).
    /// <list type="number">
    /// <item><description><c>Widen</c> separa o vetor de 16 <c>short</c> em dois vetores de 8 <c>int</c>
    /// (metade inferior e superior). Isso é necessário porque o produto intermediário não cabe em 16 bits.</description></item>
    /// <item><description>Cada metade é multiplicada por <c>0x5556</c> (21846 = (2^16 + 2) / 3, ou seja, aproximadamente 2^16 / 3).</description></item>
    /// <item><description>O shift aritmético de 16 bits para a direita equivale a dividir por 2^16, então o resultado
    /// é <c>a * (2^16 / 3) / 2^16 = a / 3</c> (truncado). O erro de arredondamento do multiplicador é pequeno o suficiente
    /// para que o resultado seja exato para todo <c>a</c> entre 0 e 32767.</description></item>
    /// <item><description><c>Narrow</c> junta as duas metades de volta em um único vetor de 16 <c>short</c>.</description></item>
    /// </list>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<short> Div3(Vector256<short> a)
    {
        var (lower, upper) = Vector256.Widen(a);
        lower *= 0x5556;
        lower >>= 16;
        upper *= 0x5556;
        upper >>= 16;
        return Vector256.Narrow(lower, upper);
    }

    /// <summary>
    /// Versão de 128 bits (8 lanes) de <see cref="Div3(Vector256{short})"/>.
    /// Alarga para <c>int</c>, multiplica por <c>0x5556</c> (aproximadamente 2^16 / 3), desloca 16 bits para a direita
    /// (dividindo por 2^16) e estreita de volta para <c>short</c>, obtendo <c>a / 3</c> sem instrução de divisão.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Div3(Vector128<short> a)
    {
        var (lower, upper) = Vector128.Widen(a);
        lower *= 0x5556;
        lower >>= 16;
        upper *= 0x5556;
        upper >>= 16;
        return Vector128.Narrow(lower, upper);
    }

    /// <summary>
    /// Calcula a divisão inteira <c>a / b</c> para as 16 lanes de <paramref name="a"/>, recebendo
    /// <paramref name="mul"/> = <c>1f / b</c> já pré-calculado.
    /// <para>
    /// Como não existe divisão inteira em SIMD, a divisão é trocada por uma multiplicação em ponto flutuante:
    /// </para>
    /// <list type="number">
    /// <item><description><c>Widen</c> separa os 16 <c>short</c> em dois vetores de 8 <c>int</c>, porque a conversão
    /// para <c>float</c> só existe a partir de inteiros de 32 bits.</description></item>
    /// <item><description>Cada metade é convertida para <c>float</c> e multiplicada pelo inverso <paramref name="mul"/>.</description></item>
    /// <item><description><c>ConvertToInt32</c> volta para inteiro truncando a parte fracionária, o que equivale à divisão inteira.</description></item>
    /// <item><description><c>Narrow</c> junta as duas metades de volta em 16 <c>short</c>.</description></item>
    /// </list>
    /// <para>
    /// <paramref name="mul"/> precisa estar arredondado para cima (ex.: <c>MathF.BitIncrement(1f / b)</c>): se ficar menor que
    /// <c>1 / b</c>, múltiplos exatos de <c>b</c> resultam em algo como <c>k - 0.00001</c>, e o truncamento devolve <c>k - 1</c>.
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<short> Div(Vector256<short> a, float mul)
    {
        var (lower, upper) = Vector256.Widen(a);
        lower = Vector256.ConvertToInt32(Vector256.ConvertToSingle(lower) * mul);
        upper = Vector256.ConvertToInt32(Vector256.ConvertToSingle(upper) * mul);

        return Vector256.Narrow(lower, upper);
    }

    /// <summary>
    /// Versão de 128 bits (8 lanes) de <see cref="Div(Vector256{short}, float)"/>.
    /// Alarga para <c>int</c>, converte para <c>float</c>, multiplica pelo inverso <paramref name="mul"/> (= <c>1f / b</c>),
    /// trunca de volta para <c>int</c> e estreita para <c>short</c>, obtendo <c>a / b</c> sem instrução de divisão.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Div(Vector128<short> a, float mul)
    {
        var (lower, upper) = Vector128.Widen(a);
        lower = Vector128.ConvertToInt32(Vector128.ConvertToSingle(lower) * mul);
        upper = Vector128.ConvertToInt32(Vector128.ConvertToSingle(upper) * mul);

        return Vector128.Narrow(lower, upper);
    }
}