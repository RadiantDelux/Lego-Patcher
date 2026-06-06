using FluentFTP;
using FluentFTP.Exceptions;
using ToyPadMaui.Models;

namespace ToyPadMaui.Services;

// All PS3 communication over FTP (webMAN MOD). Mirrors the Python server.py
// behaviour: place/remove figureN.bin in /dev_hdd0/tmp, install sprx + EBOOT.
public class Ps3Service
{
    const string TmpDir   = "/dev_hdd0/tmp";
    const string GameDir  = "/dev_hdd0/game/BLUS31473/USRDIR";
    const string SprxDst  = TmpDir + "/toypad_emu.sprx";
    const string EbootDst = GameDir + "/EBOOT.BIN";
    const string EbootBak = GameDir + "/EBOOT.BIN.original";

    readonly SemaphoreSlim _lock = new(1, 1);

    string Host => AppSettings.Host;
    string User => AppSettings.User;
    string Pass => AppSettings.Pass;

    AsyncFtpClient NewClient()
    {
        var c = new AsyncFtpClient(Host, User, Pass, 21, new FtpConfig
        {
            ConnectTimeout = 8000,
            ReadTimeout = 60000,
            DataConnectionConnectTimeout = 8000,
            DataConnectionReadTimeout = 60000,
            // webMAN's FTP is minimal: force plain PASV, no EPSV, and don't
            // probe FEAT/extended commands it doesn't implement.
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
            await c.SetWorkingDirectory(TmpDir, ct);
            return true;
        }
        catch { return false; }
        finally { _lock.Release(); }
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
            using var ms = new MemoryStream(bytes);
            await c.UploadStream(ms, $"{TmpDir}/{fname}",
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
            var path = $"{TmpDir}/{fname}";
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
                var path = $"{TmpDir}/{fname}";
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
            await c.SetWorkingDirectory(TmpDir, ct);
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
}
