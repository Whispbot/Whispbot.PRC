using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Whispbot.PRC.Scripts
{
    public static class Load
    {
        public static string File(string fileName)
        {
            var asm = Assembly.GetExecutingAssembly();
            var resourceName = asm.GetManifestResourceNames()
                .SingleOrDefault(n => n.EndsWith($".Scripts.{fileName}.lua", StringComparison.Ordinal))

            ?? throw new InvalidOperationException($"Script file not found: {fileName}");

            using var stream = asm.GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(stream!);
            return reader.ReadToEnd();
        }
    }
}
