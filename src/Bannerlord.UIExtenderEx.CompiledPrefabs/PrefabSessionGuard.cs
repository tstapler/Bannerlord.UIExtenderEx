using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Bannerlord.UIExtenderEx.CompiledPrefabs;

/// <summary>
/// Breaks a crash loop on a cached build that kills the game without an exception.
/// <para>
/// A <see cref="MarkerFileName"/> file is written next to <see cref="PrefabCache.FileName"/> once a cached build is registered, and
/// deleted when the game unloads the module. A marker found at the next start means the last session never unloaded, so the archive
/// is renamed to <c>CompiledPrefabs.zip.crashed-&lt;timestamp&gt;</c> and the session rebuilds the cache, serving XML meanwhile.
/// </para>
/// <para>
/// A session that ends any other way than a clean quit (killed from Steam, power loss) costs one rebuild, never a crash.
/// Every method is safe on a read-only or locked folder: failures are logged, never thrown into game start-up.
/// </para>
/// </summary>
public sealed class PrefabSessionGuard
{
    /// <summary>The name of the file whose presence says the last session never unloaded the module.</summary>
    public const string MarkerFileName = "session-open";

    /// <summary>What a quarantined archive's name carries between <see cref="PrefabCache.FileName"/> and the timestamp.</summary>
    public const string QuarantineInfix = ".crashed-";

    private readonly string _archivePath;
    private readonly string _markerPath;
    private readonly string _directory;
    private readonly Func<DateTime> _utcNow;
    private readonly int _keptQuarantined;
    private readonly object _lock = new();
    private bool _open;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrefabSessionGuard"/> class for the cache in <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">The cache directory holding <see cref="PrefabCache.FileName"/>.</param>
    /// <param name="utcNow">The clock the quarantine timestamp is read from.</param>
    /// <param name="keptQuarantined">The number of quarantined archives to retain, newest first.</param>
    public PrefabSessionGuard(string directory, Func<DateTime>? utcNow = null, int keptQuarantined = 3)
    {
        _directory = directory;
        _archivePath = Path.Combine(directory, PrefabCache.FileName);
        _markerPath = Path.Combine(directory, MarkerFileName);
        _utcNow = utcNow ?? (static () => DateTime.UtcNow);
        _keptQuarantined = Math.Max(1, keptQuarantined);
    }

    /// <summary>Gets the full path of the marker file.</summary>
    public string MarkerPath => _markerPath;

    /// <summary>
    /// Moves the archive aside when the last session left its marker behind. Call before the archive is opened.
    /// </summary>
    /// <returns><see langword="true"/> if the archive was quarantined, so this session starts without cached builds.</returns>
    public bool QuarantinePreviousSession()
    {
        try
        {
            if (!File.Exists(_markerPath))
                return false;

            var unclean = ReadMarker();
            if (!File.Exists(_archivePath))
            {
                TryDelete(_markerPath);
                return false;
            }

            var quarantined = _archivePath + QuarantineInfix + _utcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            if (!TryMove(_archivePath, quarantined))
            {
                Trace.TraceWarning("UIExtenderEx: the previous session did not end cleanly ({0}) but '{1}' could not be moved aside, its cached builds are used again", unclean, _archivePath);
                return false;
            }

            TryDelete(_markerPath);
            Trace.TraceWarning(
                "UIExtenderEx: the previous session did not end cleanly after it used cached compiled prefabs ({0}). The cache was moved to '{1}' and is rebuilt this session; patched movies load from XML until then. Set CrashLoopGuard to false to turn this off.",
                unclean, quarantined);
            Prune();
            return true;
        }
        catch (Exception e)
        {
            Trace.TraceWarning("UIExtenderEx: could not check whether the previous session ended cleanly: {0}", e.Message);
            return false;
        }
    }

    /// <summary>
    /// Writes the marker. Called when a cached build is about to be registered. Idempotent.
    /// </summary>
    public void MarkOpen()
    {
        lock (_lock)
        {
            if (_open)
                return;

            try
            {
                Directory.CreateDirectory(_directory);
                using var process = Process.GetCurrentProcess();
                File.WriteAllText(_markerPath, $"pid={process.Id.ToString(CultureInfo.InvariantCulture)} utc={_utcNow().ToString("o", CultureInfo.InvariantCulture)}");
                _open = true;
            }
            catch (Exception e)
            {
                Trace.TraceWarning("UIExtenderEx: could not write '{0}', a crash on a cached build is not detected next start: {1}", _markerPath, e.Message);
            }
        }
    }

    /// <summary>
    /// Deletes the marker. Called when the module unloads, which the game does on a normal quit. Idempotent.
    /// </summary>
    public void MarkClosed()
    {
        lock (_lock)
        {
            if (!_open)
                return;

            if (!TryDelete(_markerPath))
                return;
            _open = false;
        }
    }

    private string ReadMarker()
    {
        try
        {
            return File.ReadAllText(_markerPath).Trim() is { Length: > 0 } text ? text : "empty marker";
        }
        catch (Exception)
        {
            return "unreadable marker";
        }
    }

    /// <summary>Drops the quarantined archives beyond <c>keptQuarantined</c>, oldest first. The timestamp in the name sorts by age.</summary>
    private void Prune()
    {
        try
        {
            var pattern = PrefabCache.FileName + QuarantineInfix + "*";
            foreach (var old in Directory.EnumerateFiles(_directory, pattern).OrderByDescending(x => x, StringComparer.Ordinal).Skip(_keptQuarantined))
                TryDelete(old);
        }
        catch (Exception e)
        {
            Trace.TraceWarning("UIExtenderEx: could not remove the oldest quarantined caches: {0}", e.Message);
        }
    }

    private static bool TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to);
            return true;
        }
        catch (Exception e)
        {
            Trace.TraceWarning("UIExtenderEx: could not move '{0}' to '{1}': {2}", from, to, e.Message);
            return false;
        }
    }

    private static bool TryDelete(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception e)
        {
            Trace.TraceWarning("UIExtenderEx: could not delete '{0}': {1}", file, e.Message);
            return false;
        }
    }
}
