using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>Artwork loader with a memory LRU and a disk cache under user://cache/img.</summary>
public partial class ImageCache : Node
{
    private const int MemoryLimit = 300;

    public static ImageCache I { get; private set; }

    private readonly Dictionary<string, LinkedListNode<(string Url, ImageTexture Texture)>> _memory = new();
    private readonly LinkedList<(string Url, ImageTexture Texture)> _order = new();
    private readonly Dictionary<string, Task<ImageTexture>> _pending = new();
    private readonly SemaphoreSlim _downloads = new(6);
    private string _dir;

    public override void _EnterTree()
    {
        I = this;
        _dir = Path.Combine(OS.GetUserDataDir(), "cache", "img");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Bytes of artwork saved on disk.</summary>
    public long DiskBytes()
    {
        try { return new DirectoryInfo(_dir).EnumerateFiles().Sum(f => f.Length); }
        catch (IOException) { return 0; }
    }

    /// <summary>Deletes saved artwork. It downloads again as pages need it.</summary>
    public void Clear()
    {
        _memory.Clear();
        _order.Clear();
        foreach (var file in Directory.EnumerateFiles(_dir))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    /// <summary>Returns the texture for a URL, or null when it cannot be loaded. Call from the main thread.</summary>
    public Task<ImageTexture> GetAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) { return Task.FromResult<ImageTexture>(null); }
        if (_memory.TryGetValue(url, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            return Task.FromResult(node.Value.Texture);
        }
        if (_pending.TryGetValue(url, out var pending)) { return pending; }
        var task = LoadAsync(url);
        _pending[url] = task;
        return task;
    }

    private async Task<ImageTexture> LoadAsync(string url)
    {
        ImageTexture texture = null;
        try
        {
            var image = await Task.Run(() => FetchAsync(url));
            if (image != null) { texture = ImageTexture.CreateFromImage(image); }
        }
        catch (Exception e)
        {
            GD.Print("Image failed: ", e.Message);
        }
        _pending.Remove(url);
        if (texture != null)
        {
            _memory[url] = _order.AddFirst((url, texture));
            while (_order.Count > MemoryLimit)
            {
                _memory.Remove(_order.Last.Value.Url);
                _order.RemoveLast();
            }
        }
        return texture;
    }

    /// <summary>
    /// Loads an image without keeping it in memory or on disk. For large sheets such as scrubbing
    /// thumbnails, which the caller holds only while it needs them.
    /// </summary>
    public async Task<ImageTexture> LoadUncachedAsync(string url)
    {
        try
        {
            var image = await Task.Run(() => FetchAsync(url, false));
            return image != null ? ImageTexture.CreateFromImage(image) : null;
        }
        catch (Exception e)
        {
            GD.Print("Image failed: ", e.Message);
            return null;
        }
    }

    private async Task<Image> FetchAsync(string url, bool useDisk = true)
    {
        var file = Path.Combine(_dir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))));
        byte[] bytes = null;
        if (useDisk && File.Exists(file))
        {
            try { bytes = await File.ReadAllBytesAsync(file); } catch (IOException) { }
        }
        var fromDisk = bytes != null;
        if (bytes == null)
        {
            await _downloads.WaitAsync();
            try
            {
                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                // Artwork also comes from Seerr and TMDB. The Jellyfin token goes only to the Jellyfin server.
                if (Jellyfin.I.Server != "" && url.StartsWith(Jellyfin.I.Server + "/"))
                {
                    request.Headers.TryAddWithoutValidation("Authorization", Jellyfin.I.AuthHeader);
                }
                using var response = await Jellyfin.I.Http.SendAsync(request);
                if (!response.IsSuccessStatusCode) { return null; }
                bytes = await response.Content.ReadAsByteArrayAsync();
            }
            finally { _downloads.Release(); }
        }

        var image = Decode(bytes);
        if (image != null && !fromDisk && useDisk)
        {
            try { await File.WriteAllBytesAsync(file, bytes); } catch (IOException) { }
        }
        return image;
    }

    private static Image Decode(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 12) { return null; }
        var image = new Image();
        Error error;
        if (bytes[0] == 0xFF && bytes[1] == 0xD8) { error = image.LoadJpgFromBuffer(bytes); }
        else if (bytes[0] == 0x89 && bytes[1] == 0x50) { error = image.LoadPngFromBuffer(bytes); }
        else if (bytes[0] == 'R' && bytes[8] == 'W') { error = image.LoadWebpFromBuffer(bytes); }
        else { return null; }
        return error == Error.Ok ? image : null;
    }
}
