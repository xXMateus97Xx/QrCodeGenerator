using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace QrCodeGenerator;

public static class ReedSolomon
{
    public static void ReedSolomonComputeDivisor(Span<byte> result)
    {
        var degree = result.Length;
        if (degree < 1 || degree > 255)
            throw new ArgumentException("Degree out of range");

        result[degree - 1] = 1;

        if (Vector128.IsHardwareAccelerated || Vector256.IsHardwareAccelerated)
        {
            ReedSolomonComputeDivisorFast(result);
            return;
        }

        var root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (var j = 0; j < result.Length; j++)
            {
                result[j] = ReedSolomonMultiply(result[j], root);
                if (j + 1 < result.Length)
                    result[j] ^= result[j + 1];
            }

            root = ReedSolomonMultiply(root, 0x02);
        }
    }

    /// <summary>
    /// Versão vetorizada do cálculo do polinômio gerador (divisor) de Reed-Solomon de grau <c>result.Length</c>.
    /// <para>
    /// O polinômio é <c>(x - r^0)(x - r^1)...(x - r^(grau-1))</c> em GF(2^8), com <c>r = 0x02</c>. Os coeficientes ficam em
    /// <paramref name="result"/> do maior para o menor grau (o termo líder, sempre 1, é omitido) e o chamador já inicializou
    /// <c>result[grau - 1] = 1</c>. A cada iteração o polinômio atual é multiplicado por <c>(x - root)</c>, o que equivale a
    /// <c>result[j] = result[j] * root XOR result[j + 1]</c> para todo j (usando o valor antigo de <c>result[j + 1]</c>).
    /// </para>
    /// <list type="number">
    /// <item><description>Para cada uma das <c>grau</c> iterações, percorre <paramref name="result"/> em blocos usando
    /// <c>copy</c> como janela que vai sendo fatiada.</description></item>
    /// <item><description>Bloco de 256 bits: carrega 16 coeficientes <c>copy[0..15]</c>, cada um em uma lane de 16 bits
    /// (<c>short</c>), porque a multiplicação em GF(2^8) desloca os valores para a esquerda e precisa de bits extras
    /// antes da redução.</description></item>
    /// <item><description>Multiplica todas as lanes por <c>root</c> (broadcast) com
    /// <see cref="ReedSolomonMultiply(Vector256{short}, Vector256{short})"/>.</description></item>
    /// <item><description>Carrega o vizinho de cada coeficiente, <c>copy[1..16]</c>, ainda com os valores antigos (o elemento 16
    /// pertence ao próximo bloco, que ainda não foi modificado). No último bloco do array não existe <c>copy[16]</c>,
    /// então a última lane recebe 0.</description></item>
    /// <item><description>Faz o XOR (que é a soma em GF(2^8)) dos dois vetores.</description></item>
    /// <item><description>Reinterpreta o resultado como bytes e grava de volta só o byte baixo de cada <c>short</c>
    /// (índices pares, por ser little-endian), já que o resultado da multiplicação é sempre menor que 256.</description></item>
    /// <item><description>Avança 16 posições e repete; o que sobrar é tratado da mesma forma com <c>Vector128</c>
    /// (8 coeficientes por vez) e, por fim, com o laço escalar.</description></item>
    /// <item><description>Ao final da iteração, <c>root</c> é multiplicado por <c>0x02</c> para gerar a próxima raiz.</description></item>
    /// </list>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReedSolomonComputeDivisorFast(Span<byte> result)
    {
        var degree = result.Length;

        short root = 1;
        for (int i = 0; i < degree; i++)
        {
            var copy = result;

            if (Vector256.IsHardwareAccelerated && copy.Length >= Vector256<short>.Count)
            {
                var rootVec = Vector256.Create(root);

                while (copy.Length >= Vector256<short>.Count)
                {
                    var v = Vector256.Create(copy[0], copy[1], copy[2], copy[3], copy[4], copy[5], copy[6], copy[7],
                        copy[8], copy[9], copy[10], copy[11], copy[12], copy[13], copy[14], copy[15]);
                    v = ReedSolomonMultiply(v, rootVec);

                    Vector256<short> v2;
                    if (copy.Length > Vector256<short>.Count)
                        v2 = Vector256.Create(copy[1], copy[2], copy[3], copy[4], copy[5], copy[6], copy[7], copy[8],
                                copy[9], copy[10], copy[11], copy[12], copy[13], copy[14], copy[15], copy[16]);
                    else
                        v2 = Vector256.Create(copy[1], copy[2], copy[3], copy[4], copy[5], copy[6], copy[7],
                            copy[8], copy[9], copy[10], copy[11], copy[12], copy[13], copy[14], copy[15], 0);

                    v ^= v2;
                    var byteV = v.AsByte();

                    copy[0] = byteV[0];
                    copy[1] = byteV[2];
                    copy[2] = byteV[4];
                    copy[3] = byteV[6];
                    copy[4] = byteV[8];
                    copy[5] = byteV[10];
                    copy[6] = byteV[12];
                    copy[7] = byteV[14];
                    copy[8] = byteV[16];
                    copy[9] = byteV[18];
                    copy[10] = byteV[20];
                    copy[11] = byteV[22];
                    copy[12] = byteV[24];
                    copy[13] = byteV[26];
                    copy[14] = byteV[28];
                    copy[15] = byteV[30];

                    copy = copy.Slice(Vector256<short>.Count);
                }
            }

            if (Vector128.IsHardwareAccelerated && copy.Length >= Vector128<short>.Count)
            {
                var rootVec = Vector128.Create(root);

                while (copy.Length >= Vector128<short>.Count)
                {
                    var v = Vector128.Create(copy[0], copy[1], copy[2], copy[3],
                        copy[4], copy[5], copy[6], copy[7]);
                    v = ReedSolomonMultiply(v, rootVec);

                    Vector128<short> v2;
                    if (copy.Length > Vector128<short>.Count)
                        v2 = Vector128.Create(copy[1], copy[2], copy[3], copy[4], copy[5], copy[6], copy[7], copy[8]);
                    else
                        v2 = Vector128.Create(copy[1], copy[2], copy[3], copy[4], copy[5], copy[6], copy[7], 0);

                    v ^= v2;
                    var byteV = v.AsByte();

                    copy[0] = byteV[0];
                    copy[1] = byteV[2];
                    copy[2] = byteV[4];
                    copy[3] = byteV[6];
                    copy[4] = byteV[8];
                    copy[5] = byteV[10];
                    copy[6] = byteV[12];
                    copy[7] = byteV[14];

                    copy = copy.Slice(Vector128<short>.Count);
                }
            }

            if (copy.Length > 0)
            {
                for (var j = 0; j < copy.Length; j++)
                {
                    copy[j] = ReedSolomonMultiply(copy[j], root);
                    if (j + 1 < copy.Length)
                        copy[j] ^= copy[j + 1];
                }
            }

            root = ReedSolomonMultiply(root, 0x02);
        }
    }

    /// <summary>
    /// Multiplica <paramref name="x"/> por <paramref name="y"/> em GF(2^8) (polinômio de redução <c>0x11D</c>)
    /// para as 8 lanes ao mesmo tempo. Cada lane guarda um valor de 8 bits dentro de um <c>short</c>, para que o shift
    /// à esquerda tenha espaço para o bit 8 antes da redução.
    /// <para>
    /// Usa o algoritmo "shift-and-add" (multiplicação de camponês russo), percorrendo os bits de <paramref name="y"/>
    /// do mais significativo (bit 7) para o menos significativo (bit 0), sem nenhum desvio condicional por lane:
    /// </para>
    /// <list type="number">
    /// <item><description>Se todas as lanes de <paramref name="x"/> forem zero, o resultado é zero e retorna imediatamente.</description></item>
    /// <item><description><c>z</c> começa em zero. Para cada bit k de <paramref name="y"/>:</description></item>
    /// <item><description><c>z = (z &lt;&lt; 1) ^ ((z &gt;&gt; 7) * 0x11D)</c>: multiplica o acumulador por 2 em GF(2^8).
    /// <c>z &gt;&gt; 7</c> é o bit 7 do valor antigo (0 ou 1); multiplicá-lo por <c>0x11D</c> produz a máscara de redução,
    /// que é aplicada via XOR só nas lanes em que houve "estouro" para o bit 8.</description></item>
    /// <item><description><c>z ^= ((y &gt;&gt; k) &amp; 1) * x</c>: isola o bit k de cada lane de <paramref name="y"/> (0 ou 1) e o
    /// multiplica por <paramref name="x"/>, somando (XOR) <paramref name="x"/> ao acumulador apenas onde o bit está ligado.
    /// A multiplicação por 0/1 substitui um <c>if</c>, permitindo processar todas as lanes juntas.</description></item>
    /// <item><description>Após os 8 bits, cada lane de <c>z</c> contém o produto reduzido (menor que 256).</description></item>
    /// </list>
    /// </summary>
    private static Vector128<short> ReedSolomonMultiply(Vector128<short> x, Vector128<short> y)
    {
        if (x == Vector128<short>.Zero)
            return Vector128<short>.Zero;

        var z = Vector128<short>.Zero;
        var one = Vector128<short>.One;
        z ^= ((y >> 7) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 6) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 5) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 4) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 3) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 2) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 1) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= (y & one) * x;

        return z;
    }

    /// <summary>
    /// Versão de 256 bits (16 lanes) de <see cref="ReedSolomonMultiply(Vector128{short}, Vector128{short})"/>.
    /// Multiplica <paramref name="x"/> por <paramref name="y"/> em GF(2^8) (redução por <c>0x11D</c>) em todas as lanes
    /// ao mesmo tempo: para cada bit de <paramref name="y"/>, do 7 ao 0, dobra o acumulador aplicando a redução via XOR
    /// (<c>(z &lt;&lt; 1) ^ ((z &gt;&gt; 7) * 0x11D)</c>) e soma (XOR) <paramref name="x"/> multiplicado pelo bit atual (0 ou 1),
    /// evitando desvios condicionais por lane.
    /// </summary>
    private static Vector256<short> ReedSolomonMultiply(Vector256<short> x, Vector256<short> y)
    {
        if (x == Vector256<short>.Zero)
            return Vector256<short>.Zero;

        var z = Vector256<short>.Zero;
        var one = Vector256<short>.One;
        z ^= ((y >> 7) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 6) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 5) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 4) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 3) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 2) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 1) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= (y & one) * x;

        return z;
    }

    private static byte ReedSolomonMultiply(int x, int y)
    {
        if (x == 0) return 0;

        var z = 0;
        z ^= ((y >> 7) & 1) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 6) & 1) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 5) & 1) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 4) & 1) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 3) & 1) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 2) & 1) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 1) & 1) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= (y & 1) * x;

        return (byte)z;
    }

    public static void ReedSolomonComputeRemainder(ReadOnlySpan<byte> data, ReadOnlySpan<byte> divisor, Span<byte> destiny)
    {
        ref var destinyPtr = ref MemoryMarshal.GetReference(destiny);
        ref var divisorPtr = ref MemoryMarshal.GetReference(divisor);
        for (int i = 0; i < data.Length; i++)
        {
            var b = data[i];
            var factor = (b ^ destiny[0]) & 0xFF;
            destiny.Slice(1).CopyTo(destiny);

            Unsafe.Add(ref destinyPtr, destiny.Length - 1) = 0;
            for (int j = 0; j < destiny.Length; j++)
                Unsafe.Add(ref destinyPtr, j) = (byte)(Unsafe.Add(ref destinyPtr, j) ^ ReedSolomonMultiply(Unsafe.Add(ref divisorPtr, j) & 0xFF, factor));
        }
    }
}
