using System.Net;
using System.Net.Sockets;
using System.Text;
using RadioV2.Helpers;
using Xunit;

namespace RadioV2.Tests;

public class ArtworkLoadingTests
{
    [Theory]
    [InlineData(404, true)] [InlineData(410, true)] [InlineData(429, false)] [InlineData(503, false)]
    public async Task OrdinaryHttpRequestsRememberOnlyConfirmedMissingArtwork(int status, bool remembered)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/{Guid.NewGuid()}.png";
            var server = Reply(listener, status, []);
            Assert.Null(await AsyncImageLoader.LoadAsync(url, CancellationToken.None));
            await server;
            Assert.Equal(remembered, StationArtworkCache.Shared.Snapshot().Contains(url));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task WorkingLogoDecodesAtThumbnailSizeAndLoadsFromCacheWithoutNetwork()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/{Guid.NewGuid()}.png";
            // A real WPF encoder avoids hand-written/corrupt fixture image data.
            var source = System.Windows.Media.Imaging.BitmapSource.Create(4, 4, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, new byte[64], 16);
            source.Freeze();
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
            using var bytes = new System.IO.MemoryStream();
            encoder.Save(bytes);
            var server = Reply(listener, 200, bytes.ToArray());
            var bitmap = await AsyncImageLoader.LoadAsync(url, CancellationToken.None);
            await server;
            listener.Stop();
            Assert.NotNull(bitmap);
            Assert.True(bitmap.IsFrozen);
            Assert.Equal(96, bitmap.PixelWidth);
            var cached = await AsyncImageLoader.LoadAsync(url, CancellationToken.None);
            Assert.NotNull(cached);
            Assert.DoesNotContain(url, StationArtworkCache.Shared.Snapshot());
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task InvalidImageShowsFallbackAndDoesNotPoisonDiskCache()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/{Guid.NewGuid()}.png";
            var server = Reply(listener, 200, Encoding.UTF8.GetBytes("<html>not an image</html>"));
            Assert.Null(await AsyncImageLoader.LoadAsync(url, CancellationToken.None));
            await server;
            Assert.Contains(url, StationArtworkCache.Shared.Snapshot());
            // A second visible occurrence uses fallback immediately, without a new request.
            Assert.Null(await AsyncImageLoader.LoadAsync(url, CancellationToken.None));
        }
        finally { listener.Stop(); }
    }

    private static async Task Reply(TcpListener listener, int status, byte[] body)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        using var stream = client.GetStream();
        var request = new byte[4096];
        await stream.ReadAsync(request, timeout.Token);
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, timeout.Token);
        await stream.WriteAsync(body, timeout.Token);
    }
}
