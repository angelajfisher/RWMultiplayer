using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2) throw new ArgumentException("Expected game assembly and test-output copy path");
if (Path.GetFullPath(args[0]) == Path.GetFullPath(args[1]))
    throw new ArgumentException("Refusing to modify the installed game assembly");
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
using var module = ModuleDefinition.ReadModule(args[0], new ReaderParameters { AssemblyResolver = resolver });
foreach (string name in new[] { "RimWorld.Planet.WorldObject", "RimWorld.Planet.MapParent", "RimWorld.Planet.Settlement", "RimWorld.MainTabWindow_Quests", "RimWorld.MessageTypeDefOf" })
{
    var type = module.GetType(name) ?? throw new Exception("Missing fixture type: " + name);
    var initializer = type.Methods.FirstOrDefault(method => method.IsConstructor && method.IsStatic);
    if (initializer == null) continue;
    initializer.Body = new MethodBody(initializer);
    initializer.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    Console.WriteLine("Headless fixture: skipped graphics initializer for " + name);
}
string temporary = args[1] + ".preparing";
module.Write(temporary);
File.Move(temporary, args[1], overwrite: true);
