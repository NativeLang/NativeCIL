﻿using System.Diagnostics;
using System.Text;
using DiscUtils.Iso9660;
using NativeCIL;
using NativeCIL.Backend.Amd64;
using NativeCIL.Backend.IR;

var watch = new Stopwatch();
var settings = new Settings(args);

/*var ir = new IRCompiler(settings.InputFile, 8);
var sb = new StringBuilder();
ir.Compile();

foreach (var inst in ir.Instructions)
{
    sb.Append(inst.OpCode.ToString());

    if (inst.Operand1 != null)
        sb.Append(' ').Append(inst.Operand1);

    if (inst.Operand2 != null)
        sb.Append(' ').Append(inst.Operand2);

    sb.AppendLine();
}

File.WriteAllText("test.ir", sb.ToString());*/

var arch = new Amd64Architecture(settings.InputFile, settings.OutputFile);

watch.Start();
arch.Initialize();
arch.Compile();
arch.Assemble();
if (settings.Format == Format.Elf)
    arch.Link();

if (settings.ImageType == ImageType.Iso)
{
    if (settings.Format == Format.Bin)
        throw new Exception("Raw binaries cannot be used with Limine!");

    using var cd = File.OpenRead("Limine/limine-cd.bin");
    using var sys = File.OpenRead("Limine/limine.sys");
    using var kernel = File.OpenRead(arch.OutputPath);

    var iso = new CDBuilder
    {
        UseJoliet = true,
        VolumeIdentifier = arch.AssemblyName,
        UpdateIsolinuxBootTable = true
    };
    iso.AddFile("limine.sys", sys);
    iso.AddFile("limine.cfg", Encoding.ASCII.GetBytes($"TIMEOUT=0\n:{arch.AssemblyName}\nPROTOCOL=multiboot2\nKERNEL_PATH=boot:///kernel.elf"));
    iso.AddFile("kernel.elf", kernel);
    iso.SetBootImage(cd, BootDeviceEmulation.NoEmulation, 0);
    iso.Build(settings.OutputFile);

    Process.Start("Limine/limine-deploy", "--force-mbr " + settings.OutputFile).WaitForExit();
}

watch.Stop();
Console.WriteLine($"Finished! Took {watch.ElapsedMilliseconds} ms.");