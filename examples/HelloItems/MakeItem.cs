// Spike: make a new item file from a vanilla one with the game's own classes (run from the 64-bit test copy).
using System; using System.Reflection;
using UnderRailWorks;
class MakeItem {
    static void Main(string[] a) {
        // a[0] vanilla .item, a[1] output .item, a[2] new name, a[3] new description
        // As the game's tb.Initialize does: its type resolver for the data files.
        var resolver = (e6e)Activator.CreateInstance(typeof(bk3).Assembly.GetType("af9"), true);
        resolver.b(true); resolver.a(true); resolver.av1();
        AppDomain.CurrentDomain.AssemblyResolve += resolver.av0;
        bpo.RegisterService((ei2)resolver);
        resolver.d();
        typeof(dds).GetField("a").SetValue(null, resolver);
        bk3 item = bk3.LoadFromBinaryFile<bk3>(a[0]);
        Console.WriteLine("loaded " + item.GetType().FullName + " name=" + item.az() + " desc=" + item.a0());
        var f = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        typeof(bk3).GetField("c", f).SetValue(item, a[2]);
        typeof(bk3).GetField("f", f).SetValue(item, a[3]);
        item.j(a[1]);
        bk3 back = bk3.LoadFromBinaryFile<bk3>(a[1]);
        Console.WriteLine("wrote " + a[1] + ": " + back.GetType().Name + " name=" + back.az());
    }
}
