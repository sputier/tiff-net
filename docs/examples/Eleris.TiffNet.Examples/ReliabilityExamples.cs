namespace Eleris.TiffNet.Examples;

/// <summary>The examples of the page "Reliability and limits".</summary>
public static class ReliabilityExamples
{
    /// <summary>Telling a file that is too large for the limits from a damaged one.</summary>
    public static void TooLargeOrDamaged()
    {
        #region Example:ReliabilityExamples.TooLargeOrDamaged
        static string Explain(TiffException e) => e.Limit is null
            ? $"The file is damaged: {e.Message}"
            : $"The file is too large for this device ({e.Limit}).";

        Console.WriteLine(Explain(new TiffException("The image declares more pixels than the file holds.")));
        Console.WriteLine(Explain(new TiffException("The image needs 3 GB.", "MaxPixelBytes")));
        #endregion
    }

    /// <summary>Limits for files that come from anywhere.</summary>
    public static void UntrustedFiles()
    {
        #region Example:ReliabilityExamples.UntrustedFiles
        // On a phone, or a server reading files from anywhere: 256 MB for the pixels of an image, two decoding threads.
        var untrusted = TiffReadOptions.Constrained;

        Console.WriteLine($"Pixels: {untrusted.MaxPixelBytes >> 20} MB, threads: {untrusted.MaxDegreeOfParallelism}");
        #endregion
    }
}
