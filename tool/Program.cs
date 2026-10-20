using System;
using dnlib.DotNet;

class PclRestore
{
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: PclRestore <input.exe> [output.exe]");
            Console.Error.WriteLine("  IL-rewrites a PCL 2.x EXE to lift the Minecraft-download restriction.");
            return 2;
        }
        string input = args[0];
        string output = args.Length >= 2 ? args[1] : System.IO.Path.ChangeExtension(input, ".restored.exe");

        var mod = ModuleDefMD.Load(input);
        Console.WriteLine($"Loaded {mod.Name} (MVID {mod.Mvid})");

        int applied = Patcher.PatchAll(mod);
        Console.WriteLine($"Applied {applied}/5 transforms.");

        if (applied == 0)
        {
            Console.Error.WriteLine("No transform matched. This is not a PCL 2.99.0.0+ EXE with the MC-download restriction.");
            return 1;
        }

        mod.Write(output);
        Console.WriteLine($"WROTE {output}");
        return 0;
    }
}
