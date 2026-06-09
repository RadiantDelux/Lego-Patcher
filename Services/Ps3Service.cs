using FluentFTP;
using FluentFTP.Exceptions;
using ToyPadMaui.Models;

namespace ToyPadMaui.Services;

// All console communication over FTP. Supports two targets:
//   PS3 (webMAN MOD, port 21): figures in /dev_hdd0/tmp, install sprx + EBOOT.
//   PS4 (GoldHEN, port 2121): figures in /data/toypad_emu, install .prx +
//                             register in plugins.ini.
// The active target is chosen by AppSettings.Platform.
public class Ps3Service
{
    // ---- PS3 paths ----
    const string Ps3TmpDir  = "/dev_hdd0/tmp";
    const string GameDir    = "/dev_hdd0/game/BLUS31473/USRDIR";
    const string SprxDst    = Ps3TmpDir + "/toypad_emu.sprx";
    const string EbootDst   = GameDir + "/EBOOT.BIN";
    const string EbootBak   = GameDir + "/EBOOT.BIN.original";

    // ---- PS4 (GoldHEN) paths ----
    const string Ps4PluginsDir = "/data/GoldHEN/plugins";
    const string Ps4PluginsIni = "/data/GoldHEN/plugins.ini";
    const string Ps4ToypadDir  = "/data/toypad_emu";
    const string Ps4TitleId    = "CUSA00935";
    const string Ps4PrxDst     = Ps4PluginsDir + "/toypad_emu.prx";

    readonly SemaphoreSlim _lock = new(1, 1);

    string Host => AppSettings.Host;
    string User => AppSettings.User;
    string Pass => AppSettings.Pass;

    static bool IsPs4 => AppSettings.IsPs4;
    static int  Port  => AppSettings.FtpPort > 0 ? AppSettings.FtpPort : (IsPs4 ? 2121 : 21);
    // Directory where figureN.bin live for the active platform.
    static string FigureDir => IsPs4 ? Ps4ToypadDir : Ps3TmpDir;

    AsyncFtpClient NewClient()
    {
        var c = new AsyncFtpClient(Host, User, Pass, Port, new FtpConfig
        {
            ConnectTimeout = 8000,
            ReadTimeout = 60000,
            DataConnectionConnectTimeout = 8000,
            DataConnectionReadTimeout = 60000,
            // webMAN's FTP is minimal: force plain PASV, no EPSV, and don't
            // probe FEAT/extended commands it doesn't implement. GoldHEN's FTP
            // also works fine with PASV.
            DataConnectionType = FtpDataConnectionType.PASV,
            EncryptionMode = FtpEncryptionMode.None,
            ValidateAnyCertificate = true,
            SendHost = false,
            RetryAttempts = 2,
        });
        c.Encoding = System.Text.Encoding.Latin1;
        return c;
    }

