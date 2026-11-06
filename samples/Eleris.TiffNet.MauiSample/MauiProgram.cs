namespace Eleris.TiffNet.MauiSample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<App>().Build();
}

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(new MainPage());
}

/// <summary>Writes a TIFF file in the data folder of the application, reads it back, and shows what it found.</summary>
public sealed class MainPage : ContentPage
{
    private readonly Label _result = new() { Text = "Press the button.", Margin = 20 };

    public MainPage()
    {
        var button = new Button { Text = "Write and read a TIFF file", Margin = 20 };
        button.Clicked += async (_, _) => _result.Text = await Task.Run(TryCheck);
        Content = new VerticalStackLayout { Children = { button, _result } };

        // Run once at start, and write the result to the log (logcat on Android), so that the check can be automated.
        Loaded += async (_, _) =>
        {
            string result = await Task.Run(TryCheck);
            _result.Text = result;
            foreach (string line in result.Split(Environment.NewLine))
            {
                Console.WriteLine($"TIFFNET-CHECK {line}");
            }
        };
    }

    /// <summary>The check, its failure shown rather than ending the application (an event handler must not throw).</summary>
    private static string TryCheck()
    {
        try
        {
            return Check();
        }
        catch (Exception ex)
        {
            return $"FAILED: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>The check the documentation describes: every compression, written and read back on the device.</summary>
    public static string Check()
    {
        string folder = FileSystem.AppDataDirectory;
        var random = new Random(52);
        ushort[] grey = [.. Enumerable.Range(0, 640 * 480).Select(_ => (ushort)random.Next(65536))];
        var image = TiffImage.Create<ushort>(grey, 640, 480);
        var lines = new List<string>();
        foreach (var compression in new[] { TiffCompression.None, TiffCompression.PackBits, TiffCompression.Lzw, TiffCompression.Deflate })
        {
            string path = Path.Combine(folder, $"sample-{compression}.tif");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            new TiffFile().Add(image).Save(path, TiffWriteOptions.Default with { Compression = compression });
            using var file = TiffFile.Open(path, TiffReadOptions.Default with { MaxPixelBytes = 256L << 20 });
            bool same = file.Primary.ReadRawPixels<ushort>().AsSpan().SequenceEqual(grey);
            lines.Add($"{compression}: {(same ? "ok" : "DIFFERENT")} ({new FileInfo(path).Length / 1024} KB, {clock.ElapsedMilliseconds} ms)");
            File.Delete(path);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
