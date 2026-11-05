using BitMiracle.LibJpeg.Classic;

public static class JpegSeeds
{
    static byte[] Encode(int w, int h, int comps, bool prog, bool arith, int restart, bool optimize)
    {
        var c = new jpeg_compress_struct(new jpeg_error_mgr());
        var ms = new MemoryStream();
        c.jpeg_stdio_dest(ms);
        c.Image_width = w; c.Image_height = h; c.Input_components = comps;
        c.In_color_space = comps == 1 ? J_COLOR_SPACE.JCS_GRAYSCALE : J_COLOR_SPACE.JCS_RGB;
        c.jpeg_set_defaults();
        if (comps == 3) c.jpeg_set_colorspace(J_COLOR_SPACE.JCS_RGB); // no YCbCr / no subsampling
        c.jpeg_set_quality(75, true);
        if (prog) c.jpeg_simple_progression();
        c.arith_code = arith;
        if (optimize && !arith) c.Optimize_coding = true;
        if (restart > 0) c.Restart_interval = restart;
        c.jpeg_start_compress(true);
        var rnd = new Random(w * 31 + h);
        var row = new byte[1][];
        for (int y = 0; y < h; y++)
        {
            row[0] = new byte[w * comps];
            for (int i = 0; i < row[0].Length; i++) row[0][i] = (byte)(i * 3 + y * 5 + (rnd.Next(5) == 0 ? rnd.Next(64) : 0));
            c.jpeg_write_scanlines(row, 1);
        }
        c.jpeg_finish_compress();
        return ms.ToArray();
    }

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        int n = 0;
        foreach (var comps in new[] { 1, 3 })
        foreach (var prog in new[] { false, true })
        foreach (var arith in new[] { false })
        foreach (var restart in new[] { 0, 2 })
        {
            int w = comps == 1 ? 40 : 24, h = comps == 1 ? 24 : 16;
            var j = Encode(w, h, comps, prog, arith, restart, n % 2 == 0);
            string tag = $"{comps}c-{(prog ? "prog" : "base")}-{(arith ? "arith" : "huff")}-r{restart}";
            foreach (var comp in new[] { 7, 6 })
            {
                var b = new TiffBuilder { Payload = j };
                b.Long(256, w).Long(257, h).Short(258, Enumerable.Repeat(8, comps).ToArray()).Short(259, comp).Short(262, comps == 1 ? 1 : 2)
                 .Short(277, comps).Long(278, h).Long(273, TiffBuilder.PayloadOffset).Long(279, j.Length).Short(284, 1);
                if (comp == 6) b.Short(512, 1).Long(513, TiffBuilder.PayloadOffset).Long(514, j.Length);
                var bytes = b.Build();
                File.WriteAllBytes(Path.Combine(dir, $"jpg-{(comp == 7 ? "new" : "old")}-{tag}.tif"), bytes);
                n++;
            }
        }
        Console.WriteLine($"{n} jpeg seeds");
    }
}