    public async Task<bool> TestAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Host)) return false;
        await _lock.WaitAsync(ct);
        try
        {
            await using var c = NewClient();
            await c.Connect(ct);
            // PS4 figure dir may not exist yet (created on install); just probe
            // root there. PS3 tmp always exists.
            await c.SetWorkingDirectory(IsPs4 ? "/" : Ps3TmpDir, ct);
            return true;
        }
        catch { return false; }
        finally { _lock.Release(); }
    }

    // Create the PS4 figures dir if missing (so PlaceAsync works before the
    // plugin's first run created it).
    async Task EnsureFigureDirAsync(AsyncFtpClient c, CancellationToken ct)
    {
        if (!IsPs4) return;
        try { await c.CreateDirectory(Ps4ToypadDir, ct); } catch (FtpException) { }
    }

    public async Task PlaceAsync(string slot, byte[] bytes, CancellationToken ct = default)
    {
        if (!SlotMap.Files.TryGetValue(slot, out var fname))
            throw new ArgumentException($"unknown slot {slot}");
        await _lock.WaitAsync(ct);
        try
        {
            await using var c = NewClient();
            await c.Connect(ct);
            await EnsureFigureDirAsync(c, ct);
            using var ms = new MemoryStream(bytes);
            await c.UploadStream(ms, $"{FigureDir}/{fname}",
                FtpRemoteExists.Overwrite, false, token: ct);
        }
        finally { _lock.Release(); }
    }

    public async Task RemoveAsync(string slot, CancellationToken ct = default)
    {
        if (!SlotMap.Files.TryGetValue(slot, out var fname)) return;
        await _lock.WaitAsync(ct);
        try
        {
            await using var c = NewClient();
            await c.Connect(ct);
            var path = $"{FigureDir}/{fname}";
            if (await c.FileExists(path, ct))
                await c.DeleteFile(path, ct);
        }
        catch (FtpCommandException) { /* ignore not-found */ }
        finally { _lock.Release(); }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var c = NewClient();
            await c.Connect(ct);
            foreach (var fname in SlotMap.Files.Values)
            {
                var path = $"{FigureDir}/{fname}";
                try { if (await c.FileExists(path, ct)) await c.DeleteFile(path, ct); }
                catch (FtpCommandException) { }
            }
        }
        finally { _lock.Release(); }
    }

    // Returns which slot ids currently have their figureN.bin present.
    public async Task<Dictionary<string, bool>> StateAsync(CancellationToken ct = default)
    {
        var result = SlotMap.AllSlots.ToDictionary(s => s, _ => false);
        await _lock.WaitAsync(ct);
        try
        {
            await using var c = NewClient();
            await c.Connect(ct);
            await c.SetWorkingDirectory(FigureDir, ct);
            var listing = await c.GetNameListing(ct);
            var present = listing
                .Select(p => p.Split('/').Last())
                .ToHashSet();
            foreach (var (slot, fname) in SlotMap.Files)
                result[slot] = present.Contains(fname);
        }
        catch { /* leave all false */ }
        finally { _lock.Release(); }
        return result;
    }

    public async Task InstallSprxAsync(byte[] sprx, IProgress<double>? prog = null,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var c = NewClient();
            await c.Connect(ct);
            await c.UploadBytes(sprx, SprxDst, FtpRemoteExists.Overwrite, true,
                progress: prog is null ? null : new Progress<FtpProgress>(p => prog.Report(p.Progress)),
                token: ct);
        }
        finally { _lock.Release(); }
    }

    public async Task InstallEbootAsync(byte[] eboot, IProgress<double>? prog = null,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var c = NewClient();
            await c.Connect(ct);
            // Backup original EBOOT.BIN -> EBOOT.BIN.original (only once).
            try
            {
                if (!await c.FileExists(EbootBak, ct) && await c.FileExists(EbootDst, ct))
                    await c.Rename(EbootDst, EbootBak, ct);
            }
            catch (FtpCommandException) { /* best-effort */ }

            await c.UploadBytes(eboot, EbootDst, FtpRemoteExists.Overwrite, true,
                progress: prog is null ? null : new Progress<FtpProgress>(p => prog.Report(p.Progress)),
                token: ct);
        }
        finally { _lock.Release(); }
    }

    // ---- PS4 (GoldHEN) install ------------------------------------------
    // Uploads toypad_emu.prx to /data/GoldHEN/plugins/, registers it in
    // plugins.ini under [CUSA00935], and creates /data/toypad_emu/.
    public async Task InstallPs4Async(byte[] prx, IProgress<double>? prog = null,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var c = NewClient();
            await c.Connect(ct);

            // 1. ensure plugin dir exists
            try { await c.CreateDirectory("/data/GoldHEN", ct); } catch (FtpException) { }
            try { await c.CreateDirectory(Ps4PluginsDir, ct); } catch (FtpException) { }

            // 2. upload the prx
            await c.UploadBytes(prx, Ps4PrxDst, FtpRemoteExists.Overwrite, true,
                progress: prog is null ? null : new Progress<FtpProgress>(p => prog.Report(p.Progress)),
                token: ct);

            // 3. register in plugins.ini under [CUSA00935] (idempotent)
            await RegisterPluginIniAsync(c, ct);

            // 4. ensure figures dir
            try { await c.CreateDirectory(Ps4ToypadDir, ct); } catch (FtpException) { }
        }
        finally { _lock.Release(); }
    }

    async Task RegisterPluginIniAsync(AsyncFtpClient c, CancellationToken ct)
    {
        string ini = "";
        try
        {
            if (await c.FileExists(Ps4PluginsIni, ct))
            {
                var bytes = await c.DownloadBytes(Ps4PluginsIni, ct);
                if (bytes != null) ini = System.Text.Encoding.UTF8.GetString(bytes);
            }
        }
        catch (FtpException) { }

        if (ini.Contains(Ps4PrxDst, StringComparison.OrdinalIgnoreCase))
            return; // already registered

        var lines = ini.Replace("\r\n", "\n").Split('\n').ToList();
        var section = $"[{Ps4TitleId}]";
        bool placed = false;
        var outLines = new List<string>();
        bool inSection = false;
        foreach (var ln in lines)
        {
            outLines.Add(ln);
            var t = ln.Trim();
            if (t.Equals(section, StringComparison.OrdinalIgnoreCase))
            {
                inSection = true;
                outLines.Add(Ps4PrxDst);
                placed = true;
            }
            else if (t.StartsWith("[") && inSection)
            {
                inSection = false;
            }
        }
        if (!placed)
        {
            if (outLines.Count > 0 && outLines[^1].Trim().Length > 0)
                outLines.Add("");
            outLines.Add(section);
            outLines.Add(Ps4PrxDst);
        }
        var newIni = string.Join("\n", outLines).TrimEnd('\n') + "\n";
        var data = System.Text.Encoding.UTF8.GetBytes(newIni);
        await c.UploadBytes(data, Ps4PluginsIni, FtpRemoteExists.Overwrite, true, token: ct);
    }
}
