using System.Net.Sockets;
using System.Text;

namespace Bozo.Core;

public sealed class BozoClient : IDisposable
{
    public const string DefaultSocket = "/tmp/bozo.sock";

    private Socket? _sock;
    private Thread? _reader;
    private readonly Logger _log;
    private volatile bool _running;

    public bool Attached => _sock?.Connected == true;
    public event Action<string>? Output;
    public event Action? Disconnected;

    public BozoClient(Logger log) => _log = log;

    public bool Attach(string socketPath = DefaultSocket)
    {
        Detach();
        try
        {
            var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            s.Connect(new UnixDomainSocketEndPoint(socketPath));
            _sock = s;
            _running = true;
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "bozo-reader" };
            _reader.Start();
            _log.Info($"Attached to {socketPath}");
            Send(Frame.Of(Op.Ping));
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Attach failed: {ex.Message}");
            return false;
        }
    }

    public void Detach()
    {
        _running = false;
        try { _sock?.Shutdown(SocketShutdown.Both); } catch { }
        _sock?.Dispose();
        _sock = null;
    }

    public void Execute(string script)
    {
        if (!Attached) { _log.Warn("Not attached."); return; }
        Send(Frame.Of(Op.Execute, script));
    }

    public void ClearAutoExec()
    {
        if (Attached) Send(Frame.Of(Op.Clear));
    }

    private void Send(Frame f)
    {
        try { _sock?.Send(f.Serialize()); }
        catch (Exception ex) { _log.Error($"Send failed: {ex.Message}"); }
    }

    private void ReadLoop()
    {
        using var ns = new NetworkStream(_sock!, ownsSocket: false);
        while (_running)
        {
            if (!Frame.TryRead(ns, out var f)) break;
            switch (f.Op)
            {
                case Op.Output: Output?.Invoke(f.Text); break;
                case Op.Ready:  _log.Info("Dylib handshake complete."); break;
            }
        }
        _running = false;
        Disconnected?.Invoke();
    }

    public void Dispose() => Detach();
}
