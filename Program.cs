using Bozo.Core;
using Bozo.Tui;

var root = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "bozo");

Directory.CreateDirectory(root);
var log = new Logger(Path.Combine(root, "logs"));
log.Info($"Version: bozo - v1.0.0");
log.Info($"Log file: {root}");

using var client = new BozoClient(log);
var app = new App(client, log, root);

if (client.Attach()) app.RunAutoExec();
app.Run();
