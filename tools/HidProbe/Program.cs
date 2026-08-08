using HidSharp;

// Read-only HID bench probe.
//
// Answers the one question no amount of unit testing can: what does the controller in the user's
// hands actually send? Every costly misdiagnosis on this project came from guessing that — a filter
// that could not match the device path, a phantom pad read instead of the real one, a report layout
// inferred from documentation rather than observed.
//
// It only reads. Nothing is written to the device, no setting is touched, no driver is configured.
// It still needs SteamXBox.Core stopped: a HID device has one owner at a time, and two readers
// produce a conflict that looks exactly like a broken controller.

var vendor = Arg("--vid", 0x054C);
var product = Arg("--pid", 0x0CE6);
var seconds = Arg("--seconds", 10);

Console.WriteLine($"HidProbe — VID 0x{vendor:X4} PID 0x{product:X4}, {seconds} s. Lecture seule.");
Console.WriteLine();

var devices = DeviceList.Local.GetHidDevices(vendor, product).ToList();

if (devices.Count == 0)
{
    // Named rather than a bare "not found": the device may be present and simply held by something
    // else, which is a different problem with a different fix.
    Console.WriteLine("Aucune interface trouvée. Vérifier que la manette est connectée (pas seulement appairée).");
    return 1;
}

Console.WriteLine($"{devices.Count} interface(s) :");
foreach (var d in devices)
{
    Console.WriteLine($"  in={d.GetMaxInputReportLength(),3}  out={d.GetMaxOutputReportLength(),3}  {d.DevicePath}");
    // La cle durable : ce sous quoi le profil de cette manette doit etre range.
    Console.WriteLine($"      instance = {Sc2Xboxed.Windows.DeviceTree.ToInstanceId(d.DevicePath)}");
    foreach (var a in Sc2Xboxed.Windows.DeviceTree.AncestorInstanceIds(d.DevicePath)) Console.WriteLine($"      parent   = {a}");
    Console.WriteLine($"      CLE      = {Sc2Xboxed.Windows.DeviceTree.DurableKeyFor(d.DevicePath) ?? "(aucune)"}");
}

// The interface carrying the longest input report is the gamepad one; the others are control
// channels that open happily and never deliver a frame.
var device = devices.OrderByDescending(d => d.GetMaxInputReportLength()).First();
Console.WriteLine();
Console.WriteLine($"Lecture de {device.DevicePath}");

if (!device.TryOpen(out var stream))
{
    Console.WriteLine("Ouverture impossible : tenue par un autre processus ? Arrêter SteamXBox.Core.exe.");
    return 2;
}

using (stream)
{
    stream.ReadTimeout = 200;

    var buffer = new byte[Math.Max(16, device.GetMaxInputReportLength())];
    var deadline = DateTime.UtcNow.AddSeconds(seconds);
    var lastPrinted = Array.Empty<byte>();
    var frames = 0;
    var lines = 0;

    Console.WriteLine("Pousser les sticks à fond, presser quelques boutons.");
    Console.WriteLine();

    while (DateTime.UtcNow < deadline)
    {
        int read;
        try
        {
            read = stream.Read(buffer);
        }
        catch (TimeoutException)
        {
            // Nobody is touching the pad. Normal, and not a reason to stop.
            continue;
        }

        if (read <= 0)
        {
            continue;
        }

        frames++;

        // Printed only when something changed, and only over the first bytes: a full report per
        // frame at 250 Hz is unreadable, and everything that matters lives in the first dozen.
        var head = buffer.Take(Math.Min(read, 12)).ToArray();
        if (lastPrinted.SequenceEqual(head))
        {
            continue;
        }

        lastPrinted = head;
        lines++;

        Console.WriteLine(
            $"len={read,3} [{string.Join(" ", head.Select(b => b.ToString("X2")))}]"
            + $"   LX={head[1],3} LY={head[2],3} RX={head[3],3} RY={head[4],3}"
            + $"   b5=0x{head[5]:X2} b6=0x{head[6]:X2} b8=0x{head[8]:X2} b9=0x{head[9]:X2}");
    }

    Console.WriteLine();
    Console.WriteLine($"{frames} trames lues, {lines} changements.");

    if (frames == 0)
    {
        Console.WriteLine("Aucune trame : la manette est ouverte mais n'émet rien.");
    }
}

return 0;

int Arg(string name, int fallback)
{
    var argv = Environment.GetCommandLineArgs();
    for (var i = 0; i < argv.Length - 1; i++)
    {
        if (argv[i].Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            var raw = argv[i + 1];
            var hex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase);

            if (int.TryParse(
                    hex ? raw[2..] : raw,
                    hex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
            {
                return value;
            }
        }
    }

    return fallback;
}
