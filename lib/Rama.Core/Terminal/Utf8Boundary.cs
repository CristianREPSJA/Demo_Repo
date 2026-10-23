namespace Rama.Core.Terminal;

// Detecta secuencias UTF-8 cortadas al final de un bloque de bytes.
public static class Utf8Boundary
{
    // Bytes del final que forman una secuencia válida pero incompleta (0..3).
    public static int IncompleteTailLength(ReadOnlySpan<byte> data)
    {
        var limit = Math.Min(3, data.Length);
        for (var back = 1; back <= limit; back++)
        {
            var b = data[^back];
            if ((b & 0xC0) == 0x80)
                continue; // byte de continuación: seguir buscando el inicio

            var needed = SequenceLength(b);
            return needed > back ? back : 0;
        }
        return 0;
    }

    private static int SequenceLength(byte lead)
    {
        if ((lead & 0xE0) == 0xC0)
            return 2;
        if ((lead & 0xF0) == 0xE0)
            return 3;
        if ((lead & 0xF8) == 0xF0)
            return 4;
        return 1; // ASCII o byte inválido: no se retiene
    }
}
