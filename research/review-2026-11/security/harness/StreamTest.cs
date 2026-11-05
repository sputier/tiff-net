using Eleris.TiffNet;

public static class StreamTest
{
    sealed class Throwing(byte[] data, Exception ex, long after) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) { if (Position >= after) throw ex; return base.Read(buffer, offset, count); }
    }

    public static int Run(string[] a)
    {
        var data = File.ReadAllBytes(a[0]);
        foreach (var ex in new Exception[] { new OperationCanceledException("cancelled by user"), new InvalidOperationException("custom"), new TimeoutException("net timeout"), new IOException("io") })
        {
            try { TiffImage.Read(new Throwing(data, ex, 64)); Console.WriteLine($"{ex.GetType().Name}: read OK?"); }
            catch (Exception e) { Console.WriteLine($"{ex.GetType().Name} thrown by stream -> caller sees {e.GetType().Name}: {Program.Trunc(e.Message)}"); }
        }
        // Path leak in messages
        try { TiffImage.Read(a[1]); } catch (Exception e) { Console.WriteLine($"path message: {e.Message}"); }
        return 0;
    }
}
