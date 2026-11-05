using Eleris.TiffNet.Internal;
using Eleris.TiffNet.Internal.Structure;
using Eleris.TiffNet.Tests.Fixtures;

namespace Eleris.TiffNet.Tests.Directories;

// The hostile files whose defect is in the structure: they must fail while the directories and tables are read, quickly and
// without allocating what they declare. They measure allocations, so they do not run in parallel with other tests.
[Collection(nameof(Measurement))]
public class HostileStructureTests
{
    [Fact]
    public void Hostile_directories_fail_within_bounds()
    {
        foreach (string name in new[] { "unknowntags-5k.tif", "unknowntags-20k.tif", "bigtiff-unknowntags-32k.tif" })
        {
            var hostile = HostileAssert.Cases.Single(c => c.ToString() == name);
            HostileAssert.FailsWithinBounds(hostile, () =>
            {
                var reader = new StructureReader(TiffSource.FromMemory(File.ReadAllBytes(hostile.Path)), new ResourceBudget(hostile.Options, name));
                reader.ReadDirectory(reader.Header.FirstDirectory);
            });
        }
    }

    [Fact]
    public void Hostile_tables_fail_quickly_without_allocating_them()
    {
        foreach (string name in new[] { "nstrips-2G.tif", "nstrips-256M.tif", "nstrips-256M-short.tif", "ntiles-256M.tif",
                     "dos-nstrips-2G.tif", "dos-nstrips-268M.tif" })
        {
            var hostile = HostileAssert.Cases.Single(c => c.ToString() == name);
            HostileAssert.FailsWithinBounds(hostile, () => ImageDescriptorTests.Images(File.ReadAllBytes(hostile.Path), hostile.Options));
        }
    }
}
