using System;
using System.Reflection;
using System.IO;
static class StartupScan
{
    static int Main(string[] args)
    {
        Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Multiplayer.dll"));
        var addon = Assembly.LoadFrom(args[0]);
        int fields = 0;
        foreach (var type in addon.GetTypes())
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var fieldType = field.FieldType;
                fields++;
            }
        Console.WriteLine("Startup type/field scan passed: " + fields + " fields, workshop Multiplayer loaded before runtime replacement.");
        return 0;
    }
}
