using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using IdeologyFactionFix;
using Multiplayer.Client;
using Multiplayer.Client.Comp;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using Verse;
using MP = Multiplayer.Client.Multiplayer;

static class Program
{
    static int checks;
    static bool failAssign;
    static Pawn[] removedHolders;
    static readonly List<Pawn> pawns = new();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object o, string field, object value) => AccessTools.Field(o.GetType(), field).SetValue(o, value);
    static void Check(bool condition, string text) { if (!condition) throw new Exception(text); checks++; Console.WriteLine("PASS: " + text); }
    static Faction Faction(int id)
    {
        var f = Empty<Faction>(); f.def = new FactionDef { isPlayer = true }; Set(f,"loadID",id); f.ideos = new FactionIdeosTracker(f); return f;
    }
    static Pawn Pawn(Faction f, Ideo ideo, int id)
    {
        var p = Empty<Pawn>(); p.def = Empty<ThingDef>(); p.def.race = new RaceProperties { intelligence = Intelligence.Humanlike }; p.thingIDNumber = id; Set(p,"mapIndexOrState",(sbyte)-1); Set(p,"factionInt",f);
        p.ideo = new Pawn_IdeoTracker(p); Set(p.ideo,"ideo",ideo); p.guest = Empty<Pawn_GuestTracker>(); Set(p.guest,"pawn",p); pawns.Add(p); return p;
    }
    static Map Map(Faction f, int id)
    {
        var parent = Empty<Settlement>(); Set(parent,"factionInt",f); parent.def = new WorldObjectDef { canBePlayerHome = true };
        var map = Empty<Map>(); Set(map,"uniqueID",id); map.info = new MapInfo { parent = parent }; map.treeDestructionTracker = new TreeDestructionTracker(map);
        Set(map,"components",new List<MapComponent> { new IdeologyTreeData(map) }); return map;
    }
    static void Main(string[] args)
    {
        var a = Faction(20); var b = Faction(22); var spectator = Faction(21);
        var game = Empty<Game>(); var world = Empty<World>(); var fm = Empty<FactionManager>();
        Set(fm,"ofPlayer",a); Set(fm,"allFactions",new List<Faction> { spectator, a, b }); Set(world,"factionManager",fm);
        Set(game,"questManager",new QuestManager()); Set(game,"worldInt",world); Set(game,"maps",new List<Map>()); Set(game,"tickManager",Empty<TickManager>());
        var data = new IdeologyColonyData(game); Set(game,"components",new List<GameComponent> { data }); Current.Game = game;
        MP.game = Empty<MultiplayerGame>(); MP.game.worldComp = Empty<MultiplayerWorldComp>(); MP.game.worldComp.spectatorFaction = spectator;
        MP.game.gameComp = new MultiplayerGameComp { multifaction = true }; MP.session = Empty<MultiplayerSession>(); MP.session.client = new DummyConnection();
        var h = new Harmony("ideology.smoke");
        h.Patch(AccessTools.Method(typeof(DefOfHelper),nameof(DefOfHelper.EnsureInitializedInCtor)),prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
        HistoryEventDefOf.CutTree=new HistoryEventDef { defName="CutTree" };
        HistoryEventDefOf.MinifiedTreeDied=new HistoryEventDef { defName="MinifiedTreeDied" };
        h.Patch(AccessTools.PropertyGetter(typeof(PawnsFinder),nameof(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)),prefix:new HarmonyMethod(typeof(Program),nameof(PawnList)));
        h.Patch(AccessTools.Method(typeof(Precept_Role),"Notify_PawnAssigned"),prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
        h.Patch(AccessTools.Method(typeof(Precept_Role),"Notify_PawnUnassigned"),prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
        h.Patch(AccessTools.Method(typeof(Precept_Role),"ValidatePawn"),prefix:new HarmonyMethod(typeof(Program),nameof(Valid)));
        foreach (string name in new[] { "Error", "Warning", "Message" }) h.Patch(AccessTools.Method(typeof(Log),name,new[]{typeof(string)}),prefix:new HarmonyMethod(typeof(Program),nameof(Print)));
        h.Patch(AccessTools.Method(typeof(GenTypes),nameof(GenTypes.GetTypeInAnyAssembly)),prefix:new HarmonyMethod(typeof(Program),nameof(FixtureType)));
        IdeologyFactionFixMod.ApplyPatches();
        h.Patch(AccessTools.Method(typeof(Precept_RoleSingle),nameof(Precept_RoleSingle.Assign)),prefix:new HarmonyMethod(typeof(Program),nameof(MaybeThrow)){priority=Priority.Last});
        var ideo = new Ideo { id=12 }; a.ideos.IdeosMinorListForReading.Add(ideo); b.ideos.IdeosMinorListForReading.Add(ideo); var p1=Pawn(a,ideo,101); var p2=Pawn(a,ideo,102); var p3=Pawn(b,ideo,103);
        Check(ideo.ColonistBelieverCountCached==2,"follower count excludes another colony's believer");
        Set(fm,"ofPlayer",b); Check(ideo.ColonistBelieverCountCached==1,"the same ideology has a distinct count for the other faction");
        Check(!ideo.ObligationsActive,"one believer does not activate minor-ideology obligations");
        var role = new Precept_RoleSingle { ideo=ideo,def=new PreceptDef { activationBelieverCount=3,deactivationBelieverCount=0 },chosenPawn=null };
        Set(role,"ID",123); role.chosenPawn = new IdeoRoleInstance(role); Set(ideo,"precepts",new List<Precept> { role }); ideo.cachedPossibleRoles.Add(role);
        role.RecacheActivity();
        Check(!Active(role),"pooled 2+1 believers cannot activate a role for the smaller colony");
        Set(fm,"ofPlayer",a); Check(!Active(role),"pooled 2+1 believers cannot activate a role for the larger colony");
        Pawn(a,ideo,104); role.RecacheActivity(); Check(Active(role),"three local believers activate their own role");
        Set(fm,"ofPlayer",b); Check(!Active(role),"another colony remains below its own activation threshold");
        Pawn(b,ideo,105); Pawn(b,ideo,106); role.RecacheActivity(); Check(Active(role),"second colony independently activates at three believers");
        role.Assign(p3,false); Set(fm,"ofPlayer",a); role.Assign(p1,false);
        Check(role.ChosenPawnSingle()==p1 && role.IsAssigned(p1),"first colony keeps its appointed role holder");
        Set(fm,"ofPlayer",b); Check(role.ChosenPawnSingle()==p3 && role.IsAssigned(p3),"appointing in another colony does not displace this holder");
        Check(role.IsAssigned(p1),"pawn-facing assigned check resolves the pawn's faction rather than the viewer");
        var seq=role.ChosenPawns(); Set(fm,"ofPlayer",a); Check(seq.Single()==p3,"chosen-pawn iterator captures the scoped holder before the outer state is restored");
        var sa=data.Read(role,a); var sb=data.Read(role,b); sa.instance.abilities=new List<Ability>(); sb.instance.abilities=new List<Ability>();
        Check(!ReferenceEquals(role.AbilitiesFor(p1),role.AbilitiesFor(p3)),"role abilities are stored separately for each faction");
        int count=data.roles.Count; var unused=Faction(33); using(RoleScope.Enter(role,unused,false)) { _=role.ChosenPawnSingle(); }
        Check(data.roles.Count==count,"read-only UI scope does not add persistent save state");
        int depth=FactionContext.stack.Count; var original=role.chosenPawn;
        try { using(RoleScope.Enter(role,b,false)) throw new InvalidOperationException(); } catch(InvalidOperationException) { }
        Check(RimWorld.Faction.OfPlayer==a && FactionContext.stack.Count==depth && role.chosenPawn==original,"exceptions restore the original role fields and faction context");
        failAssign=true; bool caught=false;
        try { role.Assign(p3,false); } catch(InvalidOperationException e) { caught=e.Message=="fixture assignment failure"; } finally { failAssign=false; }
        Check(caught && RimWorld.Faction.OfPlayer==a && FactionContext.stack.Count==depth,"actual Harmony assignment finalizer preserves exceptions and restores faction context");
        role.Unassign(p1,false); Set(fm,"ofPlayer",b); Check(role.ChosenPawnSingle()==p3,"unassigning one colony's pawn leaves the other colony's holder intact");
        var ma=Map(a,1); var mb=Map(b,6); var ta=ma.GetComponent<IdeologyTreeData>(); var tb=mb.GetComponent<IdeologyTreeData>();
        Set(ma.treeDestructionTracker,"playerTreeDestructionTicks",new List<int>{10,20});
        ta.Migrate(); tb.Migrate(); Check(ta.Count(a,100)==2 && ta.Count(b,100)==0,"legacy tree damage stays with its original map and owner");
        Check(tb.Count(b,100)==0,"legacy tree history is not copied to another colony's map");
        ta.Record(b,100); Check(ta.Count(a,100)==2,"a visitor's tree damage does not increment the owning colony's responsibility counter");
        tb.Record(b,100); Check(tb.Count(b,100)==1 && ta.Count(a,100)==2,"new tree damage remains map- and faction-local");
        Check(ta.Count(a,900021)==0,"tree damage expires after the vanilla fifteen-day window");
        Set(fm,"ofPlayer",a); role.Assign(p1,false);
        var multi=new Precept_RoleMulti(); multi.chosenPawns.Add(new IdeoRoleInstance(multi){pawn=p1}); multi.chosenPawns.Add(new IdeoRoleInstance(multi){pawn=p3});
        Check(multi.ChosenPawns().SequenceEqual(new[]{p1}),"multi-holder role capacity and vacancy queries enumerate only the local faction");
        Set(fm,"ofPlayer",b); Check(multi.ChosenPawns().SequenceEqual(new[]{p3}),"multi-holder roles keep another faction's roster separate"); Set(fm,"ofPlayer",a);
        TestTreeHooks(a,b,p1,p3,ma,mb,game);
        TestSave(data,role,a,b,args[0]);
        Benchmark(role,p1);
        h.Patch(AccessTools.Method(typeof(Ideo),nameof(Ideo.RemovePrecept)),prefix:new HarmonyMethod(typeof(Program),nameof(RemoveRoleBody)){priority=Priority.Last});
        ideo.RemovePrecept(role);
        Check(removedHolders.Length==2 && removedHolders.Contains(p1) && removedHolders.Contains(p3),"shared definition removal enumerates and cleans up every colony's role holder");
        Check(data.roles.Count==0,"removed roles leave no unresolved references in the next save");
        MP.game.gameComp.multifaction=false; Set(ideo,"colonistBelieverCountCached",42); Check(ideo.ColonistBelieverCountCached==42,"single-faction gameplay retains the vanilla cached-count path");
        Console.WriteLine("Ideology faction smoke: "+checks+" checks passed");
    }
    static void TestSave(IdeologyColonyData data,Precept_RoleSingle role,Faction a,Faction b,string folder)
    {
        string path=Path.Combine(folder,"ideology-faction-save.xml");
        Scribe.saver.InitSaving(path,"ideology");
        data.ExposeData();
        var checker=AccessTools.Field(typeof(ScribeSaver),"loadIDsErrorsChecker").GetValue(Scribe.saver);
        foreach(var obj in new object[]{role,a,b,data.Read(role,a).instance.pawn,data.Read(role,b).instance.pawn})
            AccessTools.Method(checker.GetType(),"RegisterDeepSaved").Invoke(checker,new[]{obj,"fixture-world"});
        Scribe.saver.FinalizeSaving();
        var text=File.ReadAllText(path);
        Check(text.Contains("Faction_20") && text.Contains("Faction_22") && text.Contains("Thing_"),"save data includes both faction references and surviving holder references");
        Check(text.IndexOf("Faction_20",StringComparison.Ordinal)<text.IndexOf("Faction_22",StringComparison.Ordinal),"faction role state serializes in deterministic faction order");
        var loaded=new IdeologyColonyData(Current.Game);
        Scribe.loader.InitLoading(path); loaded.ExposeData();
        Scribe.loader.crossRefs.RegisterForCrossRefResolve(loaded);
        Scribe.loader.initer.RegisterForPostLoadInit(loaded);
        var directory=AccessTools.Field(typeof(CrossRefHandler),"loadedObjectDirectory").GetValue(Scribe.loader.crossRefs);
        foreach(var obj in new ILoadReferenceable[]{role,a,b,data.Read(role,a).instance.pawn,data.Read(role,b).instance.pawn})
            AccessTools.Method(directory.GetType(),"RegisterLoaded").Invoke(directory,new object[]{obj});
        Scribe.loader.crossRefs.ResolveAllCrossReferences();
        Check(loaded.roles.All(s => s.instance.sourceRole==role),"role sources resolve before ability post-load initialization, independent of post-load order");
        Scribe.loader.initer.DoAllPostLoadInits();
        Scribe.loader.ForceStop();
        Check(loaded.roles.Count==2 && loaded.Read(role,a).instance.pawn==data.Read(role,a).instance.pawn && loaded.Read(role,b).instance.pawn==data.Read(role,b).instance.pawn,"both role holders survive real Scribe cross-reference resolution and post-load initialization");
        Check(loaded.Read(role,a).instance.sourceRole==role && loaded.Read(role,b).instance.sourceRole==role,"loaded role instances retain their original synchronized precept identity");
    }
    static void TestTreeHooks(Faction a,Faction b,Pawn pa,Pawn pb,Map ma,Map mb,Game game)
    {
        var cutMap=Map(a,8); var manager=Find.FactionManager; Set(manager,"ofPlayer",b);
        cutMap.treeDestructionTracker.Notify_TreeCut(pa);
        Check(cutMap.GetComponent<IdeologyTreeData>().Count(a,100)==1,"real cut notification attributes responsibility to the actor rather than the viewer");
        Check(cutMap.treeDestructionTracker.PlayerResponsibleTreeDestructionCount==0,"tree-counter getter does not expose another colony's history");
        Set(manager,"ofPlayer",a); Check(cutMap.treeDestructionTracker.PlayerResponsibleTreeDestructionCount==1,"tree-counter getter exposes this faction's own map history");
        game.Maps.Add(ma); game.Maps.Add(mb); Set(pa,"mapIndexOrState",(sbyte)0);
        var worker=new ThoughtWorker_Precept_TreeDensity();
        var stage=AccessTools.Method(typeof(ThoughtWorker_Precept_TreeDensity),"ThoughtStageIndex");
        Check((int)stage.Invoke(worker,new object[]{pa})==1,"tree-density thought reads the pawn's own map and faction counter");
        Set(pa,"mapIndexOrState",(sbyte)1);
        Check((int)stage.Invoke(worker,new object[]{pa})==0,"visiting another player's map does not inherit its tree-damage mood stage");
        Set(pa,"mapIndexOrState",(sbyte)-1);
        var memory=new PreceptComp_KnowsMemoryThought { thought=new ThoughtDef { thoughtClass=typeof(Thought_Memory) } };
        var patch=AccessTools.Method(typeof(IdeologyFactionFixMod).Assembly.GetType("IdeologyFactionFix.ScopeTreeMemories"),"Prefix");
        var cut=new HistoryEvent(HistoryEventDefOf.CutTree,pb.Named(HistoryEventArgsNames.Doer));
        Check(!(bool)patch.Invoke(null,new object[]{memory,cut,pa}),"foreign tree cutting is rejected for colony mood memories");
        memory.thought.thoughtClass=typeof(Thought_MemorySocial);
        Check((bool)patch.Invoke(null,new object[]{memory,cut,pa}),"actually witnessed social opinions retain the vanilla path");
    }
    static void Benchmark(Precept_RoleSingle role,Pawn pawn)
    {
        for(int i=0;i<2000;i++) { role.IsAssigned(pawn); role.ChosenPawns(); }
        var measure=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread");
        var allocated=measure==null?null:(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),measure);
        long calibration=allocated?.Invoke()??0; var probe=new byte[1024]; GC.KeepAlive(probe);
        bool counterWorks=allocated!=null && allocated()>calibration;
        long before=allocated?.Invoke()??0; int collections=GC.CollectionCount(0);
        var timer=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<100000;i++) { if(!role.IsAssigned(pawn))throw new Exception("lost role"); role.ChosenPawns(); }
        timer.Stop();
        long bytes=(allocated?.Invoke()??0)-before;
        Console.WriteLine("BENCH 100000 assigned+roster pairs: "+timer.ElapsedMilliseconds+" ms; allocated="+(counterWorks?bytes.ToString():"counter unavailable")+"; gen0="+(GC.CollectionCount(0)-collections));
        Check(GC.CollectionCount(0)==collections && (!counterWorks || bytes<4096),"warm role reads require no gen0 collection (byte counter checked when supported)");
    }
    static bool Active(Precept_Role role) => (bool)AccessTools.PropertyGetter(typeof(Precept_Role),nameof(Precept_Role.Active)).Invoke(role,null);
    static bool PawnList(ref List<Pawn> __result) { __result=pawns; return false; }
    static bool RemoveRoleBody(Ideo __instance, Precept precept)
    {
        removedHolders=((Precept_Role)precept).ChosenPawns().ToArray();
        ((List<Precept>)AccessTools.Field(typeof(Ideo),"precepts").GetValue(__instance)).Remove(precept);
        return false;
    }
    static void MaybeThrow() { if(failAssign)throw new InvalidOperationException("fixture assignment failure"); }
    static bool FixtureType(string __0,ref Type __result)
    {
        var type=typeof(IdeologyFactionFixMod).Assembly.GetType(__0,false);
        if(type==null)return true; __result=type; return false;
    }
    static bool Print(string text) { Console.WriteLine("GAME LOG: "+text); return false; }
    static bool Skip() => false;
    static bool Valid(ref bool __result) { __result=true; return false; }
    sealed class DummyConnection:ConnectionBase { protected override void SendRaw(byte[] raw,bool reliable=true){} protected override void OnClose(ServerDisconnectPacket? packet){} }
}
