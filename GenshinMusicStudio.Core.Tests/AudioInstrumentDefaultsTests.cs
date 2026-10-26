using Xunit;

namespace GenshinMusicStudio.Core.Tests;

public sealed class AudioInstrumentDefaultsTests
{
    [Fact]
    public void Resolve_MissingSelectionUsesDefaultLyre()
    {
        using var root = new TemporaryDirectory();
        var lyre = root.CreateDirectory("Lyre");

        var resolved = AudioInstrumentDefaults.Resolve(null, root.Path);

        Assert.Equal(lyre, resolved);
    }

    [Fact]
    public void Resolve_ExplicitEmptySelectionKeepsWindowsSynthesizer()
    {
        using var root = new TemporaryDirectory();
        root.CreateDirectory("Lyre");

        var resolved = AudioInstrumentDefaults.Resolve(string.Empty, root.Path);

        Assert.Equal(string.Empty, resolved);
    }

    [Fact]
    public void Resolve_OldBuildPathUsesCurrentBundledLyre()
    {
        using var root = new TemporaryDirectory();
        var lyre = root.CreateDirectory("Lyre");
        var oldBuild = Path.Combine(root.Path, "old-build", "Assets", "Instruments", "Lyre");
        Directory.CreateDirectory(oldBuild);

        var resolved = AudioInstrumentDefaults.Resolve(oldBuild, root.Path);

        Assert.Equal(lyre, resolved);
    }

    [Fact]
    public void Resolve_UnknownSavedDirectoryKeepsExternalInstrument()
    {
        using var root = new TemporaryDirectory();
        root.CreateDirectory("Lyre");
        var external = Path.Combine(root.Path, "external", "Custom");
        Directory.CreateDirectory(external);

        var resolved = AudioInstrumentDefaults.Resolve(external, root.Path);

        Assert.Equal(external, resolved);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "GenshinMusicStudio.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateDirectory(string name)
        {
            var path = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
