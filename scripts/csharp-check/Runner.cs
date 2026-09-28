// Runs the EditMode test bodies in plain .NET against the native plugin,
// and the formation drag's pure tests, for machines without a Unity
// license. Unity's own run is still the real check. Used by
// scripts/csharp-check.sh.
using System;
using System.Linq;
using System.Reflection;

static class Runner
{
    static int Main()
    {
        int pass = 0, fail = 0;
        foreach (var t in new[] { typeof(OpenKingdomsUnity.Tests.OkSimTests), typeof(OpenKingdomsUnity.Tests.FormationTests) })
        foreach (var m in t.GetMethods().Where(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "TestAttribute")))
        {
            try
            {
                m.Invoke(Activator.CreateInstance(t), null);
                Console.WriteLine("PASS " + m.Name);
                pass++;
            }
            catch (TargetInvocationException e)
            {
                Console.WriteLine("FAIL " + m.Name + ": " + e.InnerException.Message);
                fail++;
            }
        }
        Console.WriteLine(pass + " passed, " + fail + " failed");
        return fail;
    }
}
