using System.Text;
using Bozo.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Bozo.Tui;

public sealed class App
{
    private readonly BozoClient _client;
    private readonly Logger _log;
    private readonly string _scriptsDir;
    private readonly string _autoExecDir;

    private readonly StringBuilder _editor = new();
    private readonly List<string> _output = new();
    private readonly List<string> _scripts = new();
    private int _scriptSel;
    private int _tab;          // 0 = Executor, 1 = Settings
    private bool _focusList;
    private bool _dirty = true;
    private string _status = "idle";

    public App(BozoClient client, Logger log, string root)
    {
        _client = client;
        _log = log;
        _scriptsDir = Path.Combine(root, "Scripts");
        _autoExecDir = Path.Combine(root, "AutoExec");
        Directory.CreateDirectory(_scriptsDir);
        Directory.CreateDirectory(_autoExecDir);

        _client.Output += t => { Push(t); _dirty = true; };
        _client.Disconnected += () => { Push("[WARN] Client detached."); _status = "detached"; _dirty = true; };
        _log.Line += (_, _) => _dirty = true;

        RefreshScripts();
        _log.Info("UI environment ready.");
        _log.Info("Scanning script registry...");
    }

    public void Run()
    {
        AnsiConsole.Cursor.Hide();
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            while (true)
            {
                if (_dirty) { Render(); _dirty = false; }
                if (!Console.KeyAvailable) { Thread.Sleep(16); continue; }
                if (!HandleKey(Console.ReadKey(true))) break;
            }
        }
        finally
        {
            AnsiConsole.Cursor.Show();
            AnsiConsole.Clear();
        }
    }

    private bool HandleKey(ConsoleKeyInfo k)
    {
        bool ctrl = k.Modifiers.HasFlag(ConsoleModifiers.Control);
        bool alt  = k.Modifiers.HasFlag(ConsoleModifiers.Alt);

        if (ctrl && k.Key == ConsoleKey.Q) return false;
        if (ctrl && k.Key == ConsoleKey.R) { Attach(); return true; }
        if (ctrl && k.Key == ConsoleKey.T) { _tab ^= 1; _dirty = true; return true; }

        if (_tab == 1)
        {
            if (ctrl && k.Key == ConsoleKey.A) ToggleAutoExec();
            return true;
        }

        if (ctrl && k.Key == ConsoleKey.E) { Execute(); return true; }
        if (ctrl && k.Key == ConsoleKey.K) { _editor.Clear(); _log.Info("Editor canvas cleared."); return true; }
        if (ctrl && k.Key == ConsoleKey.L) { LoadSelected(); return true; }
        if (ctrl && k.Key == ConsoleKey.D && alt) { _editor.Append('\n'); return true; }

        if (_focusList)
        {
            if (k.Key == ConsoleKey.UpArrow)   { _scriptSel = Math.Max(0, _scriptSel - 1); _dirty = true; }
            if (k.Key == ConsoleKey.DownArrow) { _scriptSel = Math.Min(Math.Max(0, _scripts.Count - 1), _scriptSel + 1); _dirty = true; }
            if (k.Key == ConsoleKey.Enter)     { LoadSelected(); }
            if (k.Key == ConsoleKey.Tab)       { _focusList = false; _dirty = true; }
            return true;
        }

        if (k.Key == ConsoleKey.Tab) { _focusList = true; _dirty = true; return true; }
        if (k.Key == ConsoleKey.Enter) { _editor.Append('\n'); _dirty = true; return true; }
        if (k.Key == ConsoleKey.Backspace)
        {
            if (_editor.Length > 0) _editor.Length--;
            _dirty = true;
            return true;
        }
        if (k.Key == ConsoleKey.UpArrow)   { ScrollOutput(-1); return true; }
        if (k.Key == ConsoleKey.DownArrow) { ScrollOutput(1); return true; }

        if (!char.IsControl(k.KeyChar)) { _editor.Append(k.KeyChar); _dirty = true; }
        return true;
    }

    // ── actions ──────────────────────────────────────────────────────────
    private void Attach()
    {
        if (_client.Attached) { _client.Detach(); _status = "detached"; Push("[WARN] Detached."); return; }
        _status = _client.Attach() ? "attached" : "attach failed";
        Push(_client.Attached ? "[INFO] Hook established." : "[ERROR] Attach failed — is Roblox running via inject.sh?");
        _dirty = true;
    }

    private void Execute()
    {
        var src = _editor.ToString();
        if (string.IsNullOrWhiteSpace(src)) { Push("[WARN] Target buffer empty."); return; }
        if (!_client.Attached) { Push("[ERROR] No client. Ctrl+R to attach."); return; }
        _client.Execute(src);
        Push($"[INFO] Dispatched {src.Length} bytes.");
        _dirty = true;
    }

    private void LoadSelected()
    {
        if (_scripts.Count == 0) { Push("[WARN] Registry empty."); return; }
        var name = _scripts[Math.Clamp(_scriptSel, 0, _scripts.Count - 1)];
        var path = Path.Combine(_scriptsDir, name);
        try
        {
            _editor.Clear().Append(File.ReadAllText(path));
            _log.Info($"Loaded buffer string from profile: {name}");
        }
        catch (Exception ex) { _log.Error($"File read operation denied: {ex.Message}"); }
        _dirty = true;
    }

    private void ToggleAutoExec()
    {
        var on = Path.Combine(_autoExecDir, ".enabled");
        if (File.Exists(on)) { File.Delete(on); Push("[INFO] AutoExec disabled."); }
        else { File.WriteAllText(on, ""); Push("[INFO] AutoExec enabled."); }
        _client.ClearAutoExec();
        _dirty = true;
    }

    public void RunAutoExec()
    {
        if (!File.Exists(Path.Combine(_autoExecDir, ".enabled"))) return;
        var files = Directory.GetFiles(_autoExecDir, "*.txt");
        if (files.Length == 0) { _log.Info("AutoExec batch directory empty."); return; }
        _log.Info("Processing AutoExec queue...");
        foreach (var f in files)
        {
            try
            {
                var body = File.ReadAllText(f);
                _log.Info($"[AutoExec] Successfully parsed context: {Path.GetFileName(f)}");
                _client.Execute(body);
            }
            catch (Exception ex) { _log.Error($"Command context break in batch profile {Path.GetFileName(f)}: {ex.Message}"); }
        }
    }

    private void RefreshScripts()
    {
        _scripts.Clear();
        try { foreach (var f in Directory.GetFiles(_scriptsDir, "*.txt")) _scripts.Add(Path.GetFileName(f)); }
        catch (Exception ex) { _log.Error($"Scanning script registry failed: {ex.Message}"); }
        _scripts.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private void Push(string s) { _output.Add(s); if (_output.Count > 500) _output.RemoveAt(0); }
    private void ScrollOutput(int d) { _dirty = true; }

    // ── render ──────────────────────────────────────────────────────────
    private void Render()
    {
        int w = Math.Max(60, Console.WindowWidth - 1);
        int h = Math.Max(24, Console.WindowHeight);

        var tabs = _tab == 0
            ? " [black on grey] Executor [/] [grey] Settings [/]"
            : " [grey] Executor [/] [black on grey] Settings [/]";

        var body = _tab == 0 ? ExecutorBody(w, h) : SettingsBody(w, h);

        var frame = new Rows(
            new Markup(tabs),
            new Rule().RuleStyle("grey"),
            body);

        AnsiConsole.Clear();
        AnsiConsole.Write(frame);
    }

    private IRenderable ExecutorBody(int w, int h)
    {
        int editorH = Math.Max(6, (int)(h * 0.34));
        var editorLines = _editor.ToString().Split('\n');
        var shown = editorLines.TakeLast(editorH);
        var editorText = string.Join('\n', shown);
        if (editorText.Length == 0) editorText = "[grey50]-- ScriptTextBox[/]";

        var editor = new Panel(new Markup(editorText)).Header("ScriptTextBox").Expand();

        var buttons = new Markup(
            "[black on grey]  EXE  [/] [black on grey] CLEAR [/] [black on grey] LOAD [/]" +
            $"   [grey50]Ctrl+E / Ctrl+K / Ctrl+L[/]");

        var listItems = _scripts.Count == 0
            ? "[grey50](empty)[/]"
            : string.Join('\n', _scripts.Select((s, i) =>
                i == _scriptSel
                    ? (_focusList ? $"[black on green]> {s}[/]" : $"[black on grey]> {s}[/]")
                    : $"  {s}"));

        var list = new Panel(new Markup(listItems))
            .Header("listBox1")
            .Width(28);

        var outText = _output.Count == 0
            ? "[grey50]OutputTextBox[/]"
            : string.Join('\n', _output.TakeLast(Math.Max(3, h / 4)));

        var output = new Panel(new Markup(Markup.Escape(outText).Length > 0 ? outText : " "))
            .Header($"OutputTextBox — {_status}")
            .Expand();

        return new Rows(
            new Columns(editor, list),
            new Markup(""),
            buttons,
            new Markup(""),
            output);
    }

    private IRenderable SettingsBody(int w, int h) => new Rows(
        new Panel(new Markup(
            "Idk what's in settings lol\n\n" +
            $"[grey50]socket[/]        {BozoClient.DefaultSocket}\n" +
            $"[grey50]scripts[/]       {_scriptsDir}\n" +
            $"[grey50]autoexec[/]      {_autoExecDir} " +
            (File.Exists(Path.Combine(_autoExecDir, ".enabled")) ? "[green]ON[/]" : "[grey50]OFF[/]") + "\n\n" +
            "[grey50]Ctrl+A[/] toggle autoexec   [grey50]Ctrl+T[/] back"))
            .Header("label1").Expand(),
        new Markup("[grey50]bozo executer v1.0.0 — Ctrl+R attach, Ctrl+Q quit[/]"));
}
