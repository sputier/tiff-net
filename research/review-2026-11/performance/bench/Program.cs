using BenchmarkDotNet.Running;
using Bench;
using Eleris.TiffNet;

// quick [n] [filter]   : Stopwatch harness over every data file + writes + references
// mem ...              : peak working set / private bytes while reading one file (fresh process)
// phases [n]           : prototype "ideal" pipeline (Proto.cs)
// loop read <file> n | loop write <u16_24|f32rgb_24> <compression> n : body for dotnet-trace
// otherwise            : BenchmarkDotNet (--filter ...)
switch (args.FirstOrDefault())
{
    case "quick": Quick.Run(args); break;
    case "mem": Mem.Run(args); break;
    case "phases": Phases.Run(args); break;
    case "loop":
        if (args[1] == "read") for (int i = 0; i < int.Parse(args[3]); i++) TiffImage.Read(Data.P(args[2]));
        else
        {
            var img = args[2] == "u16_24" ? Data.U16_24 : Data.F32Rgb_24;
            var c = Enum.Parse<TiffCompression>(args[3]);
            for (int i = 0; i < int.Parse(args[4]); i++) img.Save(Data.P("loop_out.tif"), new TiffWriteOptions { Compression = c });
            File.Delete(Data.P("loop_out.tif"));
        }
        break;
    default: BenchmarkSwitcher.FromAssembly(typeof(Data).Assembly).Run(args); break;
}
