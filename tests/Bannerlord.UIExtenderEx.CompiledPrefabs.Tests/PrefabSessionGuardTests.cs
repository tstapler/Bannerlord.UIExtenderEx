using Bannerlord.UIExtenderEx.CompiledPrefabs;

using NUnit.Framework;

using System;
using System.IO;
using System.Linq;

namespace Bannerlord.UIExtenderEx.Tests.CompiledPrefabs;

/// <summary>
/// Verifies the marker and quarantine rules of <see cref="PrefabSessionGuard"/>.
/// <para>
/// How the manager drives it is verified in <see cref="CompiledPrefabManagerTests"/>.
/// </para>
/// </summary>
public class PrefabSessionGuardTests
{
    private string _directory = string.Empty;
    private DateTime _now = new(2026, 11, 13, 12, 37, 2, DateTimeKind.Utc);

    private string Archive => Path.Combine(_directory, PrefabCache.FileName);
    private string Marker => Path.Combine(_directory, PrefabSessionGuard.MarkerFileName);

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "UIExtenderEx-session-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch (Exception) { /* ignore */ }
    }

    private PrefabSessionGuard Create(int kept = 3) => new(_directory, () => _now, kept);

    private string[] Quarantined() =>
        [.. Directory.EnumerateFiles(_directory, PrefabCache.FileName + PrefabSessionGuard.QuarantineInfix + "*").Select(x => Path.GetFileName(x)).OrderBy(x => x, StringComparer.Ordinal)];

    [Test]
    public void NoMarker_LeavesTheArchiveAlone()
    {
        File.WriteAllBytes(Archive, [1, 2, 3]);

        Assert.That(Create().QuarantinePreviousSession(), Is.False);

        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(Quarantined(), Is.Empty);
    }

    [Test]
    public void MarkOpen_WritesTheMarker_AndMarkClosedRemovesIt()
    {
        var guard = Create();

        guard.MarkOpen();
        Assert.That(File.Exists(Marker), Is.True);
        Assert.That(File.ReadAllText(Marker), Does.Contain("pid=").And.Contain("utc=2026-11-13T12:37:02"));

        guard.MarkClosed();
        Assert.That(File.Exists(Marker), Is.False);
    }

    [Test]
    public void MarkOpenAndMarkClosed_AreIdempotent()
    {
        var guard = Create();

        guard.MarkClosed();
        guard.MarkOpen();
        guard.MarkOpen();
        guard.MarkClosed();
        guard.MarkClosed();

        Assert.That(File.Exists(Marker), Is.False);
    }

    [Test]
    public void MarkerLeftBehind_SetsTheArchiveAside_WithTheTimestampInItsName()
    {
        File.WriteAllBytes(Archive, [1, 2, 3]);
        Create().MarkOpen();

        Assert.That(Create().QuarantinePreviousSession(), Is.True);

        Assert.That(File.Exists(Archive), Is.False);
        Assert.That(File.Exists(Marker), Is.False, "this session starts clean");
        Assert.That(Quarantined(), Is.EqualTo(new[] { PrefabCache.FileName + PrefabSessionGuard.QuarantineInfix + "20261113-123702" }));
        Assert.That(File.ReadAllBytes(Path.Combine(_directory, Quarantined().Single())), Is.EqualTo(new byte[] { 1, 2, 3 }), "kept for a bug report");
    }

    [Test]
    public void SecondStart_AfterTheQuarantine_DoesNotQuarantineAgain()
    {
        File.WriteAllBytes(Archive, [1]);
        Create().MarkOpen();
        Create().QuarantinePreviousSession();
        File.WriteAllBytes(Archive, [2]);

        Assert.That(Create().QuarantinePreviousSession(), Is.False);

        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(new byte[] { 2 }));
    }

    [Test]
    public void MarkerWithoutAnArchive_IsJustCleared()
    {
        Create().MarkOpen();

        Assert.That(Create().QuarantinePreviousSession(), Is.False);

        Assert.That(File.Exists(Marker), Is.False);
        Assert.That(Quarantined(), Is.Empty);
    }

    [Test]
    public void OnlyTheNewestQuarantinedArchives_AreKept()
    {
        for (var i = 0; i < 5; i++)
        {
            File.WriteAllBytes(Archive, [(byte) i]);
            File.WriteAllText(Marker, "left behind");
            _now = _now.AddMinutes(1);
            Create(kept: 3).QuarantinePreviousSession();
        }

        var kept = Quarantined();
        Assert.That(kept, Has.Length.EqualTo(3));
        Assert.That(kept.Last(), Does.EndWith("20261113-124202"));
        Assert.That(kept.First(), Does.EndWith("20261113-124002"));
    }

    [Test]
    public void ArchiveThatCannotBeMoved_IsLeftInPlace_WithoutThrowing()
    {
        File.WriteAllBytes(Archive, [1]);
        File.WriteAllText(Marker, "left behind");
        // The name the archive would take is a directory, which a file cannot be moved onto
        Directory.CreateDirectory(Archive + PrefabSessionGuard.QuarantineInfix + "20261113-123702");

        var guard = Create();
        var quarantined = true;
        Assert.That(() => quarantined = guard.QuarantinePreviousSession(), Throws.Nothing);

        Assert.That(quarantined, Is.False);
        Assert.That(File.Exists(Archive), Is.True);
    }

    [Test]
    public void FolderThatCannotBeWritten_NeverThrows()
    {
        // A folder whose parent is a file: nothing can be created, read or deleted in it
        var parent = Path.Combine(_directory, "file");
        File.WriteAllText(parent, "not a folder");
        var guard = new PrefabSessionGuard(Path.Combine(parent, "CompiledPrefabs"), () => _now);

        Assert.That(() => guard.QuarantinePreviousSession(), Throws.Nothing);
        Assert.That(() => guard.MarkOpen(), Throws.Nothing);
        Assert.That(() => guard.MarkOpen(), Throws.Nothing);
        Assert.That(() => guard.MarkClosed(), Throws.Nothing);
    }
}
